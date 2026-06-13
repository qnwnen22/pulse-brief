using System.Text.RegularExpressions;

namespace PulseBrief;

/// <summary>저장된 이슈 그룹을 기반으로 일간/주간 요약을 만들고 OpenAI 요약 결과를 캐싱합니다.</summary>
public sealed class DailySummaryService(IArticleStore store, OpenAiDailySummaryClient openAiClient, IConfiguration configuration)
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

    /// <summary>단일 날짜에 해당하는 이슈 그룹과 기사 목록으로 로컬 일간 요약 초안을 만듭니다.</summary>
    private static DailyIssueSummary BuildSummary(string date, IEnumerable<ArticleGroup> groups, IEnumerable<Article> articles)
    {
        var targetDate = DateOnly.Parse(date);
        return BuildSummary(date, groups, articles, targetDate, targetDate, "전날");
    }

    /// <summary>지정한 기간에 포함된 이슈 그룹을 모아 AI 호출 전 로컬 요약 초안을 만듭니다.</summary>
    private static DailyIssueSummary BuildSummary(string date, IEnumerable<ArticleGroup> groups, IEnumerable<Article> articles, DateOnly startDate, DateOnly endDate, string periodLabel)
    {
        var articleById = articles
            .Where(article => !string.IsNullOrWhiteSpace(article.Id))
            .GroupBy(article => article.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var targetGroups = groups
            .Where(group =>
            {
                var groupDate = ToKoreaDate(group.LatestPublishedAt);
                return groupDate >= startDate && groupDate <= endDate;
            })
            .OrderByDescending(group => EffectiveScore(group, articleById))
            .ThenByDescending(group => group.ArticleCount)
            .ThenByDescending(group => group.LatestPublishedAt)
            .ToList();

        var effectiveArticleIds = targetGroups
            .SelectMany(group => ArticleDedupe.EffectiveArticleIds(group, articleById))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sourceCount = targetGroups
            .SelectMany(group => EffectiveSources(group, articleById))
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var candidates = targetGroups
            .Select((group, index) => BuildCandidate(group, index, articleById))
            .ToArray();
        ApplyKeywordDistributionScores(candidates);
        var issueCandidates = DeduplicateIssueCandidates(BuildIssueCandidates(candidates));

        var categorySummaries = candidates
            .GroupBy(candidate => candidate.Group.Category)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Take(5)
            .Select(group =>
            {
                var topIssue = issueCandidates
                    .Where(item => string.Equals(item.Category, group.Key, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(item => item.IsLowBriefingValue ? 1 : 0)
                    .ThenByDescending(item => item.SelectionScore)
                    .ThenByDescending(item => item.Sources.Length)
                    .ThenByDescending(item => item.EffectiveArticleCount)
                    .FirstOrDefault();
                var fallback = group
                    .OrderBy(item => IsLowBriefingValueGroup(item.Group) ? 1 : 0)
                    .ThenByDescending(item => item.SelectionScore)
                    .ThenByDescending(item => item.Sources.Length)
                    .ThenByDescending(item => item.EffectiveArticleCount)
                    .First();
                var title = topIssue?.Title ?? Clean(fallback.Group.RepresentativeTitle);
                var keywords = topIssue?.Keywords ?? fallback.Keywords;
                var keywordText = keywords.Length == 0
                    ? ""
                    : $" 주요 키워드는 {string.Join(", ", keywords.Take(4))}입니다.";
                return new DailyCategorySummary
                {
                    Category = group.Key,
                    IssueCount = group.Count(),
                    ArticleCount = group.SelectMany(item => item.EffectiveArticleIds).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    Summary = $"{title} 이슈가 가장 두드러졌고, 이 카테고리에서 {group.Count()}개 이슈가 확인됐습니다.{keywordText}"
                };
            })
            .ToArray();
        var topIssues = issueCandidates
            .GroupBy(candidate => candidate.Category)
            .SelectMany(group => group
                .OrderBy(item => item.IsLowBriefingValue ? 1 : 0)
                .ThenByDescending(item => item.SelectionScore)
                .ThenByDescending(item => item.Sources.Length)
                .ThenByDescending(item => item.EffectiveArticleCount)
                .ThenByDescending(item => item.LatestPublishedAt)
                .Take(4))
            .OrderByDescending(candidate => categorySummaries.FirstOrDefault(category => category.Category == candidate.Category)?.IssueCount ?? 0)
            .ThenBy(candidate => candidate.IsLowBriefingValue ? 1 : 0)
            .ThenByDescending(candidate => candidate.SelectionScore)
            .ThenByDescending(candidate => candidate.Sources.Length)
            .ThenByDescending(candidate => candidate.EffectiveArticleCount)
            .Take(20)
            .Select(ToDailyTopIssue)
            .ToArray();

        var headline = targetGroups.Count == 0
            ? $"{date}에는 저장된 이슈가 없습니다"
            : $"{date} {periodLabel} 주요 이슈 {targetGroups.Count}건";
        var summary = targetGroups.Count == 0
            ? "해당 기간에 수집된 이슈 그룹이 아직 없습니다."
            : BuildNarrative(categorySummaries, topIssues, periodLabel);

        return new DailyIssueSummary
        {
            Date = date,
            GeneratedAt = DateTimeOffset.UtcNow,
            Provider = "local",
            Headline = headline,
            Summary = summary,
            IssueCount = targetGroups.Count,
            ArticleCount = effectiveArticleIds.Length,
            SourceCount = sourceCount,
            Categories = categorySummaries,
            TopIssues = topIssues
        };
    }

    private static SummaryIssueCandidate[] BuildIssueCandidates(IReadOnlyList<SummaryCandidate> candidates)
    {
        if (candidates.Count == 0) return [];

        var components = BuildRelatedCandidateComponents(candidates);
        var mergedCandidateIndexes = new HashSet<int>();
        var issueCandidates = new List<SummaryIssueCandidate>();

        foreach (var component in components.OrderByDescending(RollupPriority))
        {
            var availableComponent = component
                .Where(candidate => !mergedCandidateIndexes.Contains(candidate.Index))
                .ToArray();
            if (!ShouldCreateRollup(availableComponent, candidates.Count)) continue;

            issueCandidates.Add(ToMergedIssueCandidate(availableComponent));
            foreach (var candidate in availableComponent)
            {
                mergedCandidateIndexes.Add(candidate.Index);
            }
        }

        issueCandidates.AddRange(candidates
            .Where(candidate => !mergedCandidateIndexes.Contains(candidate.Index))
            .Select(ToSingleIssueCandidate));

        return issueCandidates.ToArray();
    }

    private static SummaryIssueCandidate[] DeduplicateIssueCandidates(IEnumerable<SummaryIssueCandidate> candidates)
    {
        var kept = new List<SummaryIssueCandidate>();
        foreach (var candidate in candidates
            .OrderBy(item => item.IsLowBriefingValue ? 1 : 0)
            .ThenByDescending(item => item.ComponentCount > 1)
            .ThenByDescending(item => item.ComponentCount)
            .ThenByDescending(item => item.SelectionScore)
            .ThenByDescending(item => item.Sources.Length)
            .ThenByDescending(item => item.EffectiveArticleCount)
            .ThenByDescending(item => item.LatestPublishedAt))
        {
            if (kept.Any(existing => AreSimilarIssueCandidates(candidate, existing))) continue;
            kept.Add(candidate);
        }

        return kept.ToArray();
    }

    private static bool AreSimilarIssueCandidates(SummaryIssueCandidate left, SummaryIssueCandidate right)
    {
        var leftKeywords = SimilarityKeywords(left);
        var rightKeywords = SimilarityKeywords(right);
        if (leftKeywords.Contains("__vote_issue__") && rightKeywords.Contains("__vote_issue__")) return true;

        var shared = leftKeywords.Intersect(rightKeywords, StringComparer.OrdinalIgnoreCase).ToArray();
        return shared.Length >= 4 || shared.Count(keyword => keyword.Length >= 4) >= 2;
    }

    private static HashSet<string> SimilarityKeywords(SummaryIssueCandidate candidate)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var text = $"{candidate.Title} {candidate.Summary} {string.Join(' ', candidate.Keywords)}";
        if (IsVotingShortageIssueText(text)) result.Add("__vote_issue__");

        foreach (Match match in KeywordRegex.Matches(Clean(text)))
        {
            var keyword = match.Value.ToLowerInvariant();
            if (IsRollupKeyword(keyword)) result.Add(keyword);
        }

        return result;
    }

    private static bool IsVotingShortageIssueText(string value)
    {
        var text = Clean(value);
        var hasVotingSubject = text.Contains("투표용지", StringComparison.OrdinalIgnoreCase)
            || text.Contains("투표지", StringComparison.OrdinalIgnoreCase)
            || text.Contains("투표소", StringComparison.OrdinalIgnoreCase)
            || text.Contains("개표소", StringComparison.OrdinalIgnoreCase)
            || text.Contains("선관위", StringComparison.OrdinalIgnoreCase)
            || text.Contains("참정권", StringComparison.OrdinalIgnoreCase);
        if (!hasVotingSubject) return false;

        return text.Contains("부족", StringComparison.OrdinalIgnoreCase)
            || text.Contains("사태", StringComparison.OrdinalIgnoreCase)
            || text.Contains("재선거", StringComparison.OrdinalIgnoreCase)
            || text.Contains("봉쇄", StringComparison.OrdinalIgnoreCase)
            || text.Contains("잠실", StringComparison.OrdinalIgnoreCase)
            || text.Contains("송파", StringComparison.OrdinalIgnoreCase)
            || text.Contains("검경", StringComparison.OrdinalIgnoreCase)
            || text.Contains("법원", StringComparison.OrdinalIgnoreCase)
            || text.Contains("수사", StringComparison.OrdinalIgnoreCase)
            || text.Contains("증거보전", StringComparison.OrdinalIgnoreCase);
    }

    private static List<SummaryCandidate[]> BuildRelatedCandidateComponents(IReadOnlyList<SummaryCandidate> candidates)
    {
        var candidateKeywords = candidates
            .Select(candidate => SignificantRollupKeywords(candidate).ToArray())
            .ToArray();
        var keywordPairToIndexes = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < candidateKeywords.Length; index++)
        {
            var keywords = candidateKeywords[index].Take(8).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            for (var left = 0; left < keywords.Length - 1; left++)
            {
                for (var right = left + 1; right < keywords.Length; right++)
                {
                    var key = $"{keywords[left]}\u001f{keywords[right]}";
                    if (!keywordPairToIndexes.TryGetValue(key, out var indexes))
                    {
                        indexes = [];
                        keywordPairToIndexes[key] = indexes;
                    }

                    indexes.Add(index);
                }
            }
        }

        var maxKeywordDocumentCount = Math.Min(300, Math.Max(30, candidates.Count / 8));

        return keywordPairToIndexes.Values
            .Where(indexes => indexes.Count >= 3 && indexes.Count <= maxKeywordDocumentCount)
            .Select(indexes => indexes
                .Distinct()
                .Select(index => candidates[index])
                .ToArray())
            .Where(group => group.Length > 1)
            .GroupBy(group => string.Join(',', group.Select(candidate => candidate.Index).Order()))
            .Select(group => group.First())
            .ToList();
    }

    private static int RollupPriority(IReadOnlyCollection<SummaryCandidate> candidates)
    {
        var articleCount = candidates
            .SelectMany(candidate => candidate.EffectiveArticleIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var sourceCount = candidates
            .SelectMany(candidate => candidate.Sources)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var bestSelectionScore = candidates.Max(candidate => candidate.SelectionScore);

        return Math.Min(600, candidates.Count * 6)
            + Math.Min(200, articleCount * 2)
            + Math.Min(120, sourceCount * 12)
            + Math.Min(120, bestSelectionScore);
    }

    private static bool ShouldCreateRollup(IReadOnlyCollection<SummaryCandidate> candidates, int totalCandidateCount)
    {
        if (candidates.Count < 3) return false;
        if (candidates.Count > Math.Max(250, totalCandidateCount / 8)) return false;
        if (candidates.All(candidate => IsLowBriefingValueGroup(candidate.Group))) return false;

        var articleCount = candidates
            .SelectMany(candidate => candidate.EffectiveArticleIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var sourceCount = candidates
            .SelectMany(candidate => candidate.Sources)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return candidates.Count >= 5 || articleCount >= 5 || sourceCount >= 3;
    }

    private static SummaryIssueCandidate ToSingleIssueCandidate(SummaryCandidate candidate)
    {
        return new SummaryIssueCandidate
        {
            Title = Clean(candidate.Group.RepresentativeTitle),
            Category = candidate.Group.Category,
            Summary = Clean(candidate.Group.Summary),
            EffectiveArticleCount = candidate.EffectiveArticleCount,
            EffectiveArticleIds = candidate.EffectiveArticleIds,
            Sources = candidate.Sources.Take(4).ToArray(),
            AllSources = candidate.Sources,
            Score = candidate.EffectiveScore,
            SelectionScore = candidate.SelectionScore,
            Keywords = candidate.Keywords,
            EvidenceArticles = BuildEvidenceArticles(candidate.Articles),
            LatestPublishedAt = candidate.Group.LatestPublishedAt,
            IsLowBriefingValue = IsLowBriefingValueGroup(candidate.Group),
            ComponentCount = 1
        };
    }

    private static SummaryIssueCandidate ToMergedIssueCandidate(IReadOnlyCollection<SummaryCandidate> candidates)
    {
        var representative = candidates
            .OrderBy(candidate => IsLowBriefingValueGroup(candidate.Group) ? 1 : 0)
            .ThenByDescending(candidate => candidate.SelectionScore)
            .ThenByDescending(candidate => candidate.Sources.Length)
            .ThenByDescending(candidate => candidate.EffectiveArticleCount)
            .ThenByDescending(candidate => candidate.Group.LatestPublishedAt)
            .First();
        var category = candidates
            .GroupBy(candidate => candidate.Group.Category)
            .OrderByDescending(group => group.Sum(candidate => candidate.SelectionScore))
            .ThenByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .First()
            .Key;
        var articleIds = candidates
            .SelectMany(candidate => candidate.EffectiveArticleIds)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sources = candidates
            .SelectMany(candidate => candidate.Sources)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var keywordWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var keyword in candidates.SelectMany(candidate => candidate.KeywordWeights))
        {
            keywordWeights[keyword.Key] = keywordWeights.TryGetValue(keyword.Key, out var current)
                ? current + keyword.Value
                : keyword.Value;
        }

        var keywords = TopRollupKeywords(keywordWeights, 8);
        var articleCount = articleIds.Length > 0
            ? articleIds.Length
            : candidates.Sum(candidate => candidate.EffectiveArticleCount);
        var score = Math.Min(
            100,
            IssueSignalCalculator.CalculateImpact(articleCount, sources.Length, representative.Group.RepresentativeTitle, sources)
            + Math.Min(20, (candidates.Count - 1) * 3));
        var selectionScore = representative.SelectionScore
            + Math.Min(120, candidates.Count * 6)
            + Math.Min(80, articleCount * 2)
            + Math.Min(60, sources.Length * 8);
        var relatedTitle = BuildMergedIssueTitle(representative, keywords);
        var summary = BuildMergedIssueSummary(relatedTitle, candidates.Count, keywords);
        var evidenceArticles = candidates
            .SelectMany(candidate => candidate.Articles)
            .GroupBy(article => article.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        return new SummaryIssueCandidate
        {
            Title = relatedTitle,
            Category = category,
            Summary = summary,
            EffectiveArticleCount = articleCount,
            EffectiveArticleIds = articleIds,
            Sources = sources.Take(4).ToArray(),
            AllSources = sources,
            Score = score,
            SelectionScore = selectionScore,
            Keywords = keywords,
            EvidenceArticles = BuildEvidenceArticles(evidenceArticles),
            LatestPublishedAt = candidates.Max(candidate => candidate.Group.LatestPublishedAt),
            IsLowBriefingValue = candidates.All(candidate => IsLowBriefingValueGroup(candidate.Group)),
            ComponentCount = candidates.Count
        };
    }

    private static string BuildMergedIssueTitle(SummaryCandidate representative, IReadOnlyList<string> keywords)
    {
        var title = Clean(representative.Group.RepresentativeTitle);
        if (!string.IsNullOrWhiteSpace(title)) return title;

        var labelKeywords = keywords.Take(3).ToArray();
        return labelKeywords.Length == 0
            ? "관련 이슈"
            : $"{string.Join(' ', labelKeywords)} 관련 이슈";
    }

    private static string BuildMergedIssueSummary(string title, int issueCount, IReadOnlyList<string> keywords)
    {
        var keywordText = keywords.Count == 0
            ? "반복 키워드는 아직 충분하지 않습니다"
            : $"반복 키워드는 {string.Join(", ", keywords.Take(6))}입니다";
        return $"{title}을 포함해 같은 사건 흐름으로 보이는 {issueCount}개 이슈가 함께 확인됐습니다. {keywordText}.";
    }

    private async Task<IReadOnlyList<DailyIssueSummary>> ReadDailySummariesAsync(DateOnly startDate, DateOnly endDate)
    {
        var tasks = EachDate(startDate, endDate)
            .Select(date => store.ReadDailySummaryAsync(date.ToString("yyyy-MM-dd")))
            .ToArray();
        var summaries = await Task.WhenAll(tasks);
        return summaries
            .Where(summary => summary is not null && !summary.Date.StartsWith("weekly:", StringComparison.OrdinalIgnoreCase))
            .Cast<DailyIssueSummary>()
            .OrderBy(summary => summary.Date)
            .ToArray();
    }

    private static DailyIssueSummary BuildWeeklySummaryFromDailySummaries(
        string key,
        DateOnly startDate,
        DateOnly endDate,
        IReadOnlyList<DailyIssueSummary> dailySummaries)
    {
        var summaries = dailySummaries
            .Where(summary => DateOnly.TryParse(summary.Date, out var date) && date >= startDate && date <= endDate)
            .OrderBy(summary => summary.Date)
            .ToArray();

        if (summaries.Length == 0)
        {
            return new DailyIssueSummary
            {
                Date = key,
                GeneratedAt = DateTimeOffset.UtcNow,
                Provider = "local",
                Headline = $"{startDate:yyyy-MM-dd}~{endDate:yyyy-MM-dd} 주간 요약 준비 중",
                Summary = "해당 주간에 생성된 일간 요약이 아직 없습니다.",
                IssueCount = 0,
                ArticleCount = 0,
                SourceCount = 0,
                Categories = [],
                TopIssues = []
            };
        }

        var dailyIssues = summaries
            .SelectMany(summary => summary.TopIssues.Select(issue => new DailyIssueContext(summary.Date, issue)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Issue.Title))
            .ToArray();
        var topIssues = dailyIssues
            .GroupBy(item => item.Issue.Title, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var best = group
                    .OrderByDescending(item => item.Issue.Score)
                    .ThenByDescending(item => item.Issue.ArticleCount)
                    .First()
                    .Issue;
                var articleIds = group
                    .SelectMany(item => item.Issue.ArticleIds)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var sources = group
                    .SelectMany(item => item.Issue.Sources)
                    .Where(source => !string.IsNullOrWhiteSpace(source))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(4)
                    .ToArray();
                var keywords = group
                    .SelectMany(item => item.Issue.Keywords)
                    .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
                    .GroupBy(keyword => keyword, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(keywordGroup => keywordGroup.Count())
                    .ThenBy(keywordGroup => keywordGroup.Key)
                    .Take(8)
                    .Select(keywordGroup => keywordGroup.Key)
                    .ToArray();

                return new DailyTopIssue
                {
                    Title = Clean(best.Title),
                    Category = best.Category,
                    Summary = Clean(best.Summary),
                    ArticleCount = articleIds.Length > 0 ? articleIds.Length : group.Sum(item => item.Issue.ArticleCount),
                    ArticleIds = articleIds,
                    Score = Math.Min(100, group.Max(item => item.Issue.Score) + Math.Min(20, (group.Count() - 1) * 5)),
                    Sources = sources,
                    Keywords = keywords
                };
            })
            .OrderByDescending(issue => issue.Score)
            .ThenByDescending(issue => issue.ArticleCount)
            .Take(20)
            .ToArray();

        var categories = summaries
            .SelectMany(summary => summary.Categories)
            .Where(category => !string.IsNullOrWhiteSpace(category.Category))
            .GroupBy(category => category.Category, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var categoryTopIssue = topIssues
                    .Where(issue => string.Equals(issue.Category, group.Key, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(issue => issue.Score)
                    .ThenByDescending(issue => issue.ArticleCount)
                    .FirstOrDefault();
                var issueCount = group.Sum(category => category.IssueCount);
                var articleCount = group.Sum(category => category.ArticleCount);
                var summary = categoryTopIssue is null
                    ? $"{group.Key} 분야에서 {issueCount}개 이슈와 {articleCount}개 기사가 확인됐습니다."
                    : $"{Clean(categoryTopIssue.Title)} 이슈를 중심으로 {issueCount}개 이슈와 {articleCount}개 기사가 확인됐습니다.";

                return new DailyCategorySummary
                {
                    Category = group.Key,
                    IssueCount = issueCount,
                    ArticleCount = articleCount,
                    Summary = summary
                };
            })
            .OrderByDescending(category => category.IssueCount)
            .ThenBy(category => category.Category)
            .Take(5)
            .ToArray();

        var distinctSources = topIssues
            .SelectMany(issue => issue.Sources)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var headline = $"{startDate:yyyy-MM-dd}~{endDate:yyyy-MM-dd} 주간 주요 이슈 {topIssues.Length}건";
        var summaryText = BuildWeeklyNarrative(categories, topIssues, summaries.Length);

        return new DailyIssueSummary
        {
            Date = key,
            GeneratedAt = DateTimeOffset.UtcNow,
            Provider = "local",
            Headline = headline,
            Summary = summaryText,
            IssueCount = summaries.Sum(summary => summary.IssueCount),
            ArticleCount = summaries.Sum(summary => summary.ArticleCount),
            SourceCount = Math.Max(distinctSources, summaries.Max(summary => summary.SourceCount)),
            Categories = categories,
            TopIssues = topIssues
        };
    }

    private static SummaryCandidate BuildCandidate(ArticleGroup group, int index, IReadOnlyDictionary<string, Article> articleById)
    {
        var articles = ArticleDedupe.EffectiveArticles(group.ArticleIds.Select(id => articleById.TryGetValue(id, out var article) ? article : null));
        var effectiveIds = articles.Select(article => article.Id).ToArray();
        var sources = articles
            .Select(article => Clean(article.Source))
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sources.Length == 0)
        {
            sources = group.Sources
                .Select(Clean)
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var keywordWeights = ExtractKeywordWeights(group, articles);
        return new SummaryCandidate
        {
            Index = index,
            Group = group,
            Articles = articles,
            EffectiveArticleIds = effectiveIds,
            Sources = sources,
            EffectiveScore = EffectiveScore(group, articleById),
            EffectiveArticleCount = effectiveIds.Length > 0 ? effectiveIds.Length : group.ArticleCount,
            KeywordWeights = keywordWeights,
            Keywords = TopKeywords(keywordWeights, 8)
        };
    }

    private static void ApplyKeywordDistributionScores(IReadOnlyCollection<SummaryCandidate> candidates)
    {
        var distributions = candidates
            .GroupBy(candidate => candidate.Group.Category)
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(candidate => candidate.KeywordWeights)
                    .GroupBy(keyword => keyword.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(keywordGroup => keywordGroup.Key, keywordGroup => keywordGroup.Sum(keyword => keyword.Value), StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            distributions.TryGetValue(candidate.Group.Category, out var categoryDistribution);
            var keywordScore = candidate.Keywords
                .Take(6)
                .Sum(keyword => categoryDistribution?.GetValueOrDefault(keyword) ?? 0);
            candidate.KeywordDistributionScore = keywordScore;
            candidate.SelectionScore = candidate.EffectiveScore
                + Math.Min(40, (int)Math.Round(Math.Sqrt(keywordScore) * 4))
                + Math.Min(20, candidate.Sources.Length * 4)
                + Math.Min(15, candidate.EffectiveArticleCount * 2);
        }
    }

    private static DailyTopIssue ToDailyTopIssue(SummaryIssueCandidate candidate)
    {
        return new DailyTopIssue
        {
            Title = candidate.Title,
            Category = candidate.Category,
            Summary = candidate.Summary,
            ArticleCount = candidate.EffectiveArticleCount,
            ArticleIds = candidate.EffectiveArticleIds,
            Score = candidate.Score,
            Sources = candidate.Sources,
            Keywords = candidate.Keywords,
            EvidenceArticles = candidate.EvidenceArticles
        };
    }

    /// <summary>포토/화보처럼 사실 흐름 요약 가치가 낮은 이슈를 대표 요약 후보에서 후순위로 밀기 위해 판별합니다.</summary>
    private static bool IsLowBriefingValueGroup(ArticleGroup group)
    {
        return IssueSignalCalculator.IsLowBriefingValue($"{group.RepresentativeTitle} {group.SeedTitle}", group.Sources);
    }

    /// <summary>요약 정렬과 노출에 사용할 중복 제거 기준의 중요도 점수를 계산합니다.</summary>
    private static int EffectiveScore(ArticleGroup group, IReadOnlyDictionary<string, Article> articleById)
    {
        var effectiveArticleCount = ArticleDedupe.EffectiveArticleCount(group, articleById);
        var sources = EffectiveSources(group, articleById);
        var articleCount = effectiveArticleCount > 0 ? effectiveArticleCount : group.ArticleCount;
        return IssueSignalCalculator.CalculateImpact(articleCount, sources.Length, Clean(group.RepresentativeTitle), sources);
    }

    private static Dictionary<string, int> ExtractKeywordWeights(ArticleGroup group, IReadOnlyList<Article> articles)
    {
        var weights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        AddKeywordWeights(weights, group.RepresentativeTitle, 4);
        AddKeywordWeights(weights, group.SeedTitle, 3);
        AddKeywordWeights(weights, group.Summary, 2);

        foreach (var article in articles.Take(8))
        {
            AddKeywordWeights(weights, article.Title, 3);
            AddKeywordWeights(weights, article.Summary, 2);
            AddKeywordWeights(weights, Compact(article.Content, 2500), 1);
        }

        return weights;
    }

    private static void AddKeywordWeights(IDictionary<string, int> weights, string? text, int weight)
    {
        foreach (Match match in KeywordRegex.Matches(Clean(text ?? "")))
        {
            var keyword = match.Value.ToLowerInvariant();
            if (!IsKeywordCandidate(keyword)) continue;
            weights[keyword] = weights.TryGetValue(keyword, out var current)
                ? current + weight
                : weight;
        }
    }

    private static bool IsKeywordCandidate(string keyword)
    {
        if (keyword.Length < 2 || keyword.Length > 30) return false;
        if (keyword.All(char.IsDigit)) return false;
        if (KeywordStopwords.Contains(keyword)) return false;
        if (keyword.EndsWith("기자", StringComparison.OrdinalIgnoreCase) && keyword.Length <= 5) return false;
        return true;
    }

    private static string[] TopKeywords(IReadOnlyDictionary<string, int> weights, int count)
    {
        return weights
            .OrderByDescending(keyword => keyword.Value)
            .ThenBy(keyword => keyword.Key)
            .Take(count)
            .Select(keyword => keyword.Key)
            .ToArray();
    }

    private static IEnumerable<string> SignificantRollupKeywords(SummaryCandidate candidate)
    {
        return candidate.KeywordWeights
            .Where(keyword => IsRollupKeyword(keyword.Key))
            .OrderByDescending(keyword => keyword.Value)
            .ThenBy(keyword => keyword.Key)
            .Take(12)
            .Select(keyword => keyword.Key);
    }

    private static bool IsRollupKeyword(string keyword)
    {
        if (!IsKeywordCandidate(keyword)) return false;
        if (DateLikeKeywordRegex.IsMatch(keyword)) return false;
        if (keyword.Any(char.IsDigit) && keyword.Length <= 4) return false;
        if (keyword.All(character => character < 128) && keyword.Length <= 3) return false;
        if (RollupKeywordStopwords.Contains(keyword)) return false;
        return keyword.Length >= 2;
    }

    private static string[] TopRollupKeywords(IReadOnlyDictionary<string, int> weights, int count)
    {
        return weights
            .Where(keyword => IsRollupKeyword(keyword.Key))
            .OrderByDescending(keyword => keyword.Value)
            .ThenBy(keyword => keyword.Key)
            .Take(count)
            .Select(keyword => keyword.Key)
            .ToArray();
    }

    private static DailyIssueEvidenceArticle[] BuildEvidenceArticles(IReadOnlyCollection<Article> articles)
    {
        return articles
            .OrderBy(article => string.IsNullOrWhiteSpace(article.Content) ? 1 : 0)
            .ThenByDescending(article => article.PublishedAt)
            .Take(2)
            .Select(article => new DailyIssueEvidenceArticle
            {
                Title = Clean(article.Title),
                Source = Clean(article.Source),
                Summary = Compact(article.Summary, 350),
                ContentExcerpt = Compact(article.Content, 1000),
                Url = article.Url
            })
            .ToArray();
    }

    /// <summary>중복 제거 후 남은 기사에서 출처를 추출하고, 기사 문서가 없으면 그룹의 저장 출처를 사용합니다.</summary>
    private static string[] EffectiveSources(ArticleGroup group, IReadOnlyDictionary<string, Article> articleById)
    {
        var sources = ArticleDedupe.EffectiveArticles(group.ArticleIds.Select(id => articleById.TryGetValue(id, out var article) ? article : null))
            .Select(article => Clean(article.Source))
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return sources.Length > 0
            ? sources
            : group.Sources.Select(Clean).Where(source => !string.IsNullOrWhiteSpace(source)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>카테고리 분포와 대표 이슈를 바탕으로 로컬 fallback용 요약 문장을 만듭니다.</summary>
    private static string BuildNarrative(IReadOnlyList<DailyCategorySummary> categories, IReadOnlyList<DailyTopIssue> topIssues, string periodLabel)
    {
        var categoryText = categories.Count == 0
            ? "특정 카테고리 쏠림은 크지 않았습니다"
            : $"{string.Join(", ", categories.Take(3).Select(category => category.Category))} 분야의 비중이 컸습니다";
        var topText = topIssues.Count == 0
            ? "대표 이슈는 아직 정리되지 않았습니다"
            : $"가장 주목할 이슈는 {topIssues[0].Title}입니다";
        return $"{categoryText}. {topText}. 아래 주요 이슈를 훑으면 {periodLabel} 뉴스 흐름을 빠르게 파악할 수 있습니다.";
    }

    private static string BuildWeeklyNarrative(IReadOnlyList<DailyCategorySummary> categories, IReadOnlyList<DailyTopIssue> topIssues, int dailySummaryCount)
    {
        var categoryText = categories.Count == 0
            ? "주간 카테고리 흐름은 아직 충분히 쌓이지 않았습니다"
            : $"{string.Join(", ", categories.Take(3).Select(category => category.Category))} 분야의 비중이 컸습니다";
        var topText = topIssues.Count == 0
            ? "반복적으로 확인된 대표 이슈는 아직 없습니다"
            : $"가장 반복적으로 확인된 이슈는 {topIssues[0].Title}입니다";
        return $"{dailySummaryCount}개 일간 요약을 바탕으로 정리했습니다. {categoryText}. {topText}.";
    }

    /// <summary>현재 시각을 한국 시간으로 변환해 오늘 날짜를 반환합니다.</summary>
    private static DateOnly GetTodayInKorea()
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, KoreaTimeZone);
        return DateOnly.FromDateTime(now.Date);
    }

    /// <summary>현재 시각을 한국 시간으로 변환해 전날 날짜를 반환합니다.</summary>
    private static DateOnly GetYesterdayInKorea()
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, KoreaTimeZone);
        return DateOnly.FromDateTime(now.Date).AddDays(-1);
    }

    private static DateOnly GetLatestCompletedWeekEndInKorea()
    {
        var today = GetTodayInKorea();
        var daysBack = today.DayOfWeek == DayOfWeek.Sunday
            ? 7
            : (int)today.DayOfWeek;
        return today.AddDays(-daysBack);
    }

    private static string WeeklyKey(DateOnly startDate, DateOnly endDate)
    {
        return $"weekly:{startDate:yyyy-MM-dd}:{endDate:yyyy-MM-dd}";
    }

    private static IEnumerable<DateOnly> EachDate(DateOnly startDate, DateOnly endDate)
    {
        for (var date = startDate; date.DayNumber <= endDate.DayNumber; date = date.AddDays(1))
        {
            yield return date;
        }
    }

    /// <summary>UTC 또는 임의 오프셋 시각을 한국 날짜로 변환합니다.</summary>
    private static DateOnly ToKoreaDate(DateTimeOffset value)
    {
        var local = TimeZoneInfo.ConvertTime(value, KoreaTimeZone);
        return DateOnly.FromDateTime(local.Date);
    }

    /// <summary>요약에 들어갈 텍스트에서 HTML 흔적과 불필요한 공백을 제거합니다.</summary>
    private static string Clean(string value)
    {
        return TextCleaner.Clean(value).Trim();
    }

    private static string Compact(string? value, int maxLength)
    {
        var cleaned = Clean(value ?? "");
        if (cleaned.Length <= maxLength) return cleaned;
        return $"{cleaned[..maxLength].TrimEnd()}...";
    }

    /// <summary>Windows와 Linux 환경 모두에서 한국 표준 시간대를 찾습니다.</summary>
    private static TimeZoneInfo ResolveKoreaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");
        }
    }

    private sealed class SummaryCandidate
    {
        public required int Index { get; init; }

        public required ArticleGroup Group { get; init; }

        public required Article[] Articles { get; init; }

        public required string[] EffectiveArticleIds { get; init; }

        public required string[] Sources { get; init; }

        public required int EffectiveScore { get; init; }

        public required int EffectiveArticleCount { get; init; }

        public required Dictionary<string, int> KeywordWeights { get; init; }

        public required string[] Keywords { get; init; }

        public int KeywordDistributionScore { get; set; }

        public int SelectionScore { get; set; }
    }

    private sealed class SummaryIssueCandidate
    {
        public required string Title { get; init; }

        public required string Category { get; init; }

        public required string Summary { get; init; }

        public required int EffectiveArticleCount { get; init; }

        public required string[] EffectiveArticleIds { get; init; }

        public required string[] Sources { get; init; }

        public required string[] AllSources { get; init; }

        public required int Score { get; init; }

        public required int SelectionScore { get; init; }

        public required string[] Keywords { get; init; }

        public required DailyIssueEvidenceArticle[] EvidenceArticles { get; init; }

        public required DateTimeOffset LatestPublishedAt { get; init; }

        public required bool IsLowBriefingValue { get; init; }

        public required int ComponentCount { get; init; }
    }

    private sealed record DailyIssueContext(string Date, DailyTopIssue Issue);
}
