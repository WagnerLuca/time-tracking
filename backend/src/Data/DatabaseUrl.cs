using Npgsql;

namespace TimeTracking.Api.Data;

/// <summary>Converts a Heroku-style DATABASE_URL into an Npgsql connection string.</summary>
public static class DatabaseUrl
{
    /// <summary>
    /// <c>postgres://user:pass@host:5432/db</c> (or <c>postgresql://</c>) → Npgsql connection string.
    /// Percent-encoded user/password are decoded, so generated passwords containing
    /// <c>/ : @</c> work. Returns null when the value is empty or not a postgres URL.
    /// </summary>
    public static string? ToConnectionString(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !(url.StartsWith("postgres://") || url.StartsWith("postgresql://")))
            return null;

        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        return new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null
        }.ConnectionString;
    }
}
