namespace PulseBrief;

/// <summary>RSS 피드 추가 요청 본문입니다.</summary>
public sealed class AdminRssFeedAddRequest
{
    /// <summary>추가할 RSS 피드 URL입니다.</summary>
    public string Url { get; set; } = "";

    /// <summary>추가 즉시 수집 대상으로 활성화할지 여부입니다.</summary>
    public bool IsActive { get; set; } = true;
}
