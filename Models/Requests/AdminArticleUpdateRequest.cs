namespace PulseBrief;

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
