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

    /// <summary>Longest number the box accepts (E.164 is 15 digits, plus a leading + or code).</summary>
    public const int MaxChars = 16;

    /// <summary>Characters the number box lets through: digits, * and # (phone codes), + for international.</summary>
    public static bool IsAllowed(char c) => c is >= '0' and <= '9' or '*' or '#' or '+';

    /// <summary>
    /// Input mask for the number box: drops letters and stray symbols, and formats North
    /// American numbers while they're typed. Short extensions stay as typed.
    /// <code>
    /// 201        -> 201
    /// 7055551    -> 705-5551
    /// 70555512   -> (705) 555-12
    /// 7055551234 -> (705) 555-1234
    /// 17055551234 -> 1 (705) 555-1234
    /// </code>
    /// Anything with *, #, + or more than 11 digits is shown cleaned but unformatted.
    /// </summary>
    public static string FormatAsYouType(string? raw)
    {
        var n = Normalize(raw);
        if (n.Length > MaxChars) n = n[..MaxChars];
        if (!Regex.IsMatch(n, @"^\d+$")) return n;
        if (n[0] == '1' && n.Length is >= 8 and <= 11) return "1 " + Group(n[1..]);
        return n.Length <= 10 ? Group(n) : n;
    }

    // 1-4 digits as typed, 5-7 as 555-1234, 8-10 as (705) 555-1234.
    private static string Group(string d) => d.Length switch
    {
        <= 4 => d,
        <= 7 => $"{d[..3]}-{d[3..]}",
        _ => $"({d[..3]}) {d[3..6]}-{d[6..]}",
    };

    /// <summary>
    /// Where the caret goes after <see cref="FormatAsYouType"/>: just after the same number of
    /// digits as were before it, so typing in the middle doesn't throw the caret to the end.
    /// </summary>
    public static int MapCaret(string before, int caret, string after)
    {
        int keep = 0;
        for (int i = 0; i < Math.Min(caret, before.Length); i++)
            if (IsAllowed(before[i])) keep++;
        if (keep == 0) return 0;
        for (int i = 0; i < after.Length; i++)
            if (IsAllowed(after[i]) && --keep == 0) return i + 1;
        return after.Length;
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
