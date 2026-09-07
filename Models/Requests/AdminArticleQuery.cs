namespace PulseBrief;

/// <summary>관리자 기사 목록 조회 조건입니다.</summary>
public sealed class AdminArticleQuery
{
    /// <summary>제목, 출처, URL, 작성자, RSS 요약에서 검색할 문자열입니다.</summary>
    public string Query { get; set; } = "";

    /// <summary>조회할 이슈 카테고리입니다. 비어 있으면 전체를 조회합니다.</summary>
    public string Category { get; set; } = "";

    /// <summary>조회할 언론사 또는 RSS 출처 이름입니다. 비어 있으면 전체를 조회합니다.</summary>
    public string Source { get; set; } = "";

    /// <summary>본문 수집 상태입니다. success, failed, pending 중 하나를 사용할 수 있습니다.</summary>
    public string ContentStatus { get; set; } = "";

    /// <summary>제외 기사만 볼지, 포함 기사만 볼지 지정합니다. null이면 전체입니다.</summary>
    public bool? Excluded { get; set; }

    /// <summary>1부터 시작하는 페이지 번호입니다.</summary>
    public int Page { get; set; } = 1;

    /// <summary>페이지당 기사 수입니다.</summary>
    public int PageSize { get; set; } = 25;
}
