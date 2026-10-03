# Native NZBGet support (independently maintained fork)

This fork adds an NZBGet client to Cleanuparr's Download Clients settings, read-only connection testing, normalized Usenet queue/history inspection, persisted progress observations, incident reporting and guarded recovery through Sonarr/Radarr. The shared `IUsenetDownloadService` interface supports a future SABnzbd adapter. SABnzbd is not implemented.

Upstream baseline: `58b476c36063e116ed5c582d6ee81f90856c47ec`. Existing torrent enum values, APIs and rules are retained. NZBGet is appended at enum value 5. This feature has no assumed upstream support.

## First configuration

1. Back up Cleanuparr's configuration, events and user databases. These changes add a nullable configuration column and an observations table to SQLite or PostgreSQL.
2. Add an NZBGet client. **Use the exact download-client name shown in Sonarr/Radarr**, so ownership can be correlated without guessing.
3. Choose a host such as `http://nzbget:6789`. Use the URL Base for a reverse-proxy prefix such as `/nzbget`, not `/jsonrpc`; the endpoint suffix is appended automatically. Embedded credentials and query strings in the host are rejected.
4. Supply NZBGet RPC credentials that can read status, groups, history and logs. Add-only accounts are insufficient. Credentials use HTTP Basic authentication and the existing masked-password response contract. Use the existing trusted-network/TLS setup appropriate to your installation.
5. Test the connection. A successful test confirms read access; it does **not** certify that the client currently has all safety data required for recovery.
6. Enable Queue Cleaner and schedule it frequently enough for the maximum observation gap (default ten minutes). Leave **Live validation completed** and **Enable live Usenet recovery** off. Global dry-run overrides the per-client live switches.
7. Use **Observations** on the client row and the Events view to inspect current classifications and Usenet incidents. No deletion, blocklist or replacement-search request is sent in observation/dry-run mode. Observation and failed-import strike state may be persisted locally.

## Decisions and safeguards

- Queue and history identifiers are scoped by Cleanuparr client ID and NZBID. The NZBGet `drone` parameter is the primary arr download ID; numeric NZBID is only a fallback when no drone parameter exists.
- Every configured, enabled arr queue must be fully readable before ownership is accepted. Matching is case-insensitive for drone/client names. Duplicate IDs, conflicting drone values, multiple owners, ambiguous content packs, ignored releases and unowned jobs are protected.
- Only active transfers accumulate transfer observations. Queued jobs behind them, paused jobs and unknown states do not. Repair/unpack and other known processing stages have a separate observation window and progress token.
- Byte/article or processing-stage progress resets the window. Client restarts, ownership changes, replaced job fingerprints, clock reversal and polling gaps reset observations. Persisted state survives Cleanuparr restarts.
- Pause, scheduled resume, quota, low destination/intermediate disk space, recent warning/error logs, inactive providers or missing safety fields suspend action. NZBGet versions missing `FreeInterDiskSpaceHi/Lo` (introduced in 24.3) remain readable but suspend stall decisions. Missing values never mean zero or a healthy client.
- **No progress alone is insufficient evidence for automatic deletion.** Transfers require both a validated elapsed stall and health below critical health with failed articles. Repair/unpack candidates remain observation-only because slowly processing jobs can report no finer progress. Live evidence from the actual installation is needed before expanding those rules.
- Successful history jobs can use existing failed-import rules; active, unknown, failed or absent jobs cannot bypass the Usenet safeguard. Existing force import is used only in the validated live path. Global dry-run and observation mode prevent force-import requests.
- Native recovery is limited to Sonarr/Radarr. Other arr queues are checked for conflicting ownership; jobs owned by other applications are not recovered. An enabled LazyLibrarian instance suspends native recovery because its ownership cannot be established through the arr queue API.
- Re-read the client and every arr queue immediately before acting. Verify identity, progress, phase, owner, arr status and current configuration again.
- Save action intent and the job receipt transactionally before invoking the existing arr remover. A database compare-and-set reserves the client, in addition to process-level serialization. Recover a shared season pack once with one season-search target.
- Arr deletes use `blocklist=true`, `skipRedownload=true` and `removeFromClient=true`. The existing removal consumer's implementation queues the replacement search, avoiding the arr's own immediate redownload. Usenet mutation requests bypass HTTP retry policies.
- Default action limits are one recovery per client/run and one per client/hour, with a two-hour client cooldown. After a removal, wait for continuous observed transfer progress. A reset counter or polling gap cannot prove recovery.
- A timeout, partial search outcome or interrupted action is never automatically retried. Read-only reconciliation records what can be observed, and the client halts. Lack of restored progress also halts further removals. This is intentionally conservative: an idle queue after its last bad job was removed can require review.
- **Review recovery hold** requires an explicit operator acknowledgement and a fresh safe client read. It waits for renewed progress; previously attempted jobs remain protected permanently. It does not delete or initiate a search.
- Non-action observations expire after thirty days. Action evidence and client recovery state are retained. Use one Cleanuparr instance per configuration/events database.

## Unsupported operations

NZBGet is excluded from torrent seeding, file blocking, category-changing and dead-torrent controls. The backend also rejects torrent-cleanup settings for Usenet clients. Orphan filesystem cleanup is suspended when any Usenet client is enabled, because this release cannot prove which paths its jobs claim. This avoids deleting NZBGet's active files.

No direct NZBGet deletion, filesystem cleanup, service restart or configuration change is implemented. History failures remain preserved for normal arr failed-download handling. Unrelated named Usenet clients retain existing failed-import handling; blank/ambiguous client names are protected when native NZBGet is configured.

## Live acceptance still required

Local tests and synthetic UI fixtures are not homelab acceptance. Before live cleanup, collect installed versions, hosting/network details and redacted examples of a real stall plus intentional pauses, active repair/unpack, queued followers and successful downloads. Compare read-only classifications and dry-run decisions across several polls, including a restart. Verify the exact arr client name and drone correlation. Check provider and disk constraints and whether deleting one real bad release restores progress.

Only after separately authorized live validation should an operator confirm validation and enable live recovery. Deployment, fork publication and destructive acceptance testing are separate release decisions. This implementation session did not access or mutate a live queue.

## Rollback

Disable the NZBGet client/live recovery first. Stop the application and restore the previous build with its pre-migration database backups. Do not run two versions against the same databases. Rolling back code cannot restore deleted download data.

## API references

- [NZBGet RPC authentication and transport](https://nzbget.com/documentation/api/)
- [Queue fields and states](https://nzbget.com/documentation/api/listgroups/)
- [Pause, quota, provider and disk fields](https://nzbget.com/documentation/api/status/)
- [History results](https://nzbget.com/documentation/api/history/)
