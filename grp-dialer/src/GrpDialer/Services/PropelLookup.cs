using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GrpDialer.Services;

public sealed record PropelPatient(long Id, string LastName, string FirstName, string? Phone);

/// <summary>
/// Caller lookup in PropelRx, the same read-only web API OTC Monitor uses
/// (<c>/PFSP/HTML/api/v1/SearchPatient</c>). PropelRx usually sits in IIS with Windows sign-in, so
/// requests answer the Negotiate/NTLM challenge with this Windows user, or a configured account.
/// Nothing is stored: results live in memory for the current call only.
/// </summary>
public sealed class PropelLookup : IDisposable
{
    public const string DefaultPath = "/PFSP/HTML/api/v1";
    private readonly HttpClient _http;
    private readonly string _base;

    public PropelLookup(string address, string user, string password)
    {
        _base = BaseUrl(address) ?? throw new UriFormatException("That PropelRx address isn't valid.");
        var h = new HttpClientHandler { PreAuthenticate = true };
        user = user.Trim();
        if (user.Length == 0) h.UseDefaultCredentials = true;
        else
        {
            int i = user.IndexOf('\\');
            h.Credentials = i > 0
                ? new NetworkCredential(user[(i + 1)..], password, user[..i])
                : new NetworkCredential(user, password);
        }
        _http = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(6) };
    }

    public void Dispose() => _http.Dispose();

    /// <summary>"svr1" or "192.168.1.5" becomes http://svr1/PFSP/HTML/api/v1; a full URL with a path is used as given.</summary>
    public static string? BaseUrl(string? address)
    {
        var a = (address ?? "").Trim().TrimEnd('/');
        if (a.Length == 0) return null;
        if (!a.Contains("://")) a = "http://" + a;
        if (!Uri.TryCreate(a, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https") || u.Host.Length == 0) return null;
        return u.AbsolutePath is "" or "/" ? u.GetLeftPart(UriPartial.Authority) + DefaultPath : a;
    }

    /// <summary>
    /// The 10 digits PropelRx stores for a North American number, or null when the caller's number
    /// can't be a patient's phone (an extension, a feature code, international).
    /// </summary>
    public static string? SearchDigits(string? number)
    {
        var d = Regex.Replace(number ?? "", @"\D", "");
        if (d.Length == 11 && d[0] == '1') d = d[1..];
        return d.Length == 10 ? d : null;
    }

    /// <summary>
    /// Patients whose phone is <paramref name="digits"/>. Tries the plain digits first (how PropelRx
    /// stores them), then the usual written forms. Throws on connection or sign-in failure.
    /// </summary>
    public async Task<List<PropelPatient>> FindByPhone(string digits, CancellationToken ct = default)
    {
        string[] forms = { digits, $"({digits[..3]}) {digits[3..6]}-{digits[6..]}", $"{digits[..3]}-{digits[3..6]}-{digits[6..]}" };
        foreach (var form in forms)
        {
            var found = ParsePatients(await Search(form, ct));
            if (found.Count > 0) return Matching(found, digits);
        }
        return new();
    }

    /// <summary>Connection check for Settings: a search for a number no one has.</summary>
    public async Task Test(CancellationToken ct = default) => ParsePatients(await Search("0000000000", ct));

    private async Task<string> Search(string phone, CancellationToken ct)
    {
        // Unused parameters are sent blank, as the PropelRx web client does.
        var url = $"{_base}/SearchPatient?LastName=&FirstName=&ProvincialHealthNumber=&PhoneNumber={Uri.EscapeDataString(phone)}&GroupName=&Address=&BirthDate=&_=";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.ParseAdd("application/xml");
        req.Headers.Accept.ParseAdd("text/xml;q=0.9");
        req.Headers.Accept.ParseAdd("application/json;q=0.8");
        using var resp = await _http.SendAsync(req, ct);
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new HttpRequestException("PropelRx refused the sign-in. Add a Windows account under Advanced.", null, resp.StatusCode);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Keeps the records whose phone is this number. If none of them show it, PropelRx matched on a
    /// phone field the reply doesn't include: trust a short list, but not a long one (that looks like
    /// the phone filter was ignored).
    /// </summary>
    public static List<PropelPatient> Matching(List<PropelPatient> found, string digits)
    {
        var unique = found.GroupBy(p => p.Id).Select(g => g.First()).ToList();
        var exact = unique.Where(p => SearchDigits(p.Phone) == digits).ToList();
        if (exact.Count > 0) return exact;
        return unique.Count <= 3 ? unique : new();
    }

    /// <summary>"Potentially JANE DOE", "Multiple linked profiles", or null to show nothing.</summary>
    public static string? Caption(IReadOnlyList<PropelPatient> matches) => matches.Count switch
    {
        0 => null,
        1 => "Potentially " + $"{matches[0].FirstName} {matches[0].LastName}".Trim().ToUpperInvariant(),
        _ => "Multiple linked profiles",
    };

    // ---------------- parsing (same formats OTC Monitor reads) ----------------

    /// <summary>A SearchPatient reply (ArrayOfWfPatientProfileModel) as XML (DataContract) or JSON.</summary>
    public static List<PropelPatient> ParsePatients(string body)
    {
        var t = body.Trim('﻿', ' ', '\t', '\r', '\n');
        if (t.Length == 0 || t == "null") return new();
        var list = new List<PropelPatient>();

        if (t.StartsWith('<'))
        {
            XDocument doc;
            try { doc = XDocument.Parse(t); }
            catch (System.Xml.XmlException ex) { throw new FormatException("PropelRx replied with something that isn't XML.", ex); }
            if (doc.Root is null || !doc.Root.Name.LocalName.StartsWith("ArrayOf", StringComparison.Ordinal))
                throw new FormatException("That address answered, but not like PropelRx.");
            foreach (var e in doc.Root.Elements().Where(e => e.Name.LocalName == "WfPatientProfileModel"))
            {
                string? V(string n)
                {
                    var x = e.Elements().FirstOrDefault(c => c.Name.LocalName == n);
                    if (x is null || x.Attributes().Any(a => a.Name.LocalName == "nil" && a.Value == "true")) return null;
                    return x.Value.Trim();
                }
                Add(list, V);
            }
            return list;
        }

        if (t.StartsWith('[') || t.StartsWith('{'))
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(t); }
            catch (JsonException ex) { throw new FormatException("PropelRx replied with something that isn't JSON.", ex); }
            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)   // tolerate {"Data":[...]} wrappers
                    root = root.EnumerateObject().Select(p => p.Value).FirstOrDefault(v => v.ValueKind == JsonValueKind.Array);
                if (root.ValueKind != JsonValueKind.Array) throw new FormatException("That address answered, but not like PropelRx.");
                foreach (var e in root.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
                {
                    var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in e.EnumerateObject())
                        map[p.Name] = p.Value.ValueKind switch
                        {
                            JsonValueKind.Null or JsonValueKind.Undefined => null,
                            JsonValueKind.String => p.Value.GetString()?.Trim(),
                            _ => p.Value.GetRawText(),
                        };
                    Add(list, n => map.TryGetValue(n, out var v) ? v : null);
                }
            }
            return list;
        }
        throw new FormatException("That address answered, but not like PropelRx.");
    }

    private static void Add(List<PropelPatient> list, Func<string, string?> r)
    {
        if (!long.TryParse(r("Id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0) return;
        list.Add(new PropelPatient(id, r("LastName") ?? "", r("FirstName") ?? "", r("Phone")));
    }
}
