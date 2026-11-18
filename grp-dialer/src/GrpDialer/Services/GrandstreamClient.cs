using System.Net.Http;
using System.Text.Json;

namespace GrpDialer.Services;

/// <summary>
/// Line status from api-get_line_status. The CTI guide documents only "idle" as a
/// state value, so the app only distinguishes idle from not-idle and shows the
/// phone's own state string verbatim.
/// </summary>
public sealed record LineStatus(bool Online, bool Idle, string RawState, string RemoteName, string RemoteNumber)
{
    public static readonly LineStatus Offline = new(false, true, "", "", "");
}

/// <summary>
/// Grandstream GRP26xx CTI API, per "GRP26xx Series - CTI Guide":
///   GET /cgi-bin/api-get_line_status?passcode=PW
///   GET /cgi-bin/api-make_call?passcode=PW&amp;phonenumber=NUMBER
///   GET /cgi-bin/api-phone_operation?passcode=PW&amp;cmd=endcall|holdcall|acceptcall|rejectcall|cancel
/// Success: {"response":"success","body":...}. Refused: {"response":"error","body":false|"failed"|"unknown"}.
/// </summary>
public sealed class GrandstreamClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _base;
    private readonly string _passcode;

    public GrandstreamClient(string host, bool https, string password)
    {
        var handler = new HttpClientHandler
        {
            // Desk phones ship self-signed certs; trust only for this client.
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(4) };
        host = host.Trim().TrimEnd('/');
        if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) host = host[7..];
        if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) host = host[8..];
        _base = $"{(https ? "https" : "http")}://{host}/cgi-bin/";
        _passcode = password;
    }

    /// <summary>api-make_call. Returns false if the phone refused.</summary>
    public Task<bool> Dial(string number, CancellationToken ct = default) =>
        Command("api-make_call", $"phonenumber={Uri.EscapeDataString(number)}", ct);

    /// <summary>api-phone_operation. Returns false if the phone refused (e.g. nothing to accept).</summary>
    public Task<bool> Operation(string cmd, CancellationToken ct = default) =>
        Command("api-phone_operation", $"cmd={cmd}", ct);

    /// <summary>Presses phone keys (CTI api-send_key), e.g. "HOLD" or "LINE1"; several joined with ':'.</summary>
    public Task<bool> SendKey(string keys, CancellationToken ct = default) =>
        Command("api-send_key", $"keys={keys}", ct);

    public async Task<LineStatus> GetStatus(CancellationToken ct = default)
    {
        using var doc = await Get("api-get_line_status", "", ct);
        var root = doc.RootElement;
        if (!IsSuccess(root)) throw new PhoneException("Phone refused the request. Check the password.");

        if (!root.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.Array)
            return new LineStatus(true, true, "idle", "", "");

        // First non-idle line wins; the phone lists lines in order.
        foreach (var line in body.EnumerateArray())
        {
            var state = Str(line, "state");
            if (!string.Equals(state, "idle", StringComparison.OrdinalIgnoreCase) && state.Length > 0)
                return new LineStatus(true, false, state, Str(line, "remotename"), Str(line, "remotenumber"));
        }
        return new LineStatus(true, true, "idle", "", "");
    }

    private static string Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool IsSuccess(JsonElement root) =>
        root.TryGetProperty("response", out var r) &&
        string.Equals(r.GetString(), "success", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> Command(string function, string query, CancellationToken ct)
    {
        using var doc = await Get(function, query, ct);
        return IsSuccess(doc.RootElement);
    }

    private async Task<JsonDocument> Get(string function, string query, CancellationToken ct)
    {
        var url = $"{_base}{function}?passcode={Uri.EscapeDataString(_passcode)}";
        if (query.Length > 0) url += "&" + query;

        using var resp = await _http.GetAsync(url, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new PhoneException($"Phone returned HTTP {(int)resp.StatusCode}. Is Action URI Support enabled?");
        try { return JsonDocument.Parse(text); }
        catch (JsonException) { throw new PhoneException("Unexpected reply. Is Action URI Support enabled?"); }
    }

    public void Dispose() => _http.Dispose();
}

public sealed class PhoneException(string message) : Exception(message);
