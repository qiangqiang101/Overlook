using System.Collections.ObjectModel;
using System.ComponentModel;
using BigBro.Core.Theming;

namespace BigBro.App.Editor;

/// <summary>One row of the element tree. Children are listed front-most first, like a layers panel.</summary>
public sealed class ElementNode : INotifyPropertyChanged
{
    public ElementNode(ThemeElement element)
    {
        Element = element;
        foreach (var child in Enumerable.Reverse(element.Children))
            Children.Add(new ElementNode(child));
    }

    public ThemeElement Element { get; }
    public ObservableCollection<ElementNode> Children { get; } = [];
    public bool IsExpanded { get; set; } = true;

    public string Header => $"{(Element.Visible ? "" : BigBro.Core.Settings.Loc.T("editor.hidden", "(hidden) "))}{Element.Name}  ·  {ElementFactory.LabelOf(Element)}";

    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Header)));

    public IEnumerable<ElementNode> Descendants()
    {
        yield return this;
        foreach (var c in Children)
            foreach (var d in c.Descendants())
                yield return d;
    }

    public static ObservableCollection<ElementNode> Build(ThemeDocument doc)
    {
        var list = new ObservableCollection<ElementNode>();
        foreach (var e in Enumerable.Reverse(doc.Elements)) list.Add(new ElementNode(e));
        return list;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
