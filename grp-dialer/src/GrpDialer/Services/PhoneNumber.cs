using System.Text.RegularExpressions;

namespace GrpDialer.Services;

public static class PhoneNumber
{
    /// <summary>Keep digits, *, # and a leading +. Pasted "(705) 555-1234" becomes "7055551234".</summary>
    public static string Normalize(string? raw)
    {
        var s = Regex.Replace((raw ?? "").Trim(), @"[^\d*#+]", "");
        return s.Length > 0 ? s[0] + s[1..].Replace("+", "") : s;
    }

    /// <summary>Readable form for North American numbers; anything else is shown as typed.</summary>
    public static string Display(string? number)
    {
        var n = number ?? "";
        if (!Regex.IsMatch(n, @"^\d+$")) return n;
        return n.Length switch
        {
            7 => $"{n[..3]}-{n[3..]}",
            10 => $"({n[..3]}) {n[3..6]}-{n[6..]}",
            11 when n[0] == '1' => $"1 ({n[1..4]}) {n[4..7]}-{n[7..]}",
            _ => n,
        };
    }
}

/// <summary>
/// What the line is doing, in plain words. The CTI guide documents only "idle";
/// other state names are matched loosely and anything unrecognised is just "on a call".
/// </summary>
public enum CallPhase { Offline, Idle, Incoming, Calling, Connected, OnHold, InUse }

public static class CallPhases
{
    public static CallPhase From(LineStatus s)
    {
        if (!s.Online) return CallPhase.Offline;
        if (s.Idle) return CallPhase.Idle;
        var r = s.RawState.ToLowerInvariant().Replace("_", "").Replace("-", "").Replace(" ", "");
        if (r.Contains("hold")) return CallPhase.OnHold;
        if (r is "calling" or "dialing" or "ringback" or "proceeding" or "outgoing" or "trying" || r.StartsWith("dial"))
            return CallPhase.Calling;
        if (r.Contains("ring") || r.Contains("incoming")) return CallPhase.Incoming;
        if (r is "connected" or "talking" or "active" or "incall" || r.Contains("connect") || r.Contains("talk"))
            return CallPhase.Connected;
        return CallPhase.InUse;
    }

    public static string Label(CallPhase p) => p switch
    {
        CallPhase.Offline => "Offline",
        CallPhase.Idle => "Ready",
        CallPhase.Incoming => "Incoming call",
        CallPhase.Calling => "Calling",
        CallPhase.Connected => "Connected",
        CallPhase.OnHold => "On hold",
        _ => "On a call",
    };

    /// <summary>Whether to offer Answer. Unknown states get it too: the phone just refuses if there's nothing to accept.</summary>
    public static bool CanAnswer(CallPhase p) => p is CallPhase.Incoming or CallPhase.InUse;
}
