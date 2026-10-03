# Install NZBGet-enabled Cleanuparr on Unraid

The custom Linux amd64 image is public and anonymous pull was verified:

```
ghcr.io/purcilas/cleanuparr-nzbget:2026-10-03.2
```

This is a validation prerelease of an independently maintained fork. No GitHub sign-in or registry token is required to pull it. It is not a Community Applications listing.

## Install through the Unraid interface

Open Docker → Add Container, leave Template unselected and enter:

| Setting | Value |
| --- | --- |
| Name | `cleanuparr-nzbget` |
| Repository | `ghcr.io/purcilas/cleanuparr-nzbget:2026-10-03.2` |
| Network Type | `bridge` |
| Privileged | Off |

Use Add another Path, Port, Variable, Label or Device to add:

| Type | Name | Container target | Host value | Mode |
| --- | --- | --- | --- | --- |
| Port | Web UI | `11011` | `11012` | TCP |
| Path | Appdata | `/config` | `/mnt/user/appdata/cleanuparr-nzbget` | Read/Write |
| Variable | PUID | `PUID` | `99` | |
| Variable | PGID | `PGID` | `100` | |
| Variable | Timezone | `TZ` | Your timezone, e.g. `America/New_York` | |

Use an unused host port and a fresh appdata folder. Leave privileged mode off. No media/download path or Docker socket mount is needed for API-only monitoring. Click Apply/Create and wait for the image to download. Open `http://YOUR-UNRAID-IP:11012`.

Complete the normal account setup. Add Sonarr/Radarr and NZBGet under settings. On bridge networking use each application's LAN address and published host port; `localhost` refers to this new container. Container names only resolve if services share an appropriate user-defined network.

Use the exact NZBGet download-client name already shown in Sonarr/Radarr. Test all connections, enable Queue Cleaner, keep global dry-run on and leave both **Live validation completed** and **Enable live Usenet recovery** off. API access goes into the application's authenticated settings, not a public post or this template.

## Optional prefilled template

The [release](https://github.com/purcilas/cleanuparr-nzbget/releases/tag/nzbget-2026.10.03.2) contains `cleanuparr-nzbget.xml`. To import it with Unraid Terminal:

```sh
# This intentionally stops if this template file already exists.
set -eu
template_path=/boot/config/plugins/dockerMan/templates-user/my-cleanuparr-nzbget.xml
if [ -e "$template_path" ]; then
  echo 'Template already exists; use the existing template or install manually.' >&2
  exit 1
fi
mkdir -p /boot/config/plugins/dockerMan/templates-user
curl -fL 'https://github.com/purcilas/cleanuparr-nzbget/releases/download/nzbget-2026.10.03.2/cleanuparr-nzbget.xml' -o "$template_path"
```

Then select `cleanuparr-nzbget` in Docker → Add Container and check all values before applying. Manual setup above needs no terminal commands.

## Validation and updates

This image passed container startup as UID99/GID100; it has not been installed or exercised against your homelab. Confirm pause/processing/progress classifications across several polls before enabling any recovery. Pure no-progress and repair/unpack stalls remain observation-only.

Keep this pinned release out of automatic image-update jobs. A local Docker archive, full build inputs and checksums are available in the release as a fallback. The public source is at https://github.com/purcilas/cleanuparr-nzbget.

If Cleanuparr is already installed, keep its production configuration separate during evaluation. Stop both applications before any later database migration/copy and back up the complete configuration first. Do not enable competing recovery in two applications or run two versions against the same databases. To undo this fresh install, stop the new container and retain its appdata for review.

Reference: https://docs.unraid.net/unraid-os/using-unraid-to/run-docker-containers/managing-and-customizing-containers/
