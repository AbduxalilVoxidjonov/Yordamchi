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

    /// <summary>
    /// Bo'lim yonidagi kichik nuqta: e'tibor talab qiladigan holat bor (masalan yangi versiya
    /// chiqqan). Ataylab shovqinsiz — bannerdan farqli o'laroq u foydalanuvchining ishini
    /// to'xtatmaydi, lekin "Dastur haqida" ga kirmasdan ham sezilib turadi.
    /// </summary>
    [ObservableProperty]
    private bool _hasNotification;

    public override string ToString() => Title;
}
