#!/usr/bin/env bash
set -euo pipefail
project_dir=$(cd "$(dirname "$0")/.." && pwd)
sdk_image=mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:0eeb52c76e35a5431ca707ad2bc75e38006a05393045d8532ae44c15d9474523
node_image=docker.io/library/node:26.10.0-bookworm@sha256:abbacf6ecd105ed33c408bdca7701c8a54f50e317eabfb3ed31733d4e6951db5
runtime=podman
command -v "$runtime" >/dev/null

# Use retained public library source rather than GitHub Packages credentials.
"$runtime" run --rm -v "$project_dir:/work:z" "$sdk_image" sh -c '
  dotnet build /work/provenance/dependencies/qbittorrent-net-client/src/QBittorrent.Client -c Release &&
  dotnet pack /work/provenance/dependencies/qbittorrent-net-client/src/QBittorrent.Client -c Release -o /work/provenance/local-packages -p:PackageId=FLM.QBittorrent &&
  dotnet build /work/provenance/dependencies/Transmission.API.RPC/Transmission.API.RPC -c Release &&
  dotnet pack /work/provenance/dependencies/Transmission.API.RPC/Transmission.API.RPC -c Release -o /work/provenance/local-packages -p:PackageId=FLM.Transmission'
"$runtime" run --rm -v "$project_dir:/work:z" -w /work/repo/code/frontend "$node_image" sh -c 'npm ci && npm run test:ci && npm run build && npm run lint'
"$runtime" run --rm -v "$project_dir:/work:z" -e NUGET_PACKAGES=/work/provenance/nuget-cache -w /work/repo/code/backend "$sdk_image" sh -c '
  dotnet restore cleanuparr.sln --source /work/provenance/local-packages --source https://api.nuget.org/v3/index.json &&
  dotnet test cleanuparr.sln --no-restore --nologo &&
  dotnet publish Cleanuparr.Api -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=false --source /work/provenance/local-packages --source https://api.nuget.org/v3/index.json -o /work/artifacts/linux-x64'
mkdir -p "$project_dir/artifacts/linux-x64/wwwroot"
cp -a "$project_dir/repo/code/frontend/dist/ui/browser/." "$project_dir/artifacts/linux-x64/wwwroot/"
cp "$project_dir/scripts/start-cleanuparr.sh" "$project_dir/artifacts/linux-x64/start-cleanuparr.sh"
cp "$project_dir/repo/docs/native-usenet/README.md" "$project_dir/artifacts/linux-x64/NATIVE-USENET.md"
echo "Local package: $project_dir/artifacts/linux-x64 (not deployed)"
