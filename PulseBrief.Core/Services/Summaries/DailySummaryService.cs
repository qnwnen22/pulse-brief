using System.Text.RegularExpressions;

namespace PulseBrief;

/// <summary>저장된 이슈 그룹을 기반으로 일간/주간 요약을 만들고 OpenAI 요약 결과를 캐싱합니다.</summary>
public sealed partial class DailySummaryService(IArticleStore store, OpenAiDailySummaryClient openAiClient, IConfiguration configuration)
{
    private static readonly TimeZoneInfo KoreaTimeZone = ResolveKoreaTimeZone();
    private static readonly Regex KeywordRegex = new("[a-z0-9가-힣]{2,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DateLikeKeywordRegex = new(@"^\d+(년|월|일|시|분|초|명|건|개|곳|차|위|호|회|명)$", RegexOptions.Compiled);
    private static readonly HashSet<string> KeywordStopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "기자", "뉴스", "사진", "영상", "제공", "출처", "관련", "이번", "지난", "오는", "오늘", "내일", "어제",
        "오전", "오후", "이날", "최근", "현재", "대한", "통해", "위해", "대해", "따라", "등을", "등의",
        "있다", "했다", "한다", "됐다", "밝혔다", "말했다", "전했다", "설명했다", "나타났다", "것으로",
        "가운데", "그리고", "그러나", "하지만", "또는", "면서", "에서", "으로", "에게", "까지", "부터",
        "본문", "광고", "무단", "전재", "재배포", "금지", "copyright", "your", "browser", "support", "audio", "element"
    };

    public bool IsGenerationEnabled => configuration.GetValue("Summary:EnableGeneration", true);
    private static readonly HashSet<string> RollupKeywordStopwords = new(KeywordStopwords, StringComparer.OrdinalIgnoreCase)
    {
        "속보", "단독", "종합", "등록", "수정", "서울", "뉴시스", "연합뉴스", "한겨레", "경향신문", "동아일보",
        "위원장", "위원회", "대표", "정부", "대통령", "국민", "한국", "지원", "확대", "강화", "시작", "개최",
        "진행", "발표", "추진", "계획", "참석", "자료", "브리핑", "분야", "사업", "대상", "지역", "회의",
        "포토", "현장", "관계자", "오른쪽", "왼쪽", "모습", "있는", "없는", "합니다", "했습니다", "됩니다",
        "기사를", "읽어드립니다", "이미지", "무단전재", "재판매", "금지", "email", "protected", "newsis",
        "yonhap", "yna", "com", "www", "co", "kr", "ai", "협력", "위한", "함께", "국내", "해외", "미국",
        "중국", "일본", "올해", "전년", "대비", "기준", "보다", "따르면", "시장", "주요", "최대", "최소",
        "달러", "운영", "있도록", "자료제공", "바랍니다", "자세한", "결정된", "실무", "korea", "north",
        "south", "공감언론", "계획이다", "오른", "내린", "증가했다", "감소했다", "동기", "포인트", "결과",
        "통합", "구성", "방안", "문제", "사용", "가능", "확인", "사람", "시민", "공개", "전국", "부산",
        "부산시", "광주", "전남", "전북", "경남", "경북", "충남", "충북", "강원", "제주", "대구", "대전",
        "울산", "인천", "경기", "수도권"
    };

    public async Task<DailyIssueSummary?> GetStoredDailySummaryAsync(DateOnly? date = null)
    {
        var targetDate = date ?? GetYesterdayInKorea();
        return await store.ReadDailySummaryAsync(targetDate.ToString("yyyy-MM-dd"));
    }

    /// <summary>지정한 날짜의 일간 요약을 조회하거나 새로 생성합니다. 기본값은 한국 시간 기준 전날입니다.</summary>
    public async Task<DailyIssueSummary> GetOrCreateSummaryAsync(DateOnly? date = null, bool force = false, CancellationToken cancellationToken = default)
    {
        var targetDate = date ?? GetYesterdayInKorea();
        var key = targetDate.ToString("yyyy-MM-dd");

        if (!force)
        {
            var existing = await store.ReadDailySummaryAsync(key);
            if (existing is not null && (!openAiClient.IsConfigured || existing.Provider == "openai")) return existing;
        }

        var articles = await store.ReadArticlesAsync();
        var groups = await store.ReadGroupsAsync();
        var summary = BuildSummary(key, groups, articles);
        var aiSummary = await openAiClient.TryGenerateAsync(summary, cancellationToken: cancellationToken);
        if (aiSummary is not null) summary = aiSummary;

        await store.SaveDailySummaryAsync(summary);
        return summary;
    }

    /// <summary>기존 저장 요약을 덮어쓰지 않고 지정 날짜의 일간 요약을 새 로직으로 생성해 반환합니다.</summary>
    public async Task<DailyIssueSummary> GenerateSummaryPreviewAsync(DateOnly? date = null, CancellationToken cancellationToken = default)
    {
        var targetDate = date ?? GetYesterdayInKorea();
        var key = targetDate.ToString("yyyy-MM-dd");

        var articles = await store.ReadArticlesAsync();
        var groups = await store.ReadGroupsAsync();
        var summary = BuildSummary(key, groups, articles);
        var aiSummary = await openAiClient.TryGenerateAsync(summary, cancellationToken: cancellationToken);
        return aiSummary ?? summary;
    }

    /// <summary>한국 시간 기준 전날 요약을 강제로 다시 생성합니다.</summary>
    public async Task EnsureScheduledSummariesAsync(CancellationToken cancellationToken = default)
    {
        await GetOrCreateSummaryAsync(GetYesterdayInKorea(), force: false, cancellationToken);
        await GetOrCreateWeeklySummaryAsync(GetLatestCompletedWeekEndInKorea(), force: false, cancellationToken);
    }

    public async Task<DailyIssueSummary?> GetStoredWeeklySummaryAsync(DateOnly? endDate = null)
    {
        var targetEndDate = endDate ?? GetLatestCompletedWeekEndInKorea();
        var startDate = targetEndDate.AddDays(-6);
        return await store.ReadDailySummaryAsync(WeeklyKey(startDate, targetEndDate));
    }

    /// <summary>종료일을 기준으로 최근 7일 주간 요약을 조회하거나 새로 생성합니다.</summary>
    public async Task<DailyIssueSummary> GetOrCreateWeeklySummaryAsync(DateOnly? endDate = null, bool force = false, CancellationToken cancellationToken = default)
    {
        var targetEndDate = endDate ?? GetLatestCompletedWeekEndInKorea();
        var startDate = targetEndDate.AddDays(-6);
        var key = WeeklyKey(startDate, targetEndDate);

        if (!force)
        {
            var existing = await store.ReadDailySummaryAsync(key);
            if (existing is not null && existing.Provider == "local") return existing;
        }

        var dailySummaries = await ReadDailySummariesAsync(startDate, targetEndDate);
        var summary = BuildWeeklySummaryFromDailySummaries(key, startDate, targetEndDate, dailySummaries);

        await store.SaveDailySummaryAsync(summary);
        return summary;
    }

}
