using System.Windows;
using Microsoft.Win32;
using PdfEdit.Helpers;
using PdfEdit.Services.Abstractions;

namespace PdfEdit.Services;

/// <summary>
/// Swaps <c>Themes/Colors.Light.xaml</c> and <c>Themes/Colors.Dark.xaml</c> at runtime.
/// <para>
/// The colour dictionary is always <c>MergedDictionaries[0]</c> and every style in
/// <c>Controls.xaml</c> references colours with <c>DynamicResource</c>, so replacing that one
/// entry repaints the whole app without recreating a single control.
/// </para>
/// </summary>
public sealed class ThemeService : IThemeService
{
    private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly Uri LightUri = new("pack://application:,,,/Themes/Colors.Light.xaml", UriKind.Absolute);
    private static readonly Uri DarkUri = new("pack://application:,,,/Themes/Colors.Dark.xaml", UriKind.Absolute);

    private bool _isSubscribedToSystem;

    public AppTheme CurrentTheme { get; private set; } = AppTheme.System;

    public bool IsDarkMode { get; private set; }

    public event EventHandler<bool>? ThemeChanged;

    public void Initialize(AppTheme theme = AppTheme.System) => SetTheme(theme);

    public void SetTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        SubscribeToSystemChanges(theme == AppTheme.System);
        Apply(theme == AppTheme.System ? IsSystemInDarkMode() : theme == AppTheme.Dark);
    }

    public void Toggle() => SetTheme(IsDarkMode ? AppTheme.Light : AppTheme.Dark);

    private void Apply(bool isDark)
    {
        var app = Application.Current;
        if (app is null)
            return;

        var dictionaries = app.Resources.MergedDictionaries;
        var replacement = new ResourceDictionary { Source = isDark ? DarkUri : LightUri };

        if (dictionaries.Count == 0)
            dictionaries.Add(replacement);
        else
            dictionaries[0] = replacement;

        IsDarkMode = isDark;

        // Repaint the non-client area of every open window so the title bar matches.
        foreach (var window in app.Windows.OfType<Window>())
            WindowBackdrop.SetImmersiveDarkMode(window, isDark);

        ThemeChanged?.Invoke(this, isDark);
    }

    /// <summary>Reads the Windows "Choose your default app mode" setting; defaults to light.</summary>
    private static bool IsSystemInDarkMode()
    {
        try
        {
            return Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1) is int value && value == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void SubscribeToSystemChanges(bool subscribe)
    {
        if (subscribe == _isSubscribedToSystem)
            return;

        if (subscribe)
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        else
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

        _isSubscribedToSystem = subscribe;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General || CurrentTheme != AppTheme.System)
            return;

        // SystemEvents fires on a dedicated thread; marshal back before touching resources.
        Application.Current?.Dispatcher.BeginInvoke(() => Apply(IsSystemInDarkMode()));
    }
}
