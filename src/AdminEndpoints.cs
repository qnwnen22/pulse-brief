namespace PulseBrief;

/// <summary>관리자 페이지에서 사용하는 인증, 데이터 검수, RSS 관리, 운영 작업 API를 등록합니다.</summary>
public static class AdminEndpoints
{
    private static readonly string[] KnownCategories =
    [
        "정치/정책",
        "경제/산업",
        "사회",
        "국제",
        "IT/과학",
        "문화/연예",
        "스포츠",
        "생활/건강",
        "지역"
    ];

    /// <summary>ASP.NET Core 애플리케이션에 관리자 전용 API 라우트를 연결합니다.</summary>
    public static void MapAdminEndpoints(this WebApplication app, DateTimeOffset appStartedAt)
    {
        app.MapGet("/admin", (IWebHostEnvironment environment) =>
        {
            return Results.File(AdminIndexPath(environment), "text/html");
        });

        app.MapPost("/api/admin/login", (HttpContext context, AdminLoginRequest request, AdminAuthService adminAuth) =>
        {
            var result = adminAuth.SignIn(context, request.Token);
            return result is null ? AdminAuthService.AdminRequired() : Results.Ok(result);
        });

        app.MapGet("/api/admin/session", (HttpContext context, AdminAuthService adminAuth) =>
        {
            if (!adminAuth.IsAuthenticated(context)) return Results.Ok(new { authenticated = false });

            return Results.Ok(new
            {
                authenticated = true,
                csrfToken = adminAuth.CreateCsrfToken(context)
            });
        });

        app.MapPost("/api/admin/logout", (HttpContext context, AdminAuthService adminAuth) =>
        {
            if (!RequireAdmin(context, adminAuth, requireCsrf: true, out var denied)) return denied;

            adminAuth.SignOut(context);
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/admin/dashboard", async (
            HttpContext context,
            OperationalDiagnosticsService diagnostics,
            AdminAuthService adminAuth) =>
        {
            if (!RequireAdmin(context, adminAuth, requireCsrf: false, out var denied)) return denied;

            return Results.Ok(await diagnostics.BuildAsync(appStartedAt));
        });

        app.MapGet("/api/admin/articles", async (
            HttpContext context,
            string? query,
            string? category,
            string? source,
            string? contentStatus,
            bool? excluded,
            int? page,
            int? pageSize,
            IArticleStore store,
            AdminAuthService adminAuth) =>
        {
            if (!RequireAdmin(context, adminAuth, requireCsrf: false, out var denied)) return denied;

            var adminQuery = new AdminArticleQuery
            {
                Query = query ?? "",
                Category = category ?? "",
                Source = source ?? "",
                ContentStatus = contentStatus ?? "",
                Excluded = excluded,
                Page = page.GetValueOrDefault(1),
                PageSize = pageSize.GetValueOrDefault(25)
            };

            var articles = await store.ReadArticlesAsync();
            var groups = await store.ReadGroupsAsync();
            return Results.Ok(BuildArticleSearchResult(articles, groups, adminQuery));
        });

        app.MapGet("/api/admin/articles/{id}", async (
            HttpContext context,
            string id,
            IArticleStore store,
            AdminAuthService adminAuth) =>
        {
            if (!RequireAdmin(context, adminAuth, requireCsrf: false, out var denied)) return denied;

            var articles = await store.ReadArticlesAsync();
            var groups = await store.ReadGroupsAsync();
            var article = articles.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (article is null) return Results.NotFound(new { error = "article_not_found" });

            return Results.Ok(ToAdminArticleDetail(article, groups.FirstOrDefault(group => group.ArticleIds.Contains(article.Id, StringComparer.OrdinalIgnoreCase))));
        });

        app.MapPatch("/api/admin/articles/{id}", async (
            HttpContext context,
            string id,
            AdminArticleUpdateRequest request,
            IArticleStore store,
            OperationalLogService operationalLog,
            AdminAuthService adminAuth,
            CancellationToken cancellationToken) =>
        {
            if (!RequireAdmin(context, adminAuth, requireCsrf: true, out var denied)) return denied;

            var articles = await store.ReadArticlesAsync();
            var groups = await store.ReadGroupsAsync();
            var article = articles.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (article is null) return Results.NotFound(new { error = "article_not_found" });

            ApplyArticleUpdate(article, request);
            var changedGroup = ApplyGroupCategoryForArticle(groups, article.Id, request.Category);
            await store.SaveArticlesAsync(articles);
            await store.SaveGroupsAsync(RemoveExcludedArticlesFromGroups(groups, articles));
            await operationalLog.RecordAsync("info", "admin_article_updated", "Article metadata was updated from admin console.", new
            {
                article.Id,
                article.Title,
                article.IsExcluded,
                category = changedGroup?.Category
            }, cancellationToken);

            return Results.Ok(ToAdminArticleDetail(article, changedGroup));
        });

        app.MapPatch("/api/admin/groups/{id}", async (
            HttpContext context,
            string id,
            AdminGroupUpdateRequest request,
            IArticleStore store,
            OperationalLogService operationalLog,
            AdminAuthService adminAuth,
            CancellationToken cancellationToken) =>
        {
            if (!RequireAdmin(context, adminAuth, requireCsrf: true, out var denied)) return denied;

            var groups = await store.ReadGroupsAsync();
            var group = groups.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (group is null) return Results.NotFound(new { error = "group_not_found" });

            ApplyGroupUpdate(group, request);
            await store.SaveGroupsAsync(groups);
            await operationalLog.RecordAsync("info", "admin_group_updated", "Issue group metadata was updated from admin console.", new
            {
                group.Id,
                group.Category,
                group.RepresentativeTitle
            }, cancellationToken);

            return Results.Ok(group);
        });

        app.MapGet("/api/admin/rss-feeds", async (HttpContext context, AppPaths paths, AdminAuthService adminAuth) =>
        {
            if (!RequireAdmin(context, adminAuth, requireCsrf: false, out var denied)) return denied;

            var entries = await paths.ReadFeedEntriesAsync();
            return Results.Ok(new
            {
                totalCount = entries.Count,
                activeCount = entries.Count(feed => feed.IsActive),
                inactiveCount = entries.Count(feed => !feed.IsActive),
                feeds = entries.Select(ToAdminRssFeed)
            });
        });

        app.MapPost("/api/admin/rss-feeds", async (
            HttpContext context,
            AdminRssFeedAddRequest request,
            AppPaths paths,
            OperationalLogService operationalLog,
            AdminAuthService adminAuth,
            CancellationToken cancellationToken) =>
        {
            if (!RequireAdmin(context, adminAuth, requireCsrf: true, out var denied)) return denied;
            if (!TryNormalizeUrl(request.Url, out var url)) return Results.BadRequest(new { error = "invalid_url" });

            var entries = (await paths.ReadFeedEntriesAsync()).ToList();
            if (entries.Any(feed => string.Equals(feed.Url, url, StringComparison.OrdinalIgnoreCase)))
            {
                return Results.Conflict(new { error = "feed_already_exists" });
            }

            entries.Add(new RssFeedEntry(url, request.IsActive));
            await paths.SaveFeedEntriesAsync(entries);
            await operationalLog.RecordAsync("info", "admin_rss_feed_added", "RSS feed was added from admin console.", new { url, request.IsActive }, cancellationToken);
            return Results.Ok(new { ok = true, url, request.IsActive });
        });

        app.MapPatch("/api/admin/rss-feeds", async (
            HttpContext context,
            AdminRssFeedUpdateRequest request,
            AppPaths paths,
            OperationalLogService operationalLog,
            AdminAuthService adminAuth,
            CancellationToken cancellationToken) =>
        {
            if (!RequireAdmin(context, adminAuth, requireCsrf: true, out var denied)) return denied;
            if (!TryNormalizeUrl(request.Url, out var url)) return Results.BadRequest(new { error = "invalid_url" });

            var entries = (await paths.ReadFeedEntriesAsync()).ToList();
            var index = entries.FindIndex(feed => string.Equals(feed.Url, url, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return Results.NotFound(new { error = "feed_not_found" });

            entries[index] = new RssFeedEntry(entries[index].Url, request.IsActive);
            await paths.SaveFeedEntriesAsync(entries);
            await operationalLog.RecordAsync("info", "admin_rss_feed_updated", "RSS feed status was updated from admin console.", new { url, request.IsActive }, cancellationToken);
            return Results.Ok(new { ok = true, url, request.IsActive });
        });

        app.MapPost("/api/admin/rss-feeds/remove", async (
            HttpContext context,
            AdminRssFeedRemoveRequest request,
            AppPaths paths,
            OperationalLogService operationalLog,
            AdminAuthService adminAuth,
            CancellationToken cancellationToken) =>
        {
            if (!RequireAdmin(context, adminAuth, requireCsrf: true, out var denied)) return denied;
            if (!TryNormalizeUrl(request.Url, out var url)) return Results.BadRequest(new { error = "invalid_url" });

            var entries = (await paths.ReadFeedEntriesAsync()).ToList();
            var removed = entries.RemoveAll(feed => string.Equals(feed.Url, url, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return Results.NotFound(new { error = "feed_not_found" });

            await paths.SaveFeedEntriesAsync(entries);
            await operationalLog.RecordAsync("info", "admin_rss_feed_removed", "RSS feed was removed from admin console.", new { url }, cancellationToken);
            return Results.Ok(new { ok = true, url });
        });

        app.MapPost("/api/admin/refresh", async (
            HttpContext context,
            NewsPipeline pipeline,
            OperationalLogService operationalLog,
            AdminAuthService adminAuth,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            if (!RequireAdmin(context, adminAuth, requireCsrf: true, out var denied)) return denied;
            if (!configuration.GetValue("Collector:AllowWebManualRefresh", false))
            {
                return Results.Json(
                    new
                    {
                        error = "collector_separated",
                        message = "RSS 수집은 PulseBrief.Collector에서 실행됩니다."
                    },
                    statusCode: StatusCodes.Status409Conflict);
            }

            await operationalLog.RecordAsync("info", "manual_refresh_requested", "Manual refresh was requested from admin console.", cancellationToken: cancellationToken);
            var result = await pipeline.RunAsync(cancellationToken);
            return Results.Ok(result);
        });

        app.MapPost("/api/admin/summaries/daily/regenerate", async (
            HttpContext context,
            AdminJobRequest request,
            DailySummaryService dailySummaryService,
            OperationalLogService operationalLog,
            AdminAuthService adminAuth,
            CancellationToken cancellationToken) =>
        {
            if (!RequireAdmin(context, adminAuth, requireCsrf: true, out var denied)) return denied;
            if (!TryParseDate(request.Date, "date", out var date, out var error)) return error!;

            var summary = await dailySummaryService.GetOrCreateSummaryAsync(date, force: true, cancellationToken);
            await operationalLog.RecordAsync("info", "admin_daily_summary_regenerated", "Daily summary was regenerated from admin console.", new { summary.Date, summary.Provider, summary.Model }, cancellationToken);
            return Results.Ok(summary);
        });

        app.MapPost("/api/admin/summaries/weekly/regenerate", async (
            HttpContext context,
            AdminJobRequest request,
            DailySummaryService dailySummaryService,
            OperationalLogService operationalLog,
            AdminAuthService adminAuth,
            CancellationToken cancellationToken) =>
        {
            if (!RequireAdmin(context, adminAuth, requireCsrf: true, out var denied)) return denied;
            if (!TryParseDate(request.EndDate, "endDate", out var endDate, out var error)) return error!;

            var summary = await dailySummaryService.GetOrCreateWeeklySummaryAsync(endDate, force: true, cancellationToken);
            await operationalLog.RecordAsync("info", "admin_weekly_summary_regenerated", "Weekly summary was regenerated from admin console.", new { summary.Date, summary.Provider, summary.Model }, cancellationToken);
            return Results.Ok(summary);
        });
    }

    private static string AdminIndexPath(IWebHostEnvironment environment)
    {
        var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        return Path.Combine(webRoot, "admin", "index.html");
    }

    private static bool RequireAdmin(HttpContext context, AdminAuthService adminAuth, bool requireCsrf, out IResult denied)
    {
        if (!adminAuth.IsAuthenticated(context))
        {
            denied = AdminAuthService.AdminRequired();
            return false;
        }

        if (requireCsrf && !adminAuth.HasValidCsrf(context))
        {
            denied = AdminAuthService.CsrfRequired();
            return false;
        }

        denied = Results.Empty;
        return true;
    }

    private static object ToAdminRssFeed(RssFeedEntry feed)
    {
        var source = RssSourceCatalog.SourceInfoForUrl(feed.Url);
        return new
        {
            feed.Url,
            feed.IsActive,
            publisher = source.Publisher,
            guideUrl = source.GuideUrl
        };
    }

    private static object BuildArticleSearchResult(IReadOnlyList<Article> articles, IReadOnlyList<ArticleGroup> groups, AdminArticleQuery query)
    {
        var normalizedQuery = TextCleaner.Clean(query.Query).Trim();
        var groupByArticleId = BuildGroupByArticleId(groups);
        var categories = groups
            .Select(group => group.Category)
            .Concat(KnownCategories)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sources = articles
            .Select(article => TextCleaner.Clean(article.Source))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var filtered = articles
            .Where(article => MatchesArticle(article, groupByArticleId.GetValueOrDefault(article.Id), query, normalizedQuery))
            .OrderByDescending(article => article.PublishedAt)
            .ThenByDescending(article => article.FirstSeenAt)
            .ToArray();
        var pageSize = Math.Clamp(query.PageSize, 10, 100);
        var page = Math.Max(1, query.Page);
        var pageCount = Math.Max(1, (int)Math.Ceiling((double)filtered.Length / pageSize));
        if (page > pageCount) page = pageCount;

        return new
        {
            page,
            pageSize,
            pageCount,
            totalCount = filtered.Length,
            categories,
            sources,
            items = filtered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(article => ToAdminArticleListItem(article, groupByArticleId.GetValueOrDefault(article.Id)))
                .ToArray()
        };
    }

    private static bool MatchesArticle(Article article, ArticleGroup? group, AdminArticleQuery query, string normalizedQuery)
    {
        if (query.Excluded is not null && article.IsExcluded != query.Excluded.Value) return false;

        if (!string.IsNullOrWhiteSpace(query.Category)
            && !string.Equals(group?.Category ?? "", query.Category, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.Source)
            && !string.Equals(TextCleaner.Clean(article.Source), query.Source, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.ContentStatus))
        {
            var status = string.IsNullOrWhiteSpace(article.ContentFetchStatus) ? "pending" : article.ContentFetchStatus;
            if (!string.Equals(status, query.ContentStatus, StringComparison.OrdinalIgnoreCase)) return false;
        }

        if (string.IsNullOrWhiteSpace(normalizedQuery)) return true;

        var haystack = TextCleaner.Clean($"{article.Title} {article.Source} {article.Author} {article.Url} {article.Summary} {article.FeedUrl}");
        return haystack.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
    }

    private static object ToAdminArticleListItem(Article article, ArticleGroup? group)
    {
        return new
        {
            article.Id,
            title = TextCleaner.Clean(article.Title),
            source = TextCleaner.Clean(article.Source),
            author = TextCleaner.Clean(article.Author),
            article.Url,
            article.FeedUrl,
            groupId = group?.Id ?? "",
            category = group?.Category ?? "미분류",
            isExcluded = article.IsExcluded,
            publishedAt = article.PublishedAt,
            updatedAt = article.UpdatedAt,
            contentFetchStatus = string.IsNullOrWhiteSpace(article.ContentFetchStatus) ? "pending" : article.ContentFetchStatus,
            contentFetchError = TextCleaner.Clean(article.ContentFetchError),
            hasContent = !string.IsNullOrWhiteSpace(article.Content),
            hasImage = !string.IsNullOrWhiteSpace(article.ImageUrl),
            summaryPreview = Preview(article.Summary, 180),
            contentPreview = Preview(article.Content, 220)
        };
    }

    private static object ToAdminArticleDetail(Article article, ArticleGroup? group)
    {
        return new
        {
            article.Id,
            title = TextCleaner.Clean(article.Title),
            source = TextCleaner.Clean(article.Source),
            author = TextCleaner.Clean(article.Author),
            article.Url,
            article.FeedUrl,
            groupId = group?.Id ?? "",
            category = group?.Category ?? "미분류",
            isExcluded = article.IsExcluded,
            summary = TextCleaner.Clean(article.Summary),
            content = TextCleaner.Clean(article.Content),
            imageUrl = article.ImageUrl,
            publishedAt = article.PublishedAt,
            firstSeenAt = article.FirstSeenAt,
            updatedAt = article.UpdatedAt,
            contentFetchedAt = article.ContentFetchedAt,
            contentFetchStatus = string.IsNullOrWhiteSpace(article.ContentFetchStatus) ? "pending" : article.ContentFetchStatus,
            contentFetchError = TextCleaner.Clean(article.ContentFetchError),
            group = group is null ? null : new
            {
                group.Id,
                group.Category,
                title = TextCleaner.Clean(group.RepresentativeTitle),
                summary = TextCleaner.Clean(group.Summary),
                group.ArticleCount,
                group.Score
            }
        };
    }

    private static Dictionary<string, ArticleGroup> BuildGroupByArticleId(IEnumerable<ArticleGroup> groups)
    {
        var result = new Dictionary<string, ArticleGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            foreach (var articleId in group.ArticleIds)
            {
                result.TryAdd(articleId, group);
            }
        }

        return result;
    }

    private static void ApplyArticleUpdate(Article article, AdminArticleUpdateRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Title)) article.Title = TextCleaner.Clean(request.Title);
        if (!string.IsNullOrWhiteSpace(request.Source)) article.Source = TextCleaner.Clean(request.Source);
        if (request.Author is not null) article.Author = TextCleaner.Clean(request.Author);
        if (request.Summary is not null) article.Summary = TextCleaner.Clean(request.Summary);
        if (request.IsExcluded is not null) article.IsExcluded = request.IsExcluded.Value;
        article.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static ArticleGroup? ApplyGroupCategoryForArticle(IEnumerable<ArticleGroup> groups, string articleId, string? category)
    {
        var group = groups.FirstOrDefault(item => item.ArticleIds.Contains(articleId, StringComparer.OrdinalIgnoreCase));
        if (group is null || string.IsNullOrWhiteSpace(category)) return group;

        group.Category = TextCleaner.Clean(category);
        return group;
    }

    private static void ApplyGroupUpdate(ArticleGroup group, AdminGroupUpdateRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Category)) group.Category = TextCleaner.Clean(request.Category);
        if (!string.IsNullOrWhiteSpace(request.RepresentativeTitle)) group.RepresentativeTitle = TextCleaner.Clean(request.RepresentativeTitle);
        if (request.Summary is not null) group.Summary = TextCleaner.Clean(request.Summary);
    }

    private static ArticleGroup[] RemoveExcludedArticlesFromGroups(IEnumerable<ArticleGroup> groups, IEnumerable<Article> articles)
    {
        var articleById = articles.ToDictionary(article => article.Id, StringComparer.OrdinalIgnoreCase);
        return groups
            .Select(group =>
            {
                var includedIds = group.ArticleIds
                    .Where(id => articleById.TryGetValue(id, out var article) && !article.IsExcluded)
                    .ToArray();
                var includedArticles = includedIds
                    .Select(id => articleById[id])
                    .OrderByDescending(article => article.PublishedAt)
                    .ToArray();

                group.ArticleIds = includedIds;
                group.ArticleCount = includedIds.Length;
                group.Sources = includedArticles
                    .Select(article => TextCleaner.Clean(article.Source))
                    .Where(source => !string.IsNullOrWhiteSpace(source))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                group.LatestPublishedAt = includedArticles.FirstOrDefault()?.PublishedAt ?? group.LatestPublishedAt;
                return group;
            })
            .Where(group => group.ArticleIds.Length > 0)
            .ToArray();
    }

    private static bool TryNormalizeUrl(string value, out string url)
    {
        url = "";
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;

        url = uri.ToString();
        return true;
    }

    private static bool TryParseDate(string? value, string parameterName, out DateOnly? date, out IResult? error)
    {
        date = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value)) return true;

        if (DateOnly.TryParse(value, out var parsed))
        {
            date = parsed;
            return true;
        }

        error = Results.BadRequest(new { error = $"{parameterName}_must_be_yyyy_mm_dd" });
        return false;
    }

    private static string Preview(string value, int maxLength)
    {
        var text = TextCleaner.Clean(value).Trim();
        if (text.Length <= maxLength) return text;
        return $"{text[..maxLength].Trim()}...";
    }
}
