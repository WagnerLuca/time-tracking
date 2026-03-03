namespace TimeTracking.Api.Models.Dtos;

/// <summary>Standard paginated response wrapper for list endpoints.</summary>
/// <typeparam name="T">Type of items in the collection.</typeparam>
public record PaginatedResponse<T>
{
    /// <summary>The items on the current page.</summary>
    public required List<T> Items { get; init; }

    /// <summary>Total number of items matching the query (across all pages).</summary>
    public required int TotalCount { get; init; }

    /// <summary>Maximum number of items per page.</summary>
    public required int Limit { get; init; }

    /// <summary>Number of items skipped (0-based offset).</summary>
    public required int Offset { get; init; }

    /// <summary>Whether more items exist beyond the current page.</summary>
    public bool HasMore => Offset + Items.Count < TotalCount;
}

/// <summary>Shared constants and helpers for pagination.</summary>
public static class PaginationDefaults
{
    /// <summary>Default number of items per page.</summary>
    public const int DefaultLimit = 50;

    /// <summary>Maximum allowed items per page.</summary>
    public const int MaxLimit = 200;

    /// <summary>Clamp limit and offset to valid ranges.</summary>
    public static (int limit, int offset) Normalize(int limit, int offset)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);
        offset = Math.Max(offset, 0);
        return (limit, offset);
    }
}
