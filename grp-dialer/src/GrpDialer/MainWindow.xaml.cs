using System.ComponentModel;
using System.Globalization;
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

    private LineStatus _status = LineStatus.Offline;
    private DateTime? _busySince;
    private DateTime _lastDialAt = DateTime.MinValue;
    private string _lastNumber = "";
    private bool _polling;
    private bool _busy;          // a user command is in flight

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
            ? new GrandstreamClient(_settings.PhoneHost, _settings.UseHttps, _settings.Password)
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

    private bool ReadHost(out string host)
    {
        host = HostBox.Text.Trim();
        if (host.Length > 0) return true;
        SettingsMessage("Enter the phone's IP address.", error: true);
        return false;
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveSettings();

    private void SaveSettings()
    {
        if (!ReadHost(out var host)) return;
        _settings.PhoneHost = host;
        _settings.Password = PasswordBox.Password;
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
        if (!ReadHost(out var host)) return;
        SettingsMessage("Testing...", error: false);
        using var test = new GrandstreamClient(host, HttpsCheck.IsChecked == true, PasswordBox.Password);
        try
        {
            var s = await test.GetStatus();
            SettingsMessage($"Connected. Line state: {s.RawState}.", error: false);
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
            UpdateStatus(LineStatus.Offline);
        }
        finally
        {
            _polling = false;
        }
        TickTimer();
    }

    private void UpdateStatus(LineStatus s)
    {
        bool wasBusy = _status.Online && !_status.Idle;
        bool isBusy = s.Online && !s.Idle;
        _status = s;

        if (isBusy && !wasBusy)
        {
            _busySince = DateTime.Now;
            // A call that this app did not just place: someone is calling in, or the
            // handset was lifted. Either way, put the controls in front of the user.
            bool placedByUs = DateTime.Now - _lastDialAt < TimeSpan.FromSeconds(10);
            if (!placedByUs)
            {
                if (_settings.PopOnIncoming) WindowFocus.ForceForeground(this);
                else WindowFocus.Flash(this);
            }
        }
        if (!isBusy)
        {
            _busySince = null;
            if (wasBusy && s.Online) NumberBox.Clear();
        }

        Render();
    }

    private void Render()
    {
        var s = _status;
        bool busy = s.Online && !s.Idle;

        StateText.Text = !s.Online ? "Offline" : TitleCase(s.RawState);
        var (fg, bg, border) = busy ? ("Success", "SuccessBg", "SuccessBorder") : ("TextDim", "BgUi", "Border");
        StateText.Foreground = (Brush)FindResource(fg);
        StateBadge.Background = (Brush)FindResource(bg);
        StateBadge.BorderBrush = (Brush)FindResource(border);

        string number = s.RemoteNumber.Length > 0 ? s.RemoteNumber
            : busy && DateTime.Now - _lastDialAt < TimeSpan.FromMinutes(5) ? _lastNumber : "";
        RemoteName.Text = !busy ? "No active call"
            : s.RemoteName.Length > 0 ? s.RemoteName
            : number.Length > 0 ? number
            : "In use";
        RemoteNumber.Text = busy && s.RemoteName.Length > 0 ? number : "";
        RemoteNumber.Visibility = RemoteNumber.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        PrimaryBtn.Content = busy ? "Answer" : "Call";
        PrimaryBtn.IsEnabled = s.Online && !_busy && (busy || Normalize(NumberBox.Text).Length > 0);
        HangUpBtn.IsEnabled = s.Online && !_busy && busy;
    }

    private void TickTimer()
    {
        TimerText.Text = _busySince is DateTime since ? (DateTime.Now - since).ToString(@"m\:ss") : "";
    }

    private void SetConnection(bool? ok, string text)
    {
        ConnDot.Fill = (Brush)FindResource(ok switch { true => "Success", false => "Danger", _ => "TextFaint" });
        ConnText.Text = text;
    }

    private static string TitleCase(string raw) =>
        raw.Length == 0 ? "" : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(raw.ToLowerInvariant());

    // ---------- Actions ----------

    /// <summary>
    /// Enter: idle -> call. In use -> acceptcall; the phone answers {"response":"error"}
    /// when there is nothing to accept, in which case the call is ended instead.
    /// </summary>
    private Task EnterAction() =>
        _status.Online && !_status.Idle ? Run(AnswerOrHangUp) : Call();

    private static async Task<string?> AnswerOrHangUp(GrandstreamClient c)
    {
        if (await c.Operation("acceptcall")) return null;
        return await HangUp(c);
    }

    private static async Task<string?> HangUp(GrandstreamClient c)
    {
        if (await c.Operation("endcall")) return null;
        if (await c.Operation("cancel")) return null;
        return "Phone refused to hang up.";
    }

    private static async Task<string?> Answer(GrandstreamClient c) =>
        await c.Operation("acceptcall") ? null : "No incoming call to answer.";

    private Task Call()
    {
        var number = Normalize(NumberBox.Text);
        if (number.Length == 0) { FocusDialer(); return Task.CompletedTask; }
        _lastNumber = number;
        _lastDialAt = DateTime.Now;
        return Run(async c => await c.Dial(number)
            ? null
            : "Phone refused the call. On GRP260x, enable Click-To-Dial under Phone Settings > Call Settings.");
    }

    /// <summary>Runs a phone command. The command returns an error message, or null on success.</summary>
    private async Task Run(Func<GrandstreamClient, Task<string?>> action)
    {
        if (_client is null || _busy) return;
        _busy = true;
        Render();
        ShowError(null);
        try
        {
            ShowError(await action(_client));
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
        if (_status.Online && !_status.Idle) await Run(Answer);
        else await Call();
    }

    private async void HangUp_Click(object sender, RoutedEventArgs e) => await Run(HangUp);

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
