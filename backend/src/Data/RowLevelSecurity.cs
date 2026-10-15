using System.Data.Common;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TimeTracking.Api.Data;

/// <summary>
/// Who the database should treat as the current user for Postgres row-level security.
/// Scoped per request: inside an HTTP request it is the authenticated user (anonymous
/// requests see no protected rows); outside a request (migrations, seeding, background
/// jobs) it is the trusted system and bypasses RLS.
/// </summary>
public sealed class RlsContext
{
    private readonly IHttpContextAccessor _http;
    private int? _actingUserId;

    public RlsContext(IHttpContextAccessor http) => _http = http;

    /// <summary>
    /// Lets trusted code act for a user it has authenticated by other means, e.g. the
    /// terminal punch that identified a member by API key + badge.
    /// </summary>
    public void ActAsUser(int userId) => _actingUserId = userId;

    public bool IsSystem => _http.HttpContext == null && _actingUserId == null;

    /// <summary>The signed-in caller (not a user the code acts for, like the terminal's member).</summary>
    public int? AuthenticatedUserId => _actingUserId == null ? UserId : null;

    /// <summary>
    /// Why an org admin changes a closed month (issue #57): the <c>X-Change-Reason</c> header,
    /// URL-encoded by the client so any text fits a header; trimmed, null when empty.
    /// </summary>
    public string? ChangeReason
    {
        get
        {
            var raw = _http.HttpContext?.Request.Headers["X-Change-Reason"].ToString();
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var text = Uri.UnescapeDataString(raw).Trim();
            if (text.Length == 0) return null;
            return text.Length > Services.ClosedPeriods.MaxReasonLength ? text[..Services.ClosedPeriods.MaxReasonLength] : text;
        }
    }

    public int? UserId =>
        _actingUserId
        ?? (int.TryParse(_http.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null);
}

/// <summary>
/// Stamps every opened Npgsql connection with the RLS session settings the policies in
/// migration EnableRowLevelSecurity read (<c>app.user_id</c>, <c>app.bypass_rls</c>).
/// EF opens a connection per command/transaction and Npgsql resets session state when a
/// connection returns to the pool, so a setting never leaks between requests.
/// </summary>
public sealed class RlsConnectionInterceptor : DbConnectionInterceptor
{
    private readonly RlsContext _rls;

    public RlsConnectionInterceptor(RlsContext rls) => _rls = rls;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = CreateCommand(connection);
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var cmd = CreateCommand(connection);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.user_id', @user_id, false), set_config('app.bypass_rls', @bypass, false)";
        AddParameter(cmd, "user_id", _rls.UserId?.ToString() ?? "");
        AddParameter(cmd, "bypass", _rls.IsSystem ? "on" : "off");
        return cmd;
    }

    private static void AddParameter(DbCommand cmd, string name, string value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
