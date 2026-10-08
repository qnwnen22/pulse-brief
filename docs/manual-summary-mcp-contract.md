# Daily summary MCP endpoint

## Exposure and authentication

The sessionless MCP JSON-RPC endpoint is `POST /mcp` on the dedicated hostname configured at `Mcp:CloudflareAccess:Hostname` (planned: `mcp.pulse-brief.co.kr`). The route is absent unless `Mcp:DailySummary:Enabled=true`; with the flag off, `/mcp` returns 404. With the flag on, the dedicated hostname serves only `/mcp`, rejects other paths with 404 and rejects non-POST transport requests with 405. `/mcp` on every other hostname returns 404.

Enabling the flag also requires all of these settings:

```text
Mcp__DailySummary__Enabled=true
Mcp__CloudflareAccess__TeamDomain=https://<team>.cloudflareaccess.com
Mcp__CloudflareAccess__Hostname=<dedicated MCP hostname>
Mcp__CloudflareAccess__Audience=<unique audience tag for the dedicated Access app>
Mcp__CloudflareAccess__AllowedEmails__0=<approved email identity>
```

Do not enable it until Cloudflare Managed OAuth is configured for that exact hostname, the audience is confirmed, the user allowlist is approved, and the dedicated hostname is routed to the existing origin. The origin authenticates only `Cf-Access-Jwt-Assertion`; it does not accept the opaque OAuth bearer token, admin token, session cookie, or fallback authentication. The assertion is validated with cached Cloudflare JWKS, RS256 only, exact issuer and dedicated app audience, expiry, and exact allowlisted email. Signing keys refresh every 15 minutes and refresh on an unknown key. Cloudflare must validate the connector's OAuth client token and forward its signed assertion to the origin.

This app exposes only the two tools below; it has no administrative, generic database, article mutation, or summary-generation tool. Cloudflare's Access application and connector configuration are separate deployment steps. This repo does not configure OAuth callback URLs or grant/session lifetimes.

## `pulsebrief_export_daily_articles`

Input:

```json
{
  "date": "YYYY-MM-DD",
  "maxArticles": 10000
}
```

`maxArticles` is optional, must be 1–10,000, and is the total eligible-article ceiling for the whole export. `cursor` is omitted on the first call and then set to the prior response’s `nextCursor`; keep the same date and maxArticles for every call. Each response contains at most 100 articles and at most 1 MiB of serialized article-array JSON, whichever limit is reached first. The service checks for an existing summary before every page. The first call captures `snapshotAt`, counts the eligible documents, and includes only rows with `FirstSeenAt <= snapshotAt`. Pages use a strict descending `PublishedAt`, then `Id` keyset cursor on the indexed Korea-date range and one-document Mongo batches. Each page returns `articleCount` (this page), `exportedArticleCount` (cumulative), `totalArticleCount` (fixed snapshot total), `snapshotAt`, `sha256` for this page’s JSON array, `complete`, and `nextCursor`. Continuations recheck the snapshot count; expired, changed, oversized, over-limit, or inconsistent exports fail without a partial page. New rows first seen after the snapshot are deliberately excluded. The client must hash-check each page, keep one snapshot and total, reject duplicate article IDs, follow each cursor exactly once, and summarize only after `complete=true`, `nextCursor=null`, and cumulative/article total counts match. A page retry with the same cursor is safe; the consumer should persist accepted page bodies/hashes and resume from its last confirmed page rather than regenerate from partial input.

Success returns `status`, `date`, `startInclusive`, `endExclusive`, `complete`, `articleCount`, `sha256`, and `articles`. Each article contains `Id`, `Title`, `Url`, `Source`, `Author`, `Summary`, `Content`, `PublishedAt`, and `FirstSeenAt`. If a summary already exists, the service returns `status: "existing"` and an empty article list.

## `pulsebrief_publish_daily_summary`

Input:

```json
{
  "date": "YYYY-MM-DD",
  "summary": { "Date": "YYYY-MM-DD", "Provider": "manual" }
}
```

`summary` uses the existing `DailyIssueSummary` data shape. Validation covers allowed categories, field and count limits, category totals, unique issue/article references, referenced article existence and date, excluded articles, and a 2 MiB document ceiling. The store requires the unique ascending `summaries.Date` index and inserts once. Concurrent publishers are resolved by that index. An existing document is returned with `status: "existing"` and a match indicator; it is never replaced.

## Scheduler and data freshness

The scheduler is a separate adapter seam in `tools/manual-summary/scheduled.cjs`; it is not a deployed cloud connector. Its `exportArticles` adapter must follow all continuation cursors, persist each accepted page, verify page SHA-256 values, identical `snapshotAt` and `totalArticleCount`, unique IDs, and a final cumulative count equal to the total before calling Codex. `readUsage()` must return `{ "source": "codex_get_usage_limits", "observedAt": "<ISO 8601 with Z or offset>", "windows": { "weekly": { "remainingPercent": 0 } } }`. The weekly value must be finite and at least 20%; the observation must be no more than five minutes old and cannot be in the future. Missing, unknown, stale, ambiguous, or invalid usage data fails closed. The job targets the previous Korea date and avoids regeneration when a summary already exists. Start the scheduled task at 00:00 KST and do not export before 00:20 KST, giving the 10-minute collector two additional cycles. This is a bounded grace period, not a collection watermark guarantee: late RSS arrivals after the snapshot are excluded and are not backfilled, and an existing date is never replaced. Any export or publication failure ends that run without retrying a partial payload; the next run must first observe an existing summary and no-op, or stop for operator review. Do not auto-force-regenerate or delete a published document.

The existing administrative manual-summary `force` flow intentionally retains its established replacement behavior. It is separate from these cloud tools; MCP publication is insert-only and cannot invoke the force path.

## Deployment state

The endpoint code was deployed with version 0.4.0, but `Mcp:DailySummary:Enabled` remains unset or false. The public news hostname was verified to return 404 for `/mcp`; the MCP hostname and authenticated connector flow have not been enabled or end-to-end validated. The dedicated Cloudflare Access app, callback, audience, approved identity policy, and tunnel hostname route are configured; connector OAuth remains pending. A pre-deployment recovery snapshot was verified at `/var/backups/pulsebrief/pre-0.4.0-20261008/release.tar.gz`. Keep the feature flag disabled until connector OAuth is complete and an authenticated end-to-end test is ready.

The current `tools/cloud/deploy-to-ubuntu.ps1` helper installs files in place using `rsync --delete`; it does not create an atomic release or rollback snapshot. Do not run that helper for this trial without an independently verified server-side backup. Before deployment, snapshot `/opt/pulsebrief/web`, the active systemd unit files, and `/etc/pulsebrief/pulsebrief.env` without printing the env file. Install and check the binaries with the MCP flag absent/false; verify existing site/collector health and the public news-host `/mcp` 404; only then enable the server feature flag and test authenticated MCP on its dedicated host. For rollback, remove the MCP hostname route from the tunnel first, turn the MCP flag false, restore the saved web files and units, restart the web service, verify the public health/news routes and `/mcp` 404, and only then remove the Access protection/app. No summary document should be deleted during rollback; insert-only publication makes a later retry safe.
