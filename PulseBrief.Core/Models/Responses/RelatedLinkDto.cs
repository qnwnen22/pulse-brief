using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace PulseBrief;

/// <summary>이슈 카드에서 사용자가 선택할 수 있는 관련 원문 기사 링크 모델입니다.</summary>
public sealed class RelatedLinkDto
{
    /// <summary>관련 원문 기사 제목입니다.</summary>
    public string Title { get; set; } = "";

    /// <summary>관련 원문 기사 출처입니다.</summary>
    public string Source { get; set; } = "";

    public string Publisher { get; set; } = "";

    public string FeedUrl { get; set; } = "";

    /// <summary>관련 원문 기사 URL입니다.</summary>
    public string Url { get; set; } = "";

    /// <summary>관련 원문 기사 대표 이미지 URL입니다.</summary>
    public string ImageUrl { get; set; } = "";

    /// <summary>해당 원문 URL의 본문 수집 상태입니다.</summary>
    public string ContentFetchStatus { get; set; } = "";

    /// <summary>본문 수집 실패 시 UI에 표시할 오류 설명입니다.</summary>
    public string ContentFetchError { get; set; } = "";
}
