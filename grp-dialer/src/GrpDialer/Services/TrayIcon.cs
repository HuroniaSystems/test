using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using M = System.Windows.Media;
using MI = System.Windows.Media.Imaging;
using WF = System.Windows.Forms;

namespace GrpDialer.Services;

/// <summary>
/// The notification-area icon. Left click toggles the dialer flyout; right click
/// has Open, Settings and Exit. The icon turns grey while the phone is offline.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WF.NotifyIcon _icon;
    private readonly Icon _online = Draw(Color.FromArgb(0x01, 0x6E, 0x4F));
    private readonly Icon _offline = Draw(Color.FromArgb(0x8A, 0x99, 0xA6));
    private bool _isOnline = true;

    public event Action? Toggle;
    public event Action? OpenSettings;
    public event Action? Exit;

    public TrayIcon()
    {
        var menu = new WF.ContextMenuStrip();
        menu.Items.Add("Open dialer", null, (_, _) => Toggle?.Invoke());
        menu.Items.Add("Settings", null, (_, _) => OpenSettings?.Invoke());
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Exit GRP Dialer", null, (_, _) => Exit?.Invoke());
        ((WF.ToolStripMenuItem)menu.Items[0]).Font = new Font(menu.Font, FontStyle.Bold);

        _icon = new WF.NotifyIcon { Icon = _online, Text = "GRP Dialer", ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) => { if (e.Button == WF.MouseButtons.Left) Toggle?.Invoke(); };
    }

    /// <summary>Tooltip text (Windows cuts it at 127 characters) and online/offline colour.</summary>
    public void SetStatus(string text, bool online)
    {
        text = "GRP Dialer · " + text;
        if (text.Length > 127) text = text[..127];
        if (_icon.Text != text) _icon.Text = text;
        if (online != _isOnline)
        {
            _isOnline = online;
            _icon.Icon = online ? _online : _offline;
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        Free(_online);
        Free(_offline);
    }

    /// <summary>A rounded square in <paramref name="fill"/> with the app's solid white phone, drawn at 32 px.</summary>
    private static Icon Draw(Color fill)
    {
        var dv = new M.DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var bg = new M.SolidColorBrush(M.Color.FromRgb(fill.R, fill.G, fill.B));
            dc.DrawRoundedRectangle(bg, null, new System.Windows.Rect(1, 1, 30, 30), 7, 7);
            dc.PushTransform(new M.TranslateTransform(7, 7));
            dc.PushTransform(new M.ScaleTransform(18 / 24.0, 18 / 24.0));
            dc.DrawGeometry(M.Brushes.White, null, GrpDialer.Icons.Phone);
            dc.Pop();
            dc.Pop();
        }
        var rtb = new MI.RenderTargetBitmap(32, 32, 96, 96, M.PixelFormats.Pbgra32);
        rtb.Render(dv);
        var png = new MI.PngBitmapEncoder();
        png.Frames.Add(MI.BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        png.Save(ms);
        ms.Position = 0;
        using var bmp = new Bitmap(ms);
        return Icon.FromHandle(bmp.GetHicon());
    }

    private static void Free(Icon icon)
    {
        var h = icon.Handle;
        icon.Dispose();
        DestroyIcon(h);
    }

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
}
