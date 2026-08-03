using AIChat.Application.Scheduled;
using AIChat.Domain.Scheduled;

namespace AIChat.Tests.Scheduled;

// Cadence math is the cron engine's contract. Every
// branch the production runner walks gets a test here
// so the user-visible "上次 / 下次" display and the
// "did this fire at the right time" audit both stay
// honest.
public class ScheduledTaskCadenceTests
{
    private static DateTimeOffset Today(int hour, int minute) =>
        new(2026, 8, 3, hour, minute, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Manual_NeverFires()
    {
        var task = new ScheduledTask
        {
            Cadence = ScheduledCadence.Manual,
            CadenceTime = "09:00",
        };
        Assert.Null(ScheduledTaskCadence.NextRunAt(task, Today(12, 0)));
    }

    [Fact]
    public void Paused_NeverFires()
    {
        var task = new ScheduledTask
        {
            Cadence = ScheduledCadence.Daily,
            CadenceTime = "09:00",
            IsPaused = true,
        };
        Assert.Null(ScheduledTaskCadence.NextRunAt(task, Today(12, 0)));
    }

    [Fact]
    public void Once_BeforeAnyRun_FiresNow()
    {
        // The runner polls every 30s; the Once cadence
        // returns the current tick time so the next
        // pass picks it up. The exact time isn't
        // "now" the moment the user clicks "Add" —
        // it could be up to TickInterval late.
        var task = new ScheduledTask
        {
            Cadence = ScheduledCadence.Once,
            CreatedAt = Today(8, 0),
        };
        var now = Today(8, 0);
        var next = ScheduledTaskCadence.NextRunAt(task, now);
        Assert.NotNull(next);
        Assert.Equal(now, next.Value);
    }

    [Fact]
    public void Once_AfterFirstRun_NeverFiresAgain()
    {
        var task = new ScheduledTask
        {
            Cadence = ScheduledCadence.Once,
            CreatedAt = Today(8, 0),
            LastRunAt = Today(8, 5),
        };
        Assert.Null(ScheduledTaskCadence.NextRunAt(task, Today(8, 30)));
    }

    [Fact]
    public void Daily_BeforeTime_FiresToday()
    {
        // 08:00 now, 09:00 cadence — should fire later
        // today.
        var task = new ScheduledTask
        {
            Cadence = ScheduledCadence.Daily,
            CadenceTime = "09:00",
        };
        var next = ScheduledTaskCadence.NextRunAt(task, Today(8, 0));
        Assert.NotNull(next);
        Assert.Equal(Today(9, 0), next.Value);
    }

    [Fact]
    public void Daily_AfterTime_FiresTomorrow()
    {
        // 10:00 now, 09:00 cadence — today's slot has
        // already passed; the next slot is tomorrow at
        // 09:00.
        var task = new ScheduledTask
        {
            Cadence = ScheduledCadence.Daily,
            CadenceTime = "09:00",
        };
        var next = ScheduledTaskCadence.NextRunAt(task, Today(10, 0));
        Assert.NotNull(next);
        Assert.Equal(new DateTimeOffset(2026, 8, 4, 9, 0, 0, TimeSpan.FromHours(8)), next.Value);
    }

    [Fact]
    public void Daily_AcceptsShortHourFormat()
    {
        // Users type "9:00" without the leading zero.
        // The parser is lenient on that; the next-run
        // time still resolves to a real wall clock.
        var task = new ScheduledTask
        {
            Cadence = ScheduledCadence.Daily,
            CadenceTime = "9:00",
        };
        var next = ScheduledTaskCadence.NextRunAt(task, Today(8, 0));
        Assert.NotNull(next);
        Assert.Equal(9, next.Value.Hour);
    }

    [Fact]
    public void Daily_AcceptsShortMinuteFormat()
    {
        var task = new ScheduledTask
        {
            Cadence = ScheduledCadence.Daily,
            CadenceTime = "09:5",
        };
        var next = ScheduledTaskCadence.NextRunAt(task, Today(8, 0));
        Assert.NotNull(next);
        Assert.Equal(5, next.Value.Minute);
    }

    [Fact]
    public void Daily_MalformedFallsBackToNineAm()
    {
        // The user typed garbage. The runner must not
        // crash; it falls back to 09:00 so the row is
        // still functional until the user fixes it.
        var task = new ScheduledTask
        {
            Cadence = ScheduledCadence.Daily,
            CadenceTime = "not a time",
        };
        var next = ScheduledTaskCadence.NextRunAt(task, Today(8, 0));
        Assert.NotNull(next);
        Assert.Equal(9, next.Value.Hour);
        Assert.Equal(0, next.Value.Minute);
    }

    [Fact]
    public void Weekly_BeforeTimeThisWeek_FiresThisWeek()
    {
        // 2026-08-03 is a Monday. Anchor is the same
        // day; 09:00 has passed (we're at 10:00). The
        // next slot is next Monday at 09:00.
        var task = new ScheduledTask
        {
            Cadence = ScheduledCadence.Weekly,
            CadenceTime = "09:00",
            CreatedAt = Today(8, 0),
        };
        var next = ScheduledTaskCadence.NextRunAt(task, Today(10, 0));
        Assert.NotNull(next);
        // Should be 7 days from the anchor at 09:00.
        Assert.Equal(new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.FromHours(8)), next.Value);
    }

    [Fact]
    public void Weekly_AfterTimeSameDay_FiresNextWeek()
    {
        // 2026-08-03 (Monday) at 11:00. Anchor is
        // 2026-08-03 at 09:00 (already past). Next
        // slot: 7 days later at 09:00.
        var task = new ScheduledTask
        {
            Cadence = ScheduledCadence.Weekly,
            CadenceTime = "09:00",
            LastRunAt = Today(9, 0),  // ran at 09:00 today
        };
        var next = ScheduledTaskCadence.NextRunAt(task, Today(11, 0));
        Assert.NotNull(next);
        Assert.Equal(new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.FromHours(8)), next.Value);
    }

    [Fact]
    public void Weekly_BeforeTimeThisWeek_FiresToday()
    {
        // 2026-08-03 (Monday) at 08:00. Anchor is the
        // same day at 09:00 — still in the future.
        var next = ScheduledTaskCadence.NextRunAt(
            new ScheduledTask
            {
                Cadence = ScheduledCadence.Weekly,
                CadenceTime = "09:00",
                CreatedAt = Today(8, 0),
            },
            Today(8, 0));
        Assert.NotNull(next);
        Assert.Equal(Today(9, 0), next.Value);
    }
}
