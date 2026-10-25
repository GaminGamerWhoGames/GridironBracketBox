using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GridironBracketBox.Core;
using GridironBracketBox.ViewModels;

namespace GridironBracketBox.Views;

/// <summary>
/// Setup screen. The code here is only mouse drag-and-drop; all list changes go through the view model.
/// Two drags are supported:
///  - a seed row up/down within its list (re-seeds live as you move), and
///  - a team from the picker onto the seed list (inserts it at the seed under the mouse).
/// While dragging, a "ghost" card follows the mouse. Row sliding is handled by <see cref="AnimateReorder"/>.
/// </summary>
public partial class SetupView : UserControl
{
    /// <summary>How far (in pixels) the mouse must move before a press becomes a drag, so clicks still work.</summary>
    private const double DragThreshold = 6;
    private static readonly TimeSpan SettleDuration = TimeSpan.FromMilliseconds(140);

    private object? _pressed;          // SeedSlotViewModel or TeamOptionViewModel under the press
    private Control? _pressedControl;  // the row/button that was pressed (for ghost size and grab offset)
    private Point _pressPoint;
    private Point _grabOffset;         // where inside the row the mouse grabbed it
    private bool _dragging;
    private int _settleId;             // ignores a finished settle animation if a new drag already started

    public SetupView()
    {
        InitializeComponent();
        // "Tunnel" handlers run before the buttons under the mouse see the event,
        // so we can notice a drag starting even on top of a button.
        AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
        // Losing the mouse mid-drag (e.g. the window loses focus) cancels the drag.
        PointerCaptureLost += (_, _) => { if (_dragging) CancelDrag(); };
    }

    private SetupViewModel? Vm => DataContext as SetupViewModel;

    // ---- Mouse events ---------------------------------------------------------

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var source = e.Source as Control;

        // The ▲ ▼ ✕ buttons on a seed row are plain clicks, never drags.
        if (source?.FindAncestorOfType<Button>(includeSelf: true)?.Classes.Contains("icon") == true) return;

        switch (source?.DataContext)
        {
            case SeedSlotViewModel slot:
                _pressed = slot;
                _pressedControl = source.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("slot"));
                break;
            case TeamOptionViewModel option when option.IsAvailable:
                _pressed = option;
                _pressedControl = source.FindAncestorOfType<Button>(includeSelf: true);
                break;
            default:
                _pressed = null;
                _pressedControl = null;
                break;
        }
        _pressPoint = e.GetPosition(this);
        if (_pressedControl is not null) _grabOffset = e.GetPosition(_pressedControl);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is null) return;
        var pos = e.GetPosition(this);

        if (!_dragging)
        {
            var delta = pos - _pressPoint;
            if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold) return;
            StartDrag(e);
        }
        e.Handled = true;
        MoveGhost(pos);

        if (_pressed is SeedSlotViewModel slot)
            ReorderLive(slot, e);
        else if (_pressed is TeamOptionViewModel option)
            ShowDropPreview(option.Team, e);
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
        {
            _pressed = null;
            return; // a normal click: let the button handle it
        }

        e.Handled = true; // a drag must not also count as a click
        var pressed = _pressed;
        _pressed = null;
        _dragging = false;
        DropLine.IsVisible = false;
        ToolTip.SetServiceEnabled(this, true);

        if (pressed is TeamOptionViewModel option)
        {
            DropTeam(option.Team, e);
            Ghost.IsVisible = false;
        }
        else if (pressed is SeedSlotViewModel slot)
        {
            SettleGhost(slot);
        }

        // Only release capture we took ourselves. Releasing it on a normal click would
        // take the mouse away from the button under it, and the button would never click.
        e.Pointer.Capture(null);
    }

    // ---- Drag lifecycle ---------------------------------------------------------

    private void StartDrag(PointerEventArgs e)
    {
        _dragging = true;
        _settleId++;
        // Tooltips are small pop-up windows; one opening under the cursor mid-drag can steal the mouse.
        ToolTip.SetServiceEnabled(this, false);
        // Capture: keep receiving mouse events here even when the mouse leaves the row/button,
        // and stop the button under the press from treating the release as a click.
        e.Pointer.Capture(this);

        Team team;
        if (_pressed is SeedSlotViewModel slot)
        {
            slot.IsDragging = true;
            team = slot.Team;
            GhostSeed.Text = slot.Seed.ToString();
        }
        else
        {
            team = ((TeamOptionViewModel)_pressed!).Team;
            GhostSeed.Text = "+";
        }

        GhostAbbr.Text = team.Abbr;
        GhostName.Text = team.Name;
        Ghost.Width = _pressedControl?.Bounds.Width ?? double.NaN;
        Ghost.Transitions = null;
        Ghost.RenderTransform = TransformOperations.Parse("rotate(-1.5deg)");
        Ghost.IsVisible = true;
    }

    /// <summary>Abandons a drag without dropping anything.</summary>
    private void CancelDrag()
    {
        if (_pressed is SeedSlotViewModel slot) slot.IsDragging = false;
        Ghost.IsVisible = false;
        DropLine.IsVisible = false;
        ToolTip.SetServiceEnabled(this, true);
        _pressed = null;
        _dragging = false;
    }

    private void MoveGhost(Point pos)
    {
        Canvas.SetLeft(Ghost, pos.X - _grabOffset.X);
        Canvas.SetTop(Ghost, pos.Y - _grabOffset.Y);
    }

    /// <summary>After a row drag: glide the card into the row's final spot and straighten it, then hide it.</summary>
    private void SettleGhost(SeedSlotViewModel slot)
    {
        var row = SlotListFor(slot.Group)?.ContainerFromItem(slot);
        var target = row?.TranslatePoint(default, this);
        if (target is null)
        {
            Ghost.IsVisible = false;
            slot.IsDragging = false;
            return;
        }

        Ghost.Transitions = new Transitions
        {
            new DoubleTransition { Property = Canvas.LeftProperty, Duration = SettleDuration, Easing = new CubicEaseOut() },
            new DoubleTransition { Property = Canvas.TopProperty, Duration = SettleDuration, Easing = new CubicEaseOut() },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = SettleDuration },
        };
        Canvas.SetLeft(Ghost, target.Value.X);
        Canvas.SetTop(Ghost, target.Value.Y);
        Ghost.RenderTransform = TransformOperations.Parse("rotate(0deg)");

        int id = _settleId;
        DispatcherTimer.RunOnce(() =>
        {
            if (id != _settleId) return; // a new drag started meanwhile; leave its ghost alone
            Ghost.IsVisible = false;
            Ghost.Transitions = null;
            slot.IsDragging = false;
        }, SettleDuration);
    }

    // ---- Seed-row drag ------------------------------------------------------------

    /// <summary>Moves the dragged row to whichever row the mouse is over; the card shows its new seed.</summary>
    private void ReorderLive(SeedSlotViewModel slot, PointerEventArgs e)
    {
        var list = SlotListFor(slot.Group);
        if (list is null) return;
        int target = RowIndexAt(list, e.GetPosition(list).Y, forInsert: false);
        slot.Group.MoveTo(slot, target);
        GhostSeed.Text = slot.Seed.ToString();
    }

    // ---- Picker drag ----------------------------------------------------------------

    /// <summary>While dragging a picker team: show where it would land (green line) and at what seed.</summary>
    private void ShowDropPreview(Team team, PointerEventArgs e)
    {
        DropLine.IsVisible = false;
        if (Vm is null || !IsOver(SeedsCard, e))
        {
            GhostSeed.Text = "+";
            return;
        }

        var group = Vm.GroupFor(team);
        if (group.IsFull)
        {
            GhostSeed.Text = "FULL";
            return;
        }

        var list = SlotListFor(group);
        if (list is null) return;
        int index = RowIndexAt(list, e.GetPosition(list).Y, forInsert: true);
        GhostSeed.Text = (index + 1).ToString();

        // Line goes at the top of the row it would push down, or under the last row.
        double y = index < list.ItemCount && list.ContainerFromIndex(index) is { } row ? row.Bounds.Top
            : list.ItemCount > 0 && list.ContainerFromIndex(list.ItemCount - 1) is { } last ? last.Bounds.Bottom
            : 0;
        if (list.TranslatePoint(new Point(0, y), this) is { } p)
        {
            Canvas.SetLeft(DropLine, p.X);
            Canvas.SetTop(DropLine, p.Y - 2);
            DropLine.Width = list.Bounds.Width;
            DropLine.IsVisible = true;
        }
    }

    /// <summary>Dropping a picker team anywhere on the seeds card inserts it at the seed under the mouse.</summary>
    private void DropTeam(Team team, PointerReleasedEventArgs e)
    {
        if (Vm is null || !IsOver(SeedsCard, e)) return;
        var group = Vm.GroupFor(team);
        var list = SlotListFor(group);
        int index = list is null ? group.Slots.Count : RowIndexAt(list, e.GetPosition(list).Y, forInsert: true);
        Vm.TryInsert(team, index);
    }

    // ---- Helpers ------------------------------------------------------------------------

    private static bool IsOver(Control control, PointerEventArgs e) =>
        new Rect(control.Bounds.Size).Contains(e.GetPosition(control));

    /// <summary>The on-screen list (ItemsControl with class "slots") that shows a seed group's rows.</summary>
    private ItemsControl? SlotListFor(SeedGroupViewModel group) =>
        this.GetVisualDescendants()
            .OfType<ItemsControl>()
            .FirstOrDefault(c => c.Classes.Contains("slots") && c.DataContext == group);

    /// <summary>
    /// Which row a vertical position falls on. For reordering: the row under the mouse (clamped to the list).
    /// For inserting: the gap nearest the mouse, so dropping on the lower half of a row goes after it.
    /// Uses layout positions, which ignore the slide animation, so targets don't jitter mid-animation.
    /// </summary>
    private static int RowIndexAt(ItemsControl list, double y, bool forInsert)
    {
        int count = list.ItemCount;
        for (int i = 0; i < count; i++)
        {
            if (list.ContainerFromIndex(i) is not { } row) continue;
            double edge = forInsert ? row.Bounds.Center.Y : row.Bounds.Bottom;
            if (y < edge) return i;
        }
        return forInsert ? count : Math.Max(count - 1, 0);
    }
}
