using System.Text.Json;
using System.Text.Json.Serialization;

namespace TimeTracking.Api.Infrastructure;

/// <summary>
/// Rejects enum values that are not defined (System.Text.Json accepts any integer by default),
/// so e.g. an admin cannot send <c>"role": 3</c> and end up above Owner. For nullable enums a
/// negative number means "no value"; group override fields use <see cref="EnumOverrideConverter{T}"/> instead,
/// so their -1 ("inherit from organization") survives as "clear the override".
/// </summary>
public sealed class StrictEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsEnum || Nullable.GetUnderlyingType(typeToConvert)?.IsEnum == true;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var underlying = Nullable.GetUnderlyingType(typeToConvert);
        return underlying == null
            ? (JsonConverter)Activator.CreateInstance(typeof(StrictEnumConverter<>).MakeGenericType(typeToConvert))!
            : (JsonConverter)Activator.CreateInstance(typeof(StrictNullableEnumConverter<>).MakeGenericType(underlying))!;
    }

    private sealed class StrictEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            ReadDefined<T>(ref reader);

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(Convert.ToInt64(value));
    }

    private sealed class StrictNullableEnumConverter<T> : JsonConverter<T?> where T : struct, Enum
    {
        public override bool HandleNull => true;

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var n) && n < 0) return null;
            return ReadDefined<T>(ref reader);
        }

        public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
        {
            if (value.HasValue) writer.WriteNumberValue(Convert.ToInt64(value.Value));
            else writer.WriteNullValue();
        }
    }

    internal static T ReadDefined<T>(ref Utf8JsonReader reader) where T : struct, Enum
    {
        T value;
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var number))
            value = (T)Enum.ToObject(typeof(T), number);
        else if (reader.TokenType == JsonTokenType.String && Enum.TryParse(reader.GetString(), ignoreCase: true, out T parsed)
                 && !int.TryParse(reader.GetString(), out _))
            value = parsed;
        else
            throw new JsonException($"Invalid value for {typeof(T).Name}.");

        if (!Enum.IsDefined(value))
            throw new JsonException($"Invalid value for {typeof(T).Name}.");
        return value;
    }
}

/// <summary>
/// For a partial-update field where null means "leave unchanged": a negative number is kept
/// (as an undefined value) so the service can tell "clear the override" (-1 from the UI's
/// "inherit") apart from "not sent". Any other value must be defined. Put it on the property:
/// <c>[property: JsonConverter(typeof(EnumOverrideConverter&lt;RuleMode&gt;))]</c>.
/// </summary>
public sealed class EnumOverrideConverter<T> : JsonConverter<T?> where T : struct, Enum
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var n) && n < 0)
            return (T)Enum.ToObject(typeof(T), -1);
        return StrictEnumConverterFactory.ReadDefined<T>(ref reader);
    }

    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteNumberValue(Convert.ToInt64(value.Value));
        else writer.WriteNullValue();
    }
}
