namespace PdfEdit.Services.Abstractions;

/// <summary>Theme the user picked in the app (not necessarily the effective one).</summary>
public enum AppTheme
{
    /// <summary>Follow the Windows "Choose your default app mode" setting.</summary>
    System,
    Light,
    Dark
}

/// <summary>
/// Owns the Light/Dark resource dictionary swap and tells windows when to repaint their
/// title bar. Implemented by <c>PdfEdit.Services.ThemeService</c>.
/// </summary>
public interface IThemeService
{
    AppTheme CurrentTheme { get; }

    /// <summary>The theme actually in effect after resolving <see cref="AppTheme.System"/>.</summary>
    bool IsDarkMode { get; }

    /// <summary>Raised after the dictionaries have been swapped; the payload is <see cref="IsDarkMode"/>.</summary>
    event EventHandler<bool>? ThemeChanged;

    /// <summary>Applies the startup theme. Call once, before the main window is shown.</summary>
    void Initialize(AppTheme theme = AppTheme.System);

    void SetTheme(AppTheme theme);

    /// <summary>Flips between explicit Light and Dark (leaving <see cref="AppTheme.System"/> behind).</summary>
    void Toggle();
}
