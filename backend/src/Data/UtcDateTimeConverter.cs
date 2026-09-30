using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TimeTracking.Api.Data;

/// <summary>Stores DateTime as UTC; unspecified kinds are taken as UTC, local ones converted.</summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => ToUtc(v),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
{
    public static DateTime ToUtc(DateTime v) => v.Kind switch
    {
        DateTimeKind.Utc => v,
        DateTimeKind.Local => v.ToUniversalTime(),
        _ => DateTime.SpecifyKind(v, DateTimeKind.Utc)
    };
}

/// <inheritdoc cref="UtcDateTimeConverter"/>
public sealed class UtcNullableDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
    v => v.HasValue ? UtcDateTimeConverter.ToUtc(v.Value) : v,
    v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
