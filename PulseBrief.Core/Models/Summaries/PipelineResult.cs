using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace PulseBrief;

/// <summary>RSS 수집, 저장, 그룹화 파이프라인 실행 결과입니다.</summary>
public sealed record PipelineResult(int FetchedCount, int ArticleCount, int GroupCount, DateTimeOffset UpdatedAt);
