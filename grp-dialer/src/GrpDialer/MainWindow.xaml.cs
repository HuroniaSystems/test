using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GrpDialer.Services;

namespace GrpDialer;

public sealed record QueueRow(int Index, string Position, string Display);

public partial class MainWindow : Window
{
    private static readonly TimeSpan PollFast = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PollSlow = TimeSpan.FromSeconds(3);
    /// <summary>Failed polls in a row before the phone is shown as offline (rides out a dropped packet).</summary>
    private const int OfflineAfterFailures = 2;

    private readonly Settings _settings = Settings.Load();
    private readonly DispatcherTimer _poll = new() { Interval = PollFast };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly NumpadEnterHook _hook;
    private readonly CallQueue _queue = new();

    private GrandstreamClient? _client;
    private int _clientGen;              // bumps when settings change, so stale polls are ignored
    private LineStatus _status = LineStatus.Offline;
    private bool _everConnected;
    private int _failures;
    private string _connError = "";
    private DateTime _lastDialAt = DateTime.MinValue;
    private string _lastNumber = "";
    private bool _polling;
    private bool _busy;                  // a user command is in flight
    private Action? _msgAction;
    private bool _wasCounting;           // queue countdown was showing last tick
    private bool _formatting;            // the number box mask is rewriting the text
    private DateTime? _activeSince;      // when the current call was answered / connected
    private readonly CallBar _bar = new();
    private bool _wasRinging;            // last status was an incoming call still ringing
    private bool _autoShown;             // dialer opened by itself for an answered call; untouched since
    private bool _blocked;               // phone unreachable: dialer dimmed
    private SolidColorBrush? _pillBg, _pillFg;
    private string _pillKey = "";

    // PropelRx caller lookup for calls this app didn't place.
    private PropelLookup? _propel;
    private bool _callIsOurs;            // the current call was dialed from here
    private DateTime _idleSince = DateTime.MinValue;   // when the line last went idle
    private string? _callerDigits;       // number being / been looked up for this call
    private string? _callerCaption;      // "Potentially JANE DOE", "Multiple linked profiles", or null
    private CancellationTokenSource? _lookupCts;

    // Tray flyout: anchored above the taskbar's notification area, like OTC Monitor.
    private const double Shadow = 12;    // transparent margin that holds the drop shadow
    private const double Gap = 8;        // visible gap between the card and the screen edge
    private DateTime _hiddenAt = DateTime.MinValue;
    private DateTime _deactivatedAt = DateTime.MinValue;

    /// <summary>Set by App; mirrors the call status in the notification-area tooltip.</summary>
    public TrayIcon? Tray { get; set; }

    public MainWindow()
    {
        InitializeComponent();

        _hook = new NumpadEnterHook(() => IsActive);
        _hook.Pressed += () => Dispatcher.BeginInvoke(new Action(BringUp));

        // Call bar: thin strip above the clock (or above the open dialer) for the whole call.
        _bar.AnswerClicked += async () => await Run(Answer);
        _bar.DeclineClicked += async () => await Run(HangUp);
        _bar.OpenDialer += BringUp;
        _bar.SizeChanged += (_, _) => RenderBar();

        // Number box: digits, * # + only, formatted as typed; smaller placeholder than the number.
        NumberBox.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(PhoneNumber.IsAllowed);
        NumberBox.PreviewKeyDown += NumberBox_PreviewKeyDown;
        NumberBox.Loaded += (_, _) =>
        {
            NumberBox.ApplyTemplate();
            if (NumberBox.Template.FindName("hint", NumberBox) is TextBlock hint) hint.FontSize = 14;
        };

        // Flyout: no taskbar button, sits above the clock, keeps its bottom edge there as it grows.
        SizeChanged += (_, _) => { if (IsVisible) Place(); };
        IsVisibleChanged += (_, _) => RenderBar();
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.WorkArea) && IsVisible) Dispatcher.BeginInvoke(Place);
        };
        Activated += (_, _) => _autoShown = false;
        Deactivated += async (_, _) =>
        {
            _deactivatedAt = DateTime.UtcNow;
            if (!_settings.HideOnClickAway) return;
            await Task.Delay(150);
            // Our own tray menu or a message box taking focus doesn't count as clicking away.
            if (!IsVisible || IsActive || WindowFocus.ForegroundIsOurProcess()) return;
            HideFlyout();
        };

        var v = typeof(MainWindow).Assembly.GetName().Version;
        VersionText.Text = v is null ? "GRP Dialer" : $"GRP Dialer {v.Major}.{v.Minor}.{v.Build}";

        _poll.Tick += async (_, _) => await Poll();
        _tick.Tick += (_, _) => OnTick();
        _queue.Changed += RenderQueue;
        Loaded += (_, _) =>
        {
            ApplySettings(openSettingsIfEmpty: true);
            _tick.Start();
        };
    }

    /// <summary>Numpad Enter from another app, or a second copy of the app was started.</summary>
    public void BringUp()
    {
        if (SettingsView.Visibility == Visibility.Visible && _settings.IsConfigured) ShowSettings(false);
        ShowFlyout();
    }

    // ================= Flyout =================

    /// <summary>Bottom-right of the work area: just above the clock with the taskbar at the bottom.</summary>
    private void Place()
    {
        var wa = SystemParameters.WorkArea;
        MaxHeight = Math.Max(240, wa.Height - 2 * Gap + 2 * Shadow);
        double h = ActualHeight;
        if (h <= 0 && Content is FrameworkElement root)
        {
            root.Measure(new Size(Width, double.PositiveInfinity));
            h = Math.Min(root.DesiredSize.Height, MaxHeight);
        }
        Left = wa.Right - Width + Shadow - Gap;
        Top = Math.Max(wa.Top - Shadow, wa.Bottom - h + Shadow - Gap);
        RenderBar();
    }

    // ================= Caller lookup =================

    private async Task LookUpCaller(PropelLookup propel, string digits)
    {
        _lookupCts?.Cancel();
        var cts = _lookupCts = new CancellationTokenSource();
        _callerDigits = digits;
        _callerCaption = null;
        try
        {
            var matches = await propel.FindByPhone(digits, cts.Token);
            if (cts.IsCancellationRequested || _callerDigits != digits) return;
            _callerCaption = PropelLookup.Caption(matches);
        }
        catch
        {
            // PropelRx down or refusing: show nothing, the call itself is unaffected.
            // (Settings > Test lookup explains what's wrong.)
        }
        Render();
    }

    private void ClearCaller()
    {
        _lookupCts?.Cancel();
        _lookupCts = null;
        _callerDigits = null;
        _callerCaption = null;
    }

    // ================= Call bar =================

    /// <summary>
    /// The mini call bar, only while a call is ringing in: above the clock, or stacked just above
    /// the dialer when that's open. If the open dialer leaves no room (tall Settings), it waits.
    /// Once answered it fades out and the dialer takes over (see UpdateStatus).
    /// </summary>
    private void RenderBar()
    {
        var s = _status;
        bool busy = s.Online && !s.Idle;
        if (!busy || CallPhases.From(s) != CallPhase.Incoming)
        {
            if (_bar.IsVisible) _bar.Leave(answered: busy);
            return;
        }

        string who = s.RemoteNumber.Length > 0 ? PhoneNumber.Display(s.RemoteNumber)
            : s.RemoteName.Length > 0 ? s.RemoteName
            : "Unknown number";
        const string caption = "Incoming Call";   // PropelRx matches stay in the main dialer
        _bar.Update(who, s.RemoteName, caption, enabled: !_busy);

        // Position (measure first if it hasn't been shown yet).
        var wa = SystemParameters.WorkArea;
        double h = _bar.ActualHeight;
        if (h <= 0 && _bar.Content is FrameworkElement root)
        {
            root.Measure(new Size(_bar.Width, double.PositiveInfinity));
            h = root.DesiredSize.Height;
        }
        double top = IsVisible
            ? Top + 2 * Shadow - Gap - h            // card sits Gap above the dialer's card
            : wa.Bottom - h + Shadow - Gap;         // card sits Gap above the taskbar
        if (top < wa.Top - Shadow)
        {
            if (_bar.IsVisible) _bar.Hide();
            return;
        }
        _bar.Left = wa.Right - _bar.Width + Shadow - Gap;
        _bar.Top = top;
        if (!_bar.IsVisible || _bar.Leaving) _bar.Enter();
    }

    /// <param name="activate">false shows it without taking focus from what the user is doing.</param>
    public void ShowFlyout(bool activate = true)
    {
        bool appearing = !IsVisible;
        Place();
        if (appearing && Content is UIElement root) Motion.In(root, 8);
        if (activate)
        {
            WindowFocus.ForceForeground(this);
            FocusDialer();
        }
        else if (!IsVisible)
        {
            ShowActivated = false;
            Show();
            ShowActivated = true;
        }
    }

    public void HideFlyout()
    {
        if (!IsVisible) return;
        Hide();
        _hiddenAt = DateTime.UtcNow;
    }

    /// <summary>Tray icon click: open, or close if it's open.</summary>
    public void ToggleFlyout()
    {
        var now = DateTime.UtcNow;
        if (IsVisible)
        {
            // Clicking the tray takes focus first, so "was just active" means the user is closing it.
            if (IsActive || (now - _deactivatedAt).TotalMilliseconds < 500) HideFlyout();
            else ShowFlyout();
            return;
        }
        // The click that hid it (by taking focus) must not open it again.
        if ((now - _hiddenAt).TotalMilliseconds < 500) return;
        ShowFlyout();
    }

    public void OpenSettings()
    {
        if (SettingsView.Visibility != Visibility.Visible) ShowSettings(true);
        Place();
        WindowFocus.ForceForeground(this);
    }

    // ================= Settings =================

    private void ApplySettings(bool openSettingsIfEmpty = false)
    {
        Keypad.Visibility = _settings.ShowKeypad ? Visibility.Visible : Visibility.Collapsed;
        KeypadBtnText.Text = _settings.ShowKeypad ? "Hide keypad" : "Keypad";
        _queue.Delay = TimeSpan.FromSeconds(_settings.QueueDelaySeconds);

        try { if (_settings.GlobalNumpadEnter) _hook.Start(); else _hook.Stop(); }
        catch { /* hook unavailable: the in-window keys still work */ }

        _clientGen++;
        _client?.Dispose();
        _client = _settings.IsConfigured
            ? new GrandstreamClient(_settings.PhoneHost, _settings.UseHttps, _settings.Password)
            : null;
        _status = LineStatus.Offline;
        _everConnected = false;
        _failures = 0;
        _connError = "";
        _activeSince = null;
        ClearCaller();
        _propel?.Dispose();
        _propel = null;
        if (_settings.LookupEnabled)
        {
            try { _propel = new PropelLookup(_settings.PropelAddress, _settings.PropelUser, _settings.PropelPassword); }
            catch { /* bad address: lookup stays off; Test lookup in Settings says why */ }
        }

        if (_client is null)
        {
            _poll.Stop();
            ShowMessage("Add your phone's address to start calling.", info: true, "Set up", () => ShowSettings(true));
            Render();
            if (openSettingsIfEmpty) ShowSettings(true);
            return;
        }

        ShowMessage(null);
        _poll.Interval = PollFast;
        _poll.Start();
        Render();
        _ = Poll();
        FocusDialer();
    }

    private void ShowSettings(bool show)
    {
        DialerView.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        SettingsView.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        HeaderTitle.Text = show ? "Settings" : "GRP Dialer";
        SettingsGlyph.Data = show ? Icons.Back : Icons.Settings;
        SettingsBtn.ToolTip = show ? "Back to dialer" : "Settings";
        SettingsMsg.Visibility = Visibility.Collapsed;

        if (show)
        {
            HostBox.Text = _settings.PhoneHost;
            PasswordBox.Password = _settings.Password;
            HttpsCheck.IsChecked = _settings.UseHttps;
            GlobalKeyCheck.IsChecked = _settings.GlobalNumpadEnter;
            ClickAwayCheck.IsChecked = _settings.HideOnClickAway;
            PropelBox.Text = _settings.PropelAddress;
            PropelUserBox.Text = _settings.PropelUser;
            PropelPasswordBox.Password = _settings.PropelPassword;
            SetPropelResult(null, "");
            KeypadCheck.IsChecked = _settings.ShowKeypad;
            DelayBox.Text = _settings.QueueDelaySeconds.ToString();
            SetAdvanced(false);
            SetTestResult(null, "");
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                HostBox.Focus();
                HostBox.SelectAll();
            }));
        }
        else
        {
            FocusDialer();
        }
    }

    private void SaveSettings()
    {
        var host = HostBox.Text.Trim();
        if (host.Length == 0) { SettingsError("Enter the phone's address."); return; }
        if (PropelBox.Text.Trim().Length > 0 && PropelLookup.BaseUrl(PropelBox.Text) is null)
        {
            SettingsError("That PropelRx server address isn't valid (for example svr1 or 192.168.1.10).");
            return;
        }
        if (!int.TryParse(DelayBox.Text.Trim(), out var delay) || delay < 0 || delay > 60)
        {
            SetAdvanced(true);
            SettingsError("Seconds before the next queued call must be between 0 and 60.");
            return;
        }

        _settings.PhoneHost = host;
        _settings.Password = PasswordBox.Password;
        _settings.UseHttps = HttpsCheck.IsChecked == true;
        _settings.GlobalNumpadEnter = GlobalKeyCheck.IsChecked == true;
        _settings.HideOnClickAway = ClickAwayCheck.IsChecked == true;
        _settings.PropelAddress = PropelBox.Text.Trim();
        _settings.PropelUser = PropelUserBox.Text.Trim();
        _settings.PropelPassword = PropelPasswordBox.Password;
        _settings.ShowKeypad = KeypadCheck.IsChecked == true;
        _settings.QueueDelaySeconds = delay;
        try { _settings.Save(); }
        catch (Exception ex) { SettingsError("Couldn't save settings: " + ex.Message); return; }

        ShowSettings(false);
        ApplySettings();
    }

    private void SettingsError(string text)
    {
        SettingsMsg.Text = text;
        SettingsMsg.Visibility = Visibility.Visible;
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        var host = HostBox.Text.Trim();
        if (host.Length == 0) { SettingsError("Enter the phone's address."); return; }
        SettingsMsg.Visibility = Visibility.Collapsed;
        TestBtn.IsEnabled = false;
        SetTestResult(null, "Testing…");
        try
        {
            using var test = new GrandstreamClient(host, HttpsCheck.IsChecked == true, PasswordBox.Password);
            await test.GetStatus();
            SetTestResult(true, "Connected");
        }
        catch (Exception ex)
        {
            SetTestResult(false, Friendly(ex));
        }
        finally
        {
            TestBtn.IsEnabled = true;
        }
    }

    private async void PropelTest_Click(object sender, RoutedEventArgs e)
    {
        if (PropelBox.Text.Trim().Length == 0) { SetPropelResult(false, "Enter the PropelRx server."); return; }
        PropelTestBtn.IsEnabled = false;
        SetPropelResult(null, "Testing…");
        try
        {
            using var test = new PropelLookup(PropelBox.Text, PropelUserBox.Text, PropelPasswordBox.Password);
            await test.Test();
            SetPropelResult(true, "Connected");
        }
        catch (Exception ex)
        {
            SetPropelResult(false, ex is FormatException or HttpRequestException { StatusCode: not null } ? ex.Message : Friendly(ex) switch
            {
                "Can't reach the phone." => "Can't reach PropelRx.",
                "The phone didn't respond." => "PropelRx didn't respond.",
                "That phone address isn't valid." => "That address isn't valid.",
                var other => other,
            });
        }
        finally
        {
            PropelTestBtn.IsEnabled = true;
        }
    }

    private void SetPropelResult(bool? ok, string text)
    {
        PropelTestText.Text = text;
        PropelTestText.Foreground = Res(ok == false ? "DangerInk" : ok == true ? "OkInk" : "Muted");
        PropelTestDot.Visibility = ok is null ? Visibility.Collapsed : Visibility.Visible;
        PropelTestDot.Fill = Res(ok == true ? "OkEdge" : "Danger");
    }

    private void SetTestResult(bool? ok, string text)
    {
        TestText.Text = text;
        TestText.Foreground = Res(ok == false ? "DangerInk" : ok == true ? "OkInk" : "Muted");
        TestDot.Visibility = ok is null ? Visibility.Collapsed : Visibility.Visible;
        TestDot.Fill = Res(ok == true ? "OkEdge" : "Danger");
    }

    private void SetAdvanced(bool open)
    {
        AdvancedPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        AdvancedGlyph.Data = open ? Icons.ChevronDown : Icons.ChevronRight;
    }

    private void Advanced_Click(object sender, RoutedEventArgs e) =>
        SetAdvanced(AdvancedPanel.Visibility != Visibility.Visible);

    private void Save_Click(object sender, RoutedEventArgs e) => SaveSettings();
    private void Cancel_Click(object sender, RoutedEventArgs e) => ShowSettings(false);

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        ShowSettings(SettingsView.Visibility != Visibility.Visible);

    private void KeypadToggle_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowKeypad = !_settings.ShowKeypad;
        Keypad.Visibility = _settings.ShowKeypad ? Visibility.Visible : Visibility.Collapsed;
        KeypadBtnText.Text = _settings.ShowKeypad ? "Hide keypad" : "Keypad";
        TrySave();
        FocusDialer(selectAll: false);
    }

    private void TrySave()
    {
        try { _settings.Save(); } catch { /* best effort */ }
    }

    // ================= Polling =================

    private async Task Poll()
    {
        var c = _client;
        int gen = _clientGen;
        if (_polling || c is null) return;
        _polling = true;
        try
        {
            var s = await c.GetStatus();
            if (gen != _clientGen) return;
            _everConnected = true;
            _failures = 0;
            _connError = "";
            _poll.Interval = PollFast;
            UpdateStatus(s);
        }
        catch (Exception ex)
        {
            if (gen != _clientGen) return;
            _connError = Friendly(ex);
            _failures++;
            if (_failures >= OfflineAfterFailures)
            {
                _poll.Interval = PollSlow;
                UpdateStatus(LineStatus.Offline);
            }
        }
        finally
        {
            _polling = false;
        }
    }

    private void UpdateStatus(LineStatus s)
    {
        bool wasBusy = _status.Online && !_status.Idle;
        bool isBusy = s.Online && !s.Idle;
        _status = s;

        // Caller lookup: only for calls this app didn't dial (incoming, or the handset). A call is
        // ours if we dialed since the line last went idle; a ringing inbound call never is.
        if (!isBusy && (wasBusy || _idleSince == DateTime.MinValue)) _idleSince = DateTime.Now;
        if (isBusy && !wasBusy)
            _callIsOurs = _lastDialAt >= _idleSince && DateTime.Now - _lastDialAt < TimeSpan.FromSeconds(10);
        if (isBusy && CallPhases.From(s) == CallPhase.Incoming) _callIsOurs = false;
        if (!isBusy) ClearCaller();
        else if (!_callIsOurs && _propel is not null
                 && PropelLookup.SearchDigits(s.RemoteNumber) is string digits && digits != _callerDigits)
            _ = LookUpCaller(_propel, digits);

        // Incoming calls show on the call bar (see RenderBar) instead of popping the whole dialer.
        // Call time (bar and dialer) counts from the answer, not from the first ring.
        var phase = CallPhases.From(s);
        if (!isBusy) _activeSince = null;
        else if (_activeSince is null && phase is CallPhase.Connected or CallPhase.OnHold or CallPhase.InUse)
            _activeSince = DateTime.Now;

        bool ringing = isBusy && phase == CallPhase.Incoming;
        if (_wasRinging && isBusy && !ringing && !IsVisible) _ = TakeOverFromBar();
        if (wasBusy && !isBusy && _autoShown && IsVisible && !IsActive && _queue.Count == 0) HideFlyout();
        if (!isBusy) _autoShown = false;
        _wasRinging = ringing;

        Render();
    }

    /// <summary>Answered from the mini bar (or the handset): once its phone has turned orange, the dialer appears.</summary>
    private async Task TakeOverFromBar()
    {
        await Task.Delay(220);
        if (IsVisible || !(_status.Online && !_status.Idle)) return;
        ShowFlyout(activate: false);
        _autoShown = true;
    }

    /// <summary>Runs 4x a second: call timer, queue countdown.</summary>
    private void OnTick()
    {
        if (_activeSince is not null) StateText.Text = PillText();

        if (_client is not null)
        {
            // A half-typed number or open Settings holds the queue, but only while the user can see
            // it: a hidden flyout must not stall the queue without saying why.
            bool hold = _busy
                        || IsVisible && (PhoneNumber.Normalize(NumberBox.Text).Length > 0
                                         || SettingsView.Visibility == Visibility.Visible);
            var next = _queue.Tick(_status.Online, _status.Idle, hold, DateTime.Now);
            if (next is not null) _ = Dial(next, fromQueue: true);

            // Countdown to the next queued call: show it (without taking focus) so it can be paused.
            bool counting = _queue.DialAt is not null;
            if (counting && !_wasCounting && !IsVisible) ShowFlyout(activate: false);
            _wasCounting = counting;
        }
        RenderQueueStatus();
    }

    private static string Elapsed(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    // ================= Rendering =================

    private Brush Res(string key) => (Brush)FindResource(key);

    private void Render()
    {
        var s = _status;
        var phase = CallPhases.From(s);
        bool busy = s.Online && !s.Idle;
        bool configured = _client is not null;
        bool hasText = PhoneNumber.Normalize(NumberBox.Text).Length > 0;

        // Header: nothing when all is well; a short error otherwise (details in the tooltip).
        bool unreachable = configured && !s.Online && _connError.Length > 0;
        if (!configured) SetConnection("Not set up", null);
        else if (unreachable) SetConnection(ShortError(_connError), $"{_settings.PhoneHost}: {_connError}");
        else SetConnection(null, null);
        SetBlocked(unreachable);

        // Call card
        string label = !configured ? "Not set up" : !_everConnected && !s.Online && _connError.Length == 0
            ? "Connecting" : CallPhases.Label(phase);
        var (bg, fg) = !configured ? ("NeutralBg", "Muted") : phase switch
        {
            CallPhase.Idle => ("BrandLight", "Brand"),
            CallPhase.Offline => _connError.Length == 0 ? ("NeutralBg", "Muted") : ("DangerBg", "DangerInk"),
            CallPhase.Incoming or CallPhase.OnHold => ("WarnBg", "WarnInk"),
            _ => ("OkBg", "OkInk"),
        };
        _pillLabel = label;
        StateText.Text = PillText();
        if (bg + fg != _pillKey)
        {
            bool animate = _pillKey.Length > 0;
            _pillKey = bg + fg;
            _pillBg = Motion.Tint(_pillBg, ((SolidColorBrush)Res(bg)).Color, b => StatePill.Background = b, animate);
            _pillFg = Motion.Tint(_pillFg, ((SolidColorBrush)Res(fg)).Color, b => StateText.Foreground = b, animate);
        }
        StatePill.ToolTip = busy && s.RawState.Length > 0 ? $"Phone reports \"{s.RawState}\"" : null;

        string number = s.RemoteNumber.Length > 0 ? s.RemoteNumber
            : busy && DateTime.Now - _lastDialAt < TimeSpan.FromMinutes(5) ? _lastNumber : "";
        if (busy)
        {
            RemoteName.Text = s.RemoteName.Length > 0 ? s.RemoteName
                : number.Length > 0 ? PhoneNumber.Display(number)
                : "On a call";
            RemoteNumber.Text = s.RemoteName.Length > 0 ? PhoneNumber.Display(number) : "";
        }
        else if (!configured)
        {
            RemoteName.Text = "No phone yet";
            RemoteNumber.Text = "";
        }
        else if (!s.Online && _connError.Length > 0)
        {
            RemoteName.Text = "Can't reach the phone";
            RemoteNumber.Text = "Trying again every few seconds";
        }
        else
        {
            RemoteName.Text = "No active call";
            RemoteNumber.Text = "";
        }
        RemoteNumber.Visibility = RemoteNumber.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CallerText.Text = busy ? _callerCaption ?? "" : "";
        Reveal(CallerText, CallerText.Text.Length > 0);

        // Entry
        NumberBox.Tag = busy ? "Add number to queue" : "Type a number";
        QueueBtn.Visibility = busy && hasText ? Visibility.Visible : Visibility.Collapsed;
        QueueBtn.IsEnabled = !_busy;

        // Actions
        bool canAnswer = busy && CallPhases.CanAnswer(phase);
        CallBtn.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        AnswerBtn.Visibility = canAnswer ? Visibility.Visible : Visibility.Collapsed;
        HangUpBtn.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(HangUpBtn, canAnswer ? 2 : 0);
        Grid.SetColumnSpan(HangUpBtn, canAnswer ? 1 : 3);
        bool holdable = phase is CallPhase.Connected or CallPhase.OnHold;
        bool onHold = phase == CallPhase.OnHold;
        HoldBtn.Visibility = holdable ? Visibility.Visible : Visibility.Collapsed;
        HoldBtn.IsEnabled = !_busy;
        HoldIcon.Data = onHold ? Icons.Play : Icons.Pause;
        HoldBtn.ToolTip = onHold ? "Resume" : "Hold";

        CallBtnText.Text = !hasText && _queue.Count > 0 ? "Call next" : "Call";
        CallBtn.IsEnabled = s.Online && !_busy && (hasText || _queue.Count > 0);
        AnswerBtn.IsEnabled = !_busy;
        HangUpBtn.IsEnabled = !_busy;

        // [ENTER] + what it does right now
        TipText.Text = busy
            ? (hasText ? "Add to queue" : canAnswer ? "Answer" : "Hang up")
            : !hasText && _queue.Count > 0 ? "Call next" : "Call";

        RenderQueueStatus();
        RenderTray(label, busy ? RemoteName.Text : "");
        RenderBar();
    }

    private void RenderTray(string label, string who)
    {
        if (Tray is null) return;
        string text = _client is null ? "Not set up"
            : !_status.Online ? (_connError.Length > 0 ? "Phone offline" : "Connecting")
            : who.Length > 0 && who != "On a call" ? $"{label}: {who}"
            : label;
        if (_queue.Count > 0) text += $" · {_queue.Count} queued";
        Tray.SetStatus(text, _client is not null && _status.Online);
    }

    private string _pillLabel = "Ready";

    /// <summary>"Connected 1:23": the state, with the call time once the call is answered.</summary>
    private string PillText() =>
        _activeSince is DateTime since && _status.Online && !_status.Idle
            ? $"{_pillLabel} {Elapsed(DateTime.Now - since)}"
            : _pillLabel;

    /// <summary>A few words for the header; the full reason is in its tooltip.</summary>
    private static string ShortError(string detail) =>
        detail.Contains("password", StringComparison.OrdinalIgnoreCase) ? "Wrong password"
        : detail.Contains("Action URI", StringComparison.OrdinalIgnoreCase) ? "Remote control off"
        : "Failed to connect";

    private void SetConnection(string? error, string? detail)
    {
        bool show = error is not null;
        if (show) ConnText.Text = error;
        ConnPanel.ToolTip = detail;
        Reveal(ConnPanel, show);
    }

    /// <summary>Dim and lock the dialer while the phone can't be reached.</summary>
    private void SetBlocked(bool blocked)
    {
        if (blocked == _blocked) return;
        _blocked = blocked;
        DialerView.IsEnabled = !blocked;
        if (blocked)
        {
            Blocker.Visibility = Visibility.Visible;
            Blocker.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.6, Motion.Settle));
        }
        else
        {
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, Motion.Quick);
            fade.Completed += (_, _) => { if (!_blocked) Blocker.Visibility = Visibility.Collapsed; };
            Blocker.BeginAnimation(OpacityProperty, fade);
            FocusDialer(selectAll: false);
        }
    }

    /// <summary>Show/hide, fading in when something appears.</summary>
    private static void Reveal(UIElement el, bool show)
    {
        bool was = el.Visibility == Visibility.Visible;
        el.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show && !was) Motion.FadeIn(el);
    }

    private void RenderQueue()
    {
        int n = _queue.Count;
        Reveal(QueuePanel, n > 0);
        QueueTitle.Text = $"Up next · {n}";
        QueueList.ItemsSource = _queue.Items
            .Select((num, i) => new QueueRow(i, $"{i + 1}", PhoneNumber.Display(num)))
            .ToList();
        Render();
    }

    private void RenderQueueStatus()
    {
        if (_queue.Count == 0) return;
        PauseBtn.Content = _queue.Paused ? "Resume" : "Pause";

        string text;
        if (_queue.DialAt is DateTime at)
        {
            int secs = Math.Max(1, (int)Math.Ceiling((at - DateTime.Now).TotalSeconds));
            text = $"Calling {PhoneNumber.Display(_queue.Next)} in {secs}s";
        }
        else
        {
            text = _queue.Waiting switch
            {
                QueueWait.OnCall => "Starts when this call ends",
                QueueWait.Paused => "Paused",
                QueueWait.Hold => _busy ? "Waiting" : "Waiting: clear the box to continue",
                QueueWait.Offline => "Waiting for the phone",
                _ => _queue.Delay == TimeSpan.Zero ? "Calling now" : "",
            };
        }
        QueueStatus.Text = text;
        QueueStatus.Foreground = Res(_queue.DialAt is not null ? "Brand" : "Muted");
        QueueStatus.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowMessage(string? text, bool info = false, string? actionLabel = null, Action? action = null)
    {
        MessageText.Text = text ?? "";
        Reveal(MsgPanel, !string.IsNullOrEmpty(text));
        MsgPanel.Background = Res(info ? "BrandLight" : "DangerBg");
        MessageText.Foreground = Res(info ? "Ink" : "DangerInk");
        _msgAction = action;
        MessageAction.Content = actionLabel;
        MessageAction.Visibility = actionLabel is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void MessageAction_Click(object sender, RoutedEventArgs e) => _msgAction?.Invoke();

    // ================= Actions =================

    /// <summary>
    /// Enter. Idle: call the typed number, or the next queued one.
    /// On a call: queue the typed number; with nothing typed, answer (or hang up if there's nothing to answer).
    /// </summary>
    private async Task EnterAction()
    {
        if (_blocked) return;
        var typed = PhoneNumber.Normalize(NumberBox.Text);
        bool busy = _status.Online && !_status.Idle;
        bool callStarting = DateTime.Now - _lastDialAt < TimeSpan.FromSeconds(3) && !busy;

        if (busy || callStarting)
        {
            if (typed.Length > 0) AddToQueue(typed);
            else if (busy) await Run(AnswerOrHangUp);
            return;
        }
        if (typed.Length > 0) await Dial(typed, fromQueue: false);
        else if (_queue.Count > 0) await CallNextNow();
        else FocusDialer();
    }

    private void AddToQueue(string number)
    {
        if (_queue.Add(number)) NumberBox.Clear();
        FocusDialer();
    }

    private async Task CallNextNow()
    {
        var next = _queue.TakeNext();
        if (next is not null) await Dial(next, fromQueue: true);
    }

    private async Task Dial(string number, bool fromQueue)
    {
        _lastNumber = number;
        _lastDialAt = DateTime.Now;
        _queue.NoteDialed(DateTime.Now);

        var error = await Run(async c => await c.Dial(number)
            ? null
            : "The phone didn't accept the call. Setup help is in Settings.");

        if (error is null)
        {
            if (!fromQueue) NumberBox.Clear();
            return;
        }
        _lastDialAt = DateTime.MinValue;
        _queue.NoteDialFailed();
        if (fromQueue)
        {
            _queue.PutBack(number);
            if (error.Length > 0) ShowMessage(error + " The queue is paused.");
        }
    }

    private static async Task<string?> AnswerOrHangUp(GrandstreamClient c)
    {
        if (await c.Operation("acceptcall")) return null;
        return await HangUp(c);
    }

    private static async Task<string?> HangUp(GrandstreamClient c)
    {
        if (await c.Operation("endcall")) return null;
        if (await c.Operation("cancel")) return null;
        return "The phone wouldn't hang up.";
    }

    private static async Task<string?> Answer(GrandstreamClient c) =>
        await c.Operation("acceptcall") ? null : "There's no incoming call to answer.";

    /// <summary>
    /// Runs one phone command. Returns null on success, else the error (already shown);
    /// "" if it couldn't start because another command is in flight.
    /// </summary>
    private async Task<string?> Run(Func<GrandstreamClient, Task<string?>> action)
    {
        var c = _client;
        if (c is null || _busy) return "";
        _busy = true;
        ShowMessage(null);
        Render();
        string? error;
        try
        {
            error = await action(c);
        }
        catch (Exception ex)
        {
            error = Friendly(ex);
        }
        finally
        {
            _busy = false;
        }
        if (error is not null) ShowMessage(error);
        await Poll();
        Render();
        return error;
    }

    private async void Call_Click(object sender, RoutedEventArgs e)
    {
        var typed = PhoneNumber.Normalize(NumberBox.Text);
        if (typed.Length > 0) await Dial(typed, fromQueue: false);
        else await CallNextNow();
    }

    private async void Answer_Click(object sender, RoutedEventArgs e) => await Run(Answer);

    private async void Hold_Click(object sender, RoutedEventArgs e) =>
        await Run(CallPhases.From(_status) == CallPhase.OnHold ? Resume : Hold);

    private static async Task<string?> Hold(GrandstreamClient c) =>
        await c.Operation("holdcall") ? null : "The phone didn't put the call on hold.";

    /// <summary>
    /// The CTI guide has no "resume" command, so press the phone's HOLD key (it toggles), and
    /// failing that the line key, which picks a held call back up.
    /// </summary>
    private static async Task<string?> Resume(GrandstreamClient c) =>
        await c.SendKey("HOLD") || await c.SendKey("LINE1") ? null : "The phone didn't take the call off hold.";
    private async void HangUp_Click(object sender, RoutedEventArgs e) => await Run(HangUp);

    private void Queue_Click(object sender, RoutedEventArgs e)
    {
        var typed = PhoneNumber.Normalize(NumberBox.Text);
        if (typed.Length > 0) AddToQueue(typed);
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        _queue.Paused = !_queue.Paused;
        if (!_queue.Paused) ShowMessage(null);
        RenderQueueStatus();
        FocusDialer(selectAll: false);
    }

    private void ClearQueue_Click(object sender, RoutedEventArgs e)
    {
        _queue.Clear();
        FocusDialer(selectAll: false);
    }

    private void RemoveQueued_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is QueueRow row) _queue.RemoveAt(row.Index);
        FocusDialer(selectAll: false);
    }

    private void Key_Click(object sender, RoutedEventArgs e)
    {
        var key = (string)((Button)sender).Content;
        NumberBox.Text += key;
        FocusDialer(selectAll: false);
    }

    private void FocusDialer(bool selectAll = true)
    {
        if (DialerView.Visibility != Visibility.Visible) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            NumberBox.Focus();
            Keyboard.Focus(NumberBox);
            if (selectAll) NumberBox.SelectAll();
            else NumberBox.CaretIndex = NumberBox.Text.Length;
        }));
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (SettingsView.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Enter) { e.Handled = true; SaveSettings(); }
            else if (e.Key == Key.Escape) { e.Handled = true; ShowSettings(false); }
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await EnterAction();
                break;
            case Key.Escape:
                e.Handled = true;
                // Esc clears the box; on an empty box it closes the flyout.
                if (NumberBox.Text.Length == 0) HideFlyout();
                else
                {
                    NumberBox.Clear();
                    FocusDialer();
                }
                break;
            default:
                // Typing anywhere in the window goes to the dialer.
                if (!NumberBox.IsKeyboardFocused)
                {
                    NumberBox.Focus();
                    NumberBox.CaretIndex = NumberBox.Text.Length;
                }
                break;
        }
    }

    /// <summary>Input mask: strip anything that isn't part of a number and format as typed.</summary>
    private void NumberBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_formatting)
        {
            var text = NumberBox.Text;
            var formatted = PhoneNumber.FormatAsYouType(text);
            if (formatted != text)
            {
                int caret = PhoneNumber.MapCaret(text, NumberBox.CaretIndex, formatted);
                _formatting = true;
                NumberBox.Text = formatted;
                NumberBox.CaretIndex = caret;
                _formatting = false;
            }
        }
        Render();
    }

    /// <summary>
    /// Backspace / Delete next to a bracket, space or dash removes the neighbouring digit, so
    /// the mask's punctuation never makes a key press look like it did nothing.
    /// </summary>
    private void NumberBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Back or Key.Delete) || NumberBox.SelectionLength > 0) return;
        var text = NumberBox.Text;
        int caret = NumberBox.CaretIndex;
        int i = e.Key == Key.Back ? caret - 1 : caret;
        if (i < 0 || i >= text.Length || PhoneNumber.IsAllowed(text[i])) return;

        int step = e.Key == Key.Back ? -1 : 1;
        while (i >= 0 && i < text.Length && !PhoneNumber.IsAllowed(text[i])) i += step;
        if (i < 0 || i >= text.Length) return;

        e.Handled = true;
        var removed = text.Remove(i, 1);
        var formatted = PhoneNumber.FormatAsYouType(removed);
        int newCaret = PhoneNumber.MapCaret(removed, e.Key == Key.Back ? i : caret, formatted);
        _formatting = true;
        NumberBox.Text = formatted;
        NumberBox.CaretIndex = newCaret;
        _formatting = false;
        Render();
    }

    private static string Friendly(Exception ex) => ex switch
    {
        PhoneException pe => pe.Message,
        TaskCanceledException => "The phone didn't respond.",
        HttpRequestException => "Can't reach the phone.",
        ObjectDisposedException => "Cancelled.",
        UriFormatException => "That phone address isn't valid.",
        _ => "Something went wrong: " + ex.Message,
    };

    // ================= Window chrome =================

    private void Close_Click(object sender, RoutedEventArgs e) => HideFlyout();

    private void Quit_Click(object sender, RoutedEventArgs e) => (Application.Current as App)?.ExitApp();

    protected override void OnClosing(CancelEventArgs e)
    {
        // Alt+F4 and the like just tuck it back into the tray; Exit is in the tray menu.
        if (!((Application.Current as App)?.Exiting ?? true))
        {
            e.Cancel = true;
            HideFlyout();
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _bar.Close();
        _lookupCts?.Cancel();
        _propel?.Dispose();
        _poll.Stop();
        _tick.Stop();
        _hook.Dispose();
        _client?.Dispose();
        base.OnClosed(e);
    }
}
