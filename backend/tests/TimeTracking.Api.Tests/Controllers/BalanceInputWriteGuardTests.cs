using System.Text.RegularExpressions;
using FluentAssertions;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// The balance checkpoints are invalidated only by <c>TimeTrackingDbContext.SaveChangesAsync</c> (<c>BalanceInvalidation</c>),
/// so a write to a balance input that bypasses it — <c>ExecuteUpdate</c>/<c>ExecuteDelete</c>, raw SQL, a COPY — would leave
/// stale checkpoints valid. Like <c>ErrorCodeTests</c> this reads the source of <c>backend/src</c>: bulk/raw writes may only
/// appear in the allow-listed files, and those may only touch tables that are no balance input.
/// </summary>
public class BalanceInputWriteGuardTests
{
    /// <summary>Tables (and the one column) whose changes feed a balance.</summary>
    private static readonly string[] BalanceInputs = ["TimeEntries", "AbsenceDays", "WorkSchedules", "Holidays", "AbsenceTypeDefinitions"];

    private static readonly Regex BulkOrRaw = new(
        @"\b(ExecuteUpdate|ExecuteDelete|ExecuteSql|ExecuteSqlRaw|ExecuteSqlInterpolated|SqlQuery|SqlQueryRaw|FromSql|FromSqlRaw|FromSqlInterpolated|NpgsqlCommand|BeginBinaryImport|CreateCommand)\w*\b");

    /// <summary>Why each file may write in bulk or raw SQL. Anything else in <c>src</c> must go through tracked entities.</summary>
    private static readonly Dictionary<string, string> Allowed = new()
    {
        ["Services/Common/BalanceCheckpoints.cs"] = "the checkpoint upsert and the version bump/carry (checked below)",
        ["Services/Auth/RefreshTokenCleanupService.cs"] = "retention deletes of tokens, links, notifications, punches and invalid checkpoints (checked below)",
        ["Data/RowLevelSecurity.cs"] = "the RLS session stamp (set_config, no table)",
        ["Data/PerfSeeder.cs"] = "the perf-test history generator: COPY into a fresh perf organization, drop of such an organization (never real data)"
    };

    private static string SrcDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Services"))) dir = dir.Parent;
        dir.Should().NotBeNull("the test runs below backend/");
        return Path.Combine(dir!.FullName, "src");
    }

    private static IEnumerable<(string Path, string[] Lines)> Sources()
    {
        var src = SrcDir();
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(f => (Path.GetRelativePath(src, f).Replace('\\', '/'), File.ReadAllLines(f)));
    }

    private static bool IsComment(string line) => line.TrimStart().StartsWith("//");

    [Fact]
    public void BulkAndRawSqlWrites_OnlyInAllowListedFiles()
    {
        var offenders = Sources()
            .SelectMany(s => s.Lines.Select((line, i) => (s.Path, Line: i + 1, Text: line)))
            .Where(l => !IsComment(l.Text) && BulkOrRaw.IsMatch(l.Text) && !Allowed.ContainsKey(l.Path))
            .Select(l => $"{l.Path}:{l.Line}: {l.Text.Trim()}")
            .ToList();
        offenders.Should().BeEmpty(
            "balance inputs must be written through SaveChangesAsync so BalanceInvalidation sees them; a new bulk/raw write needs a reason in Allowed");
    }

    [Fact]
    public void RawSql_NeverWritesABalanceInput_OutsideThePerfSeeder()
    {
        var write = new Regex(@"(UPDATE|INSERT\s+INTO|DELETE\s+FROM|TRUNCATE|COPY)\s+""?(\w+)""?", RegexOptions.IgnoreCase);
        var offenders = Sources()
            .Where(s => s.Path != "Data/PerfSeeder.cs")
            .SelectMany(s => s.Lines.Select((line, i) => (s.Path, Line: i + 1, Text: line)))
            .Where(l => !IsComment(l.Text) && !l.Text.TrimStart().StartsWith("///"))
            .SelectMany(l => write.Matches(l.Text).Select(m => (l.Path, l.Line, Table: m.Groups[2].Value, l.Text)))
            .Where(m => BalanceInputs.Contains(m.Table) || (m.Table == "Organizations" && !m.Text.Contains("\"BalanceVersion\"")))
            .Select(m => $"{m.Path}:{m.Line}: {m.Text.Trim()}")
            .ToList();
        offenders.Should().BeEmpty("raw SQL may bump versions, never change a balance input or the time zone");
    }

    [Fact]
    public void TheCheckpointCode_UpdatesOnlyItsOwnTableAndTheVersionColumns()
    {
        var source = File.ReadAllText(Path.Combine(SrcDir(), "Services", "Common", "BalanceCheckpoints.cs"));
        var updates = Regex.Matches(source, @"UPDATE\s+""(\w+)""\s+SET\s+""(\w+)""").Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();
        updates.Should().NotBeEmpty();
        updates.Should().OnlyContain(u => (u.Item1 == "BalanceCheckpoints" && (u.Item2 == "Version" || u.Item2 == "OrgVersion"))
                                          || ((u.Item1 == "UserOrganizations" || u.Item1 == "Organizations") && u.Item2 == "BalanceVersion"));
        Regex.Matches(source, @"INSERT\s+INTO\s+""(\w+)""").Select(m => m.Groups[1].Value).Should().OnlyContain(t => t == "BalanceCheckpoints");
        Regex.IsMatch(source, @"DELETE\s+FROM", RegexOptions.IgnoreCase).Should().BeFalse();
    }

    [Fact]
    public void TheRetentionCleanup_DeletesNoBalanceInput()
    {
        var source = File.ReadAllText(Path.Combine(SrcDir(), "Services", "Auth", "RefreshTokenCleanupService.cs"));
        var targets = Regex.Matches(source, @"DeleteAsync\(context,\s*(?:context\.(\w+)|(BalanceCheckpoints)\.Invalid)").Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToList();
        targets.Should().NotBeEmpty();
        targets.Should().OnlyContain(t => !BalanceInputs.Contains(t), "retention deletes never touch a balance input");
        Regex.Matches(source, @"\bExecute(Update|Delete)\w*\(").Count.Should().Be(1, "the one generic DeleteAsync helper");
    }
}
