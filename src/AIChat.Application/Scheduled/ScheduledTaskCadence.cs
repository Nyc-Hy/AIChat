using AIChat.Domain.Scheduled;

namespace AIChat.Application.Scheduled;

// Pure cadence math. Lives in AIChat.Application so the
// runner can be unit-tested without a registry / executor
// stub. The methods are stateless — pass the task and a
// clock, get back a DateTimeOffset? (null = "never fires
// again", e.g. a Manual cadence or a Once task that
// already ran).
//
// Wall-clock model: the runner compares NextRunAt against
// the host's local clock (DateTimeOffset.Now). All
// cadences are user-local-time — "Daily 09:00" means
// 09:00 in whatever timezone the user is sitting in.
// A follow-up slice that needs explicit-timezone support
// would add a `TimeZoneInfo` parameter here; for now
// the desktop app's host timezone is the right default.
public static class ScheduledTaskCadence
{
    // Compute the next time this task should fire.
    // Returns null for cadences that never auto-fire
    // (Manual) or that have already exhausted their
    // run budget (Once after one run).
    public static DateTimeOffset? NextRunAt(
        ScheduledTask task,
        DateTimeOffset now)
    {
        if (task.IsPaused)
        {
            // Paused tasks don't schedule. Once the
            // user un-pauses, the next call from a
            // fresh tick re-evaluates.
            return null;
        }

        return task.Cadence switch
        {
            ScheduledCadence.Manual => null,
            ScheduledCadence.Once => task.LastRunAt is null
                ? EarliestTime(now)
                : null,
            ScheduledCadence.Daily => NextDailyAt(task, now),
            ScheduledCadence.Weekly => NextWeeklyAt(task, now),
            _ => null,
        };
    }

    // "Run as soon as possible" — used by the Once cadence
    // before its first run. Returns now (the tick loop
    // will pick it up on the next pass) so the runner
    // doesn't have a special "immediate" code path.
    private static DateTimeOffset EarliestTime(DateTimeOffset now) => now;

    // Daily 09:00. If today's 09:00 is in the future,
    // fire today; else fire tomorrow. CadenceTime is
    // "HH:mm" in the host's local clock; the schedule
    // honours DST transitions because we re-parse on
    // every tick.
    private static DateTimeOffset NextDailyAt(ScheduledTask task, DateTimeOffset now)
    {
        if (!TryParseHHmm(task.CadenceTime, out var hour, out var minute))
        {
            // Malformed cadence — default to the next
            // 09:00. The user can edit the row to fix
            // the value; the runner doesn't crash.
            hour = 9;
            minute = 0;
        }

        var today = new DateTimeOffset(now.Year, now.Month, now.Day, hour, minute, 0, now.Offset);
        if (today > now)
        {
            return today;
        }
        var tomorrow = today.AddDays(1);
        return tomorrow;
    }

    // Weekly at CadenceTime. The runner fires on whatever
    // weekday the task was created on (the "first run"
    // anchor), then every 7 days from there. A future
    // slice that wants Mon-Fri pickers would add a
    // ScheduledCadence.Weekdays enum; for now the user's
    // "Weekly 09:00" matches the daily pattern with a
    // 7-day interval.
    private static DateTimeOffset NextWeeklyAt(ScheduledTask task, DateTimeOffset now)
    {
        if (!TryParseHHmm(task.CadenceTime, out var hour, out var minute))
        {
            hour = 9;
            minute = 0;
        }

        var anchor = task.LastRunAt ?? task.CreatedAt;
        var anchorLocal = anchor.ToLocalTime();
        var anchorWeekday = new DateTimeOffset(
            anchorLocal.Year, anchorLocal.Month, anchorLocal.Day,
            hour, minute, 0, now.Offset);

        // Walk forward in 7-day steps until we find the
        // next slot that is still in the future. Capped
        // at 8 weeks so a freshly-created task (with
        // LastRunAt == CreatedAt = 5 years ago) doesn't
        // burn CPU stepping 260+ times.
        for (var i = 0; i < 8; i++)
        {
            var candidate = anchorWeekday.AddDays(i * 7);
            if (candidate > now)
            {
                return candidate;
            }
        }
        // 8+ weeks stale: jump to next week's slot.
        return anchorWeekday.AddDays(7 * 7);
    }

    // Lenient "HH:mm" parser. Accepts "9:00" as well as
    // "09:00" (the form the user types in the create
    // form). Returns false on garbage; the caller falls
    // back to a sane default.
    private static bool TryParseHHmm(string value, out int hour, out int minute)
    {
        hour = 0;
        minute = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        var parts = value.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }
        if (!int.TryParse(parts[0], out hour) || hour is < 0 or > 23)
        {
            return false;
        }
        if (!int.TryParse(parts[1], out minute) || minute is < 0 or > 59)
        {
            return false;
        }
        return true;
    }
}
