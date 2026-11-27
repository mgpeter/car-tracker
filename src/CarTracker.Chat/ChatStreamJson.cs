using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarTracker.Chat;

/// <summary>
/// How a typed stream frame is written: web defaults (camelCase), enums as their names, and compact.
/// </summary>
/// <remarks>
/// <para>
/// Compact because an SSE frame is line-oriented; indented JSON splits into lines the client cannot parse.
/// </para>
/// <para>
/// Enums as names because the web client types them from the generated contract's string unions (a
/// <c>DocumentType</c> is <c>"PartsReceipt"</c> there), so a number on the wire would match nothing. The endpoint's
/// general frame options write numbers, and the frames that use them carry no enums.
/// </para>
/// </remarks>
public static class ChatStreamJson
{
    public static JsonSerializerOptions Options { get; } = Build();

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = false };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
