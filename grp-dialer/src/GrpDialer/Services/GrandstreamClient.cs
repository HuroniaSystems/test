using System.Net.Http;
using System.Text.Json;

namespace GrpDialer.Services;

public enum CallState { Unknown, Idle, Incoming, Outgoing, Active, OnHold }

public sealed record LineStatus(CallState State, string RemoteName, string RemoteNumber, int Line);

/// <summary>
/// Thin wrapper over the Grandstream GRP26xx CTI HTTP API.
///   GET /cgi-bin/api-make_call?phonenumber=..&amp;account=..&amp;passcode=..
///   GET /cgi-bin/api-phone_operation?cmd=acceptcall|endcall|rejectcall|...&amp;passcode=..
///   GET /cgi-bin/api-get_line_status?passcode=..
/// Replies are JSON: {"response":"success","body":...}.
/// Phone side: enable Click-To-Dial (Settings > Call Features) and use the admin password.
/// </summary>
public sealed class GrandstreamClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _base;
    private readonly string _passcode;
    private readonly int _account;

    public GrandstreamClient(string host, bool https, string password, int account)
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
        _account = account;
    }

    public Task Dial(string number, CancellationToken ct = default) =>
        Command($"api-make_call?phonenumber={Uri.EscapeDataString(number)}&account={_account}", ct);

    public Task Answer(CancellationToken ct = default) => Operation("acceptcall", ct);
    public Task Reject(CancellationToken ct = default) => Operation("rejectcall", ct);
    public Task HangUp(CancellationToken ct = default) => Operation("endcall", ct);

    private Task Operation(string cmd, CancellationToken ct) =>
        Command($"api-phone_operation?cmd={cmd}", ct);

    public async Task<LineStatus> GetStatus(CancellationToken ct = default)
    {
        using var doc = await Get("api-get_line_status", ct);
        var root = doc.RootElement;
        EnsureSuccess(root);

        if (!root.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.Array)
            return new LineStatus(CallState.Idle, "", "", 0);

        // Pick the most interesting line: incoming beats active beats outgoing.
        LineStatus best = new(CallState.Idle, "", "", 0);
        foreach (var line in body.EnumerateArray())
        {
            var state = MapState(Str(line, "state"));
            var current = new LineStatus(state, Str(line, "remotename"), Str(line, "remotenumber"),
                line.TryGetProperty("line", out var l) && l.TryGetInt32(out var n) ? n : 0);
            if (Rank(current.State) > Rank(best.State)) best = current;
        }
        return best;
    }

    private static int Rank(CallState s) => s switch
    {
        CallState.Incoming => 4,
        CallState.Active => 3,
        CallState.OnHold => 2,
        CallState.Outgoing => 1,
        _ => 0,
    };

    private static CallState MapState(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "idle" or "" => CallState.Idle,
        "ringing" or "incoming" => CallState.Incoming,
        "connected" or "talking" or "conference" or "transfer" or "transferring" => CallState.Active,
        "onhold" or "hold" or "holding" => CallState.OnHold,
        "dialing" or "calling" or "proceeding" or "ringback" or "outgoing" => CallState.Outgoing,
        "failed" or "busy" => CallState.Idle,
        _ => CallState.Active,
    };

    private static string Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private async Task Command(string pathAndQuery, CancellationToken ct)
    {
        using var doc = await Get(pathAndQuery, ct);
        EnsureSuccess(doc.RootElement);
    }

    private async Task<JsonDocument> Get(string pathAndQuery, CancellationToken ct)
    {
        var sep = pathAndQuery.Contains('?') ? '&' : '?';
        var url = $"{_base}{pathAndQuery}{sep}passcode={Uri.EscapeDataString(_passcode)}";
        using var resp = await _http.GetAsync(url, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new PhoneException($"Phone returned HTTP {(int)resp.StatusCode}.");
        try { return JsonDocument.Parse(text); }
        catch (JsonException) { throw new PhoneException("Unexpected reply. Is this a Grandstream phone?"); }
    }

    private static void EnsureSuccess(JsonElement root)
    {
        if (root.TryGetProperty("response", out var r) &&
            !string.Equals(r.GetString(), "success", StringComparison.OrdinalIgnoreCase))
        {
            var detail = root.TryGetProperty("body", out var b) ? b.ToString() : "";
            throw new PhoneException(detail.Contains("auth", StringComparison.OrdinalIgnoreCase)
                ? "Wrong password."
                : $"Phone refused the request ({detail}).");
        }
    }

    public void Dispose() => _http.Dispose();
}

public sealed class PhoneException(string message) : Exception(message);
