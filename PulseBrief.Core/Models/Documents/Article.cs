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

    /// <summary>관리자 검수에서 공개 화면과 요약 후보에서 제외하도록 표시한 기사인지 여부입니다.</summary>
    public bool IsExcluded { get; set; }

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
