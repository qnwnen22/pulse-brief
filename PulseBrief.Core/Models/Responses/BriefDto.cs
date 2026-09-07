using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace PulseBrief;

/// <summary>이슈 피드 화면에 내려주는 프론트엔드 전용 브리프 응답 모델입니다.</summary>
public sealed class BriefDto
{
    /// <summary>프론트엔드 이슈 피드에 표시할 이슈 제목입니다.</summary>
    public string Title { get; set; } = "";

    /// <summary>프론트엔드 필터에 사용하는 이슈 카테고리입니다.</summary>
    public string Category { get; set; } = "";

    /// <summary>프론트엔드에 표시할 대표 출처 문자열입니다.</summary>
    public string Source { get; set; } = "";

    public string[] Publishers { get; set; } = [];

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
