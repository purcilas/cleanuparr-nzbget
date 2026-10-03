# Install the custom NZBGet Cleanuparr build on Unraid

This is a local Linux amd64 Docker image, not a published Community Applications entry. Image tag: `localhost/cleanuparr-nzbget:2026-10-03`. The image includes the tested application and UI plus upstream OS/Apprise dependencies; its base image is pinned in Dockerfile. Packaging and container startup were checked locally. No Unraid host has been accessed or changed.

## Transfer first

You need `cleanuparr-nzbget-unraid-image.tar.gz`, `cleanuparr-nzbget.xml` and the matching checksum file on your Unraid server. These have been prepared in the project but no public download or registry exists yet. Arrange direct file transfer or a separately approved release before proceeding. Do not install the standard Cleanuparr image expecting NZBGet support.

## Load the image

Place the delivered files in `/mnt/user/cleanuparr-nzbget-install/`, then open Unraid Terminal:

```sh
cd /mnt/user/cleanuparr-nzbget-install
sha256sum -c UNRAID-SHA256SUMS
gzip -dc cleanuparr-nzbget-unraid-image.tar.gz | docker load
mkdir -p /boot/config/plugins/dockerMan/templates-user
# Stop here if this template filename already exists; do not overwrite an existing setup.
cp -n cleanuparr-nzbget.xml /boot/config/plugins/dockerMan/templates-user/my-cleanuparr-nzbget.xml
```

## Add the container

On Docker → Add Container, select `cleanuparr-nzbget`. Confirm:

| Setting | Value |
| --- | --- |
| Repository | `localhost/cleanuparr-nzbget:2026-10-03` |
| Network | `bridge` |
| Host port → container port | `11012` → `11011` TCP |
| Appdata → container path | `/mnt/user/appdata/cleanuparr-nzbget` → `/config`, read/write |
| PUID / PGID | `99` / `100` |
| TZ | Your local timezone; default `America/New_York` |
| Privileged | Off |

Port 11012 and this appdata folder must be unused. The template is for a fresh separate install. Do not point it at another running Cleanuparr database. Do not enable automatic image updates: this tag exists locally only. Keep the image archive to reload if Unraid's Docker image storage is recreated.

Open `http://YOUR-UNRAID-IP:11012`, complete normal account setup, then add Sonarr/Radarr and NZBGet under settings. Use LAN IPs and each application's published host port on bridge networking; `localhost` refers to the Cleanuparr container. Docker container names require a shared user-defined network.

Use the exact NZBGet download-client name already shown in Sonarr/Radarr. Test connections, enable Queue Cleaner, and leave **Live validation completed** and **Enable live Usenet recovery** off. Keep global dry-run on while validating. No media/download filesystem mount is required for API-only observation.

The software must first classify actual stalls correctly across several polls. Pure no-progress and repair/unpack stalls remain observation-only. Deployment does not establish live-cleanup acceptance.

## Existing Cleanuparr and rollback

If you already have Cleanuparr, keep its production configuration separate during evaluation. Stop both applications before any later migration/copy of database files and back up the complete configuration first. Do not enable competing recovery in two applications. To undo this fresh install, stop the new container and retain its appdata for review; the old installation is unaffected by the separate paths.

Reference: https://docs.unraid.net/unraid-os/using-unraid-to/run-docker-containers/managing-and-customizing-containers/
