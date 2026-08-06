using CommunityToolkit.Mvvm.ComponentModel;

namespace PdfEdit.ViewModels;

/// <summary>One entry in the left navigation rail.</summary>
public sealed partial class NavigationItemViewModel : ObservableObject
{
    public NavigationItemViewModel(string glyph, ViewModelBase content)
    {
        Glyph = glyph;
        Content = content;
    }

    /// <summary>Segoe Fluent Icons code point, e.g. <c>\uE8A5</c>.</summary>
    public string Glyph { get; }

    /// <summary>The workspace shown when this entry is selected.</summary>
    public ViewModelBase Content { get; }

    public string Title => Content.Title;

    public string Description => Content.Description;

    [ObservableProperty]
    private bool _isSelected;

    public override string ToString() => Title;
}
