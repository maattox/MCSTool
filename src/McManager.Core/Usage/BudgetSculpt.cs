using System.Globalization;

namespace McManager.Core.Usage;

/// <summary>UTC-day budget sculpt: even-split default, zero out = 0, unbudgeted vs rollover pools.</summary>
public static class BudgetSculpt
{
    public const double Epsilon = 1e-6;
    public const double MaxWallClockHoursPerDay = 24;

    public static string DayKey(DateOnly day) =>
        day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly UtcToday(DateTime nowUtc) =>
        DateOnly.FromDateTime(EnsureUtc(nowUtc));

    public static double FlatDailyOcpu(double monthlyOcpuTarget, int year, int month)
    {
        var days = DateTime.DaysInMonth(year, month);
        if (days <= 0)
            return 45.0;
        return monthlyOcpuTarget / days;
    }

    public static double FlatDailyGb(double monthlyGbTarget, int year, int month)
    {
        var days = DateTime.DaysInMonth(year, month);
        if (days <= 0)
            return 280.0;
        return monthlyGbTarget / days;
    }

    public static double ShapeOcpus(BudgetConfigDocument budget) =>
        budget.ShapeOcpus > 0 ? budget.ShapeOcpus : 4;

    public static double ShapeMemoryGb(BudgetConfigDocument budget) =>
        budget.ShapeMemoryGb > 0 ? budget.ShapeMemoryGb : 24;

    public static double WallClockHours(double ocpuHours, double shapeOcpus)
    {
        var shape = shapeOcpus > 0 ? shapeOcpus : 4;
        return ocpuHours / shape;
    }

    public static double OcpuHoursFromWallClock(double wallHours, double shapeOcpus)
    {
        var shape = shapeOcpus > 0 ? shapeOcpus : 4;
        return wallHours * shape;
    }

    public static double GbHoursForAllocation(double ocpuHours, BudgetConfigDocument budget) =>
        WallClockHours(ocpuHours, ShapeOcpus(budget)) * ShapeMemoryGb(budget);

    public static bool TryGetExplicit(BudgetConfigDocument budget, DateOnly day, out double ocpuHours)
    {
        budget.NormalizeSculptMaps();
        return budget.DailyOcpu.TryGetValue(DayKey(day), out ocpuHours);
    }

    public static bool IsZeroed(BudgetConfigDocument budget, DateOnly day) =>
        TryGetExplicit(budget, day, out var v) && v <= Epsilon;

    public static double AllocationOcpu(BudgetConfigDocument budget, DateOnly day)
    {
        if (TryGetExplicit(budget, day, out var v))
            return v;
        return FlatDailyOcpu(budget.MonthlyOcpuTarget, day.Year, day.Month);
    }

    public static double PlannedOcpu(BudgetConfigDocument budget, DateOnly day)
    {
        budget.NormalizeSculptMaps();
        if (budget.DailyOcpuPlanned.TryGetValue(DayKey(day), out var planned))
            return planned;
        return AllocationOcpu(budget, day);
    }

    /// <summary>
    /// Write <c>daily_ocpu_planned</c> for elapsed UTC days this month that lack a snapshot.
    /// </summary>
    /// <returns>True if any key was added.</returns>
    public static bool SnapshotClosedDays(BudgetConfigDocument budget, DateTime nowUtc)
    {
        budget.NormalizeSculptMaps();
        var now = EnsureUtc(nowUtc);
        var today = DateOnly.FromDateTime(now);

        var changed = false;
        var dim = DateTime.DaysInMonth(now.Year, now.Month);
        for (var dayNum = 1; dayNum <= dim; dayNum++)
        {
            var day = new DateOnly(now.Year, now.Month, dayNum);
            if (day >= today)
                break;
            var key = DayKey(day);
            if (budget.DailyOcpuPlanned.ContainsKey(key))
                continue;
            double planned;
            if (budget.DailyOcpu.TryGetValue(key, out var explicitAlloc))
                planned = explicitAlloc;
            else
                planned = FlatDailyOcpu(budget.MonthlyOcpuTarget, day.Year, day.Month);
            budget.DailyOcpuPlanned[key] = planned;
            changed = true;
        }

        return changed;
    }

    public readonly record struct Envelope(
        double UsedClosedOcpu,
        double ReservedOcpu,
        double BankOcpu,
        double UsedClosedGb,
        double ReservedGb,
        double BankGb,
        bool FitsMonthly,
        double UnbudgetedOcpu,
        double RolloverOcpu,
        double ClosedUnusedOcpu,
        double RolloverSpentOcpu)
    {
        public double UnbudgetedPoolOcpu => Math.Max(0, UnbudgetedOcpu);
    }

    public static Envelope ComputeEnvelope(
        BudgetConfigDocument budget,
        IReadOnlyDictionary<DateOnly, (double Ocpu, double Gb)> usedByDay,
        DateTime nowUtc)
    {
        budget.NormalizeSculptMaps();
        var now = EnsureUtc(nowUtc);
        var today = DateOnly.FromDateTime(now);
        var dim = DateTime.DaysInMonth(now.Year, now.Month);
        var even = FlatDailyOcpu(budget.MonthlyOcpuTarget, now.Year, now.Month);
        double usedClosedOcpu = 0, reservedOcpu = 0;
        double usedClosedGb = 0, reservedGb = 0;
        double closedUnusedOcpu = 0, deficit = 0, surplus = 0;
        for (var dayNum = 1; dayNum <= dim; dayNum++)
        {
            var day = new DateOnly(now.Year, now.Month, dayNum);
            if (day < today)
            {
                usedByDay.TryGetValue(day, out var used);
                usedClosedOcpu += used.Ocpu;
                usedClosedGb += used.Gb;
                var planned = PlannedOcpu(budget, day);
                closedUnusedOcpu += Math.Max(0, planned - used.Ocpu);
            }
            else
            {
                var alloc = AllocationOcpu(budget, day);
                reservedOcpu += alloc;
                reservedGb += GbHoursForAllocation(alloc, budget);
                deficit += Math.Max(0, even - alloc);
                surplus += Math.Max(0, alloc - even);
            }
        }

        var rolloverSpent = Math.Max(0, surplus - deficit);
        var unused = Math.Max(0, deficit - surplus);
        var rollover = Math.Max(0, closedUnusedOcpu - rolloverSpent);
        var unfunded = Math.Max(0, rolloverSpent - closedUnusedOcpu);
        var unbudgeted = unused - unfunded;
        var bankOcpu = Math.Max(0, budget.MonthlyOcpuTarget - usedClosedOcpu - reservedOcpu);
        var bankGb = Math.Max(0, budget.MonthlyGbTarget - usedClosedGb - reservedGb);
        var fits = usedClosedOcpu + reservedOcpu <= budget.MonthlyOcpuTarget + Epsilon;
        return new Envelope(
            usedClosedOcpu,
            reservedOcpu,
            bankOcpu,
            usedClosedGb,
            reservedGb,
            bankGb,
            fits,
            unbudgeted,
            rollover,
            closedUnusedOcpu,
            rolloverSpent);
    }

    /// <summary>
    /// Rollover OCPU-hours that may be spent after reserving a wall-clock minimum buffer.
    /// </summary>
    public static double AvailableRolloverOcpu(
        double rolloverOcpu,
        double minBufferWallClockHours,
        double shapeOcpus)
    {
        var bufferOcpu = OcpuHoursFromWallClock(Math.Max(0, minBufferWallClockHours), shapeOcpus);
        return Math.Max(0, rolloverOcpu - bufferOcpu);
    }

    /// <summary>
    /// OCPU-hours still needed from rollover after unbudgeted (and allowed rollover) are applied.
    /// </summary>
    public static double RolloverShortfallOcpu(double rolloverSpentOcpu, double availableRolloverOcpu) =>
        Math.Max(0, rolloverSpentOcpu - availableRolloverOcpu);

    /// <summary>
    /// Suggested minimum rollover buffer (wall-clock hours, floored to a tenth) so a plan can save.
    /// Pass leftover rollover after the plan’s spend (<see cref="Envelope.RolloverOcpu"/>).
    /// </summary>
    public static double SuggestedMinBufferWallHours(double leftoverRolloverOcpu, double shapeOcpus)
    {
        var wall = Math.Max(0, WallClockHours(leftoverRolloverOcpu, shapeOcpus));
        return Math.Floor(wall * 10.0 + Epsilon) / 10.0;
    }

    public readonly record struct SaveGate(
        bool CanSave,
        bool ExceedsMonthly,
        bool BufferBlocks,
        double SuggestedMinBufferWallHours,
        string Warning);

    /// <summary>
    /// Save-time check: monthly target, then unbudgeted, then allowed rollover (never below the min buffer).
    /// </summary>
    public static SaveGate EvaluateSave(
        in Envelope env,
        bool useRolloverHours,
        double minBufferWallClockHours,
        double shapeOcpus)
    {
        if (!env.FitsMonthly)
        {
            return new SaveGate(
                false,
                true,
                false,
                0,
                "This plan exceeds this month’s hour target. Zero out or lower other days before saving.");
        }

        if (env.RolloverSpentOcpu <= Epsilon)
            return new SaveGate(true, false, false, 0, "");

        if (!useRolloverHours)
        {
            return new SaveGate(
                false,
                false,
                false,
                0,
                "This plan needs rollover hours. Turn on Use rollover hours, or zero out / lower other days.");
        }

        var available = AvailableRolloverOcpu(env.ClosedUnusedOcpu, minBufferWallClockHours, shapeOcpus);
        var shortfall = RolloverShortfallOcpu(env.RolloverSpentOcpu, available);
        if (shortfall <= Epsilon)
            return new SaveGate(true, false, false, 0, "");

        var suggested = SuggestedMinBufferWallHours(env.RolloverOcpu, shapeOcpus);
        return new SaveGate(
            false,
            false,
            true,
            suggested,
            $"You don’t have enough hours for this plan, but you can get enough if you set the minimum rollover buffer to {suggested:0.#} hours first.");
    }

    public static bool IsClosed(DateOnly day, DateTime nowUtc) =>
        day < DateOnly.FromDateTime(EnsureUtc(nowUtc));

    public static bool IsEditable(DateOnly day, DateTime nowUtc) =>
        !IsClosed(day, nowUtc);

    public static string? TrySetDays(
        BudgetConfigDocument budget,
        IReadOnlyList<DateOnly> days,
        double wallClockHours,
        IReadOnlyDictionary<DateOnly, (double Ocpu, double Gb)> usedByDay,
        DateTime nowUtc)
    {
        if (days.Count == 0)
            return "Select at least one day.";
        if (wallClockHours < 0)
            return "Hours cannot be negative.";
        if (wallClockHours > MaxWallClockHoursPerDay + Epsilon)
            return "A day cannot have more than 24 hours.";

        var now = EnsureUtc(nowUtc);
        var today = DateOnly.FromDateTime(now);
        var shape = ShapeOcpus(budget);
        var ocpu = OcpuHoursFromWallClock(wallClockHours, shape);

        foreach (var day in days)
        {
            if (day.Year != now.Year || day.Month != now.Month)
                return "Sculpt only days in the current UTC month.";
            if (IsClosed(day, now))
                return "Closed UTC days are not editable.";
            if (day == today)
            {
                usedByDay.TryGetValue(today, out var used);
                if (wallClockHours <= Epsilon && used.Ocpu > Epsilon)
                    return "Cannot zero out today after the server has already run. Stop it first if you need a lower cap.";
                if (ocpu + Epsilon < used.Ocpu)
                    return "Today cannot go below hours already used. Stop the server first if you need a lower cap.";
            }
        }

        budget.NormalizeSculptMaps();
        foreach (var day in days)
            budget.DailyOcpu[DayKey(day)] = ocpu;
        return null;
    }

    public static string? TryZeroDays(
        BudgetConfigDocument budget,
        IReadOnlyList<DateOnly> days,
        IReadOnlyDictionary<DateOnly, (double Ocpu, double Gb)> usedByDay,
        DateTime nowUtc) =>
        TrySetDays(budget, days, 0, usedByDay, nowUtc);

    /// <summary>
    /// Spread a pool of OCPU-hours evenly across unspecified today+future days
    /// (writes explicit <c>daily_ocpu</c> values added onto the even-split default).
    /// </summary>
    public static string? TryRedistributePoolOntoUnspecified(
        BudgetConfigDocument budget,
        IReadOnlyDictionary<DateOnly, (double Ocpu, double Gb)> usedByDay,
        DateTime nowUtc,
        double poolOcpu)
    {
        _ = usedByDay;
        var now = EnsureUtc(nowUtc);
        if (poolOcpu <= Epsilon)
            return "No hours to redistribute.";

        var unspecified = EditableDays(now).Where(d => !TryGetExplicit(budget, d, out _)).ToList();
        if (unspecified.Count == 0)
            return "Every remaining UTC day already has an hours value. Select days, then distribute onto those.";

        return AddPoolOntoDays(budget, unspecified, poolOcpu);
    }

    /// <summary>Spread a pool of OCPU-hours evenly onto the selected editable days (added to current allocation).</summary>
    public static string? TryRedistributePoolOntoSelected(
        BudgetConfigDocument budget,
        IReadOnlyList<DateOnly> days,
        IReadOnlyDictionary<DateOnly, (double Ocpu, double Gb)> usedByDay,
        DateTime nowUtc,
        double poolOcpu)
    {
        _ = usedByDay;
        var now = EnsureUtc(nowUtc);
        if (poolOcpu <= Epsilon)
            return "No hours to redistribute.";

        var selected = days.Distinct().OrderBy(d => d).ToList();
        if (selected.Count == 0)
            return "Select at least one today or future UTC day.";
        foreach (var day in selected)
        {
            if (!IsEditable(day, now) || day.Year != now.Year || day.Month != now.Month)
                return "Closed UTC days are not editable.";
        }

        return AddPoolOntoDays(budget, selected, poolOcpu);
    }

    public static double MaxOcpuHoursPerDay(BudgetConfigDocument budget) =>
        OcpuHoursFromWallClock(MaxWallClockHoursPerDay, ShapeOcpus(budget));

    public static double HeadroomOcpu(BudgetConfigDocument budget, DateOnly day) =>
        Math.Max(0, MaxOcpuHoursPerDay(budget) - AllocationOcpu(budget, day));

    /// <summary>
    /// Spread a pool of OCPU-hours evenly, never raising a day above 24 wall-clock hours.
    /// Leftover that cannot fit stays unassigned.
    /// </summary>
    internal static string? AddPoolOntoDays(
        BudgetConfigDocument budget,
        IReadOnlyList<DateOnly> days,
        double poolOcpu)
    {
        if (poolOcpu <= Epsilon)
            return "No hours to redistribute.";
        if (days.Count == 0)
            return "Select at least one today or future UTC day.";

        budget.NormalizeSculptMaps();
        var remaining = poolOcpu;
        var open = days.Where(d => HeadroomOcpu(budget, d) > Epsilon).ToList();
        if (open.Count == 0)
            return "A day cannot have more than 24 hours.";

        var placed = false;
        while (remaining > Epsilon && open.Count > 0)
        {
            var share = remaining / open.Count;
            var next = new List<DateOnly>(open.Count);
            var progressed = false;
            foreach (var day in open)
            {
                var add = Math.Min(share, HeadroomOcpu(budget, day));
                if (add <= Epsilon)
                    continue;
                budget.DailyOcpu[DayKey(day)] = AllocationOcpu(budget, day) + add;
                remaining -= add;
                placed = true;
                progressed = true;
                if (HeadroomOcpu(budget, day) > Epsilon)
                    next.Add(day);
            }

            if (!progressed)
                break;
            open = next;
        }

        return placed ? null : "A day cannot have more than 24 hours.";
    }

    /// <summary>
    /// Remove <c>daily_ocpu</c> keys for today and future UTC days this month (even-split default).
    /// Does not rewrite closed <c>daily_ocpu_planned</c>.
    /// </summary>
    public static void ResetTodayAndFutureToDefault(BudgetConfigDocument budget, DateTime nowUtc)
    {
        budget.NormalizeSculptMaps();
        var now = EnsureUtc(nowUtc);
        var today = DateOnly.FromDateTime(now);
        var toRemove = budget.DailyOcpu.Keys
            .Where(key =>
                DateOnly.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                && day.Year == now.Year
                && day.Month == now.Month
                && day >= today)
            .ToList();
        foreach (var key in toRemove)
            budget.DailyOcpu.Remove(key);
    }

    public static IEnumerable<DateOnly> EditableDays(DateTime nowUtc)
    {
        var now = EnsureUtc(nowUtc);
        var today = DateOnly.FromDateTime(now);
        var dim = DateTime.DaysInMonth(now.Year, now.Month);
        for (var dayNum = today.Day; dayNum <= dim; dayNum++)
            yield return new DateOnly(now.Year, now.Month, dayNum);
    }

    internal static DateTime EnsureUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
