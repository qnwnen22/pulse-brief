namespace PulseBrief;

/// <summary>RSS 피드 삭제 요청 본문입니다.</summary>
public sealed class AdminRssFeedRemoveRequest
{
    /// <summary>삭제할 RSS 피드 URL입니다.</summary>
    public string Url { get; set; } = "";
}
