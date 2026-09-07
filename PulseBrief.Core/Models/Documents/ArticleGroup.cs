using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace PulseBrief;

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
