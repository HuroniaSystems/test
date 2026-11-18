namespace GrpDialer.Services;

/// <summary>
/// Numbers waiting to be dialed. When the line is idle and nothing else is going on,
/// the next number is dialed after a short countdown, so a call ending leads straight
/// into the next one. No UI here: the window calls <see cref="Tick"/> a few times a second.
/// </summary>
public sealed class CallQueue
{
    /// <summary>After a dial, how long to wait for the line to leave idle before moving on.</summary>
    public static readonly TimeSpan StartGrace = TimeSpan.FromSeconds(10);

    private readonly List<string> _items = new();
    private DateTime? _awaitStartUntil;

    public IReadOnlyList<string> Items => _items;
    public int Count => _items.Count;
    public string? Next => _items.Count > 0 ? _items[0] : null;

    public bool Paused { get; set; }
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>When the next number will be dialed, while a countdown is running.</summary>
    public DateTime? DialAt { get; private set; }

    /// <summary>Why the queue is not counting down (for the status line), or null.</summary>
    public QueueWait Waiting { get; private set; } = QueueWait.None;

    public event Action? Changed;

    public bool Add(string number)
    {
        if (string.IsNullOrEmpty(number)) return false;
        _items.Add(number);
        Changed?.Invoke();
        return true;
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        _items.RemoveAt(index);
        if (_items.Count == 0) DialAt = null;
        Changed?.Invoke();
    }

    public void Clear()
    {
        _items.Clear();
        DialAt = null;
        Paused = false;
        Changed?.Invoke();
    }

    /// <summary>Takes the next number off the queue to dial it right now (user asked).</summary>
    public string? TakeNext()
    {
        if (_items.Count == 0) return null;
        var n = _items[0];
        _items.RemoveAt(0);
        DialAt = null;
        Paused = false;
        Changed?.Invoke();
        return n;
    }

    /// <summary>Any call was just placed: don't start the next one until this one has run its course.</summary>
    public void NoteDialed(DateTime now)
    {
        _awaitStartUntil = now + StartGrace;
        DialAt = null;
    }

    /// <summary>A dial attempt failed: stop waiting for it to start.</summary>
    public void NoteDialFailed() => _awaitStartUntil = null;

    /// <summary>A queued number could not be dialed: put it back on top and pause, so it doesn't loop.</summary>
    public void PutBack(string number)
    {
        _items.Insert(0, number);
        Paused = true;
        DialAt = null;
        _awaitStartUntil = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// Advances the countdown. Returns a number to dial now (already removed from the queue), or null.
    /// </summary>
    /// <param name="online">Phone answered the last status poll.</param>
    /// <param name="idle">Line is idle.</param>
    /// <param name="hold">Something else needs the line first (a command in flight, the user is typing a number).</param>
    public string? Tick(bool online, bool idle, bool hold, DateTime now)
    {
        if (!online) return Stop(QueueWait.Offline);
        if (!idle)
        {
            _awaitStartUntil = null;   // the call we placed has started (or another call came in)
            return Stop(QueueWait.OnCall);
        }
        if (_awaitStartUntil is DateTime until && now < until) return Stop(QueueWait.OnCall);
        _awaitStartUntil = null;

        if (_items.Count == 0) return Stop(QueueWait.None);
        if (Paused) return Stop(QueueWait.Paused);
        if (hold) return Stop(QueueWait.Hold);

        Waiting = QueueWait.None;
        if (DialAt is null)
        {
            DialAt = now + Delay;
            return null;
        }
        if (now < DialAt) return null;

        DialAt = null;
        var n = _items[0];
        _items.RemoveAt(0);
        _awaitStartUntil = now + StartGrace;
        Changed?.Invoke();
        return n;
    }

    private string? Stop(QueueWait why)
    {
        DialAt = null;
        Waiting = why;
        return null;
    }
}

public enum QueueWait { None, Offline, OnCall, Paused, Hold }
