using System.Text.Json;
using System.Text.Json.Serialization;
using BigBro.Core.Legacy;

namespace BigBro.Core.Theming;

/// <summary>
/// Reads and writes <see cref="SensorBinding"/>. A theme file written before 2.0 can contain a 1.x sensor name as its kind;
/// instead of failing, that is kept as <see cref="SensorBinding.LegacyKind"/> (the element is unbound) so the editor can
/// ask the user which sensor to use.
/// </summary>
public sealed class SensorBindingConverter : JsonConverter<SensorBinding>
{
    public override SensorBinding? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        string? Text(string name) =>
            root.EnumerateObject().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { Value.ValueKind: JsonValueKind.String } p
                ? p.Value.GetString() : null;

        var kindText = Text("kind");
        var binding = new SensorBinding { Param = Text("param"), LegacyKind = Text("legacyKind") };

        if (Enum.TryParse<SensorKind>(kindText, ignoreCase: true, out var kind) && Enum.IsDefined(kind))
            binding.Kind = kind;
        else if (!string.IsNullOrEmpty(kindText) && Enum.TryParse<LegacySensorKind>(kindText, ignoreCase: true, out _))
            binding.LegacyKind ??= kindText;
        // anything else is an unknown kind: unbound
        return binding;
    }

    public override void Write(Utf8JsonWriter writer, SensorBinding value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", JsonNamingPolicy.CamelCase.ConvertName(value.Kind.ToString()));
        if (value.Param is not null) writer.WriteString("param", value.Param);
        if (value.LegacyKind is not null) writer.WriteString("legacyKind", value.LegacyKind);
        writer.WriteEndObject();
    }
}
