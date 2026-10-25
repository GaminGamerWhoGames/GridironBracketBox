using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;

namespace GridironBracketBox.Views;

/// <summary>
/// Attached behavior: <c>views:AnimateReorder.IsEnabled="True"</c> on an ItemsControl makes its rows
/// glide to new positions instead of snapping, and new rows fade in.
///
/// Technique ("FLIP"): after every layout pass, compare each row's new position with where it was last
/// time. If it moved, instantly offset it back to where it was, then animate that offset to zero.
/// Because it watches layout rather than specific actions, any change animates: drag, ▲ ▼, shuffle, etc.
/// </summary>
public sealed class AnimateReorder
{
    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(220);

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<AnimateReorder, ItemsControl, bool>("IsEnabled");

    public static bool GetIsEnabled(ItemsControl control) => control.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(ItemsControl control, bool value) => control.SetValue(IsEnabledProperty, value);

    static AnimateReorder()
    {
        IsEnabledProperty.Changed.AddClassHandler<ItemsControl>((list, e) =>
        {
            if (e.NewValue is not true) return;
            var lastPositions = new Dictionary<object, double>(); // row item → last layout Y
            list.LayoutUpdated += (_, _) => OnLayoutUpdated(list, lastPositions);
        });
    }

    private AnimateReorder() { }

    private static void OnLayoutUpdated(ItemsControl list, Dictionary<object, double> lastPositions)
    {
        var current = new Dictionary<object, double>();
        foreach (var row in list.GetRealizedContainers())
        {
            if (row.DataContext is not { } item) continue;
            double y = row.Bounds.Y;
            current[item] = y;

            if (lastPositions.TryGetValue(item, out double lastY))
            {
                if (Math.Abs(lastY - y) > 0.5)
                    // Start from where the row is *visually* (it may be mid-slide already).
                    SlideFrom(row, lastY + CurrentOffsetY(row) - y);
            }
            else if (lastPositions.Count > 0)
            {
                FadeIn(row); // a row added to a list that already had rows
            }
        }
        lastPositions.Clear();
        foreach (var (item, y) in current) lastPositions[item] = y;
    }

    private static double CurrentOffsetY(Control row) => row.RenderTransform?.Value.M32 ?? 0;

    private static void SlideFrom(Control row, double offsetY)
    {
        // Jump to the old spot with no transition...
        row.Transitions = null;
        row.RenderTransform = Translate(offsetY);
        // ...then turn the transition on and head for the new spot.
        row.Transitions = new Transitions
        {
            new TransformOperationsTransition
            {
                Property = Visual.RenderTransformProperty,
                Duration = SlideDuration,
                Easing = new CubicEaseOut(),
            },
        };
        row.RenderTransform = Translate(0);
    }

    private static void FadeIn(Control row)
    {
        row.Transitions = null;
        row.Opacity = 0;
        row.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = FadeDuration },
        };
        row.Opacity = 1;
    }

    private static ITransform Translate(double y) =>
        TransformOperations.Parse($"translate(0px, {y.ToString(CultureInfo.InvariantCulture)}px)");
}
