# Native NZBGet implementation handoff — 2026-10-03

Native NZBGet support is implemented locally in Cleanuparr. The pinned working source is in `repo/`; an immutable upstream snapshot and build provenance are retained. No git commands, deployment or live queue mutation were performed during local implementation. Subsequent authorized publication is recorded below.

## Delivered

- NZBGet client configuration, protocol validation, existing password masking and read-only connection testing.
- A shared Usenet inspection interface and capability model, with a bounded, cancellable JSON-RPC adapter using Basic authentication. Reads queue, history, status and recent logs; handles large unsigned counters and missing fields conservatively.
- Persisted per-client/job progress observations and action receipts. SQLite and PostgreSQL migrations preserve existing configuration and support state across restarts.
- Ownership matching by exact arr client name, drone parameter or verified NZBID fallback; protection for ambiguous owners, unowned jobs and duplicate IDs; one removal/search target for a season pack.
- Queue Cleaner routing through the existing arr remover, notifications and replacement-search queue, with a fresh client/owner/configuration recheck before mutation.
- Observation/dry-run defaults, protected waiting/paused/processing/unknown jobs, separate processing observations, action caps, cooldowns, client recovery holds, operator review and read-only reconciliation of uncertain outcomes. Transactional intent/receipt persistence and a compare-and-set client reservation prevent duplicate actions. Usenet deletes bypass HTTP retries.
- Integrated client settings and current observations, plus a dedicated Usenet incident type in Events. Torrent-only controls reject NZB clients. Orphan cleanup suspends while Usenet clients are enabled.
- A local self-contained Linux x64 package, complete source/build inputs, upstream-relative patch, changed-file manifest and SHA-256 checksums.

## Conservative scope decisions

A pure no-progress stall cannot reliably distinguish a bad release from a provider outage. It is recorded, but automatic transfer recovery also requires health below critical health with failed articles. Repair/unpack stalls are observation-only. Successful history jobs can use guarded existing failed-import recovery. These limits were explained during implementation; real stall evidence is required before expanding deletion eligibility.

Initial recovery is for Sonarr/Radarr. Other arr queues are checked for conflicting ownership. Enabled LazyLibrarian suspends native recovery. Older NZBGet versions missing intermediate-disk safety fields can connect but suspend stall decisions. SABnzbd has a shared interface foundation and no adapter yet.

There were two build-environment adjustments. Node 26 needs a host library that is unavailable, so frontend checks use a container. GitHub Packages rejected the existing token; backend verification instead built version 1.0.3 torrent libraries from pinned public Cleanuparr repositories. Their revisions are recorded in `provenance/dependencies/*.revision`. They are not asserted to be byte-identical to upstream's published packages. An upstream hidden-tooltip overflow bug was also corrected while checking mobile layout.

## Verification

| Check | Result |
| --- | --- |
| Unchanged backend baseline with source-built torrent packages | 3,785 passed; 10 existing container-dependent tests skipped |
| Final backend suite | 3,828 passed; same 10 tests skipped |
| New backend coverage | RPC, identifiers, pause/quota/disk/provider constraints, progress/gaps, ownership, packs, dry-run, restart state, uncertain outcomes, review controls, API contracts and migration persistence |
| SQLite and real disposable PostgreSQL | New migration models and persistence across context reopen passed on both providers |
| Unchanged frontend baseline | 909 passed; production build passed |
| Final frontend | 911 passed; production build and ESLint passed |
| Browser smoke test | Synthetic NZBGet selection, credentials, connection test, save, observation display and edit; 1440px and 390px; no browser page errors; mobile list and dialog fit |
| Screenshot inspection | Desktop connection/rules and mobile rules/list inspected |
| Packaged application startup | Loopback-only smoke test; health and homepage HTTP 200; unauthenticated configuration HTTP 403 |
| Patch roundtrip | Checked separately against the retained upstream snapshot and changed-file hashes |

Detailed logs are in `provenance/`. The baseline's dependency advisory and compiler warnings remain; this is a local validation package, not a production release qualification. Existing Docker-dependent parity tests remained skipped, although the new PostgreSQL migration/state test ran against a real disposable database.

## Artifacts and reproduction

- `artifacts/cleanuparr-nzbget-linux-x64.tar.gz`: self-contained Linux x64 application and compiled UI. Extract and use its `start-cleanuparr.sh`, which starts from the correct content root. Preserve a separately backed-up configuration directory with `CLEANUPARR_CONFIG_PATH`.
- `artifacts/cleanuparr-nzbget-source.tar.gz`: working source, pinned public dependency source, upstream archive and local build scripts. It uses the same project-directory layout.
- `artifacts/native-nzbget.patch`: patch relative to the pinned upstream root; apply with `patch -p1` to a clean snapshot of that revision.
- `artifacts/changes.json`: before/after hashes for every changed or added source file.
- `artifacts/SHA256SUMS`: artifact checksums.
- `scripts/build-local.sh`: repeat frontend checks, backend tests and Linux packaging with Podman. No credentials are needed for the retained public source build.
- `scripts/export-source.py`: regenerate patch and archives without git.
- `repo/docs/native-usenet/README.md`: configuration, safeguards, limitations, live acceptance and rollback instructions.

Do not run two versions against the same databases. Back up configuration/events/users before migration. The package contains no homelab credentials, smoke-test databases or login tokens.

## Still pending

Read-only and dry-run validation against the actual NZBGet/Sonarr/Radarr installation needs hosting details, installed versions, securely supplied API access and representative stall responses. Compare intentional pauses, queued followers, repair/unpack and successful progress across several polls and a restart. Confirm client names and drone correlation before accepting any cleanup rule.

Fork publication, deployment, enabling live recovery and destructive acceptance testing remain separately authorized release decisions. No live-cleanup validation is claimed.

## Authorized publication — 2026-10-03

Public fork: https://github.com/purcilas/cleanuparr-nzbget (default branch `nzbget`). Application commit `44a0b7730eb2a8e0273a01accb5853af390ced8f` matches all 58 locally validated application changes. Inherited upstream workflows are retained outside the active workflows directory. The only active workflow manually publishes the exact checksum-verified image archive.

Validation release: https://github.com/purcilas/cleanuparr-nzbget/releases/tag/nzbget-2026.10.03. Anonymous downloads of image/source/template/checksums were hashed and match the local artifacts.

Public image: `ghcr.io/purcilas/cleanuparr-nzbget:2026-10-03`, digest `sha256:989967cc7af82f129fe609a2a500c917fa13e9587c4ba493f9116a47230e0c38`. GitHub Actions run 37147465555 succeeded; an anonymous pull matched the locally tested image ID. No additional registry credential was requested or stored.

Unraid guide: `unraid/INSTALL.md`; fresh appdata and host port11012. Actual Unraid installation and homelab acceptance remain pending. Project still has no local git source control; publication used GitHub APIs. The original source archive is the retained local build-input snapshot, while current publication instructions are on the public branch.
