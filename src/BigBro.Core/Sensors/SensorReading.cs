namespace BigBro.Core.Sensors;

/// <summary>One sensor value in every shape the elements need it.</summary>
public readonly record struct SensorReading
{
    public static readonly SensorReading None = new();

    /// <summary>Full display text with unit, as shown by a text label ("46.5 °C").</summary>
    public string Text { get; init; }
    /// <summary>Display text without unit, as shown in the value column of a custom text ("46").</summary>
    public string Value { get; init; }
    /// <summary>Numeric value for bars, charts and the like.</summary>
    public double Number { get; init; }
    public double Min { get; init; }
    /// <summary>Upper end of the scale; null when there is no natural one (clock speeds, throughput) and the element should track the largest value seen.</summary>
    public double? Max { get; init; }
    /// <summary>Suffix used by charts when they draw the current value ("°C", "%", " MHz").</summary>
    public string ChartUnit { get; init; }
    /// <summary>Position on the 0..1 scale used to pick a dynamic image, when the sensor has one.</summary>
    public double? Fraction { get; init; }
    /// <summary>Dynamic image index to use as is (weather code).</summary>
    public int? ImageIndex { get; init; }

    public SensorReading()
    {
        Text = "";
        Value = "";
        ChartUnit = "";
    }
}
