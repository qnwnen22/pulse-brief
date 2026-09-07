namespace PulseBrief;

/// <summary>RSS 설정 조회 및 변경과 운영 이력 기록을 담당합니다.</summary>
public sealed class AdminFeedService(AppPaths paths, OperationalLogService operationalLog)
{
    public async Task<object> ReadAsync()
    {
        var entries = await paths.ReadFeedEntriesAsync();
        return new
        {
            totalCount = entries.Count,
            activeCount = entries.Count(feed => feed.IsActive),
            inactiveCount = entries.Count(feed => !feed.IsActive),
            feeds = entries.Select(feed =>
            {
                var source = RssSourceCatalog.SourceInfoForUrl(feed.Url);
                return new { feed.Url, feed.IsActive, publisher = source.Publisher, guideUrl = source.GuideUrl };
            })
        };
    }

    public async Task<bool> AddAsync(string url, bool isActive, CancellationToken cancellationToken)
    {
        var entries = (await paths.ReadFeedEntriesAsync()).ToList();
        if (entries.Any(feed => string.Equals(feed.Url, url, StringComparison.OrdinalIgnoreCase))) return false;
        entries.Add(new RssFeedEntry(url, isActive));
        await paths.SaveFeedEntriesAsync(entries);
        await operationalLog.RecordAsync("info", "admin_rss_feed_added", "RSS feed was added from admin console.", new { url, IsActive = isActive }, cancellationToken);
        return true;
    }

    public async Task<bool> UpdateAsync(string url, bool isActive, CancellationToken cancellationToken)
    {
        var entries = (await paths.ReadFeedEntriesAsync()).ToList();
        var index = entries.FindIndex(feed => string.Equals(feed.Url, url, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;
        entries[index] = new RssFeedEntry(entries[index].Url, isActive);
        await paths.SaveFeedEntriesAsync(entries);
        await operationalLog.RecordAsync("info", "admin_rss_feed_updated", "RSS feed status was updated from admin console.", new { url, IsActive = isActive }, cancellationToken);
        return true;
    }

    public async Task<bool> RemoveAsync(string url, CancellationToken cancellationToken)
    {
        var entries = (await paths.ReadFeedEntriesAsync()).ToList();
        if (entries.RemoveAll(feed => string.Equals(feed.Url, url, StringComparison.OrdinalIgnoreCase)) == 0) return false;
        await paths.SaveFeedEntriesAsync(entries);
        await operationalLog.RecordAsync("info", "admin_rss_feed_removed", "RSS feed was removed from admin console.", new { url }, cancellationToken);
        return true;
    }
}
