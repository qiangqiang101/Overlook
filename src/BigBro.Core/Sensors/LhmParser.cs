using System.Globalization;
using System.Text.Json;

namespace BigBro.Core.Sensors;

/// <summary>Reads the tree served at <c>http://host:8085/data.json</c> by LibreHardwareMonitor's remote web server.</summary>
public static class LhmParser
{
    public static SensorSnapshot Parse(Stream json, DateTimeOffset? takenAt = null)
    {
        using var doc = JsonDocument.Parse(json);
        return Parse(doc.RootElement, takenAt ?? DateTimeOffset.UtcNow);
    }

    public static SensorSnapshot Parse(string json, DateTimeOffset? takenAt = null)
    {
        using var doc = JsonDocument.Parse(json);
        return Parse(doc.RootElement, takenAt ?? DateTimeOffset.UtcNow);
    }

    private static SensorSnapshot Parse(JsonElement root, DateTimeOffset takenAt)
    {
        var hardware = new List<HardwareItem>();
        var sensors = new List<LhmSensor>();
        // The root node is just "Sensor"; its first child is the computer, which starts every path.
        var computer = "";
        if (root.TryGetProperty("Children", out var top) && top.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in top.EnumerateArray())
            {
                if (computer.Length == 0) computer = Str(child, "Text");
                Walk(child, null, "", "", sensors, hardware);
            }
        }
        return new SensorSnapshot(hardware, sensors, takenAt, computer);
    }

    private static void Walk(JsonElement node, HardwareItem? top, string group, string parentPath,
        List<LhmSensor> sensors, List<HardwareItem> hardware)
    {
        var text = Str(node, "Text");
        var path = parentPath.Length == 0 ? text : parentPath + "/" + text;

        if (node.TryGetProperty("SensorId", out var sid) && sid.ValueKind == JsonValueKind.String && top is not null)
        {
            var type = Str(node, "Type");
            var (value, unit) = ParseRaw(Str(node, "RawValue"), type);
            sensors.Add(new LhmSensor
            {
                Id = sid.GetString()!,
                Name = text,
                Type = type,
                Value = value,
                Unit = unit,
                DisplayValue = Str(node, "Value"),
                HardwareId = top.Id,
                HardwareName = top.Name,
                Hardware = top.Kind,
                Group = group,
                Path = path,
            });
        }
        else if (top is null && node.TryGetProperty("HardwareId", out var hid) && hid.ValueKind == JsonValueKind.String)
        {
            top = new HardwareItem(hid.GetString()!, text, KindOf(hid.GetString()!));
            hardware.Add(top);
        }
        else if (top is not null && !node.TryGetProperty("HardwareId", out _))
        {
            // Not hardware, not a sensor: a group header such as "Temperatures".
            group = text;
        }

        if (node.TryGetProperty("Children", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var child in children.EnumerateArray())
                Walk(child, top, group, path, sensors, hardware);
    }

    public static HardwareKind KindOf(string hardwareId)
    {
        static bool Is(string id, string prefix) =>
            id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            (id.Length == prefix.Length || id[prefix.Length] == '/' || id[prefix.Length] == '-');

        if (Is(hardwareId, "/intelcpu") || Is(hardwareId, "/amdcpu")) return HardwareKind.Cpu;
        if (hardwareId.StartsWith("/gpu-", StringComparison.OrdinalIgnoreCase)) return HardwareKind.Gpu;
        if (Is(hardwareId, "/ram")) return HardwareKind.Memory;
        if (Is(hardwareId, "/vram")) return HardwareKind.VirtualMemory;
        if (hardwareId.StartsWith("/memory/", StringComparison.OrdinalIgnoreCase)) return HardwareKind.Dimm;
        if (Is(hardwareId, "/motherboard") || Is(hardwareId, "/lpc")) return HardwareKind.Motherboard;
        if (Is(hardwareId, "/ssd") || Is(hardwareId, "/hdd") || Is(hardwareId, "/nvme")) return HardwareKind.Storage;
        if (Is(hardwareId, "/nic")) return HardwareKind.Network;
        if (Is(hardwareId, "/battery")) return HardwareKind.Battery;
        return HardwareKind.Other;
    }

    /// <summary>
    /// Splits "5291.5 MHz" into (5291.5, "MHz"). Temperatures take their unit from the sensor type, so a degree sign
    /// that was mangled by a code-page mismatch somewhere between LHM and here never reaches the display.
    /// </summary>
    public static (double? Value, string Unit) ParseRaw(string raw, string type)
    {
        var unitFromType = type == "Temperature" ? "°C" : null;
        if (string.IsNullOrWhiteSpace(raw)) return (null, unitFromType ?? "");

        raw = raw.Trim();
        var i = 0;
        while (i < raw.Length && (char.IsDigit(raw[i]) || raw[i] is '.' or ',' or '-' or '+')) i++;
        var number = raw[..i];
        var unit = raw[i..].Trim();

        if (number.Contains(',') && !number.Contains('.')) number = number.Replace(',', '.');
        else number = number.Replace(",", "");

        double? value = double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        return (value, unitFromType ?? unit);
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
}
