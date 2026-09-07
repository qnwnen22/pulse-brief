using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace PulseBrief;

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
