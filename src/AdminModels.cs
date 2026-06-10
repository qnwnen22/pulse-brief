namespace PulseBrief;

/// <summary>관리자 로그인 요청 본문입니다.</summary>
public sealed class AdminLoginRequest
{
    /// <summary>운영 설정에 저장된 관리자 토큰입니다.</summary>
    public string Token { get; set; } = "";
}

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

/// <summary>관리자 기사 수정 요청 본문입니다.</summary>
public sealed class AdminArticleUpdateRequest
{
    /// <summary>기사 제목을 수동 보정할 때 사용합니다. 비어 있으면 기존 값을 유지합니다.</summary>
    public string? Title { get; set; }

    /// <summary>기사 출처를 수동 보정할 때 사용합니다. 비어 있으면 기존 값을 유지합니다.</summary>
    public string? Source { get; set; }

    /// <summary>기사 작성자를 수동 보정할 때 사용합니다. null이면 기존 값을 유지합니다.</summary>
    public string? Author { get; set; }

    /// <summary>RSS 대표 내용을 수동 보정할 때 사용합니다. null이면 기존 값을 유지합니다.</summary>
    public string? Summary { get; set; }

    /// <summary>기사의 제외 여부를 변경합니다. null이면 기존 값을 유지합니다.</summary>
    public bool? IsExcluded { get; set; }

    /// <summary>해당 기사가 속한 이슈 그룹의 카테고리를 변경합니다. null이면 기존 값을 유지합니다.</summary>
    public string? Category { get; set; }
}

/// <summary>관리자 이슈 그룹 수정 요청 본문입니다.</summary>
public sealed class AdminGroupUpdateRequest
{
    /// <summary>이슈 그룹 카테고리입니다.</summary>
    public string? Category { get; set; }

    /// <summary>이슈 그룹 대표 제목입니다.</summary>
    public string? RepresentativeTitle { get; set; }

    /// <summary>이슈 그룹 대표 내용입니다.</summary>
    public string? Summary { get; set; }
}

/// <summary>RSS 피드 추가 요청 본문입니다.</summary>
public sealed class AdminRssFeedAddRequest
{
    /// <summary>추가할 RSS 피드 URL입니다.</summary>
    public string Url { get; set; } = "";

    /// <summary>추가 즉시 수집 대상으로 활성화할지 여부입니다.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>RSS 피드 상태 변경 요청 본문입니다.</summary>
public sealed class AdminRssFeedUpdateRequest
{
    /// <summary>변경할 RSS 피드 URL입니다.</summary>
    public string Url { get; set; } = "";

    /// <summary>수집 대상 활성화 여부입니다.</summary>
    public bool IsActive { get; set; }
}

/// <summary>RSS 피드 삭제 요청 본문입니다.</summary>
public sealed class AdminRssFeedRemoveRequest
{
    /// <summary>삭제할 RSS 피드 URL입니다.</summary>
    public string Url { get; set; } = "";
}

/// <summary>관리자 작업 실행 요청 본문입니다.</summary>
public sealed class AdminJobRequest
{
    /// <summary>본문/이미지 재수집 등 일괄 작업에서 처리할 최대 기사 수입니다.</summary>
    public int? Limit { get; set; }

    /// <summary>일간 요약을 재생성할 날짜입니다. yyyy-MM-dd 형식이며 비어 있으면 전날입니다.</summary>
    public string? Date { get; set; }

    /// <summary>주간 요약을 재생성할 종료 날짜입니다. yyyy-MM-dd 형식이며 비어 있으면 오늘입니다.</summary>
    public string? EndDate { get; set; }
}
