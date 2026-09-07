using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace PulseBrief;

/// <summary>AI 요약 생성 시 대표 이슈의 사실 근거로 넘기는 기사 일부입니다.</summary>
public sealed class DailyIssueEvidenceArticle
{
    /// <summary>근거 기사 제목입니다.</summary>
    public string Title { get; set; } = "";

    /// <summary>근거 기사 출처입니다.</summary>
    public string Source { get; set; } = "";

    /// <summary>RSS에서 제공된 요약입니다.</summary>
    public string Summary { get; set; } = "";

    /// <summary>수집된 본문 일부입니다.</summary>
    public string ContentExcerpt { get; set; } = "";

    /// <summary>원문 URL입니다.</summary>
    public string Url { get; set; } = "";
}
