using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace PulseBrief;

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
