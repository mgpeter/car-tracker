using Microsoft.AspNetCore.OpenApi;
// Microsoft.OpenApi v2 flattened the old Microsoft.OpenApi.Models namespace — the types live here now.
using Microsoft.OpenApi;

namespace CarTracker.WebApi.OpenApi;

/// <summary>
/// Documents numbers as numbers, not as "a number or a string".
/// </summary>
/// <remarks>
/// <para>
/// Left alone, every numeric property in the document is emitted as <c>type: ["number", "string"]</c> (plus
/// <c>"null"</c> where nullable), with a regex pattern to match. That is not a bug — it is accurate about
/// <em>input</em>. <c>ConfigureHttpJsonOptions</c> uses <see cref="System.Text.Json.JsonSerializerDefaults.Web"/>,
/// which sets <c>NumberHandling = AllowReadingFromString</c>, so the API really does accept <c>"2003"</c> for an
/// <c>int</c>. The schema is describing what the deserializer will tolerate.
/// </para>
/// <para>
/// The trouble is that one schema describes both directions, and on the way <em>out</em> the API always writes
/// real JSON numbers — <c>AllowReadingFromString</c> only affects reading. Without this, every derived figure
/// generates as <c>number | string</c>, and the front end has to <c>parseFloat</c> an MPG that is never a
/// string. That is worse than a missing type: it invents a case nobody will ever hit and makes the null case —
/// the one that <em>is</em> real, and the reason this project generates types at all — harder to see.
/// </para>
/// <para>
/// So: narrow the document, keep the server lenient. The generated client sends numbers, and the API still
/// accepts a stringified one from anything that is careless about it — which an LLM writing
/// <c>"litres": "47.03"</c> through the MCP server (Phase 4) very plausibly will be. Under-promising in the
/// contract while over-accepting at the door is the safe direction for that asymmetry.
/// </para>
/// </remarks>
public sealed class NumericTypeSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (schema.Type is not { } type)
        {
            return Task.CompletedTask;
        }

        // JsonSchemaType is a flags enum in Microsoft.OpenApi v2, so the union is one value.
        var isNumeric = type.HasFlag(JsonSchemaType.Number) || type.HasFlag(JsonSchemaType.Integer);

        if (isNumeric && type.HasFlag(JsonSchemaType.String))
        {
            schema.Type = type & ~JsonSchemaType.String;

            // The pattern exists only to validate the string form. With the string form gone it describes
            // nothing, and a leftover regex on a number is the kind of detail that later reads as intent.
            schema.Pattern = null;
        }

        return Task.CompletedTask;
    }
}
