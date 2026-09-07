using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace PulseBrief;

/// <summary>공개 화면에서 DB 전체 스캔 없이 사용하는 캐시된 뉴스 지표입니다.</summary>
[BsonIgnoreExtraElements]
public sealed class NewsStats
{
    public const string PublicId = "public";

    /// <summary>통계 문서 식별자입니다. 공개 지표는 public 단일 문서를 사용합니다.</summary>
    public string Id { get; set; } = PublicId;

    /// <summary>통계가 계산된 한국 시간 날짜입니다.</summary>
    public string TodayDate { get; set; } = "";

    /// <summary>한국 시간 기준 오늘 발행된 저장 기사 수입니다.</summary>
    public int TodayArticleCount { get; set; }

    /// <summary>통계가 마지막으로 계산된 시각입니다.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>실제 수집 파이프라인에서 계산된 값인지 여부입니다.</summary>
    public bool IsReady { get; set; } = true;

    public static NewsStats WaitingFor(DateOnly today)
    {
        return new NewsStats
        {
            TodayDate = KoreaDate.Key(today),
            UpdatedAt = DateTimeOffset.UtcNow,
            IsReady = false
        };
    }
}
