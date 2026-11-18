using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrpDialer.Services;

/// <summary>
/// User settings, stored as JSON in %APPDATA%\GrpDialer\settings.json.
/// The phone password is encrypted with DPAPI (current Windows user only).
/// </summary>
public sealed class Settings
{
    public string PhoneHost { get; set; } = "";
    public bool UseHttps { get; set; }
    public string PasswordProtected { get; set; } = "";
    public bool GlobalNumpadEnter { get; set; } = true;
    /// <summary>Hide the flyout when another window is clicked, like the taskbar's own flyouts.</summary>
    public bool HideOnClickAway { get; set; } = true;
    public bool ShowKeypad { get; set; }
    public int QueueDelaySeconds { get; set; } = 3;
    /// <summary>PropelRx server for caller lookup (e.g. svr1); blank turns lookup off.</summary>
    public string PropelAddress { get; set; } = "";
    /// <summary>Windows account for PropelRx, only if this Windows user can't open it.</summary>
    public string PropelUser { get; set; } = "";
    public string PropelPasswordProtected { get; set; } = "";

    public static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GrpDialer");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    [JsonIgnore]
    public string Password
    {
        get => Unprotect(PasswordProtected);
        set => PasswordProtected = Protect(value);
    }

    [JsonIgnore]
    public string PropelPassword
    {
        get => Unprotect(PropelPasswordProtected);
        set => PropelPasswordProtected = Protect(value);
    }

    [JsonIgnore]
    public bool LookupEnabled => !string.IsNullOrWhiteSpace(PropelAddress);

    private static string Protect(string value) =>
        string.IsNullOrEmpty(value)
            ? ""
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));

    private static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null,
                DataProtectionScope.CurrentUser));
        }
        catch { return ""; }
    }

    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(PhoneHost);

    public static int ClampDelay(int seconds) => Math.Clamp(seconds, 0, 60);

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
                s.QueueDelaySeconds = ClampDelay(s.QueueDelaySeconds);
                return s;
            }
        }
        catch { /* corrupt file: fall back to defaults */ }
        return new Settings();
    }

    /// <summary>Writes via a temp file so a crash mid-save can't leave a truncated settings file.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Dir);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
