using BigBro.Core.Theming;

namespace BigBro.App.Editor;

/// <summary>
/// The theme being edited, its folder, and undo/redo. Undo works on whole-document JSON snapshots: simple, and cheap
/// next to the images (which live in files, not in the JSON).
/// </summary>
public sealed class EditorSession
{
    private const int MaxUndo = 100;
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private readonly ImageTable _images = new();
    private string _savedJson;

    public EditorSession(ThemeDocument document, string directory)
    {
        Document = document;
        Directory = directory;
        document.Directory = directory;
        _savedJson = ThemeJson.Snapshot(document, _images);
    }

    public ThemeDocument Document { get; private set; }
    public string Directory { get; private set; }
    public string JsonPath => Path.Combine(Directory, ThemeJson.ThemeFileName);

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool IsDirty => Snapshot() != _savedJson;

    /// <summary>Compact (images by reference) so it is cheap even for themes with many megabytes of pictures.</summary>
    public string Snapshot() => ThemeJson.Snapshot(Document, _images);

    /// <summary>Remember <paramref name="before"/> (taken with <see cref="Snapshot"/>) as an undo step if the document changed since.</summary>
    public bool RecordUndo(string before)
    {
        if (before == Snapshot()) return false;
        _undo.Push(before);
        if (_undo.Count > MaxUndo)
        {
            var kept = _undo.Reverse().Skip(_undo.Count - MaxUndo).ToArray();
            _undo.Clear();
            foreach (var s in kept) _undo.Push(s);
        }
        _redo.Clear();
        return true;
    }

    /// <summary>Runs a change and records it for undo.</summary>
    public bool Edit(Action change)
    {
        var before = Snapshot();
        change();
        return RecordUndo(before);
    }

    public void Undo() => Restore(_undo, _redo);
    public void Redo() => Restore(_redo, _undo);

    private void Restore(Stack<string> from, Stack<string> to)
    {
        if (from.Count == 0) return;
        to.Push(Snapshot());
        var target = ThemeJson.FromSnapshot(from.Pop(), _images);
        target.Directory = Directory;
        Document = target;
    }

    public void Save()
    {
        ThemeJson.Save(Document, JsonPath);
        _savedJson = Snapshot();
    }

    /// <summary>Saves a copy in a new folder under <paramref name="themesDirectory"/> and continues editing that copy.</summary>
    public void SaveAs(string themesDirectory, string name)
    {
        var folder = UniqueFolder(themesDirectory, name);
        BigBro.App.AppServices.CopyDirectory(Directory, folder);
        Directory = folder;
        Document.Name = name;
        Document.Directory = folder;
        Save();
    }

    public static string UniqueFolder(string themesDirectory, string name)
    {
        var baseName = BigBro.Core.Legacy.LegacyThemeConverter.SanitizeFolderName(name);
        var path = Path.Combine(themesDirectory, baseName);
        for (var i = 2; System.IO.Directory.Exists(path); i++)
            path = Path.Combine(themesDirectory, $"{baseName} ({i})");
        return path;
    }

    /// <summary>Creates a new empty theme folder and session.</summary>
    public static EditorSession CreateNew(string themesDirectory, int width = 1920, int height = 480)
    {
        var doc = new ThemeDocument
        {
            Name = "New theme",
            Author = Environment.UserName,
            Width = width,
            Height = height,
            BackgroundColor = ThemeColor.Black,
            TextColor = ThemeColor.White,
            UpdateInterval = 1000,
        };
        var folder = UniqueFolder(themesDirectory, doc.Name);
        System.IO.Directory.CreateDirectory(folder);
        var session = new EditorSession(doc, folder);
        session.Save();
        return session;
    }

    /// <summary>Reads an image file and returns the <c>data:</c> URI to store in the theme.</summary>
    public static string ImportImage(string sourceFile)
    {
        var bytes = File.ReadAllBytes(sourceFile);
        if (BigBro.Core.Legacy.ImageSniffer.Extension(bytes) == ".bin")
            throw new InvalidDataException("That file is not a PNG, JPEG, GIF, BMP or WebP image.");
        return ImageData.ToDataUri(bytes);
    }
}
