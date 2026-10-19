using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace TimeTracking.Api.Tests.Infrastructure;

/// <summary>
/// Counts the SQL commands EF sends (readers, non-queries and scalars). <see cref="Count"/> is every
/// command through this interceptor (a test with its own DbContext resets it to 0 before the call it
/// measures); <see cref="Begin"/> opens a scope that counts — and optionally records — only the commands
/// of its own async flow, so background services sharing the app's DbContext registration do not
/// leak into it. Over HTTP the flow reaches the server only with
/// <c>TestServerOptions.PreserveExecutionContext</c> (see <see cref="PerfApiFactory"/>).
/// The RLS stamp (<c>RlsConnectionInterceptor</c>) bypasses EF and is never counted.
/// </summary>
public sealed class QueryCounter : DbCommandInterceptor
{
    private static readonly AsyncLocal<Scope?> Current = new();
    private int _count;

    public int Count
    {
        get => Volatile.Read(ref _count);
        set => Interlocked.Exchange(ref _count, value);
    }

    /// <summary>Counts the commands of the calling async flow until disposed; <paramref name="capture"/> keeps their SQL.</summary>
    public static Scope Begin(bool capture = false) => Current.Value = new Scope(capture);

    /// <summary>One command of a scope: its SQL, cloned parameters and how long it ran.</summary>
    public sealed record Command(string Sql, NpgsqlParameter[] Parameters, TimeSpan Duration);

    public sealed class Scope : IDisposable
    {
        private readonly bool _capture;
        private int _count;
        private readonly List<Command> _commands = [];

        internal Scope(bool capture) => _capture = capture;

        public int Count => Volatile.Read(ref _count);

        public IReadOnlyList<Command> Commands
        {
            get { lock (_commands) return _commands.ToList(); }
        }

        internal void Add(DbCommand command, TimeSpan duration)
        {
            Interlocked.Increment(ref _count);
            if (!_capture) return;
            var parameters = command.Parameters.OfType<NpgsqlParameter>().Select(p => p.Clone()).ToArray();
            lock (_commands) _commands.Add(new Command(command.CommandText, parameters, duration));
        }

        public void Dispose()
        {
            if (Current.Value == this) Current.Value = null;
        }
    }

    private void Record(DbCommand command, TimeSpan duration)
    {
        Interlocked.Increment(ref _count);
        Current.Value?.Add(command, duration);
    }

    // Counted once the command ran (so a duration is known); a failing command is counted too
    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Record(command, eventData.Duration);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Record(command, eventData.Duration);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Record(command, eventData.Duration);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) => Record(command, eventData.Duration);

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Record(command, eventData.Duration);
        return Task.CompletedTask;
    }
}
