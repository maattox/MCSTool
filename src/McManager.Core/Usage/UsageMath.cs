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
        DateTime? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTime.UtcNow);
        var year = now.Year;
        var month = now.Month;
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var dailyOcpu = monthlyOcpuTarget / daysInMonth;
        var dailyGb = monthlyGbTarget / daysInMonth;

        var rows = MonthDayRows(
            ledger,
            year,
            month,
            dailyOcpu,
            dailyGb,
            now);

        var monthOcpu = rows.Sum(r => r.OcpuHours);
        var monthGb = rows.Sum(r => r.GbHours);
        var monthUptime = rows.Sum(r => r.UptimeHours);
        double todayOcpu = 0;
        double todayGb = 0;
        if (rows.Count > 0)
        {
            todayOcpu = rows[^1].OcpuHours;
            todayGb = rows[^1].GbHours;
        }
        var leftoverOcpu = rows.Take(Math.Max(0, rows.Count - 1)).Sum(r => r.LeftoverOcpuContrib);
        var leftoverGb = rows.Take(Math.Max(0, rows.Count - 1)).Sum(r => r.LeftoverGbContrib);
        var dayOfMonth = now.Day;

        return new BudgetReport
        {
            Year = year,
            Month = month,
            DaysInMonth = daysInMonth,
            DailyOcpuAllowance = dailyOcpu,
            DailyGbAllowance = dailyGb,
            MonthlyOcpuTarget = monthlyOcpuTarget,
            MonthlyGbTarget = monthlyGbTarget,
            SoftOcpuCap = softOcpuCap,
            SoftGbCap = softGbCap,
            MonthOcpu = monthOcpu,
            MonthGb = monthGb,
            MonthUptime = monthUptime,
            TodayOcpu = todayOcpu,
            TodayGb = todayGb,
            LeftoverOcpu = leftoverOcpu,
            LeftoverGb = leftoverGb,
            OcpuOverDaily = todayOcpu > dailyOcpu,
            GbOverDaily = todayGb > dailyGb,
            HitSoftCap = monthOcpu >= softOcpuCap || monthGb >= softGbCap,
            DayOfMonth = dayOfMonth,
            AvgHoursPerDay = monthUptime / Math.Max(1, dayOfMonth),
        };
    }

    private static List<DayRow> MonthDayRows(
        UsageLedgerDocument ledger,
        int year,
        int month,
        double dailyOcpu,
        double dailyGb,
        DateTime nowUtc)
    {
        var lastDay = DateTime.DaysInMonth(year, month);
        if (year == nowUtc.Year && month == nowUtc.Month)
            lastDay = nowUtc.Day;

        var rows = new List<DayRow>(lastDay);
        for (var dayNum = 1; dayNum <= lastDay; dayNum++)
        {
            var day = new DateOnly(year, month, dayNum);
            var (tot, _) = DayTotals(ledger, day, nowUtc);
            rows.Add(new DayRow(
                day,
                tot.UptimeHours,
                tot.OcpuHours,
                tot.GbHours,
                Math.Max(0, dailyOcpu - tot.OcpuHours),
                Math.Max(0, dailyGb - tot.GbHours)));
        }

        return rows;
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

    private readonly record struct DayRow(
        DateOnly Day,
        double UptimeHours,
        double OcpuHours,
        double GbHours,
        double LeftoverOcpuContrib,
        double LeftoverGbContrib);
}
