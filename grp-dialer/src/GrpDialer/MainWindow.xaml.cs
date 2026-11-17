using System.ComponentModel;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GrpDialer.Services;

namespace GrpDialer;

public partial class MainWindow : Window
{
    private readonly Settings _settings = Settings.Load();
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly NumpadEnterHook _hook;
    private GrandstreamClient? _client;

    private LineStatus _status = new(CallState.Unknown, "", "", 0);
    private DateTime? _activeSince;
    private bool _polling;
    private bool _busy;          // a user command is in flight
    private string _lastNumber = "";

    public MainWindow()
    {
        InitializeComponent();

        _hook = new NumpadEnterHook(() => IsActive);
        _hook.Pressed += () => Dispatcher.BeginInvoke(new Action(OnGlobalNumpadEnter));

        if (_settings.Left is double l && _settings.Top is double t &&
            l >= SystemParameters.VirtualScreenLeft && t >= SystemParameters.VirtualScreenTop &&
            l < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50 &&
            t < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = l;
            Top = t;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        _poll.Tick += async (_, _) => await Poll();
        Loaded += (_, _) => ApplySettings(openSettingsIfEmpty: true);
    }

    // ---------- Settings ----------

    private void ApplySettings(bool openSettingsIfEmpty = false)
    {
        Topmost = _settings.AlwaysOnTop;

        if (_settings.GlobalNumpadEnter) _hook.Start(); else _hook.Stop();

        _client?.Dispose();
        _client = _settings.IsConfigured
            ? new GrandstreamClient(_settings.PhoneHost, _settings.UseHttps, _settings.Password, _settings.Account)
            : null;

        if (_client is null)
        {
            _poll.Stop();
            SetConnection(null, "Not configured");
            if (openSettingsIfEmpty) ShowSettings(true);
            return;
        }

        SetConnection(null, "Connecting...");
        _poll.Start();
        _ = Poll();
        FocusDialer();
    }

    private void ShowSettings(bool show)
    {
        DialerView.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        SettingsView.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SettingsBtn.Content = show ? "Dialer" : "Settings";
        SettingsMsg.Visibility = Visibility.Collapsed;

        if (show)
        {
            HostBox.Text = _settings.PhoneHost;
            PasswordBox.Password = _settings.Password;
            AccountBox.Text = (_settings.Account + 1).ToString();
            HttpsCheck.IsChecked = _settings.UseHttps;
            GlobalKeyCheck.IsChecked = _settings.GlobalNumpadEnter;
            PopCheck.IsChecked = _settings.PopOnIncoming;
            TopCheck.IsChecked = _settings.AlwaysOnTop;
            HostBox.Focus();
            HostBox.SelectAll();
        }
        else
        {
            FocusDialer();
        }
    }

    private bool ReadSettingsForm(out string host, out int account)
    {
        host = HostBox.Text.Trim();
        account = 0;
        if (host.Length == 0)
        {
            SettingsMessage("Enter the phone's IP address.", error: true);
            return false;
        }
        if (!int.TryParse(AccountBox.Text.Trim(), out var acct) || acct < 1 || acct > 16)
        {
            SettingsMessage("Account must be a number from 1 to 16.", error: true);
            return false;
        }
        account = acct - 1;
        return true;
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveSettings();

    private void SaveSettings()
    {
        if (!ReadSettingsForm(out var host, out var account)) return;
        _settings.PhoneHost = host;
        _settings.Password = PasswordBox.Password;
        _settings.Account = account;
        _settings.UseHttps = HttpsCheck.IsChecked == true;
        _settings.GlobalNumpadEnter = GlobalKeyCheck.IsChecked == true;
        _settings.PopOnIncoming = PopCheck.IsChecked == true;
        _settings.AlwaysOnTop = TopCheck.IsChecked == true;
        _settings.Save();
        ShowSettings(false);
        ApplySettings();
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (!ReadSettingsForm(out var host, out var account)) return;
        SettingsMessage("Testing...", error: false);
        using var test = new GrandstreamClient(host, HttpsCheck.IsChecked == true, PasswordBox.Password, account);
        try
        {
            var s = await test.GetStatus();
            SettingsMessage($"Connected. Line is {Describe(s.State).ToLowerInvariant()}.", error: false);
        }
        catch (Exception ex)
        {
            SettingsMessage(Friendly(ex), error: true);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.IsConfigured) ShowSettings(false);
        else Close();
    }

    private void SettingsMessage(string text, bool error)
    {
        SettingsMsg.Text = text;
        SettingsMsg.Foreground = (Brush)FindResource(error ? "Danger" : "TextDim");
        SettingsMsg.Visibility = Visibility.Visible;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        bool open = SettingsView.Visibility != Visibility.Visible;
        if (!open && !_settings.IsConfigured) return;
        ShowSettings(open);
    }

    // ---------- Polling ----------

    private async Task Poll()
    {
        if (_polling || _client is null) return;
        _polling = true;
        try
        {
            var s = await _client.GetStatus();
            SetConnection(true, _settings.PhoneHost);
            UpdateStatus(s);
        }
        catch (Exception ex)
        {
            SetConnection(false, Friendly(ex));
            UpdateStatus(new LineStatus(CallState.Unknown, "", "", 0));
        }
        finally
        {
            _polling = false;
        }
        TickTimer();
    }

    private void UpdateStatus(LineStatus s)
    {
        var prev = _status.State;
        _status = s;

        if (s.State == CallState.Active && prev != CallState.Active && prev != CallState.OnHold)
            _activeSince = DateTime.Now;
        if (s.State is CallState.Idle or CallState.Unknown or CallState.Incoming or CallState.Outgoing)
            _activeSince = null;

        if (s.State == CallState.Incoming && prev != CallState.Incoming)
        {
            if (_settings.PopOnIncoming) WindowFocus.ForceForeground(this);
            else WindowFocus.Flash(this);
        }

        // Once a call we placed ends, clear the dialer for the next one.
        if (s.State == CallState.Idle && prev is CallState.Active or CallState.OnHold or CallState.Outgoing)
            NumberBox.Clear();

        Render();
    }

    private void Render()
    {
        var s = _status;
        StateText.Text = Describe(s.State);
        var (fg, bg, border) = s.State switch
        {
            CallState.Incoming => ("Warn", "WarnBg", "Warn"),
            CallState.Active => ("Success", "SuccessBg", "SuccessBorder"),
            CallState.Outgoing => ("Info", "AccentBg", "Accent"),
            CallState.OnHold => ("Warn", "WarnBg", "Warn"),
            _ => ("TextDim", "BgUi", "Border"),
        };
        StateText.Foreground = (Brush)FindResource(fg);
        StateBadge.Background = (Brush)FindResource(bg);
        StateBadge.BorderBrush = (Brush)FindResource(border);

        bool inCall = s.State is CallState.Active or CallState.OnHold or CallState.Outgoing or CallState.Incoming;
        string number = s.RemoteNumber.Length > 0 ? s.RemoteNumber : (s.State == CallState.Outgoing ? _lastNumber : "");
        RemoteName.Text = !inCall ? "No active call"
            : s.RemoteName.Length > 0 ? s.RemoteName
            : number.Length > 0 ? number
            : "Unknown caller";
        RemoteNumber.Text = inCall && s.RemoteName.Length > 0 ? number : "";
        RemoteNumber.Visibility = RemoteNumber.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        bool online = s.State != CallState.Unknown;
        PrimaryBtn.Content = s.State == CallState.Incoming ? "Answer" : "Call";
        PrimaryBtn.IsEnabled = online && !_busy &&
            (s.State == CallState.Incoming || (s.State == CallState.Idle && Normalize(NumberBox.Text).Length > 0));
        HangUpBtn.Content = s.State == CallState.Incoming ? "Decline" : "Hang up";
        HangUpBtn.IsEnabled = online && !_busy && inCall;
    }

    private void TickTimer()
    {
        TimerText.Text = _activeSince is DateTime since
            ? (DateTime.Now - since).ToString(@"m\:ss")
            : "";
    }

    private void SetConnection(bool? ok, string text)
    {
        ConnDot.Fill = (Brush)FindResource(ok switch { true => "Success", false => "Danger", _ => "TextFaint" });
        ConnText.Text = text;
    }

    private static string Describe(CallState s) => s switch
    {
        CallState.Idle => "Idle",
        CallState.Incoming => "Incoming call",
        CallState.Outgoing => "Calling",
        CallState.Active => "Connected",
        CallState.OnHold => "On hold",
        _ => "Offline",
    };

    // ---------- Actions ----------

    /// <summary>The single Enter action: answer, hang up, or call, by state.</summary>
    private async Task EnterAction()
    {
        switch (_status.State)
        {
            case CallState.Incoming:
                await Run(c => c.Answer());
                break;
            case CallState.Active or CallState.OnHold or CallState.Outgoing:
                await Run(c => c.HangUp());
                break;
            default:
                await Call();
                break;
        }
    }

    private async Task Call()
    {
        var number = Normalize(NumberBox.Text);
        if (number.Length == 0) { FocusDialer(); return; }
        _lastNumber = number;
        await Run(c => c.Dial(number));
    }

    private async Task Run(Func<GrandstreamClient, Task> action)
    {
        if (_client is null || _busy) return;
        _busy = true;
        Render();
        ShowError(null);
        try
        {
            await action(_client);
        }
        catch (Exception ex)
        {
            ShowError(Friendly(ex));
        }
        finally
        {
            _busy = false;
        }
        await Poll();
    }

    private async void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (_status.State == CallState.Incoming) await Run(c => c.Answer());
        else await Call();
    }

    private async void HangUp_Click(object sender, RoutedEventArgs e)
    {
        if (_status.State == CallState.Incoming) await Run(c => c.Reject());
        else await Run(c => c.HangUp());
    }

    private void Key_Click(object sender, RoutedEventArgs e)
    {
        var key = (string)((Button)sender).Content;
        NumberBox.Text += key;
        FocusDialer(selectAll: false);
    }

    private void OnGlobalNumpadEnter()
    {
        if (SettingsView.Visibility == Visibility.Visible) ShowSettings(false);
        WindowFocus.ForceForeground(this);
        FocusDialer();
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
            else if (e.Key == Key.Escape && _settings.IsConfigured) { e.Handled = true; ShowSettings(false); }
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
                NumberBox.Clear();
                FocusDialer();
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

    private void NumberBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        NumberHint.Visibility = NumberBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Render();
    }

    private void ShowError(string? text)
    {
        ErrorText.Text = text ?? "";
        ErrorText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Keep digits, *, # and a leading +. Pasted "(705) 555-1234" becomes "7055551234".</summary>
    private static string Normalize(string raw)
    {
        var s = Regex.Replace(raw.Trim(), @"[^\d*#+]", "");
        return s.Length > 0 ? s[0] + s[1..].Replace("+", "") : s;
    }

    private static string Friendly(Exception ex) => ex switch
    {
        PhoneException pe => pe.Message,
        TaskCanceledException => "Phone did not respond.",
        HttpRequestException => "Can't reach the phone.",
        _ => ex.Message,
    };

    // ---------- Window chrome ----------

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is Border or Grid or StackPanel)
            DragMove();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (WindowState == WindowState.Normal)
        {
            _settings.Left = Left;
            _settings.Top = Top;
            try { _settings.Save(); } catch { /* best effort */ }
        }
        _poll.Stop();
        _hook.Dispose();
        _client?.Dispose();
        base.OnClosing(e);
    }
}
