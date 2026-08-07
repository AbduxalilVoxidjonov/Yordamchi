using CommunityToolkit.Mvvm.ComponentModel;

namespace Yordamchi.ViewModels;

/// <summary>Chap yon paneldagi bitta bo'lim.</summary>
public sealed partial class NavigationItemViewModel : ObservableObject
{
    public NavigationItemViewModel(string glyph, ViewModelBase content)
    {
        Glyph = glyph;
        Content = content;
    }

    /// <summary>Segoe Fluent Icons belgisi, masalan <c></c>.</summary>
    public string Glyph { get; }

    /// <summary>Bo'lim tanlanganda ko'rsatiladigan sahifa.</summary>
    public ViewModelBase Content { get; }

    public string Title => Content.Title;

    public string Description => Content.Description;

    [ObservableProperty]
    private bool _isSelected;

    public override string ToString() => Title;
}
