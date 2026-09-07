namespace PulseBrief;

/// <summary>관리자가 관리하는 RSS 피드 URL과 활성 상태입니다.</summary>
public sealed record RssFeedEntry(string Url, bool IsActive);
