# Cleanuparr with native NZBGet support

An independently maintained, experimental fork of Cleanuparr. NZBGet configuration, connection testing, persisted progress observations and guarded Sonarr/Radarr recovery are integrated into the existing application. SABnzbd is not implemented. Upstream does not support these changes.

## Install on Unraid

Image: `ghcr.io/purcilas/cleanuparr-nzbget:2026-10-03.1` (Linux amd64). A downloadable Docker archive is also available in the [release](https://github.com/purcilas/cleanuparr-nzbget/releases/tag/nzbget-2026.10.03.1). The image is public; anonymous pull and public release download checksums were verified.

Use Docker → Add Container with this image, bridge networking, host TCP port **11012** mapped to **11011**, `/mnt/user/appdata/cleanuparr-nzbget` mapped read/write to `/config`, `PUID=99`, `PGID=100`, and your timezone in `TZ`. Privileged mode is unnecessary. Use an unused host port and a fresh appdata directory. Open `http://YOUR-UNRAID-IP:11012`.

An [Unraid template](unraid/cleanuparr-nzbget.xml) and [installation guide](unraid/INSTALL.md) are included. The standard upstream image does not contain NZBGet support. Do not run two versions against the same database. Do not enable automatic updates for this validation release.

Add Sonarr/Radarr and NZBGet using LAN addresses and published host ports. Use the exact download-client name already shown in Sonarr/Radarr. Enable Queue Cleaner, keep global dry-run on and leave both **Live validation completed** and **Enable live Usenet recovery** off. API-only observation needs no media filesystem mounts.

## Safety and current limitations

Observation is the default. Pure no-progress and repair/unpack stalls are recorded without automatic deletion. Transfer recovery also requires damaged-download evidence; pauses, disk/provider constraints, unknown states and ambiguous ownership suspend action. Caps, cooldowns and recovery holds prevent clearing a queue repeatedly. See [native Usenet documentation](docs/native-usenet/README.md).

Live homelab validation is pending. Local verification passed 3,828 backend tests (10 existing skips), 911 frontend tests, production build/lint, synthetic desktop/mobile checks, SQLite and disposable PostgreSQL migration checks, and packaged/container startup. This is a prerelease, not a claim of production acceptance.

## Source and provenance

Baseline: Cleanuparr `58b476c36063e116ed5c582d6ee81f90856c47ec`. Application source is on this branch. The release includes complete retained build inputs and pinned public-source torrent libraries, the upstream-relative patch and checksums. See [implementation notes](IMPLEMENTATION.md) and [build provenance](provenance/UPSTREAM.md).

The image layers retain a pinned upstream OS/Apprise environment and replace the entire application with the locally validated package. The manual publisher verifies the exact Docker archive checksum before pushing; it does not rebuild or fetch unreviewed application code. No upstream release, auto-approval or deployment workflows run on this branch. Updates require a new reviewed build and release.

License: GNU GPL v3; upstream attribution and LICENSE are retained.

## 2026-10-03.1 constructor fix

The initial image could not test/create the NZBGet adapter when both HttpClient and the dynamic HTTP provider were registered. This patch explicitly marks the configured-provider constructor for dependency injection. The factory regression reproduces the original exception and now passes with both registrations. The packaged application's real connection-test API returns HTTP200 against a synthetic NZBGet RPC server. Fresh checks: infrastructure2,544 passed and API584 passed; 11 infrastructure skips (10 pre-existing Docker-dependent tests and the PostgreSQL test without a provisioned database). No migrations or UI behavior changed.
