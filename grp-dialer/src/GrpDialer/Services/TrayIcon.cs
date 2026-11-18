using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
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

    /// <summary>A rounded square in <paramref name="fill"/> with a white handset, drawn at 32 px.</summary>
    private static Icon Draw(Color fill)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using (var path = RoundRect(new RectangleF(1, 1, 30, 30), 7))
            using (var brush = new SolidBrush(fill))
                g.FillPath(brush, path);

            using var font = new Font("Segoe MDL2 Assets", 15f, GraphicsUnit.Pixel);
            using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("", font, Brushes.White, new RectangleF(0, 1, 32, 32), fmt);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static void Free(Icon icon)
    {
        var h = icon.Handle;
        icon.Dispose();
        DestroyIcon(h);
    }

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
}
