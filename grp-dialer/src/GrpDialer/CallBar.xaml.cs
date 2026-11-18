using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GrpDialer;

/// <summary>
/// Thin always-visible strip for the current call: green dot + Answer / Decline while it rings,
/// orange dot + call time + End once it's active. Never takes keyboard focus, never hides on
/// click-away; it goes away when the line is idle again.
/// </summary>
public partial class CallBar : Window
{
    public event Action? AnswerClicked;
    public event Action? DeclineClicked;
    public event Action? OpenDialer;

    private readonly DoubleAnimation _pulse = new(1, 0.3, TimeSpan.FromMilliseconds(700))
    {
        AutoReverse = true,
        RepeatBehavior = RepeatBehavior.Forever,
    };
    private bool? _ringing;             // null until the first update

    public CallBar()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            // A tool window that never activates: clicking Answer doesn't pull focus from the user's app
            // and it stays out of Alt+Tab.
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
    }

    public bool Ringing => _ringing == true;

    /// <param name="ringing">Incoming and not answered yet.</param>
    /// <param name="caption">Call time or "Calling…"; null hides the caption line.</param>
    public void Update(string who, string? name, bool ringing, string? caption, bool enabled)
    {
        NumberText.Text = who;
        Who.ToolTip = string.IsNullOrEmpty(name) ? "Open the dialer" : name + " · open the dialer";

        if (ringing != _ringing)
        {
            _ringing = ringing;
            Dot.Fill = (Brush)FindResource(ringing ? "RingDot" : "ActiveDot");
            Dot.BeginAnimation(OpacityProperty, ringing ? _pulse : null);
            AnswerBtn.Visibility = ringing ? Visibility.Visible : Visibility.Collapsed;
            DeclineText.Text = ringing ? "Decline" : "End";
            DeclineBtn.ToolTip = ringing ? "Decline the call" : "Hang up";
            // Ringing: two buttons, so the caption takes the full width underneath.
            // Active: one small End button centred beside both lines.
            System.Windows.Controls.Grid.SetColumnSpan(Caption, ringing ? 2 : 1);
            System.Windows.Controls.Grid.SetRowSpan(Buttons, ringing ? 1 : 2);
            Caption.Margin = new Thickness(18, ringing ? 4 : 1, 0, 0);
        }

        Caption.Text = caption ?? "";
        Caption.Visibility = caption is null ? Visibility.Collapsed : Visibility.Visible;
        AnswerBtn.IsEnabled = DeclineBtn.IsEnabled = enabled;
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
