using System.Text.Json;
using CarTracker.ModelContextProtocol;

namespace CarTracker.Chat;

/// <summary>
/// What the owner confirmed, read and checked against the tool's own schema.
/// </summary>
/// <remarks>
/// The check is deliberately narrow: <b>a field the tool does not have, and a required field left empty</b>.
/// Those are the two mistakes a draft card can actually make, and both can be reported against the field that
/// caused them, which is what makes them worth catching here. Everything else — a mileage below the current
/// reading, a fuel row typed as an expense — is a <i>domain</i> refusal, and those are not schema problems: they
/// come back through the loop as the tool's own sentence, which the assistant then explains. Re-implementing
/// them here would be a second copy of rules the domain already owns, and the copy would be the one that drifted.
/// </remarks>
public static class ChatArguments
{
    /// <summary>Reads the confirmed values. Absent means "exactly what was proposed".</summary>
    public static IDictionary<string, object?>? Read(JsonElement? arguments)
    {
        if (arguments is not { ValueKind: JsonValueKind.Object } values) return null;

        // Values stay as JsonElement: the function factory deserialises each one against the parameter's own
        // type, which is a better converter than anything guessed from the JSON shape here.
        return values.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
    }

    /// <summary>
    /// Field errors in the shape the client already knows how to render — the same RFC 9457 <c>errors</c> map
    /// every add sheet marks its fields from.
    /// </summary>
    public static Dictionary<string, string[]> Check(
        string tool,
        IDictionary<string, object?>? arguments,
        IServiceProvider services)
    {
        Dictionary<string, string[]> errors = [];

        if (arguments is null) return errors;

        var function = CarTrackerToolCatalogue.AIFunctions(services).FirstOrDefault(f => f.Name == tool);
        if (function is null) return errors;

        var schema = function.JsonSchema;

        if (!schema.TryGetProperty("properties", out var properties)) return errors;

        foreach (var supplied in arguments.Keys)
        {
            if (!properties.TryGetProperty(supplied, out _))
            {
                errors[supplied] = [$"'{tool}' has no field called '{supplied}'."];
            }
        }

        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var name in required.EnumerateArray().Select(r => r.GetString()).OfType<string>())
            {
                if (!arguments.TryGetValue(name, out var value) || IsEmpty(value))
                {
                    errors[name] = ["This is needed before the record can be saved."];
                }
            }
        }

        return errors;
    }

    private static bool IsEmpty(object? value) => value switch
    {
        null => true,
        JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => true,
        JsonElement { ValueKind: JsonValueKind.String } text => string.IsNullOrWhiteSpace(text.GetString()),
        string text => string.IsNullOrWhiteSpace(text),
        _ => false,
    };
}
