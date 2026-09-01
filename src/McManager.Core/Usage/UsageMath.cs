namespace McManager.Core.Usage;

/// <summary>Ports lab <c>app/usage.py</c> <c>compute_budget_report</c> (UTC month).</summary>
public static class UsageMath
{
    public static BudgetReport ComputeBudgetReport(
        UsageLedgerDocument ledger,
        double monthlyOcpuTarget,
        double monthlyGbTarget,
        double softOcpuCap,
        double softGbCap,
        DateTime? nowUtc = null) =>
        ComputeBudgetReport(
            ledger,
            new BudgetConfigDocument
            {
                MonthlyOcpuTarget = monthlyOcpuTarget,
                MonthlyGbTarget = monthlyGbTarget,
                SoftOcpuCap = softOcpuCap,
                SoftGbCap = softGbCap,
            },
            nowUtc);

    public static BudgetReport ComputeBudgetReport(
        UsageLedgerDocument ledger,
        BudgetConfigDocument budget,
        DateTime? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(budget);
        budget.NormalizeSculptMaps();
        var now = EnsureUtc(nowUtc ?? DateTime.UtcNow);
        BudgetSculpt.SnapshotClosedDays(budget, now);

        var year = now.Year;
        var month = now.Month;
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var today = DateOnly.FromDateTime(now);
        var shape = BudgetSculpt.ShapeOcpus(budget);
        var todayAlloc = BudgetSculpt.AllocationOcpu(budget, today);
        var todayAllocGb = BudgetSculpt.GbHoursForAllocation(todayAlloc, budget);

        var usedByDay = UsedByDay(ledger, year, month, daysInMonth, now);
        var env = BudgetSculpt.ComputeEnvelope(budget, ToOcpuGb(usedByDay), now);

        var calendar = new List<UsageDayRow>(daysInMonth);
        for (var dayNum = 1; dayNum <= daysInMonth; dayNum++)
        {
            var day = new DateOnly(year, month, dayNum);
            usedByDay.TryGetValue(day, out var used);
            var isClosed = day < today;
            var isFuture = day > today;
            var budgetOcpu = isClosed
                ? BudgetSculpt.PlannedOcpu(budget, day)
                : BudgetSculpt.AllocationOcpu(budget, day);
            calendar.Add(new UsageDayRow
            {
                Day = day,
                UptimeHours = used.Uptime,
                OcpuHours = used.Ocpu,
                GbHours = used.Gb,
                BudgetOcpuHours = budgetOcpu,
                BudgetWallClockHours = BudgetSculpt.WallClockHours(budgetOcpu, shape),
                IsClosed = isClosed,
                IsZeroed = BudgetSculpt.IsZeroed(budget, day),
                IsSculpted = BudgetSculpt.TryGetExplicit(budget, day, out _),
                IsFuture = isFuture,
                StillRunning = !isFuture && DayHasOpenInterval(ledger, day, now),
            });
        }

        var throughToday = calendar.Where(r => !r.IsFuture).ToList();
        var monthOcpu = throughToday.Sum(r => r.OcpuHours);
        var monthGb = throughToday.Sum(r => r.GbHours);
        var monthUptime = throughToday.Sum(r => r.UptimeHours);
        usedByDay.TryGetValue(today, out var todayUsed);
        var dayOfMonth = now.Day;

        return new BudgetReport
        {
            Year = year,
            Month = month,
            DaysInMonth = daysInMonth,
            DailyOcpuAllowance = todayAlloc,
            DailyGbAllowance = todayAllocGb,
            MonthlyOcpuTarget = budget.MonthlyOcpuTarget,
            MonthlyGbTarget = budget.MonthlyGbTarget,
            SoftOcpuCap = budget.SoftOcpuCap,
            SoftGbCap = budget.SoftGbCap,
            MonthOcpu = monthOcpu,
            MonthGb = monthGb,
            MonthUptime = monthUptime,
            TodayUptimeHours = todayUsed.Uptime,
            TodayOcpu = todayUsed.Ocpu,
            TodayGb = todayUsed.Gb,
            LeftoverOcpu = env.BankOcpu,
            LeftoverGb = env.BankGb,
            ReservedOcpu = env.ReservedOcpu,
            UsedClosedOcpu = env.UsedClosedOcpu,
            UnbudgetedOcpu = env.UnbudgetedOcpu,
            RolloverOcpu = env.RolloverOcpu,
            ClosedUnusedOcpu = env.ClosedUnusedOcpu,
            EnvelopeFits = env.FitsMonthly,
            OcpuOverDaily = todayUsed.Ocpu > todayAlloc,
            GbOverDaily = todayUsed.Gb > todayAllocGb,
            HitSoftCap = monthOcpu >= budget.SoftOcpuCap || monthGb >= budget.SoftGbCap,
            DayOfMonth = dayOfMonth,
            AvgHoursPerDay = monthUptime / Math.Max(1, dayOfMonth),
            Days = throughToday,
            CalendarDays = calendar,
        };
    }

    public static Dictionary<DateOnly, (double Ocpu, double Gb)> UsedOcpuGbByDay(
        UsageLedgerDocument ledger,
        DateTime nowUtc)
    {
        var now = EnsureUtc(nowUtc);
        var dim = DateTime.DaysInMonth(now.Year, now.Month);
        return ToOcpuGb(UsedByDay(ledger, now.Year, now.Month, dim, now));
    }

    private static bool DayHasOpenInterval(
        UsageLedgerDocument ledger,
        DateOnly day,
        DateTime nowUtc)
    {
        var windowStart = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var windowEnd = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        foreach (var item in ledger.Intervals)
        {
            if (!string.IsNullOrWhiteSpace(item.StoppedAt))
                continue;
            var parsedStart = ParseIso(item.StartedAt);
            if (parsedStart is null)
                continue;
            var end = nowUtc;
            var start = parsedStart.Value > windowStart ? parsedStart.Value : windowStart;
            end = end < windowEnd ? end : windowEnd;
            if (end > start)
                return true;
        }

        return false;
    }

    private static Dictionary<DateOnly, (double Uptime, double Ocpu, double Gb)> UsedByDay(
        UsageLedgerDocument ledger,
        int year,
        int month,
        int daysInMonth,
        DateTime nowUtc)
    {
        var rows = new Dictionary<DateOnly, (double Uptime, double Ocpu, double Gb)>();
        for (var dayNum = 1; dayNum <= daysInMonth; dayNum++)
        {
            var day = new DateOnly(year, month, dayNum);
            var (tot, _) = DayTotals(ledger, day, nowUtc);
            rows[day] = (tot.UptimeHours, tot.OcpuHours, tot.GbHours);
        }

        return rows;
    }

    private static Dictionary<DateOnly, (double Ocpu, double Gb)> ToOcpuGb(
        Dictionary<DateOnly, (double Uptime, double Ocpu, double Gb)> raw)
    {
        var result = new Dictionary<DateOnly, (double Ocpu, double Gb)>(raw.Count);
        foreach (var kv in raw)
            result[kv.Key] = (kv.Value.Ocpu, kv.Value.Gb);
        return result;
    }

    private static (UsageTotals Totals, bool IsOverride) DayTotals(
        UsageLedgerDocument ledger,
        DateOnly day,
        DateTime nowUtc)
    {
        var key = day.ToString("yyyy-MM-dd");
        if (ledger.DailyOverrides.TryGetValue(key, out var ov) && ov is not null)
        {
            return (new UsageTotals(ov.UptimeHours, ov.OcpuHours, ov.GbHours), true);
        }

        var start = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return (TotalsForWindow(ledger, start, end, nowUtc), false);
    }

    private static UsageTotals TotalsForWindow(
        UsageLedgerDocument ledger,
        DateTime windowStart,
        DateTime windowEnd,
        DateTime nowUtc)
    {
        double uptime = 0, ocpu = 0, gb = 0;
        foreach (var item in ledger.Intervals)
        {
            var hours = IntervalHours(item, windowStart, windowEnd, nowUtc);
            if (hours <= 0)
                continue;
            uptime += hours;
            ocpu += hours * item.Ocpus;
            gb += hours * item.MemoryGb;
        }

        return new UsageTotals(uptime, ocpu, gb);
    }

    private static double IntervalHours(
        UsageInterval item,
        DateTime windowStart,
        DateTime windowEnd,
        DateTime nowUtc)
    {
        var parsedStart = ParseIso(item.StartedAt);
        if (parsedStart is null)
            return 0;

        var end = ParseIso(item.StoppedAt) ?? nowUtc;
        var start = parsedStart.Value > windowStart ? parsedStart.Value : windowStart;
        end = end < windowEnd ? end : windowEnd;
        if (end <= start)
            return 0;
        return (end - start).TotalSeconds / 3600.0;
    }

    public static DateTime? ParseIso(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim();
        if (text.EndsWith("Z", StringComparison.Ordinal))
            text = text[..^1] + "+00:00";

        if (!DateTimeOffset.TryParse(text, out var dto))
            return null;

        return dto.UtcDateTime;
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    private readonly record struct UsageTotals(double UptimeHours, double OcpuHours, double GbHours);
}
