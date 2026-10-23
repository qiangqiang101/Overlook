using BigBro.Core.Theming;

namespace BigBro.Core.Tests;

public class ThemeEntryTests
{
    private static string ThemesDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "BigBro.App", "Themes");
    }

    [Fact]
    public void Scanning_reads_headers_without_loading_any_theme()
    {
        var themes = ThemeStore.Scan(ThemesDirectory());
        Assert.NotEmpty(themes);
        Assert.All(themes, t => Assert.False(t.IsLoaded, $"{t.Name} was loaded by the scan"));
    }

    [Fact]
    public void Header_matches_what_a_full_load_reads()
    {
        foreach (var entry in ThemeStore.Scan(ThemesDirectory()))
        {
            var doc = ThemeJson.Load(entry.JsonPath);
            var header = entry.Header;
            Assert.Equal(doc.Name, header.Name);
            Assert.Equal(doc.Author, header.Author);
            Assert.Equal(doc.Version, header.Version);
            Assert.Equal(doc.Width, header.Width);
            Assert.Equal(doc.Height, header.Height);
            Assert.Equal(doc.UpdateInterval, header.UpdateInterval);
            Assert.Equal(doc.Tags, header.Tags);
            Assert.Equal(doc.Rgb is { Enabled: true }, header.HasRgb);
            Assert.Equal(ImageData.TryGetBytes(doc.Preview, doc)?.Length, header.PreviewBytes?.Length);
        }
    }

    [Fact]
    public void A_theme_loads_on_first_use_and_is_dropped_by_release()
    {
        var entry = ThemeStore.Scan(ThemesDirectory()).First();
        Assert.False(entry.IsLoaded);

        var doc = entry.Document;
        Assert.True(entry.IsLoaded);
        Assert.Same(doc, entry.Document);            // not read again while loaded

        entry.Release();
        Assert.False(entry.IsLoaded);
        Assert.NotSame(doc, entry.Document);         // a fresh copy comes back from disk on demand
    }

    [Fact]
    public void Header_reading_copes_with_a_huge_image_before_the_preview()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"overlook-header-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "theme.json");
            var bigImage = "data:image/png;base64," + new string('A', 3_000_000);      // far larger than the read buffer
            File.WriteAllText(path, $$"""
                {"formatVersion":2,"name":"Big","author":"me","version":"2.5","tags":["a","b"],"width":800,"height":480,
                 "backgroundColor":"#FF000000","textColor":"#FFFFFFFF","backgroundImage":"{{bigImage}}","preview":"data:image/png;base64,iVBORw0KGgo=",
                 "updateInterval":2000,"opacity":1,"rgb":{"enabled":true},"elements":[{"type":"text","name":"x"}]}
                """);

            var header = ThemeHeaderReader.Read(path);
            Assert.Equal("Big", header.Name);
            Assert.Equal("me", header.Author);
            Assert.Equal("2.5", header.Version);
            Assert.Equal(["a", "b"], header.Tags);
            Assert.Equal((800, 480, 2000), (header.Width, header.Height, header.UpdateInterval));
            Assert.True(header.HasRgb);
            Assert.NotNull(header.PreviewBytes);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Header_reading_rejects_a_newer_format()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"overlook-header-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "theme.json");
            File.WriteAllText(path, """{"formatVersion":99,"name":"Future","width":1,"height":1,"elements":[]}""");
            Assert.Throws<InvalidDataException>(() => ThemeHeaderReader.Read(path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
