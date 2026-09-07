namespace PulseBrief;

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
