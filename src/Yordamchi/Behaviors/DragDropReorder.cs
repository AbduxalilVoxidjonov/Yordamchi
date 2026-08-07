using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Yordamchi.Behaviors;

/// <summary>
/// Attached behavior that turns any <see cref="ItemsControl"/> — in this app a <see cref="ListBox"/> whose
/// items panel is a horizontal <see cref="WrapPanel"/> — into a drag-to-reorder surface, without a
/// dependency on <c>Microsoft.Xaml.Behaviors</c>.
/// </summary>
/// <example>
/// <code language="xml">
/// &lt;ListBox behaviors:DragDropReorder.IsEnabled="True"
///          behaviors:DragDropReorder.AutoScroll="True" /&gt;
/// </code>
/// </example>
public static class DragDropReorder
{
    /// <summary>
    /// Private clipboard format for the reorder payload.
    /// <para>
    /// Deliberately <em>not</em> <see cref="DataFormats.FileDrop"/>: the same visual tree also carries
    /// <see cref="FileDrop"/> for PDFs dragged in from Explorer, and drag events bubble. Using a private
    /// format lets each handler recognise its own drags and ignore — without handling — everybody else's.
    /// </para>
    /// </summary>
    private const string ReorderFormat = "Yordamchi.ReorderItem";

    /// <summary>Distance from the top/bottom edge of the scroll viewer that triggers auto-scrolling.</summary>
    private const double AutoScrollHotZone = 40d;

    /// <summary>Maximum pixels scrolled per timer tick, reached at the very edge of the hot zone.</summary>
    private const double AutoScrollMaxStep = 16d;

    /// <summary>Cached <c>Move(int, int)</c> lookups, keyed by the concrete collection type.</summary>
    private static readonly Dictionary<Type, MethodInfo?> MoveMethodCache = [];

    private static readonly object MoveMethodCacheLock = new();

    // ---------------------------------------------------------------------------------------------
    // IsEnabled
    // ---------------------------------------------------------------------------------------------

    /// <summary>Identifies the <c>IsEnabled</c> attached property: master switch for the behavior.</summary>
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(DragDropReorder),
            new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Gets whether drag reordering is enabled on <paramref name="element"/>.</summary>
    /// <param name="element">The items control.</param>
    /// <returns><see langword="true"/> when the behavior is wired up.</returns>
    [AttachedPropertyBrowsableForType(typeof(ItemsControl))]
    public static bool GetIsEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsEnabledProperty);
    }

    /// <summary>Enables or disables drag reordering on <paramref name="element"/>.</summary>
    /// <param name="element">The items control.</param>
    /// <param name="value"><see langword="true"/> to wire the behavior up, <see langword="false"/> to unhook it completely.</param>
    public static void SetIsEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsEnabledProperty, value);
    }

    // ---------------------------------------------------------------------------------------------
    // AutoScroll
    // ---------------------------------------------------------------------------------------------

    /// <summary>Identifies the <c>AutoScroll</c> attached property. Defaults to <see langword="true"/>.</summary>
    public static readonly DependencyProperty AutoScrollProperty =
        DependencyProperty.RegisterAttached(
            "AutoScroll",
            typeof(bool),
            typeof(DragDropReorder),
            new PropertyMetadata(true));

    /// <summary>Gets whether the host scroll viewer scrolls while the pointer hovers near its edges during a drag.</summary>
    /// <param name="element">The items control.</param>
    /// <returns><see langword="true"/> when auto-scrolling is enabled.</returns>
    [AttachedPropertyBrowsableForType(typeof(ItemsControl))]
    public static bool GetAutoScroll(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(AutoScrollProperty);
    }

    /// <summary>Sets whether the host scroll viewer scrolls while the pointer hovers near its edges during a drag.</summary>
    /// <param name="element">The items control.</param>
    /// <param name="value"><see langword="true"/> to enable auto-scrolling.</param>
    public static void SetAutoScroll(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(AutoScrollProperty, value);
    }

    // ---------------------------------------------------------------------------------------------
    // IsDraggedItem (read-only, set on the item container)
    // ---------------------------------------------------------------------------------------------

    private static readonly DependencyPropertyKey IsDraggedItemPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly(
            "IsDraggedItem",
            typeof(bool),
            typeof(DragDropReorder),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the read-only <c>IsDraggedItem</c> attached property, set on the container currently being
    /// dragged so a style trigger can dim it (<c>Opacity 0.4</c>).
    /// </summary>
    public static readonly DependencyProperty IsDraggedItemProperty = IsDraggedItemPropertyKey.DependencyProperty;

    /// <summary>Gets whether <paramref name="element"/> is the container currently being dragged.</summary>
    /// <param name="element">The item container.</param>
    /// <returns><see langword="true"/> while this container is the drag source.</returns>
    [AttachedPropertyBrowsableForType(typeof(UIElement))]
    public static bool GetIsDraggedItem(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsDraggedItemProperty);
    }

    private static void SetIsDraggedItem(DependencyObject element, bool value)
        => element.SetValue(IsDraggedItemPropertyKey, value);

    // ---------------------------------------------------------------------------------------------
    // IsDragActive (read-only, set on the items control)
    // ---------------------------------------------------------------------------------------------

    private static readonly DependencyPropertyKey IsDragActivePropertyKey =
        DependencyProperty.RegisterAttachedReadOnly(
            "IsDragActive",
            typeof(bool),
            typeof(DragDropReorder),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the read-only <c>IsDragActive</c> attached property, set on the items control for the
    /// duration of a reorder drag (useful to suppress hover chrome across all cards at once).
    /// </summary>
    public static readonly DependencyProperty IsDragActiveProperty = IsDragActivePropertyKey.DependencyProperty;

    /// <summary>Gets whether a reorder drag is in progress on <paramref name="element"/>.</summary>
    /// <param name="element">The items control.</param>
    /// <returns><see langword="true"/> between the drag starting and its cleanup.</returns>
    [AttachedPropertyBrowsableForType(typeof(ItemsControl))]
    public static bool GetIsDragActive(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsDragActiveProperty);
    }

    private static void SetIsDragActive(DependencyObject element, bool value)
        => element.SetValue(IsDragActivePropertyKey, value);

    // ---------------------------------------------------------------------------------------------
    // Per-host state
    // ---------------------------------------------------------------------------------------------

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(DragState),
            typeof(DragDropReorder),
            new PropertyMetadata(null));

    /// <summary>Mutable per-<see cref="ItemsControl"/> drag bookkeeping, stored on the control itself so it dies with it.</summary>
    private sealed class DragState
    {
        /// <summary>Pointer position at mouse-down, in items-control coordinates; origin for the drag threshold.</summary>
        public Point Origin;

        /// <summary>The data item under the pointer at mouse-down, armed but not yet dragging.</summary>
        public object? PendingItem;

        /// <summary>The container for <see cref="PendingItem"/>.</summary>
        public DependencyObject? PendingContainer;

        /// <summary>The container the in-flight drag started from; kept separately so it survives pending-state resets.</summary>
        public DependencyObject? DraggedContainer;

        /// <summary><see langword="true"/> between <c>DoDragDrop</c> starting and its cleanup; guards reentrancy.</summary>
        public bool IsDragging;

        /// <summary>The host scroll viewer, resolved lazily on the first <c>DragOver</c>.</summary>
        public ScrollViewer? ScrollViewer;

        /// <summary>Auto-scroll ticker; null when not scrolling.</summary>
        public DispatcherTimer? ScrollTimer;

        /// <summary>Signed pixels to scroll per tick.</summary>
        public double ScrollStep;

        /// <summary><see langword="true"/> when the input handlers are currently attached.</summary>
        public bool Hooked;
    }

    private static DragState GetState(ItemsControl host)
    {
        if (host.GetValue(StateProperty) is DragState state)
            return state;

        state = new DragState();
        host.SetValue(StateProperty, state);
        return state;
    }

    // ---------------------------------------------------------------------------------------------
    // Wiring
    // ---------------------------------------------------------------------------------------------

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl host)
            return;

        if (e.NewValue is true)
        {
            host.Loaded += OnHostLoaded;
            host.Unloaded += OnHostUnloaded;
            Hook(host);
        }
        else
        {
            host.Loaded -= OnHostLoaded;
            host.Unloaded -= OnHostUnloaded;
            Unhook(host);
        }
    }

    private static void OnHostLoaded(object sender, RoutedEventArgs e)
    {
        // Re-arm after the control comes back (tab switch, template re-apply).
        if (sender is ItemsControl host && GetIsEnabled(host))
            Hook(host);
    }

    private static void OnHostUnloaded(object sender, RoutedEventArgs e)
    {
        // Unhook on unload so nothing — most importantly the dispatcher-rooted auto-scroll timer —
        // outlives the visual tree. OnHostLoaded restores the hooks if the control is shown again.
        if (sender is ItemsControl host)
            Unhook(host);
    }

    private static void Hook(ItemsControl host)
    {
        var state = GetState(host);
        if (state.Hooked)
            return;

        host.AllowDrop = true;
        host.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        host.PreviewMouseMove += OnPreviewMouseMove;
        host.DragOver += OnDragOver;
        host.DragLeave += OnDragLeave;
        host.Drop += OnDrop;
        host.QueryContinueDrag += OnQueryContinueDrag;
        host.GiveFeedback += OnGiveFeedback;
        state.Hooked = true;
    }

    private static void Unhook(ItemsControl host)
    {
        var state = GetState(host);

        host.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        host.PreviewMouseMove -= OnPreviewMouseMove;
        host.DragOver -= OnDragOver;
        host.DragLeave -= OnDragLeave;
        host.Drop -= OnDrop;
        host.QueryContinueDrag -= OnQueryContinueDrag;
        host.GiveFeedback -= OnGiveFeedback;
        state.Hooked = false;

        EndDrag(host, state);
    }

    // ---------------------------------------------------------------------------------------------
    // Mouse: arming and starting the drag
    // ---------------------------------------------------------------------------------------------

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ItemsControl host)
            return;

        var state = GetState(host);
        state.PendingItem = null;
        state.PendingContainer = null;

        if (state.IsDragging)
            return;

        var container = FindContainer(host, e.OriginalSource as DependencyObject);
        if (container is null)
            return;

        // The page cards float Delete/Rotate buttons on top of the thumbnail. Walking from the actual hit
        // element up to the container tells us whether the press landed on interactive chrome; if it did,
        // arming a drag would swallow the click.
        if (IsInteractiveChrome(e.OriginalSource as DependencyObject, container))
            return;

        var item = host.ItemContainerGenerator.ItemFromContainer(container);
        if (item is null || item == DependencyProperty.UnsetValue)
            return;

        state.Origin = e.GetPosition(host);
        state.PendingItem = item;
        state.PendingContainer = container;
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not ItemsControl host)
            return;

        var state = GetState(host);

        // The button may have been released over another window, in which case no MouseUp reaches us.
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            state.PendingItem = null;
            state.PendingContainer = null;
            return;
        }

        if (state.IsDragging || state.PendingItem is null || state.PendingContainer is not UIElement source)
            return;

        var position = e.GetPosition(host);
        if (Math.Abs(position.X - state.Origin.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - state.Origin.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        BeginDrag(host, state, source, state.PendingItem);
    }

    private static void BeginDrag(ItemsControl host, DragState state, UIElement source, object item)
    {
        state.IsDragging = true;
        state.DraggedContainer = source;
        state.ScrollViewer = FindScrollViewer(host);

        SetIsDragActive(host, true);
        SetIsDraggedItem(source, true);

        var data = new DataObject(ReorderFormat, item);

        try
        {
            // Blocking: the nested message loop pumps DragOver/Drop before this returns.
            DragDrop.DoDragDrop(source, data, DragDropEffects.Move);
        }
        catch (COMException)
        {
            // The shell occasionally refuses to start a drag (RPC_E_CHANGED_MODE / DRAGDROP_E_*).
            // Nothing to recover, but the finally below must still restore the visuals.
        }
        finally
        {
            // Runs for every exit path: successful drop, drop on a foreign window, Esc, or an exception.
            EndDrag(host, state);
        }
    }

    /// <summary>Restores every transient visual/state change made by a drag. Idempotent.</summary>
    private static void EndDrag(ItemsControl host, DragState state)
    {
        StopAutoScroll(state);
        InsertionAdorner.Detach();

        if (state.DraggedContainer is not null)
            SetIsDraggedItem(state.DraggedContainer, false);

        SetIsDragActive(host, false);

        state.IsDragging = false;
        state.PendingItem = null;
        state.PendingContainer = null;
        state.DraggedContainer = null;
        state.ScrollViewer = null;
    }

    // ---------------------------------------------------------------------------------------------
    // Drag events
    // ---------------------------------------------------------------------------------------------

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        if (sender is not ItemsControl host)
            return;

        if (!e.Data.GetDataPresent(ReorderFormat))
        {
            // Not ours (most likely an Explorer file drop). Leave e.Handled false so it keeps bubbling
            // to the FileDrop behavior further up the tree.
            e.Effects = DragDropEffects.None;
            return;
        }

        var position = e.GetPosition(host);

        // Only the anchor matters here; the index itself is recomputed at drop time so an auto-scroll tick
        // that moved the content under a stationary pointer cannot leave a stale target behind.
        _ = ComputeInsertionIndex(host, position, out var nearest, out var insertAfter);

        var state = GetState(host);

        if (nearest is UIElement anchor)
            InsertionAdorner.Attach(anchor, insertAfter);
        else
            InsertionAdorner.Detach();

        UpdateAutoScroll(host, state, e);

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private static void OnDragLeave(object sender, DragEventArgs e)
    {
        if (sender is not ItemsControl host)
            return;

        if (!e.Data.GetDataPresent(ReorderFormat))
            return;

        // DragLeave also fires when the pointer crosses from one child container to the next, which would
        // make the indicator strobe. Only tear down when the pointer is genuinely outside the control.
        var position = e.GetPosition(host);
        if (position.X >= 0d && position.Y >= 0d
            && position.X <= host.ActualWidth && position.Y <= host.ActualHeight)
        {
            return;
        }

        StopAutoScroll(GetState(host));
        InsertionAdorner.Detach();
        e.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not ItemsControl host)
            return;

        if (!e.Data.GetDataPresent(ReorderFormat))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Handled = true;
        e.Effects = DragDropEffects.Move;

        var state = GetState(host);
        StopAutoScroll(state);
        InsertionAdorner.Detach();

        var item = e.Data.GetData(ReorderFormat);
        if (item is null)
            return;

        // Recompute from the drop point rather than trusting the last DragOver: a drop can arrive without
        // a preceding DragOver at the same position (e.g. after an auto-scroll tick moved the content).
        var insertionIndex = ComputeInsertionIndex(host, e.GetPosition(host), out _, out _);
        Reorder(host, item, insertionIndex);
    }

    private static void OnQueryContinueDrag(object sender, QueryContinueDragEventArgs e)
    {
        if (!e.EscapePressed)
            return;

        e.Action = DragAction.Cancel;
        e.Handled = true;

        if (sender is ItemsControl host)
        {
            // Pull the indicator immediately rather than waiting for the nested loop to unwind; the
            // cancelled drag never raises Drop, so nothing is reordered.
            StopAutoScroll(GetState(host));
            InsertionAdorner.Detach();
        }
    }

    private static void OnGiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        e.UseDefaultCursors = false;
        Mouse.SetCursor(e.Effects.HasFlag(DragDropEffects.Move) ? Cursors.Hand : Cursors.No);
        e.Handled = true;
    }

    // ---------------------------------------------------------------------------------------------
    // Hit testing
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Computes the insertion index in <c>0..Items.Count</c> for a pointer position in
    /// <paramref name="host"/> coordinates.
    /// </summary>
    /// <param name="host">The items control.</param>
    /// <param name="position">Pointer position, relative to <paramref name="host"/>.</param>
    /// <param name="anchor">Receives the container the insertion indicator should be drawn against, if any.</param>
    /// <param name="insertAfter">Receives whether the indicator belongs on the anchor's trailing edge.</param>
    /// <returns>The index the dragged item would occupy <em>before</em> it is removed from its old slot.</returns>
    private static int ComputeInsertionIndex(ItemsControl host, Point position, out DependencyObject? anchor, out bool insertAfter)
    {
        anchor = null;
        insertAfter = true;

        var count = host.Items.Count;
        if (count == 0)
            return 0;

        // Fast path: something is directly under the pointer. HitTest gives the deepest visual, which is
        // usually a Border/Image inside the DataTemplate, so walk back up to the generated container.
        var hit = VisualTreeHelper.HitTest(host, position);
        var container = hit?.VisualHit is DependencyObject visual ? FindContainer(host, visual) : null;

        if (container is FrameworkElement direct && direct.IsVisible)
        {
            var index = host.ItemContainerGenerator.IndexFromContainer(direct);
            if (index >= 0)
            {
                var local = host.TranslatePoint(position, direct);

                // Horizontal WrapPanel flow: the left half of a card means "insert before it", the right
                // half means "insert after it".
                insertAfter = local.X > direct.RenderSize.Width / 2d;
                anchor = direct;
                return insertAfter ? index + 1 : index;
            }
        }

        // Past the end: anywhere below the last card's row, or to its right on the same row, means "append".
        // This is checked before the nearest-neighbour scan because a wide gap at the end of a short final
        // row is geometrically closest to a card on the row *above*, which would insert in the wrong place.
        if (host.ItemContainerGenerator.ContainerFromIndex(count - 1) is FrameworkElement last && last.IsVisible)
        {
            var lastBounds = TryGetBounds(last, host);
            if (lastBounds.HasValue
                && (position.Y > lastBounds.Value.Bottom
                    || (position.Y >= lastBounds.Value.Top && position.X > lastBounds.Value.Right)))
            {
                anchor = last;
                insertAfter = true;
                return count;
            }
        }

        // Slow path: the pointer is in a gap between cards or before the first one. Fall back to the
        // geometrically nearest realized container.
        var bestDistance = double.MaxValue;
        var bestIndex = -1;
        FrameworkElement? best = null;
        var bestAfter = true;

        for (var i = 0; i < count; i++)
        {
            if (host.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement element || !element.IsVisible)
                continue;

            var maybeBounds = TryGetBounds(element, host);
            if (maybeBounds is not { } bounds)
                continue;

            var dx = position.X < bounds.Left ? bounds.Left - position.X
                   : position.X > bounds.Right ? position.X - bounds.Right
                   : 0d;
            var dy = position.Y < bounds.Top ? bounds.Top - position.Y
                   : position.Y > bounds.Bottom ? position.Y - bounds.Bottom
                   : 0d;
            var distance = (dx * dx) + (dy * dy);

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = i;
            best = element;
            bestAfter = position.Y > bounds.Bottom
                || (position.Y >= bounds.Top && position.X > bounds.Left + (bounds.Width / 2d));
        }

        if (best is null || bestIndex < 0)
            return count;

        anchor = best;
        insertAfter = bestAfter;
        return bestAfter ? bestIndex + 1 : bestIndex;
    }

    /// <summary>Bounds of <paramref name="element"/> in <paramref name="host"/> coordinates, or <see langword="null"/> when it is not connected to the same tree.</summary>
    private static Rect? TryGetBounds(FrameworkElement element, ItemsControl host)
    {
        try
        {
            return new Rect(element.TranslatePoint(new Point(0d, 0d), host), element.RenderSize);
        }
        catch (InvalidOperationException)
        {
            // Container recycled by virtualization and no longer shares a common visual ancestor.
            return null;
        }
    }

    /// <summary>Walks up from <paramref name="source"/> to the item container generated by <paramref name="host"/>.</summary>
    private static DependencyObject? FindContainer(ItemsControl host, DependencyObject? source)
    {
        if (source is null)
            return null;

        // ContainerFromElement handles the ContentPresenter/template indirection for us, but it only
        // accepts visual/logical descendants — Run and other non-Visual sources must be lifted first.
        var current = source;
        while (current is not null and not Visual and not System.Windows.Media.Media3D.Visual3D)
            current = LogicalTreeHelper.GetParent(current);

        return current is null ? null : host.ContainerFromElement(current);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the mouse-down landed on interactive chrome (a button, thumb,
    /// scroll bar, text box, or a nested drop target) somewhere between <paramref name="source"/> and
    /// <paramref name="container"/>.
    /// </summary>
    private static bool IsInteractiveChrome(DependencyObject? source, DependencyObject container)
    {
        var current = source;

        while (current is not null && !ReferenceEquals(current, container))
        {
            if (current is ButtonBase or Thumb or ScrollBar or TextBoxBase)
                return true;

            // A nested drop target (e.g. a per-card FileDrop zone) owns its own gesture. AllowDrop is an
            // *inherited* dependency property, so the effective value is true on every descendant of the
            // host we just set it on — only a locally set value marks a real nested drop target.
            if (current is UIElement element
                && element.ReadLocalValue(UIElement.AllowDropProperty) is bool localAllowDrop
                && localAllowDrop)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // Auto-scroll
    // ---------------------------------------------------------------------------------------------

    private static void UpdateAutoScroll(ItemsControl host, DragState state, DragEventArgs e)
    {
        if (!GetAutoScroll(host))
        {
            StopAutoScroll(state);
            return;
        }

        var scroller = state.ScrollViewer ??= FindScrollViewer(host);
        if (scroller is null || scroller.ScrollableHeight <= 0d)
        {
            StopAutoScroll(state);
            return;
        }

        var position = e.GetPosition(scroller);
        var height = scroller.ActualHeight;
        if (height <= AutoScrollHotZone * 2d)
        {
            // Viewport too short for two hot zones; auto-scrolling would fire everywhere.
            StopAutoScroll(state);
            return;
        }

        double intensity;
        if (position.Y < AutoScrollHotZone)
            intensity = -(AutoScrollHotZone - position.Y) / AutoScrollHotZone;
        else if (position.Y > height - AutoScrollHotZone)
            intensity = (position.Y - (height - AutoScrollHotZone)) / AutoScrollHotZone;
        else
            intensity = 0d;

        // Deeper into the hot zone => faster, clamped so overshooting the edge does not run away.
        intensity = Math.Clamp(intensity, -1d, 1d);

        if (intensity == 0d)
        {
            StopAutoScroll(state);
            return;
        }

        state.ScrollStep = intensity * AutoScrollMaxStep;

        if (state.ScrollTimer is not null)
            return;

        var timer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(16d)
        };
        timer.Tick += (_, _) =>
        {
            var target = state.ScrollViewer;
            if (target is null || state.ScrollStep == 0d)
                return;

            target.ScrollToVerticalOffset(target.VerticalOffset + state.ScrollStep);
        };
        state.ScrollTimer = timer;
        timer.Start();
    }

    private static void StopAutoScroll(DragState state)
    {
        // DispatcherTimer is not IDisposable; stopping it removes it from the dispatcher's timer list,
        // which is what actually releases the reference chain back to this state object.
        state.ScrollTimer?.Stop();
        state.ScrollTimer = null;
        state.ScrollStep = 0d;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer self)
            return self;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
                return found;
        }

        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // The move itself
    // ---------------------------------------------------------------------------------------------

    private static void Reorder(ItemsControl host, object item, int insertionIndex)
    {
        // The bound collection is the source of truth; when nothing is bound, Items is directly editable.
        var list = host.ItemsSource as IList ?? (host.ItemsSource is null ? host.Items : null);
        if (list is null || list.IsReadOnly || list.IsFixedSize)
            return;

        var oldIndex = list.IndexOf(item);
        if (oldIndex < 0)
            return;

        var newIndex = Math.Clamp(insertionIndex, 0, list.Count);

        // The classic off-by-one. The insertion index is expressed against the list *as it is now*, i.e.
        // with the dragged item still occupying oldIndex. Removing it first shifts everything after
        // oldIndex down by one, so any insertion point past oldIndex must be decremented:
        // dropping item 2 "after item 5" yields insertionIndex 6, but the item's final index is 5.
        if (newIndex > oldIndex)
            newIndex--;

        newIndex = Math.Clamp(newIndex, 0, list.Count - 1);
        if (newIndex == oldIndex)
            return;

        MoveItem(list, oldIndex, newIndex);

        // ObservableCollection.Move keeps the item selected already; re-setting is cheap insurance for
        // the RemoveAt/Insert fallback, which clears the selection.
        if (host is Selector selector)
            selector.SelectedItem = item;
    }

    /// <summary>
    /// Moves an element inside <paramref name="list"/>, preferring a native <c>Move(int, int)</c>.
    /// <see cref="System.Collections.ObjectModel.ObservableCollection{T}.Move"/> raises a single
    /// <c>Move</c> notification, which keeps selection and avoids the remove/insert flicker of the fallback.
    /// </summary>
    private static void MoveItem(IList list, int oldIndex, int newIndex)
    {
        var move = GetMoveMethod(list.GetType());
        if (move is not null)
        {
            try
            {
                move.Invoke(list, [oldIndex, newIndex]);
                return;
            }
            catch (TargetInvocationException)
            {
                // A custom collection may reject the move; fall through to remove/insert.
            }
        }

        var item = list[oldIndex];
        list.RemoveAt(oldIndex);
        list.Insert(newIndex, item);
    }

    private static MethodInfo? GetMoveMethod(Type type)
    {
        lock (MoveMethodCacheLock)
        {
            if (MoveMethodCache.TryGetValue(type, out var cached))
                return cached;

            var method = type.GetMethod(
                "Move",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                [typeof(int), typeof(int)],
                modifiers: null);

            if (method is not null && method.ReturnType != typeof(void))
                method = null;

            MoveMethodCache[type] = method;
            return method;
        }
    }
}
