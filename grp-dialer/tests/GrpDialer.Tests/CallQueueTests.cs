using GrpDialer.Services;

namespace GrpDialer.Tests;

public class CallQueueTests
{
    private static readonly DateTime T0 = new(2026, 11, 18, 9, 0, 0);
    private static DateTime At(double s) => T0.AddSeconds(s);

    private static CallQueue Queue(params string[] numbers)
    {
        var q = new CallQueue { Delay = TimeSpan.FromSeconds(3) };
        foreach (var n in numbers) q.Add(n);
        return q;
    }

    [Fact]
    public void Waits_while_on_a_call()
    {
        var q = Queue("111");
        for (int i = 0; i < 20; i++)
            Assert.Null(q.Tick(online: true, idle: false, hold: false, At(i)));
        Assert.Equal(QueueWait.OnCall, q.Waiting);
        Assert.Equal(1, q.Count);
    }

    [Fact]
    public void Dials_next_after_countdown_when_call_ends()
    {
        var q = Queue("111", "222");
        Assert.Null(q.Tick(true, false, false, At(0)));   // on a call
        Assert.Null(q.Tick(true, true, false, At(1)));    // call ended: countdown starts
        Assert.Equal(At(4), q.DialAt);
        Assert.Null(q.Tick(true, true, false, At(3.9)));
        Assert.Equal("111", q.Tick(true, true, false, At(4)));
        Assert.Equal(new[] { "222" }, q.Items);
    }

    [Fact]
    public void Does_not_chain_dial_before_the_placed_call_starts()
    {
        var q = Queue("111", "222");
        q.Tick(true, true, false, At(0));
        Assert.Equal("111", q.Tick(true, true, false, At(3)));
        // The line still reports idle for a moment after dialing: 222 must wait.
        for (double s = 3.25; s < 3 + CallQueue.StartGrace.TotalSeconds; s += 0.25)
            Assert.Null(q.Tick(true, true, false, At(s)));
        Assert.Equal(1, q.Count);
    }

    [Fact]
    public void Moves_on_if_placed_call_never_started()
    {
        var q = Queue("111", "222");
        q.Tick(true, true, false, At(0));
        Assert.Equal("111", q.Tick(true, true, false, At(3)));
        var after = 3 + CallQueue.StartGrace.TotalSeconds;
        Assert.Null(q.Tick(true, true, false, At(after)));          // grace over: new countdown
        Assert.Equal("222", q.Tick(true, true, false, At(after + 3)));
    }

    [Fact]
    public void Full_cycle_dial_connect_hangup_next()
    {
        var q = Queue("111", "222");
        q.Tick(true, true, false, At(0));
        Assert.Equal("111", q.Tick(true, true, false, At(3)));
        Assert.Null(q.Tick(true, false, false, At(4)));     // 111 connected
        Assert.Null(q.Tick(true, false, false, At(60)));
        Assert.Null(q.Tick(true, true, false, At(61)));     // hung up: countdown
        Assert.Equal("222", q.Tick(true, true, false, At(64)));
        Assert.Equal(0, q.Count);
    }

    [Fact]
    public void Paused_queue_does_not_dial()
    {
        var q = Queue("111");
        q.Paused = true;
        for (int i = 0; i < 20; i++) Assert.Null(q.Tick(true, true, false, At(i)));
        Assert.Equal(QueueWait.Paused, q.Waiting);
        q.Paused = false;
        Assert.Null(q.Tick(true, true, false, At(20)));
        Assert.Equal("111", q.Tick(true, true, false, At(23)));
    }

    [Fact]
    public void Hold_resets_countdown()
    {
        var q = Queue("111");
        q.Tick(true, true, false, At(0));
        Assert.Null(q.Tick(true, true, hold: true, At(2)));   // user started typing
        Assert.Null(q.DialAt);
        Assert.Equal(QueueWait.Hold, q.Waiting);
        Assert.Null(q.Tick(true, true, false, At(5)));         // box cleared: countdown restarts
        Assert.Null(q.Tick(true, true, false, At(7)));
        Assert.Equal("111", q.Tick(true, true, false, At(8)));
    }

    [Fact]
    public void Offline_does_not_dial()
    {
        var q = Queue("111");
        for (int i = 0; i < 10; i++) Assert.Null(q.Tick(false, true, false, At(i)));
        Assert.Equal(QueueWait.Offline, q.Waiting);
    }

    [Fact]
    public void Incoming_call_during_countdown_cancels_it()
    {
        var q = Queue("111");
        q.Tick(true, true, false, At(0));
        Assert.NotNull(q.DialAt);
        Assert.Null(q.Tick(true, false, false, At(1)));   // phone rings
        Assert.Null(q.DialAt);
        Assert.Null(q.Tick(true, false, false, At(10)));
        Assert.Equal(1, q.Count);
    }

    [Fact]
    public void PutBack_returns_number_to_top_and_pauses()
    {
        var q = Queue("111", "222");
        q.Tick(true, true, false, At(0));
        var n = q.Tick(true, true, false, At(3))!;
        q.PutBack(n);
        Assert.True(q.Paused);
        Assert.Equal(new[] { "111", "222" }, q.Items);
        Assert.Null(q.Tick(true, true, false, At(30)));
    }

    [Fact]
    public void Zero_delay_dials_on_next_tick()
    {
        var q = Queue("111");
        q.Delay = TimeSpan.Zero;
        Assert.Null(q.Tick(true, true, false, At(0)));
        Assert.Equal("111", q.Tick(true, true, false, At(0.25)));
    }

    [Fact]
    public void TakeNext_and_remove()
    {
        var q = Queue("111", "222", "333");
        q.RemoveAt(1);
        Assert.Equal(new[] { "111", "333" }, q.Items);
        q.Paused = true;
        Assert.Equal("111", q.TakeNext());
        Assert.False(q.Paused);
        q.RemoveAt(5);   // out of range: ignored
        Assert.Equal(1, q.Count);
        Assert.False(q.Add(""));
    }
}

public class PhoneNumberTests
{
    [Theory]
    [InlineData("(705) 555-1234", "7055551234")]
    [InlineData(" +1 705.555.1234 ", "+17055551234")]
    [InlineData("12+34", "1234")]
    [InlineData("*69#", "*69#")]
    [InlineData("abc", "")]
    [InlineData(null, "")]
    public void Normalize(string? raw, string expected) => Assert.Equal(expected, PhoneNumber.Normalize(raw));

    [Theory]
    [InlineData("7055551234", "(705) 555-1234")]
    [InlineData("17055551234", "1 (705) 555-1234")]
    [InlineData("5551234", "555-1234")]
    [InlineData("201", "201")]
    [InlineData("+447700900123", "+447700900123")]
    [InlineData("*69", "*69")]
    public void Display(string n, string expected) => Assert.Equal(expected, PhoneNumber.Display(n));

    [Theory]
    [InlineData("", "")]
    [InlineData("7", "7")]
    [InlineData("2001", "2001")]
    [InlineData("70555", "705-55")]
    [InlineData("7055551", "705-5551")]
    [InlineData("70555512", "(705) 555-12")]
    [InlineData("7055551234", "(705) 555-1234")]
    [InlineData("(705) 555-1234", "(705) 555-1234")]
    [InlineData("705.555.1234", "(705) 555-1234")]
    [InlineData("17055551234", "1 (705) 555-1234")]
    [InlineData("1705555", "170-5555")]
    [InlineData("170555512", "1 (705) 555-12")]
    [InlineData("70555512345", "70555512345")]
    [InlineData("call 705-555-1234 now", "(705) 555-1234")]
    [InlineData("abc", "")]
    [InlineData("705 ABC 1", "7051")]
    [InlineData("*69", "*69")]
    [InlineData("+44 7700 900123", "+447700900123")]
    [InlineData("12345678901234567890", "1234567890123456")]
    public void FormatAsYouType(string raw, string expected) => Assert.Equal(expected, PhoneNumber.FormatAsYouType(raw));

    [Theory]
    [InlineData("70555512", 8, "(705) 555-12", 12)]     // typing at the end stays at the end
    [InlineData("(705) 555-12", 2, "(705) 555-12", 2)]  // after the 7
    [InlineData("(705) 5x55-12", 8, "(705) 555-12", 7)] // blocked letter: caret stays after the 5
    [InlineData("705", 0, "705", 0)]
    public void MapCaret(string before, int caret, string after, int expected) =>
        Assert.Equal(expected, PhoneNumber.MapCaret(before, caret, after));
}

public class CallPhaseTests
{
    private static LineStatus Busy(string raw) => new(true, false, raw, "", "");

    [Theory]
    [InlineData("ringing", CallPhase.Incoming)]
    [InlineData("calling", CallPhase.Calling)]
    [InlineData("dialing", CallPhase.Calling)]
    [InlineData("ringback", CallPhase.Calling)]
    [InlineData("connected", CallPhase.Connected)]
    [InlineData("talking", CallPhase.Connected)]
    [InlineData("onhold", CallPhase.OnHold)]
    [InlineData("on-hold", CallPhase.OnHold)]
    [InlineData("whatever", CallPhase.InUse)]
    public void Maps_raw_states(string raw, CallPhase expected) => Assert.Equal(expected, CallPhases.From(Busy(raw)));

    [Fact]
    public void Idle_and_offline()
    {
        Assert.Equal(CallPhase.Idle, CallPhases.From(new LineStatus(true, true, "idle", "", "")));
        Assert.Equal(CallPhase.Offline, CallPhases.From(LineStatus.Offline));
    }

    [Fact]
    public void Answer_offered_only_when_it_might_work()
    {
        Assert.True(CallPhases.CanAnswer(CallPhase.Incoming));
        Assert.True(CallPhases.CanAnswer(CallPhase.InUse));
        Assert.False(CallPhases.CanAnswer(CallPhase.Connected));
        Assert.False(CallPhases.CanAnswer(CallPhase.Calling));
    }
}
