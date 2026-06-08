using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace PulseBrief;

/// <summary>RSS 수집과 원문 본문 추출 결과를 담는 개별 뉴스 기사 문서입니다.</summary>
[BsonIgnoreExtraElements]
public sealed class Article
{
    /// <summary>기사 URL과 제목/발행일을 기준으로 생성한 내부 기사 식별자입니다.</summary>
    public string Id { get; set; } = "";

    /// <summary>RSS 또는 기사 페이지에서 수집한 기사 제목입니다.</summary>
    public string Title { get; set; } = "";

    /// <summary>원문 기사 페이지 URL입니다.</summary>
    public string Url { get; set; } = "";

    /// <summary>RSS 채널명 또는 언론사/출처 이름입니다.</summary>
    public string Source { get; set; } = "";

    /// <summary>RSS 항목에서 확인된 기사 작성자 또는 제공자 이름입니다. 제공되지 않으면 빈 문자열입니다.</summary>
    public string Author { get; set; } = "";

    /// <summary>이 기사를 발견한 RSS 피드 URL입니다.</summary>
    public string FeedUrl { get; set; } = "";

    /// <summary>RSS에서 제공한 짧은 기사 요약 또는 설명입니다.</summary>
    public string Summary { get; set; } = "";

    /// <summary>RSS 이미지 태그 또는 원문 페이지의 og:image에서 수집한 대표 이미지 URL입니다.</summary>
    public string ImageUrl { get; set; } = "";

    /// <summary>원문 URL에 접근해 추출한 기사 본문입니다. 추출 실패 시 빈 문자열입니다.</summary>
    public string Content { get; set; } = "";

    /// <summary>기사 본문 수집을 마지막으로 시도한 시각입니다.</summary>
    public DateTimeOffset? ContentFetchedAt { get; set; }

    /// <summary>본문 수집 상태입니다. 빈 값은 아직 미시도, success는 성공, failed는 실패를 의미합니다.</summary>
    public string ContentFetchStatus { get; set; } = "";

    /// <summary>본문 수집 실패 시 사람이 이해할 수 있도록 저장하는 오류 설명입니다.</summary>
    public string ContentFetchError { get; set; } = "";

    /// <summary>기사 발행 시각입니다. RSS에 발행일이 없으면 수집 시각으로 대체됩니다.</summary>
    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>이 기사가 처음 DB에 저장된 시각입니다.</summary>
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>이 기사 문서가 마지막으로 갱신된 시각입니다.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>유사 기사 그룹화를 위해 생성한 로컬 임베딩 벡터입니다.</summary>
    public double[]? Embedding { get; set; }
}

/// <summary>유사 기사들을 하나의 이슈로 묶은 그룹 문서입니다.</summary>
[BsonIgnoreExtraElements]
public sealed class ArticleGroup
{
    /// <summary>그룹화 과정에서 생성한 이슈 그룹 식별자입니다.</summary>
    public string Id { get; set; } = "";

    /// <summary>자동 분류된 이슈 카테고리입니다.</summary>
    public string Category { get; set; } = "사회";

    /// <summary>이 그룹에 포함된 기사 ID 목록입니다.</summary>
    public string[] ArticleIds { get; set; } = [];

    /// <summary>이 그룹에 포함된 기사 수입니다.</summary>
    public int ArticleCount { get; set; }

    /// <summary>이 그룹에 포함된 기사들의 출처 목록입니다.</summary>
    public string[] Sources { get; set; } = [];

    /// <summary>그룹 내 가장 최근 기사 발행 시각입니다.</summary>
    public DateTimeOffset LatestPublishedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>기사 수와 출처 다양성 등을 기반으로 계산한 중요도 점수입니다.</summary>
    public int Score { get; set; }

    /// <summary>그룹 생성 시 기준이 된 초기 기사 제목입니다.</summary>
    public string SeedTitle { get; set; } = "";

    /// <summary>그룹 생성 시 기준이 된 초기 기사 요약 또는 본문 일부입니다.</summary>
    public string SeedSummary { get; set; } = "";

    /// <summary>이슈 피드에 표시할 대표 제목입니다.</summary>
    public string RepresentativeTitle { get; set; } = "";

    /// <summary>이슈 피드에 표시할 그룹 요약입니다.</summary>
    public string Summary { get; set; } = "";
}

/// <summary>이슈 피드 화면에 내려주는 프론트엔드 전용 브리프 응답 모델입니다.</summary>
public sealed class BriefDto
{
    /// <summary>프론트엔드 이슈 피드에 표시할 이슈 제목입니다.</summary>
    public string Title { get; set; } = "";

    /// <summary>프론트엔드 필터에 사용하는 이슈 카테고리입니다.</summary>
    public string Category { get; set; } = "";

    /// <summary>프론트엔드에 표시할 대표 출처 문자열입니다.</summary>
    public string Source { get; set; } = "";

    /// <summary>가장 최근 기사 발행 후 경과 시간(분)입니다.</summary>
    public int Minutes { get; set; }

    /// <summary>프론트엔드에서 중요도 표시와 hot 여부 판단에 쓰는 점수입니다.</summary>
    public int Impact { get; set; }

    /// <summary>프론트엔드 강조 상태입니다. 보통 hot 또는 normal을 사용합니다.</summary>
    public string Heat { get; set; } = "normal";

    /// <summary>프론트엔드 카드에서 더보기로 확인하는 이슈 요약입니다.</summary>
    public string Summary { get; set; } = "";

    /// <summary>이슈 피드 썸네일에 표시할 대표 이미지 URL입니다.</summary>
    public string ImageUrl { get; set; } = "";

    /// <summary>이슈 제목에서 추출한 간단한 키워드 목록입니다.</summary>
    public string[] Keywords { get; set; } = [];

    /// <summary>이 이슈 그룹에 포함된 기사 수입니다.</summary>
    public int ArticleCount { get; set; }

    /// <summary>이 이슈 그룹에 포함된 기사 ID 목록입니다.</summary>
    public string[] ArticleIds { get; set; } = [];

    /// <summary>이 그룹 내 가장 최근 기사 발행 시각입니다.</summary>
    public DateTimeOffset LatestPublishedAt { get; set; }

    /// <summary>사용자가 원문 출처를 선택할 수 있도록 내려주는 관련 기사 링크 목록입니다.</summary>
    public RelatedLinkDto[] RelatedLinks { get; set; } = [];
}

/// <summary>이슈 카드에서 사용자가 선택할 수 있는 관련 원문 기사 링크 모델입니다.</summary>
public sealed class RelatedLinkDto
{
    /// <summary>관련 원문 기사 제목입니다.</summary>
    public string Title { get; set; } = "";

    /// <summary>관련 원문 기사 출처입니다.</summary>
    public string Source { get; set; } = "";

    /// <summary>관련 원문 기사 URL입니다.</summary>
    public string Url { get; set; } = "";

    /// <summary>관련 원문 기사 대표 이미지 URL입니다.</summary>
    public string ImageUrl { get; set; } = "";

    /// <summary>해당 원문 URL의 본문 수집 상태입니다.</summary>
    public string ContentFetchStatus { get; set; } = "";

    /// <summary>본문 수집 실패 시 UI에 표시할 오류 설명입니다.</summary>
    public string ContentFetchError { get; set; } = "";
}

/// <summary>전날 또는 주간 이슈 요약 결과를 저장하는 요약 문서입니다.</summary>
[BsonIgnoreExtraElements]
public sealed class DailyIssueSummary
{
    /// <summary>요약 대상 날짜 또는 주간 요약 키입니다. 주간은 weekly:시작일:종료일 형식을 사용합니다.</summary>
    public string Date { get; set; } = "";

    /// <summary>요약 문서가 생성되거나 갱신된 시각입니다.</summary>
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>요약 생성 방식입니다. openai는 AI 요약, local은 로컬 규칙 기반 요약입니다.</summary>
    public string Provider { get; set; } = "local";

    /// <summary>AI 요약에 사용한 모델명입니다. 로컬 요약이면 빈 값일 수 있습니다.</summary>
    public string Model { get; set; } = "";

    /// <summary>요약 전체를 대표하는 짧은 제목입니다.</summary>
    public string Headline { get; set; } = "";

    /// <summary>전체 요약 문장입니다. 현재 UI에서는 주로 카테고리별 요약을 우선 사용합니다.</summary>
    public string Summary { get; set; } = "";

    /// <summary>요약 대상 기간에 포함된 이슈 그룹 수입니다.</summary>
    public int IssueCount { get; set; }

    /// <summary>요약 대상 기간에 포함된 중복 제거 기사 수입니다.</summary>
    public int ArticleCount { get; set; }

    /// <summary>요약 대상 기간에 확인된 중복 제거 출처 수입니다.</summary>
    public int SourceCount { get; set; }

    /// <summary>카테고리별 이슈 수, 기사 수, 요약 문장 목록입니다.</summary>
    public DailyCategorySummary[] Categories { get; set; } = [];

    /// <summary>요약 대상 기간에서 중요도가 높은 대표 이슈 목록입니다.</summary>
    public DailyTopIssue[] TopIssues { get; set; } = [];
}

/// <summary>요약 문서 안에서 카테고리별 지표와 요약 문장을 표현합니다.</summary>
public sealed class DailyCategorySummary
{
    /// <summary>요약 카테고리 이름입니다.</summary>
    public string Category { get; set; } = "";

    /// <summary>해당 카테고리에 포함된 이슈 그룹 수입니다.</summary>
    public int IssueCount { get; set; }

    /// <summary>해당 카테고리에 포함된 중복 제거 기사 수입니다.</summary>
    public int ArticleCount { get; set; }

    /// <summary>해당 카테고리의 핵심 흐름을 설명하는 요약 문장입니다.</summary>
    public string Summary { get; set; } = "";
}

/// <summary>요약 문서 안에서 중요도가 높은 대표 이슈를 표현합니다.</summary>
public sealed class DailyTopIssue
{
    /// <summary>대표 이슈 제목입니다.</summary>
    public string Title { get; set; } = "";

    /// <summary>대표 이슈의 카테고리입니다.</summary>
    public string Category { get; set; } = "";

    /// <summary>대표 이슈의 핵심 내용을 설명하는 요약 문장입니다.</summary>
    public string Summary { get; set; } = "";

    /// <summary>대표 이슈에 연결된 기사 수입니다.</summary>
    public int ArticleCount { get; set; }

    /// <summary>대표 이슈를 구성하는 원본 기사 ID 목록입니다.</summary>
    public string[] ArticleIds { get; set; } = [];

    /// <summary>대표 이슈의 중요도 점수입니다.</summary>
    public int Score { get; set; }

    /// <summary>대표 이슈를 보도한 주요 출처 목록입니다.</summary>
    public string[] Sources { get; set; } = [];
}

/// <summary>RSS 수집, 저장, 그룹화 파이프라인 실행 결과입니다.</summary>
public sealed record PipelineResult(int FetchedCount, int ArticleCount, int GroupCount, DateTimeOffset UpdatedAt);
