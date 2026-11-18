using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace GrpDialer;

/// <summary>
/// The app's icon set: solid (filled) shapes on a 24×24 grid, all from Material Design Icons
/// (Pictogrammers, Apache 2.0) so weights and corners match. Used in XAML as
/// <c>&lt;local:Ico Data="{x:Static local:Icons.Phone}" /&gt;</c> and for the tray icon.
/// </summary>
public static class Icons
{
    public static readonly Geometry Phone = G(
        "M6.62,10.79C8.06,13.62 10.38,15.94 13.21,17.38L15.41,15.18C15.69,14.9 16.08,14.82 16.43,14.93C17.55,15.3 18.75,15.5 20,15.5A1,1 0 0,1 21,16.5V20A1,1 0 0,1 20,21A17,17 0 0,1 3,4A1,1 0 0,1 4,3H7.5A1,1 0 0,1 8.5,4C8.5,5.25 8.7,6.45 9.07,7.57C9.18,7.92 9.1,8.31 8.82,8.59L6.62,10.79Z");

    public static readonly Geometry HangUp = G(
        "M12,9C10.4,9 8.85,9.25 7.4,9.72V12.82C7.4,13.21 7.17,13.56 6.84,13.72C5.86,14.21 4.97,14.84 4.18,15.57C4,15.75 3.75,15.86 3.5,15.86C3.2,15.86 2.95,15.74 2.77,15.56L0.29,13.08C0.11,12.9 0,12.65 0,12.38C0,12.1 0.11,11.85 0.29,11.67C3.34,8.77 7.46,7 12,7C16.54,7 20.66,8.77 23.71,11.67C23.89,11.85 24,12.1 24,12.38C24,12.65 23.89,12.9 23.71,13.08L21.23,15.56C21.05,15.74 20.8,15.86 20.5,15.86C20.25,15.86 20,15.75 19.82,15.57C19.03,14.84 18.14,14.21 17.16,13.72C16.83,13.56 16.6,13.21 16.6,12.82V9.72C15.15,9.25 13.6,9 12,9Z");

    public static readonly Geometry Pause = G("M14,19H18V5H14M6,19H10V5H6V19Z");
    public static readonly Geometry Play = G("M8,5.14V19.14L19,12.14L8,5.14Z");

    public static readonly Geometry Settings = G(
        "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54,5.05 19.27,4.96 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46,2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44,6.05L4.95,5.05C4.73,4.96 4.46,5.05 4.34,5.27L2.34,8.73C2.21,8.95 2.27,9.22 2.46,9.37L4.57,11C4.53,11.34 4.5,11.67 4.5,12C4.5,12.33 4.53,12.65 4.57,12.97L2.46,14.63C2.27,14.78 2.21,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.03 4.95,18.95L7.44,17.94C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10,22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.67 16.04,18.34 16.56,17.94L19.05,18.95C19.27,19.03 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73,14.78 21.54,14.63L19.43,12.97Z");

    public static readonly Geometry Close = G(
        "M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z");

    public static readonly Geometry Back = G("M20,11V13H8L13.5,18.5L12.08,19.92L4.16,12L12.08,4.08L13.5,5.5L8,11H20Z");
    public static readonly Geometry Plus = G("M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z");
    public static readonly Geometry ChevronRight = G("M8.59,16.58L13.17,12L8.59,7.41L10,6L16,12L10,18L8.59,16.58Z");
    public static readonly Geometry ChevronDown = G("M7.41,8.58L12,13.17L16.59,8.58L18,10L12,16L6,10L7.41,8.58Z");

    /// <summary>3×3 keys plus the 0 key, as filled dots.</summary>
    public static readonly Geometry Dialpad = Dots();

    private static Geometry G(string data)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }

    private static Geometry Dots()
    {
        var g = new GeometryGroup();
        foreach (var y in new[] { 3.0, 9, 15 })
            foreach (var x in new[] { 6.0, 12, 18 })
                g.Children.Add(new EllipseGeometry(new Point(x, y), 2, 2));
        g.Children.Add(new EllipseGeometry(new Point(12, 21), 2, 2));
        g.Freeze();
        return g;
    }
}

/// <summary>
/// A solid icon from <see cref="Icons"/>, drawn at <see cref="Size"/> px in the surrounding text
/// colour (so it follows button states and colour animations like text does).
/// </summary>
public sealed class Ico : FrameworkElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(Ico),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(Ico),
        new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(Ico), new FrameworkPropertyMetadata(Brushes.Black,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public Geometry? Data { get => (Geometry?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public Ico()
    {
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Center;
        SnapsToDevicePixels = false;
    }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size available) => new(Size, Size);

    protected override void OnRender(DrawingContext dc)
    {
        if (Data is null) return;
        double s = Size / 24;
        dc.PushTransform(new ScaleTransform(s, s));
        dc.DrawGeometry(Foreground, null, Data);
        dc.Pop();
    }
}
