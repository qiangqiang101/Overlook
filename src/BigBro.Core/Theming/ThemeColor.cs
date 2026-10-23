using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BigBro.Core.Theming;

/// <summary>sRGB colour, written in JSON as <c>#AARRGGBB</c> (<c>#RRGGBB</c> is accepted on read).</summary>
[JsonConverter(typeof(ThemeColorJsonConverter))]
public readonly record struct ThemeColor(byte A, byte R, byte G, byte B)
{
    public static readonly ThemeColor Transparent = new(0, 255, 255, 255);
    public static readonly ThemeColor Black = new(255, 0, 0, 0);
    public static readonly ThemeColor White = new(255, 255, 255, 255);
    public static readonly ThemeColor Red = new(255, 255, 0, 0);
    public static readonly ThemeColor Lime = new(255, 0, 255, 0);
    public static readonly ThemeColor Gray = new(255, 128, 128, 128);

    public bool IsTransparent => A == 0;

    public uint ToArgb() => ((uint)A << 24) | ((uint)R << 16) | ((uint)G << 8) | B;

    public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";

    public static ThemeColor Parse(string text)
    {
        if (!TryParse(text, out var c)) throw new FormatException($"'{text}' is not a colour like #AARRGGBB.");
        return c;
    }

    public static bool TryParse(string? text, out ThemeColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().TrimStart('#');
        if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        switch (s.Length)
        {
            case 6:
                color = new ThemeColor(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
                return true;
            case 8:
                color = new ThemeColor((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
                return true;
            default:
                return false;
        }
    }
}

public sealed class ThemeColorJsonConverter : JsonConverter<ThemeColor>
{
    public override ThemeColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        return ThemeColor.TryParse(s, out var c) ? c : throw new JsonException($"'{s}' is not a colour like #AARRGGBB.");
    }

    public override void Write(Utf8JsonWriter writer, ThemeColor value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
