using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TimeTracking.Api.Data;

public static class DbErrors
{
    /// <summary>True when the save failed on a unique index (Postgres 23505).</summary>
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
