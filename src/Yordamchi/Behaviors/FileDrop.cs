using System.IO;
using System.Windows;
using System.Windows.Input;

namespace Yordamchi.Behaviors;

/// <summary>
/// Attached behavior that turns any <see cref="UIElement"/> into an Explorer drop zone for PDFs and
/// images, forwarding the accepted paths to a view-model <see cref="ICommand"/>.
/// </summary>
/// <example>
/// <code language="xml">
/// &lt;Border behaviors:FileDrop.Command="{Binding AddFilesCommand}"
///         behaviors:FileDrop.Extensions=".pdf,.png,.jpg"&gt;
///   &lt;Border.Style&gt;
///     &lt;Style TargetType="Border"&gt;
///       &lt;Style.Triggers&gt;
///         &lt;Trigger Property="behaviors:FileDrop.IsDragOver" Value="True"&gt;
///           &lt;Setter Property="BorderBrush" Value="{DynamicResource AccentFillColorDefaultBrush}" /&gt;
///         &lt;/Trigger&gt;
///       &lt;/Style.Triggers&gt;
///     &lt;/Style&gt;
///   &lt;/Border.Style&gt;
/// &lt;/Border&gt;
/// </code>
/// </example>
public static class FileDrop
{
    // ---------------------------------------------------------------------------------------------
    // Command
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Identifies the <c>Command</c> attached property. Setting it wires the drop handlers up and sets
    /// <see cref="UIElement.AllowDrop"/>; clearing it unhooks them again.
    /// </summary>
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.RegisterAttached(
            "Command",
            typeof(ICommand),
            typeof(FileDrop),
            new PropertyMetadata(null, OnCommandChanged));

    /// <summary>Gets the command invoked with the accepted file paths when files are dropped.</summary>
    /// <param name="element">The drop zone.</param>
    /// <returns>The command, or <see langword="null"/>.</returns>
    [AttachedPropertyBrowsableForType(typeof(UIElement))]
    public static ICommand? GetCommand(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (ICommand?)element.GetValue(CommandProperty);
    }

    /// <summary>Sets the command invoked with a <see cref="string"/><c>[]</c> of accepted file paths on drop.</summary>
    /// <param name="element">The drop zone.</param>
    /// <param name="value">The command, or <see langword="null"/> to disable the drop zone.</param>
    public static void SetCommand(DependencyObject element, ICommand? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(CommandProperty, value);
    }

    // ---------------------------------------------------------------------------------------------
    // Extensions
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Identifies the <c>Extensions</c> attached property: a comma-separated, case-insensitive list such as
    /// <c>".pdf"</c> or <c>".jpg,.jpeg,.png"</c>. Empty or unset accepts every file.
    /// </summary>
    public static readonly DependencyProperty ExtensionsProperty =
        DependencyProperty.RegisterAttached(
            "Extensions",
            typeof(string),
            typeof(FileDrop),
            new PropertyMetadata(null, OnExtensionsChanged));

    /// <summary>Gets the comma-separated extension filter.</summary>
    /// <param name="element">The drop zone.</param>
    /// <returns>The filter string, or <see langword="null"/> when everything is accepted.</returns>
    [AttachedPropertyBrowsableForType(typeof(UIElement))]
    public static string? GetExtensions(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string?)element.GetValue(ExtensionsProperty);
    }

    /// <summary>Sets the comma-separated extension filter. A leading dot is optional on each entry.</summary>
    /// <param name="element">The drop zone.</param>
    /// <param name="value">The filter, e.g. <c>".jpg,.jpeg,.png"</c>.</param>
    public static void SetExtensions(DependencyObject element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ExtensionsProperty, value);
    }

    // ---------------------------------------------------------------------------------------------
    // IncludeFolders
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Identifies the <c>IncludeFolders</c> attached property. When <see langword="true"/>, a dropped
    /// directory is forwarded <em>as its own path</em> instead of being expanded into its files.
    /// </summary>
    /// <remarks>
    /// Off by default, because every PDF/image drop zone wants files. The archive page is the exception:
    /// zipping a folder has to keep the folder — expanding it here would flatten the structure the user
    /// dropped, and the extension filter would silently discard everything inside it.
    /// </remarks>
    public static readonly DependencyProperty IncludeFoldersProperty =
        DependencyProperty.RegisterAttached(
            "IncludeFolders",
            typeof(bool),
            typeof(FileDrop),
            new PropertyMetadata(false));

    /// <summary>Gets whether dropped directories are forwarded as directories.</summary>
    /// <param name="element">The drop zone.</param>
    /// <returns><see langword="true"/> when folders are passed through unexpanded.</returns>
    [AttachedPropertyBrowsableForType(typeof(UIElement))]
    public static bool GetIncludeFolders(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IncludeFoldersProperty);
    }

    /// <summary>Sets whether dropped directories are forwarded as directories rather than expanded.</summary>
    /// <param name="element">The drop zone.</param>
    /// <param name="value"><see langword="true"/> to pass folders through unexpanded.</param>
    public static void SetIncludeFolders(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IncludeFoldersProperty, value);
    }

    // ---------------------------------------------------------------------------------------------
    // IsDragOver (read-only)
    // ---------------------------------------------------------------------------------------------

    private static readonly DependencyPropertyKey IsDragOverPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly(
            "IsDragOver",
            typeof(bool),
            typeof(FileDrop),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the read-only <c>IsDragOver</c> attached property: <see langword="true"/> while an
    /// <em>acceptable</em> file drag hovers the zone. Intended for style triggers.
    /// </summary>
    public static readonly DependencyProperty IsDragOverProperty = IsDragOverPropertyKey.DependencyProperty;

    /// <summary>Gets whether an acceptable file drag is currently hovering <paramref name="element"/>.</summary>
    /// <param name="element">The drop zone.</param>
    /// <returns><see langword="true"/> while hovering with matching files.</returns>
    [AttachedPropertyBrowsableForType(typeof(UIElement))]
    public static bool GetIsDragOver(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsDragOverProperty);
    }

    private static void SetIsDragOver(DependencyObject element, bool value)
        => element.SetValue(IsDragOverPropertyKey, value);

    // ---------------------------------------------------------------------------------------------
    // Private state
    // ---------------------------------------------------------------------------------------------

    /// <summary>Parsed <see cref="ExtensionsProperty"/>, rebuilt only when the filter string changes.</summary>
    private static readonly DependencyProperty AcceptedExtensionsProperty =
        DependencyProperty.RegisterAttached(
            "AcceptedExtensions",
            typeof(HashSet<string>),
            typeof(FileDrop),
            new PropertyMetadata(null));

    /// <summary>Whether the drag that entered the zone was accepted; avoids re-scanning on every DragOver.</summary>
    private static readonly DependencyProperty IsCurrentDragAcceptedProperty =
        DependencyProperty.RegisterAttached(
            "IsCurrentDragAccepted",
            typeof(bool),
            typeof(FileDrop),
            new PropertyMetadata(false));

    // ---------------------------------------------------------------------------------------------
    // Wiring
    // ---------------------------------------------------------------------------------------------

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
            return;

        if (e.OldValue is null && e.NewValue is not null)
        {
            element.AllowDrop = true;
            element.DragEnter += OnDragEnter;
            element.DragOver += OnDragOver;
            element.DragLeave += OnDragLeave;
            element.Drop += OnDrop;
        }
        else if (e.OldValue is not null && e.NewValue is null)
        {
            element.DragEnter -= OnDragEnter;
            element.DragOver -= OnDragOver;
            element.DragLeave -= OnDragLeave;
            element.Drop -= OnDrop;
            element.AllowDrop = false;
            SetIsDragOver(element, false);
            element.SetValue(IsCurrentDragAcceptedProperty, false);
        }
    }

    private static void OnExtensionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Parsed once here rather than on every DragOver, which fires dozens of times a second.
        d.SetValue(AcceptedExtensionsProperty, ParseExtensions(e.NewValue as string));
    }

    private static HashSet<string>? ParseExtensions(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var extension = part.StartsWith('.') ? part : "." + part;
            set.Add(extension);
        }

        return set.Count == 0 ? null : set;
    }

    // ---------------------------------------------------------------------------------------------
    // Drag events
    // ---------------------------------------------------------------------------------------------

    private static void OnDragEnter(object sender, DragEventArgs e)
    {
        if (sender is not UIElement element)
            return;

        var accepted = CollectFiles(e.Data, element).Length > 0;
        element.SetValue(IsCurrentDragAcceptedProperty, accepted);
        SetIsDragOver(element, accepted);
        ApplyEffects(e, accepted);
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        if (sender is not UIElement element)
            return;

        // Reuse the DragEnter verdict: the payload cannot change mid-drag, and enumerating a dropped
        // directory on every mouse move would hit the disk continuously.
        var accepted = element.GetValue(IsCurrentDragAcceptedProperty) is true;
        ApplyEffects(e, accepted);
    }

    private static void OnDragLeave(object sender, DragEventArgs e)
    {
        if (sender is not UIElement element)
            return;

        SetIsDragOver(element, false);
        element.SetValue(IsCurrentDragAcceptedProperty, false);
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not UIElement element)
            return;

        SetIsDragOver(element, false);
        element.SetValue(IsCurrentDragAcceptedProperty, false);

        var files = CollectFiles(e.Data, element);
        if (files.Length == 0)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;

        var command = GetCommand(element);
        if (command?.CanExecute(files) == true)
            command.Execute(files);
    }

    private static void ApplyEffects(DragEventArgs e, bool accepted)
    {
        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;

        // Only claim the drags we can actually handle; anything else keeps bubbling (a reorder drag from
        // DragDropReorder passes straight through this zone).
        if (accepted)
            e.Handled = true;
    }

    // ---------------------------------------------------------------------------------------------
    // Payload
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Extracts the accepted, existing, absolute, de-duplicated file paths from a drag payload, preserving
    /// the order Explorer supplied.
    /// </summary>
    private static string[] CollectFiles(IDataObject? data, DependencyObject element)
    {
        // Only DataFormats.FileDrop: a DragDropReorder payload uses a private format and must fall through.
        if (data is null || !data.GetDataPresent(DataFormats.FileDrop))
            return [];

        if (data.GetData(DataFormats.FileDrop) is not string[] dropped || dropped.Length == 0)
            return [];

        var filter = element.GetValue(AcceptedExtensionsProperty) as HashSet<string>;
        var includeFolders = GetIncludeFolders(element);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var accepted = new List<string>(dropped.Length);

        foreach (var entry in dropped)
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            string full;
            try
            {
                full = Path.GetFullPath(entry);
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException or IOException or System.Security.SecurityException)
            {
                continue;
            }

            if (Directory.Exists(full))
            {
                // Opted in (archive page): the folder itself is the payload, so no filter and no walk.
                if (includeFolders)
                {
                    if (seen.Add(full))
                        accepted.Add(full);

                    continue;
                }

                // Directories expand to their immediate children only (non-recursive). Dropping a folder
                // of scans is the common case; walking an arbitrarily deep tree from the UI thread would
                // stall the drop and can pull in thousands of unintended files.
                try
                {
                    foreach (var file in Directory.EnumerateFiles(full))
                        TryAdd(file, filter, seen, accepted);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Unreadable folder: skip it rather than failing the whole drop.
                }
            }
            else if (File.Exists(full))
            {
                TryAdd(full, filter, seen, accepted);
            }
        }

        return accepted.Count == 0 ? [] : accepted.ToArray();
    }

    private static void TryAdd(string path, HashSet<string>? filter, HashSet<string> seen, List<string> accepted)
    {
        if (filter is not null && !filter.Contains(Path.GetExtension(path)))
            return;

        if (seen.Add(path))
            accepted.Add(path);
    }
}
