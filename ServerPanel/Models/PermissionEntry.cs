using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServerPanel.Models;

public class PermissionEntry
{
    [JsonPropertyName("identity")]
    public string Identity { get; set; } = "";

    [JsonPropertyName("flags")]
    public List<string> Flags { get; set; } = new();

    [JsonPropertyName("immunity")]
    [JsonConverter(typeof(IntOrStringConverter))]
    public int Immunity { get; set; } = 0;

    [JsonPropertyName("groups")]
    public List<string>? Groups { get; set; }
}

file sealed class IntOrStringConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            return int.TryParse(s, out var n) ? n : 0;
        }
        return reader.GetInt32();
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}
