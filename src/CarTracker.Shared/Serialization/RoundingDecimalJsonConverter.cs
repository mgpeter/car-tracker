using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarTracker.Shared.Serialization;

/// <summary>
/// Rounds <see cref="decimal"/> values as they cross the wire, so a derived figure serialises as <c>45.1993</c>
/// rather than <c>45.19929999999999998</c>. Write-only: it rounds on serialize and reads back verbatim, and the
/// in-memory values the domain computes keep full precision — only the JSON a client sees is cleaned.
/// </summary>
/// <remarks>
/// Registered on the <b>MCP tool serializer only</b> (see <c>McpServerRegistration</c>), not the REST host's
/// <c>ConfigureHttpJsonOptions</c>: a custom decimal converter defeats the OpenAPI generator's type introspection,
/// emitting every decimal property as an empty schema (→ <c>unknown</c> TypeScript). The web app formats numbers
/// in JS, so the raw tail is a non-issue there; the assistant is where it was reported. Lives in Shared so it
/// stays close to the DTOs both surfaces share.
/// </remarks>
/// <remarks>
/// Four decimals is chosen to hold everything the app shows without inventing precision: money to the penny,
/// price-per-litre and MPG to a tenth, and volume-weighted averages (e.g. 1.5973 £/L) to four. A raw
/// double-derived decimal that would otherwise trail a long fractional tail is what an assistant flagged.
/// </remarks>
public sealed class RoundingDecimalJsonConverter : JsonConverter<decimal>
{
    internal const int Decimals = 4;

    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        => writer.WriteNumberValue(decimal.Round(value, Decimals, MidpointRounding.AwayFromZero));
}

/// <summary>The nullable companion — <see cref="JsonSerializerOptions"/> matches converters by exact type.</summary>
public sealed class RoundingNullableDecimalJsonConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null ? null : reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value is { } v)
            writer.WriteNumberValue(decimal.Round(v, RoundingDecimalJsonConverter.Decimals, MidpointRounding.AwayFromZero));
        else
            writer.WriteNullValue();
    }
}
