namespace PulseBrief;

/// <summary>RSS 피드 상태 변경 요청 본문입니다.</summary>
public sealed class AdminRssFeedUpdateRequest
{
    /// <summary>변경할 RSS 피드 URL입니다.</summary>
    public string Url { get; set; } = "";

    /// <summary>수집 대상 활성화 여부입니다.</summary>
    public bool IsActive { get; set; }
}
