# Source provenance

Cleanuparr baseline: 58b476c36063e116ed5c582d6ee81f90856c47ec.
Source: https://codeload.github.com/Cleanuparr/Cleanuparr/tar.gz/58b476c36063e116ed5c582d6ee81f90856c47ec
Archive SHA-256: 375995ae20331e8e4fe4a834df4e1fead043f1b54da111626e0068d558d87926.
The archive is retained as the immutable baseline; repo/ is the working snapshot, without git.

Backend checks run using Podman and Microsoft SDK 10.0.401 (image digest sha256:0eeb52c76e35a5431ca707ad2bc75e38006a05393045d8532ae44c15d9474523).
Frontend checks use Node 26.10.0 in a container because the host lacks libatomic.so.1.
Original backend restore cannot find FLM.QBittorrent and FLM.Transmission on NuGet; the existing GitHub token is denied by GitHub Packages.
Public Cleanuparr library sources are retained under dependencies/, with revisions recorded in *.revision. Local verification builds version 1.0.3 packages from these sources; these are not asserted to be byte-identical to upstream's published packages.
