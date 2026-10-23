namespace BigBro.Core.Theming;

/// <summary>
/// What an element's value comes from: nothing, a LibreHardwareMonitor sensor (addressed by its tree path in
/// <see cref="SensorBinding.Param"/>), or one of the few values that aren't a single hardware sensor.
/// </summary>
public enum SensorKind
{
    None,

    /// <summary>A LibreHardwareMonitor sensor. <c>param</c> is its tree path, e.g. <c>ROGPC2/Intel Core i5-14600K/Load/CPU Total</c>, or an LHM sensor id.</summary>
    Lhm,

    CpuCoreCount,
    RamTotal,
    RamClockSpeed,
    RamVirtualTotal,
    HddTotalSize,
    HddTotalFreeSpace,
    HddUsage,
    Ping,
    LongDate,
    ShortDate,
    LongTime,
    ShortTime,
    CustomDateTime,
    SystemUptime,
    DisplayScreenResolution,
    DisplayRefreshRate,
    DisplayFramerate,
    DisplayFrametime,
    WeatherTempC,
    WeatherTempF,
    WeatherName,
    WeatherCode,
    WeatherWindSpeedKmh,
    WeatherWindSpeedMph,
    WeatherWindDirection,
}

/// <summary>The 1.x "detail sensor" list controls: one column of names and one of values.</summary>
public enum DetailGroup
{
    None,
    CpuClockSpeed,
    CpuTemperature,
    CpuLoad,
    CpuPowerWattage,
    GpuClockSpeed,
    GpuTemperature,
    GpuLoadPercent,
    MoboTemperature,
    MoboFan,
}

public enum ContentAlign
{
    TopLeft, TopCenter, TopRight,
    MiddleLeft, MiddleCenter, MiddleRight,
    BottomLeft, BottomCenter, BottomRight,
}

public enum BorderKind { None, FixedSingle, Fixed3D }

public enum ImageLayoutKind { None, Tile, Center, Stretch, Zoom }

public enum ImageSizeMode { Normal, StretchImage, AutoSize, CenterImage, Zoom }

public enum FillDirection { LeftToRight, RightToLeft, DownToUp, UpToDown }

public enum CircularStyle { Style1, Style2, Style3 }

public enum ProgressShape { Flat, Round, Arrow, RoundAnchor, ArrowAnchor }

public enum GradientDirection { Horizontal, Vertical, ForwardDiagonal, BackwardDiagonal }

public enum CircularTextMode { None, Value, Text }

public enum ChartScaleMode { Absolute, Relative }

public enum LineDash { Solid, Dash, Dot, DashDot, DashDotDot, Custom }

public enum ChartBorder
{
    None, Adjust, Bump, Etched, Flat, Raised, RaisedInner, RaisedOuter, Sunken, SunkenInner, SunkenOuter,
}


