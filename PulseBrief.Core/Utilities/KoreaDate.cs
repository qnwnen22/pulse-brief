namespace PulseBrief;

/// <summary>공개 지표와 요약 기준일을 한국 시간 기준으로 계산합니다.</summary>
public static class KoreaDate
{
    public static DateOnly Today()
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZone);
        return DateOnly.FromDateTime(now.DateTime);
    }

    public static DateTimeOffset StartOfDay(DateOnly date)
    {
        var localStart = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(localStart, TimeZone.GetUtcOffset(localStart));
    }

    public static string Key(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd");
    }

    private static TimeZoneInfo TimeZone { get; } = ResolveTimeZone();

    private static TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");
        }
    }
}
