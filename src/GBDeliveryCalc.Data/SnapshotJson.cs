using System.Text.Json;
using System.Text.Json.Serialization;

namespace GBDeliveryCalc.Data;

/// <summary>JSON settings for estimate snapshots and actuals. Unknown properties are ignored, so older snapshots still load.</summary>
internal static class SnapshotJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.Strict,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidDataException("Empty JSON document.");
}
