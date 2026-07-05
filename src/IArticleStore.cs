namespace PulseBrief;

/// <summary>기사, 이슈 그룹, 요약 데이터를 저장하고 조회하는 저장소 계약입니다.</summary>
public interface IArticleStore
{
    /// <summary>저장된 전체 기사 목록을 최신 발행 순으로 조회합니다.</summary>
    Task<List<Article>> ReadArticlesAsync();

    Task<List<Article>> ReadRecentArticlesAsync(int limit);

    /// <summary>저장된 전체 이슈 그룹 목록을 최신 발행 순으로 조회합니다.</summary>
    Task<List<ArticleGroup>> ReadGroupsAsync();

    Task<List<ArticleGroup>> ReadRecentGroupsAsync(int limit);

    Task<List<Article>> ReadArticlesByIdsAsync(IReadOnlyCollection<string> ids);

    /// <summary>날짜 또는 주간 키에 해당하는 저장된 요약을 조회합니다.</summary>
    Task<DailyIssueSummary?> ReadDailySummaryAsync(string date);

    /// <summary>저장된 모든 일간/주간 요약 문서를 조회합니다.</summary>
    Task<List<DailyIssueSummary>> ReadDailySummariesAsync();

    /// <summary>일간 또는 주간 요약 문서를 저장하거나 갱신합니다.</summary>
    Task SaveDailySummaryAsync(DailyIssueSummary summary);

    /// <summary>기사 목록을 저장하거나 기존 기사 문서를 갱신합니다.</summary>
    Task SaveArticlesAsync(IReadOnlyCollection<Article> articles);

    /// <summary>현재 계산된 이슈 그룹 목록으로 저장소의 그룹 데이터를 교체합니다.</summary>
    Task SaveGroupsAsync(IReadOnlyCollection<ArticleGroup> groups);

    /// <summary>RSS로 새로 수집한 기사를 기존 데이터와 병합하고 전체 기사 목록을 반환합니다.</summary>
    Task<List<Article>> UpsertArticlesAsync(IReadOnlyCollection<Article> incoming);
}
