using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using BigBro.App.Rendering;
using BigBro.Core.Theming;

namespace BigBro.App.Editor;

/// <summary>
/// Transparent layer over the preview: click to select, drag to move, drag the handles to resize.
/// Coordinates are theme pixels (the preview is not pixel-scaled in the editor).
/// </summary>
public sealed class SelectionLayer : Control
{
    private enum Handle { None, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left, Body }

    private readonly Func<ThemeView?> _view;
    private Handle _drag = Handle.None;
    private Point _dragStart;
    private (int X, int Y, int W, int H) _original;
    private Point _parentOrigin;
    private bool _moved;
    private ThemeElement? _hover;

    public SelectionLayer(Func<ThemeView?> view)
    {
        _view = view;
        Focusable = true;
    }

    public ThemeElement? Selected { get; set; }
    public double Zoom { get; set; } = 1;
    /// <summary>Grid spacing in theme pixels; elements snap to it while dragging when <see cref="SnapToGrid"/> is on.</summary>
    public int GridSize { get; set; } = 10;
    public bool SnapToGrid { get; set; }
    public bool ShowGrid { get; set; }

    /// <summary>The user clicked an element (or empty space: null).</summary>
    public event Action<ThemeElement?>? SelectionRequested;
    /// <summary>A drag is about to change the selected element; take the undo snapshot now.</summary>
    public event Action? DragStarted;
    /// <summary>The element changed during a drag (repaint, update the property panel).</summary>
    public event Action? DragChanged;
    public event Action<bool>? DragCompleted;

    public override void Render(DrawingContext context)
    {
        // an (almost) invisible fill so the layer receives pointer events everywhere
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));

        var view = _view();
        if (view is null) return;

        if (ShowGrid && GridSize > 0 && GridSize * Zoom >= 4)
        {
            var dot = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255));
            var dotRadius = Math.Max(0.7, 1.1 / Zoom);
            for (var x = 0; x <= Bounds.Width; x += GridSize)
                for (var y = 0; y <= Bounds.Height; y += GridSize)
                    context.DrawEllipse(dot, null, new Point(x, y), dotRadius, dotRadius);
        }

        if (_hover is not null && _hover != Selected && view.AbsoluteBounds(_hover) is { } h)
            context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(160, 90, 170, 255)), 1 / Zoom), h);

        if (Selected is null || view.AbsoluteBounds(Selected) is not { } r) return;

        var pen = new Pen(new SolidColorBrush(Color.FromRgb(255, 170, 0)), 1.5 / Zoom, new DashStyle([4, 3], 0));
        context.DrawRectangle(null, pen, r);

        var s = 8 / Zoom;
        var fill = new SolidColorBrush(Colors.White);
        var edge = new Pen(new SolidColorBrush(Color.FromRgb(255, 170, 0)), 1 / Zoom);
        foreach (var handle in Handles)
        {
            var c = HandleCenter(r, handle);
            context.DrawRectangle(fill, edge, new Rect(c.X - s / 2, c.Y - s / 2, s, s));
        }
    }

    private static readonly Handle[] Handles =
        [Handle.TopLeft, Handle.Top, Handle.TopRight, Handle.Right, Handle.BottomRight, Handle.Bottom, Handle.BottomLeft, Handle.Left];

    private static Point HandleCenter(Rect r, Handle h) => h switch
    {
        Handle.TopLeft => r.TopLeft,
        Handle.Top => new Point(r.Center.X, r.Top),
        Handle.TopRight => r.TopRight,
        Handle.Right => new Point(r.Right, r.Center.Y),
        Handle.BottomRight => r.BottomRight,
        Handle.Bottom => new Point(r.Center.X, r.Bottom),
        Handle.BottomLeft => r.BottomLeft,
        _ => new Point(r.Left, r.Center.Y),
    };

    private Handle HitHandle(Point p)
    {
        if (Selected is null || _view()?.AbsoluteBounds(Selected) is not { } r) return Handle.None;
        var reach = 7 / Zoom;
        foreach (var h in Handles)
        {
            var c = HandleCenter(r, h);
            if (Math.Abs(p.X - c.X) <= reach && Math.Abs(p.Y - c.Y) <= reach) return h;
        }
        return Handle.None;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        var p = e.GetPosition(this);

        var handle = HitHandle(p);
        if (handle == Handle.None)
        {
            var hit = _view()?.HitTest(p);
            Selected = hit;
            SelectionRequested?.Invoke(hit);
            if (hit is null) { InvalidateVisual(); return; }
            handle = Handle.Body;
        }

        _drag = handle;
        _dragStart = p;
        _moved = false;
        _original = (Selected!.X, Selected.Y, Selected.Width, Selected.Height);
        var absolute = _view()?.AbsoluteBounds(Selected);
        _parentOrigin = absolute is { } a ? new Point(a.X - Selected.X, a.Y - Selected.Y) : default;
        DragStarted?.Invoke();
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);

        if (_drag == Handle.None || Selected is null)
        {
            var hover = _view()?.HitTest(p);
            if (hover != _hover) { _hover = hover; InvalidateVisual(); }
            Cursor = HitHandle(p) switch
            {
                Handle.TopLeft or Handle.BottomRight => new Cursor(StandardCursorType.TopLeftCorner),
                Handle.TopRight or Handle.BottomLeft => new Cursor(StandardCursorType.TopRightCorner),
                Handle.Top or Handle.Bottom => new Cursor(StandardCursorType.SizeNorthSouth),
                Handle.Left or Handle.Right => new Cursor(StandardCursorType.SizeWestEast),
                _ => hover is null ? null : new Cursor(StandardCursorType.SizeAll),
            };
            return;
        }

        var dx = (int)Math.Round(p.X - _dragStart.X);
        var dy = (int)Math.Round(p.Y - _dragStart.Y);
        if (dx == 0 && dy == 0 && !_moved) return;
        _moved = true;

        var (x, y, w, h) = _original;
        switch (_drag)
        {
            case Handle.Body: x += dx; y += dy; break;
            case Handle.Left: x += dx; w -= dx; break;
            case Handle.Right: w += dx; break;
            case Handle.Top: y += dy; h -= dy; break;
            case Handle.Bottom: h += dy; break;
            case Handle.TopLeft: x += dx; w -= dx; y += dy; h -= dy; break;
            case Handle.TopRight: w += dx; y += dy; h -= dy; break;
            case Handle.BottomLeft: x += dx; w -= dx; h += dy; break;
            case Handle.BottomRight: w += dx; h += dy; break;
        }

        if (SnapToGrid && GridSize > 1) (x, y, w, h) = Snap(x, y, w, h, _drag);

        const int min = 4;
        if (w < min) { if (_drag is Handle.Left or Handle.TopLeft or Handle.BottomLeft) x -= min - w; w = min; }
        if (h < min) { if (_drag is Handle.Top or Handle.TopLeft or Handle.TopRight) y -= min - h; h = min; }

        (Selected.X, Selected.Y, Selected.Width, Selected.Height) = (x, y, w, h);
        DragChanged?.Invoke();
        InvalidateVisual();
    }

    /// <summary>Snaps the edges that are being dragged to the grid (in panel coordinates, so children line up too).</summary>
    private (int X, int Y, int W, int H) Snap(int x, int y, int w, int h, Handle handle)
    {
        int Grid(double panelCoordinate) => (int)(Math.Round(panelCoordinate / GridSize) * GridSize);
        int px = (int)_parentOrigin.X, py = (int)_parentOrigin.Y;

        var moveLeft = handle is Handle.Left or Handle.TopLeft or Handle.BottomLeft;
        var moveRight = handle is Handle.Right or Handle.TopRight or Handle.BottomRight;
        var moveTop = handle is Handle.Top or Handle.TopLeft or Handle.TopRight;
        var moveBottom = handle is Handle.Bottom or Handle.BottomLeft or Handle.BottomRight;

        if (handle == Handle.Body)
        {
            x = Grid(px + x) - px;
            y = Grid(py + y) - py;
            return (x, y, w, h);
        }

        int right = x + w, bottom = y + h;
        if (moveLeft) x = Grid(px + x) - px;
        if (moveRight) right = Grid(px + right) - px;
        if (moveTop) y = Grid(py + y) - py;
        if (moveBottom) bottom = Grid(py + bottom) - py;
        return (x, y, right - x, bottom - y);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag == Handle.None) return;
        _drag = Handle.None;
        e.Pointer.Capture(null);
        DragCompleted?.Invoke(_moved);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover is not null) { _hover = null; InvalidateVisual(); }
    }
}
