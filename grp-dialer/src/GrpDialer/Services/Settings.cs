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

    public static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GrpDialer");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    [JsonIgnore]
    public string Password
    {
        get
        {
            if (string.IsNullOrEmpty(PasswordProtected)) return "";
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(PasswordProtected), null,
                    DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return ""; }
        }
        set
        {
            PasswordProtected = string.IsNullOrEmpty(value)
                ? ""
                : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null,
                    DataProtectionScope.CurrentUser));
        }
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
