namespace MemoTack;

/// <summary>提醒的重複方式（以整數存進 JSON，未知值載入時視為不重複）</summary>
public enum ReminderRepeat
{
    None = 0,
    Daily = 1,
    Weekdays = 2, // 週一至週五
    Weekly = 3,
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
    public static DateTime Next(DateTime at, ReminderRepeat repeat, DateTime now)
    {
        if (repeat == ReminderRepeat.None)
            throw new ArgumentException("不重複的提醒沒有下一次", nameof(repeat));

        var t = at;
        do { t = Step(t, repeat); } while (t <= now);
        return t;
    }

    private static DateTime Step(DateTime t, ReminderRepeat repeat) => repeat switch
    {
        ReminderRepeat.Daily => t.AddDays(1),
        ReminderRepeat.Weekly => t.AddDays(7),
        ReminderRepeat.Weekdays => t.AddDays(t.DayOfWeek switch
        {
            DayOfWeek.Friday => 3,   // 週五 → 下週一
            DayOfWeek.Saturday => 2, // 週六 → 下週一
            _ => 1,
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(repeat)),
    };

    /// <summary>平日提醒若落在週末，順延到週一同一時間</summary>
    public static DateTime SkipWeekend(DateTime t)
    {
        while (t.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            t = t.AddDays(1);
        return t;
    }

    /// <summary>簡短時間文字：今天「14:30」、明天「明天 09:00」、其他「10/12 09:00」</summary>
    public static string FormatShort(DateTime t, DateTime now)
    {
        if (t.Date == now.Date) return t.ToString("HH:mm");
        if (t.Date == now.Date.AddDays(1)) return "明天 " + t.ToString("HH:mm");
        return t.ToString("M/d HH:mm");
    }

    public static string RepeatName(ReminderRepeat repeat) => repeat switch
    {
        ReminderRepeat.Daily => "每天",
        ReminderRepeat.Weekdays => "平日（週一至週五）",
        ReminderRepeat.Weekly => "每週",
        _ => "不重複",
    };
}
