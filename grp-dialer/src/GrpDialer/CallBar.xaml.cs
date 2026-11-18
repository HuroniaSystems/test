using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace GrpDialer;

/// <summary>
/// Thin strip shown only while a call is ringing in: green phone, number, "Incoming Call",
/// Answer (gently glowing) and Decline. When the call is answered the phone turns orange and the
/// strip fades away as the dialer takes over. Never takes keyboard focus and never hides on
/// click-away.
/// </summary>
public partial class CallBar : Window
{
    public event Action? AnswerClicked;
    public event Action? DeclineClicked;
    public event Action? OpenDialer;

    private static readonly Color Ring = Color.FromRgb(0x22, 0xA3, 0x5A);
    private static readonly Color Active = Color.FromRgb(0xF0, 0x8C, 0x1A);
    private readonly DropShadowEffect _glow = new() { Color = Ring, ShadowDepth = 0, BlurRadius = 6, Opacity = 0 };
    private SolidColorBrush? _phone;
    private int _visit;                  // bumps each time the bar comes in, so a stale fade-out can't hide a new call

    public CallBar()
    {
        InitializeComponent();
        AnswerBtn.Effect = _glow;
        Caption.Visibility = Visibility.Visible;
        SourceInitialized += (_, _) =>
        {
            // A tool window that never activates: clicking Answer doesn't pull focus from the user's app
            // and it stays out of Alt+Tab.
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
    }

    /// <summary>Fading out after the call was answered, declined or missed.</summary>
    public bool Leaving { get; private set; }

    public void Update(string who, string? name, string caption, bool enabled)
    {
        NumberText.Text = who;
        Who.ToolTip = string.IsNullOrEmpty(name) ? "Open the dialer" : name + " · open the dialer";
        Caption.Text = caption;
        AnswerBtn.IsEnabled = DeclineBtn.IsEnabled = enabled;
    }

    /// <summary>Show for a ringing call: settle in from below, green phone, Answer starts to breathe.</summary>
    public void Enter()
    {
        _visit++;
        bool wasShown = IsVisible && !Leaving;
        Leaving = false;
        _phone = Motion.Tint(_phone, Ring, b => Dot.Foreground = b, animate: false);
        _glow.BeginAnimation(DropShadowEffect.OpacityProperty, Motion.Breathe(0.12, 0.5));
        _glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, Motion.Breathe(6, 16));
        if (!IsVisible) Show();
        if (!wasShown) Motion.In(Root, 10);
    }

    /// <summary>
    /// The call stopped ringing. Answered: the phone eases to orange first, then the strip fades.
    /// Declined or missed: it just fades.
    /// </summary>
    public async void Leave(bool answered)
    {
        if (!IsVisible || Leaving) return;
        Leaving = true;
        int visit = _visit;
        _glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        _glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
        _glow.Opacity = 0;
        if (answered)
        {
            _phone = Motion.Tint(_phone, Active, b => Dot.Foreground = b);
            await Task.Delay(320);
            if (visit != _visit) return;
        }
        Motion.Leave(Root, () => { if (visit == _visit && Leaving) Hide(); });
    }

    private void Answer_Click(object sender, RoutedEventArgs e) => AnswerClicked?.Invoke();
    private void Decline_Click(object sender, RoutedEventArgs e) => DeclineClicked?.Invoke();
    private void Who_Click(object sender, MouseButtonEventArgs e) => OpenDialer?.Invoke();

    protected override void OnClosing(CancelEventArgs e)
    {
        // Only the app closing it counts; Alt+F4 can't reach a no-activate window anyway.
        if (!((Application.Current as App)?.Exiting ?? true)) e.Cancel = true;
        base.OnClosing(e);
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
