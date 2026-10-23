using Avalonia;
using BigBro.Core.Theming;

namespace BigBro.App.Rendering;

/// <summary>Geometry queries used by the theme editor and by the host window (to place YouTube web views).</summary>
public sealed partial class ThemeView
{
    /// <summary>Where <paramref name="element"/> sits on the panel, in theme pixels. Null if it isn't part of this theme.</summary>
    public Rect? AbsoluteBounds(ThemeElement element)
    {
        foreach (var root in _roots)
            if (Find(root, element, new Point(0, 0)) is { } r) return r;
        return null;
    }

    private Rect? Find(Node node, ThemeElement target, Point origin)
    {
        var size = ContentSize(node);
        var topLeft = new Point(origin.X + node.Element.X, origin.Y + node.Element.Y);
        if (ReferenceEquals(node.Element, target)) return new Rect(topLeft, size);
        foreach (var child in node.Children)
            if (Find(child, target, topLeft) is { } r) return r;
        return null;
    }

    /// <summary>The front-most visible element at <paramref name="point"/> (theme pixels), honouring the parent clipping.</summary>
    public ThemeElement? HitTest(Point point)
    {
        for (var i = _roots.Count - 1; i >= 0; i--)
            if (Hit(_roots[i], point, new Point(0, 0), null) is { } hit) return hit;
        return null;
    }

    private ThemeElement? Hit(Node node, Point point, Point origin, Rect? clip)
    {
        if (!node.Element.Visible) return null;
        var size = ContentSize(node);
        var topLeft = new Point(origin.X + node.Element.X, origin.Y + node.Element.Y);
        var rect = new Rect(topLeft, size);
        var visible = clip is { } c ? rect.Intersect(c) : rect;
        if (!visible.Contains(point)) return null;

        for (var i = node.Children.Count - 1; i >= 0; i--)
            if (Hit(node.Children[i], point, topLeft, visible) is { } child) return child;
        return node.Element;
    }

    /// <summary>YouTube elements to host as web views: element, panel rectangle and the video id to play.</summary>
    public IEnumerable<(YoutubeElement Element, Rect Bounds, string? VideoId)> YoutubeSlots()
    {
        foreach (var node in _all)
        {
            if (node.Element is not YoutubeElement y || !y.Visible || AbsoluteBounds(y) is not { } rect) continue;
            var id = node.Override?.YoutubeId is { Length: > 0 } o ? o : y.YoutubeId;
            yield return (y, rect, id);
        }
    }
}
