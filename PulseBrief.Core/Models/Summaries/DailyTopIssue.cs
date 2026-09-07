using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace PulseBrief;

/// <summary>요약 문서 안에서 중요도가 높은 대표 이슈를 표현합니다.</summary>
public sealed class DailyTopIssue
{
    // Resolve public links at read time without changing stored summary documents.
    [BsonIgnore]
    public RelatedLinkDto[] RelatedLinks { get; set; } = [];

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

    /// <summary>대표 이슈 선별에 사용한 주요 키워드 목록입니다.</summary>
    public string[] Keywords { get; set; } = [];

    /// <summary>OpenAI 입력에만 사용하는 대표 기사 근거입니다. 저장소와 공개 API 응답에는 포함하지 않습니다.</summary>
    [BsonIgnore]
    [JsonIgnore]
    public DailyIssueEvidenceArticle[] EvidenceArticles { get; set; } = [];
}
