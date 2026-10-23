using System.IO.MemoryMappedFiles;
using System.Text;

namespace BigBro.Core.Platform;

/// <summary>
/// Reads frame rate and frame time from MSI Afterburner / RivaTuner's "MAHMSharedMemory" block (Windows, same PC only).
/// Replaces the 1.x MSIAfterburnerNET dependency with a direct read of the documented structure.
/// </summary>
public sealed class AfterburnerReader : IDisposable
{
    private const string MapName = "MAHMSharedMemory";
    private const uint Signature = 0x4D41484D;          // 'MAHM'
    private const uint SourceFramerate = 0x50;
    private const uint SourceFrametime = 0x51;
    private const uint GlobalGpu = 0xFFFFFFFF;

    // header: signature, version, headerSize, numEntries, entrySize, time, numGpuEntries, gpuEntrySize
    // entry: 5 x char[260] names/units/format, float data, float min, float max, uint flags, uint gpu, uint srcId
    private const int NameBlock = 260 * 5;

    private MemoryMappedFile? _map;

    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>Latest values, or null when Afterburner isn't running or isn't showing the counters.</summary>
    public (double Fps, double FrameTimeMs)? Read()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            _map ??= MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
            using var view = _map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            return Parse(view);
        }
        catch (FileNotFoundException)
        {
            return null;                                  // Afterburner not running
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _map?.Dispose();
            _map = null;
            return null;
        }
    }

    internal static (double Fps, double FrameTimeMs)? Parse(UnmanagedMemoryAccessor view)
    {
        if (view.ReadUInt32(0) != Signature) return null;
        var headerSize = view.ReadInt32(8);
        var count = view.ReadInt32(12);
        var entrySize = view.ReadInt32(16);
        if (entrySize < NameBlock + 24 || count <= 0 || count > 4096) return null;

        double? fps = null, frameTime = null;
        for (var i = 0; i < count; i++)
        {
            var offset = (long)headerSize + (long)i * entrySize;
            if (offset + entrySize > view.Capacity) break;
            var data = view.ReadSingle(offset + NameBlock);
            var gpu = view.ReadUInt32(offset + NameBlock + 16);
            var source = view.ReadUInt32(offset + NameBlock + 20);
            if (gpu != GlobalGpu) continue;
            if (!float.IsFinite(data) || data >= 1e9f || data < 0) data = 0;   // MAHM uses FLT_MAX for "no value yet"
            if (source == SourceFramerate) fps = data;
            else if (source == SourceFrametime) frameTime = data;
        }
        if (fps is null && frameTime is null) return null;

        // Afterburner only publishes "Frametime" when it is ticked in its monitoring list. Without it, 1000 / fps is the average
        // frame time, which is what the counter reports anyway.
        var rate = fps ?? 0;
        var time = frameTime ?? 0;
        if (time <= 0 && rate > 0) time = 1000.0 / rate;
        return (rate, time);
    }

    public void Dispose() => _map?.Dispose();

    /// <summary>Builds a MAHM block in memory; used by tests.</summary>
    internal static byte[] BuildBlock(double fps, double frameTime, bool frameTimeEntry = true)
    {
        const int entrySize = NameBlock + 24;
        var buffer = new byte[32 + entrySize * 2];
        BitConverter.GetBytes(Signature).CopyTo(buffer, 0);
        BitConverter.GetBytes(0x20000).CopyTo(buffer, 4);
        BitConverter.GetBytes(32).CopyTo(buffer, 8);
        BitConverter.GetBytes(frameTimeEntry ? 2 : 1).CopyTo(buffer, 12);
        BitConverter.GetBytes(entrySize).CopyTo(buffer, 16);
        void Entry(int index, uint source, float value)
        {
            var o = 32 + index * entrySize + NameBlock;
            Encoding.ASCII.GetBytes("x").CopyTo(buffer, 32 + index * entrySize);
            BitConverter.GetBytes(value).CopyTo(buffer, o);
            BitConverter.GetBytes(GlobalGpu).CopyTo(buffer, o + 16);
            BitConverter.GetBytes(source).CopyTo(buffer, o + 20);
        }
        Entry(0, SourceFramerate, (float)fps);
        if (frameTimeEntry) Entry(1, SourceFrametime, (float)frameTime);
        return buffer;
    }
}
