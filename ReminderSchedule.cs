namespace MemoTack;

/// <summary>
/// 提醒的重複方式（以整數存進 JSON，未知值載入時視為不重複）。
/// Weekdays 只為了讀舊版存檔：載入時會轉成 Weekly + 週一至週五。
/// </summary>
public enum ReminderRepeat
{
    None = 0,
    Daily = 1,
    Weekdays = 2, // 舊版的「平日」；新版存成 Weekly + 週一至週五
    Weekly = 3,
    Monthly = 4,
}

/// <summary>
/// 重複規則：種類 + 每週的星期（位元遮罩，bit n = DayOfWeek n，週日為 bit 0）+ 每月幾號。
/// </summary>
public readonly record struct RepeatRule(ReminderRepeat Kind, int WeekDays = 0, int MonthDay = 0)
{
    public const int WeekdayMask = 0b0111110; // 週一至週五
    public const int AllDaysMask = 0b1111111;

    public static RepeatRule None => new(ReminderRepeat.None);

    public static RepeatRule From(NoteData d) => new(d.ReminderRepeat, d.ReminderWeekDays, d.ReminderMonthDay);

    public static int Bit(DayOfWeek day) => 1 << (int)day;

    public bool Includes(DayOfWeek day) => (WeekDays & Bit(day)) != 0;
}

/// <summary>
/// 提醒的時間計算：是否到期、下一次時間、顯示文字。純邏輯、不碰 UI。
/// </summary>
public static class ReminderSchedule
{
    /// <summary>實際要響鈴的時間：有延後就用延後時間，否則用原定時間</summary>
    public static DateTime? DueTime(NoteData d) =>
        d.ReminderAt == null ? null : d.ReminderSnoozeUntil ?? d.ReminderAt;

    public static bool IsDue(NoteData d, DateTime now) => DueTime(d) is { } t && t <= now;

    /// <summary>
    /// 重複提醒的下一次時間：從「原定時間」往後推（不是從按下完成的時間），
    /// 直到晚於 now。錯過多次（例如關機一週）也只會跳到下一個未來的時間。
    /// </summary>
    public static DateTime Next(DateTime at, RepeatRule rule, DateTime now)
    {
        if (rule.Kind == ReminderRepeat.None)
            throw new ArgumentException("不重複的提醒沒有下一次", nameof(rule));

        var t = at;
        do { t = Step(t, rule); } while (t <= now);
        return t;
    }

    /// <summary>
    /// 設定重複提醒時的第一次時間：從 candidate（今天的指定時刻）起，第一個符合規則且晚於 now 的時間。
    /// 例如每週一三五 09:00、今天是週二 → 週三 09:00。
    /// </summary>
    public static DateTime FirstOnOrAfter(DateTime candidate, RepeatRule rule, DateTime now)
    {
        var t = Align(candidate, rule);
        return t > now ? t : Next(t, rule, now);
    }

    /// <summary>把時間對齊到規則上的某一天（同一時刻）：每週 → 往後找到勾選的星期；每月 → 本月的指定日</summary>
    private static DateTime Align(DateTime t, RepeatRule rule)
    {
        switch (rule.Kind)
        {
            case ReminderRepeat.Weekly:
            case ReminderRepeat.Weekdays:
                int mask = EffectiveMask(rule, t);
                for (int i = 0; i < 7 && (mask & RepeatRule.Bit(t.DayOfWeek)) == 0; i++)
                    t = t.AddDays(1);
                return t;
            case ReminderRepeat.Monthly:
                return MonthDate(t.Year, t.Month, rule.MonthDay > 0 ? rule.MonthDay : t.Day, t.TimeOfDay);
            default:
                return t;
        }
    }

    private static DateTime Step(DateTime t, RepeatRule rule)
    {
        switch (rule.Kind)
        {
            case ReminderRepeat.Daily:
                return t.AddDays(1);

            case ReminderRepeat.Weekly:
            case ReminderRepeat.Weekdays:
                int mask = EffectiveMask(rule, t);
                do { t = t.AddDays(1); } while ((mask & RepeatRule.Bit(t.DayOfWeek)) == 0);
                return t;

            case ReminderRepeat.Monthly:
                // 每月的日子以規則為準，不從上一次推：31 日在 2 月落到 28 日後，3 月仍回到 31 日
                var next = t.AddMonths(1);
                return MonthDate(next.Year, next.Month, rule.MonthDay > 0 ? rule.MonthDay : t.Day, t.TimeOfDay);

            default:
                throw new ArgumentOutOfRangeException(nameof(rule));
        }
    }

    /// <summary>每週的星期遮罩；舊版平日為週一至週五；沒勾任何一天時視為原定那天的星期</summary>
    private static int EffectiveMask(RepeatRule rule, DateTime t)
    {
        if (rule.Kind == ReminderRepeat.Weekdays)
            return RepeatRule.WeekdayMask;
        int mask = rule.WeekDays & RepeatRule.AllDaysMask;
        return mask != 0 ? mask : RepeatRule.Bit(t.DayOfWeek);
    }

    /// <summary>某年某月的第 day 日；該月沒有這天（例如 2 月 31 日）就用月底</summary>
    private static DateTime MonthDate(int year, int month, int day, TimeSpan time) =>
        new DateTime(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)), 0, 0, 0, DateTimeKind.Local) + time;

    /// <summary>簡短時間文字：今天「14:30」、明天「明天 09:00」、其他「10/12 09:00」</summary>
    public static string FormatShort(DateTime t, DateTime now)
    {
        if (t.Date == now.Date) return t.ToString("HH:mm");
        if (t.Date == now.Date.AddDays(1)) return "明天 " + t.ToString("HH:mm");
        return t.ToString("M/d HH:mm");
    }

    private static readonly DayOfWeek[] WeekOrder =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    };

    /// <summary>星期的中文簡稱：一、二…日</summary>
    public static string DayName(DayOfWeek day) => "日一二三四五六"[(int)day].ToString();

    /// <summary>重複規則的說明文字：「每天」「平日（週一至週五）」「每週一、三、五」「每月 15 日」</summary>
    public static string Describe(RepeatRule rule)
    {
        switch (rule.Kind)
        {
            case ReminderRepeat.Daily:
                return "每天";
            case ReminderRepeat.Weekdays:
                return "平日（週一至週五）";
            case ReminderRepeat.Weekly:
                int mask = rule.WeekDays & RepeatRule.AllDaysMask;
                if (mask == RepeatRule.WeekdayMask) return "平日（週一至週五）";
                if (mask == RepeatRule.AllDaysMask) return "每天";
                if (mask == 0) return "每週";
                return "每週" + string.Join("、", WeekOrder.Where(d => (mask & RepeatRule.Bit(d)) != 0).Select(DayName));
            case ReminderRepeat.Monthly:
                return $"每月 {rule.MonthDay} 日";
            default:
                return "不重複";
        }
    }
}
