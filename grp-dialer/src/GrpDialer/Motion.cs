using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GrpDialer;

/// <summary>
/// The app's few animations, kept short and eased so state changes read as smooth rather than
/// flashy: things settle in from a few pixels below, colours cross-fade.
/// </summary>
public static class Motion
{
    public static readonly Duration Quick = TimeSpan.FromMilliseconds(180);
    public static readonly Duration Settle = TimeSpan.FromMilliseconds(260);
    private static readonly IEasingFunction Out = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction InOut = new SineEase { EasingMode = EasingMode.EaseInOut };

    private static TranslateTransform Shift(UIElement el)
    {
        if (el.RenderTransform is TranslateTransform t) return t;
        t = new TranslateTransform();
        el.RenderTransform = t;
        return t;
    }

    /// <summary>Fade in while rising <paramref name="rise"/> px into place.</summary>
    public static void In(UIElement el, double rise = 8)
    {
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Quick) { EasingFunction = Out });
        Shift(el).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(rise, 0, Settle) { EasingFunction = Out });
    }

    /// <summary>Fade out while sinking a little; <paramref name="done"/> runs when finished.</summary>
    public static void Leave(UIElement el, Action done, double sink = 6)
    {
        var fade = new DoubleAnimation(0, Quick) { EasingFunction = InOut };
        fade.Completed += (_, _) => done();
        el.BeginAnimation(UIElement.OpacityProperty, fade);
        Shift(el).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(sink, Quick) { EasingFunction = InOut });
    }

    /// <summary>Plain fade-in for panels appearing inside the dialer.</summary>
    public static void FadeIn(UIElement el) =>
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Settle) { EasingFunction = Out });

    /// <summary>Animate an element's own brush to a colour (creates an unfrozen brush once).</summary>
    public static SolidColorBrush Tint(SolidColorBrush? current, Color to, Action<SolidColorBrush> assign, bool animate = true)
    {
        var b = current is { IsFrozen: false } ? current : new SolidColorBrush(current?.Color ?? to);
        if (!ReferenceEquals(b, current)) assign(b);
        if (animate) b.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, Settle) { EasingFunction = InOut });
        else { b.BeginAnimation(SolidColorBrush.ColorProperty, null); b.Color = to; }
        return b;
    }

    /// <summary>Slow, calm breathing: from → to and back, forever.</summary>
    public static AnimationTimeline Breathe(double from, double to, double seconds = 1.6) =>
        new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = InOut,
        };
}
