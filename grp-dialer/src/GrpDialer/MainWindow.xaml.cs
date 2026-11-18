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
    private DateTime? _busySince;
    private DateTime _lastDialAt = DateTime.MinValue;
    private string _lastNumber = "";
    private bool _polling;
    private bool _busy;                  // a user command is in flight
    private Action? _msgAction;
    private bool _wasCounting;           // queue countdown was showing last tick

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

        // Flyout: no taskbar button, sits above the clock, keeps its bottom edge there as it grows.
        SizeChanged += (_, _) => { if (IsVisible) Place(); };
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.WorkArea) && IsVisible) Dispatcher.BeginInvoke(Place);
        };
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
    }

    /// <param name="activate">false shows it without taking focus from what the user is doing.</param>
    public void ShowFlyout(bool activate = true)
    {
        Place();
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
        _busySince = null;

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
        SettingsGlyph.Text = show ? "" : "";
        SettingsBtn.ToolTip = show ? "Back to dialer" : "Settings";
        SettingsMsg.Visibility = Visibility.Collapsed;

        if (show)
        {
            HostBox.Text = _settings.PhoneHost;
            PasswordBox.Password = _settings.Password;
            HttpsCheck.IsChecked = _settings.UseHttps;
            GlobalKeyCheck.IsChecked = _settings.GlobalNumpadEnter;
            PopCheck.IsChecked = _settings.PopOnIncoming;
            ClickAwayCheck.IsChecked = _settings.HideOnClickAway;
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
        _settings.PopOnIncoming = PopCheck.IsChecked == true;
        _settings.HideOnClickAway = ClickAwayCheck.IsChecked == true;
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
        AdvancedGlyph.Text = open ? "" : "";
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

        if (isBusy && !wasBusy)
        {
            _busySince = DateTime.Now;
            // A call this app did not just place: someone is calling in, or the handset
            // was lifted. Put the controls in front of the user.
            bool placedByUs = DateTime.Now - _lastDialAt < TimeSpan.FromSeconds(10);
            if (!placedByUs)
            {
                if (_settings.PopOnIncoming) ShowFlyout();
            }
        }
        if (!isBusy) _busySince = null;

        Render();
    }

    /// <summary>Runs 4x a second: call timer, queue countdown.</summary>
    private void OnTick()
    {
        TimerText.Text = _busySince is DateTime since ? Elapsed(DateTime.Now - since) : "";

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

        // Header: one plain-words connection line. Details only in the tooltip.
        if (!configured) SetConnection(null, "Not set up", null);
        else if (s.Online) SetConnection(true, "Phone connected", _settings.PhoneHost);
        else if (!_everConnected && _connError.Length == 0) SetConnection(null, "Connecting…", _settings.PhoneHost);
        else SetConnection(false, "Phone offline", $"{_settings.PhoneHost}: {_connError}");

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
        StateText.Text = label;
        StateText.Foreground = Res(fg);
        StatePill.Background = Res(bg);
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

        // Entry
        NumberBox.Tag = busy ? "Type a number to queue it" : "Type a number";
        QueueBtn.Visibility = busy && hasText ? Visibility.Visible : Visibility.Collapsed;
        QueueBtn.IsEnabled = !_busy;

        // Actions
        bool canAnswer = busy && CallPhases.CanAnswer(phase);
        CallBtn.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        AnswerBtn.Visibility = canAnswer ? Visibility.Visible : Visibility.Collapsed;
        HangUpBtn.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(HangUpBtn, canAnswer ? 2 : 0);
        Grid.SetColumnSpan(HangUpBtn, canAnswer ? 1 : 3);

        CallBtnText.Text = !hasText && _queue.Count > 0 ? "Call next" : "Call";
        CallBtn.IsEnabled = s.Online && !_busy && (hasText || _queue.Count > 0);
        AnswerBtn.IsEnabled = !_busy;
        HangUpBtn.IsEnabled = !_busy;

        TipText.Text = busy
            ? (hasText ? "Enter adds it to the queue" : canAnswer ? "Enter answers · type to queue" : "Enter hangs up · type to queue")
            : "Enter calls · Esc clears";

        RenderQueueStatus();
        RenderTray(label, busy ? RemoteName.Text : "");
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

    private void SetConnection(bool? ok, string text, string? detail)
    {
        ConnDot.Fill = ok switch
        {
            true => Brushes.White,
            false => Res("Danger"),
            _ => Res("BrandSub"),
        };
        ConnText.Text = text;
        ConnText.Foreground = ok == true ? Brushes.White : Res("BrandSub");
        ConnPanel.ToolTip = detail;
    }

    private void RenderQueue()
    {
        int n = _queue.Count;
        QueuePanel.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
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
        MsgPanel.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
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

    private void NumberBox_TextChanged(object sender, TextChangedEventArgs e) => Render();

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
        _poll.Stop();
        _tick.Stop();
        _hook.Dispose();
        _client?.Dispose();
        base.OnClosed(e);
    }
}
