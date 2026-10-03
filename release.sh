#!/usr/bin/env bash
# Publishes a Rain release to GitHub Releases, which installed copies auto-update from.
# Usage: ./release.sh <version>      e.g. ./release.sh 1.2.0
# Needs: gh (logged in), .NET 10 SDK, vpk (dotnet tool install -g vpk), a clean working tree.
set -euo pipefail
cd "$(dirname "$0")"

version="${1:?usage: ./release.sh <version>}"
repo="https://github.com/haiderking1/Rain"
token="$(gh auth token)"

if [[ -n "$(git status --porcelain)" ]]; then
  echo "Commit or stash your changes first." >&2
  exit 1
fi

# Bump the version and push it, so the release tag points at exactly what was built.
sed -i "s|<Version>.*</Version>|<Version>$version</Version>|" src/Rain/Rain.csproj
if [[ -n "$(git status --porcelain)" ]]; then
  git commit -qam "Release v$version"
fi
git push -q

# Fetch the previous release so vpk can build a small delta update from it.
rm -rf releases
vpk download github --repoUrl "$repo" --token "$token" --outputDir releases || echo "No previous release, full package only."

./pack.sh "$version"

vpk upload github --repoUrl "$repo" --token "$token" --outputDir releases \
  --publish --releaseName "Rain $version" --tag "v$version" --targetCommitish "$(git rev-parse HEAD)"

echo "Released: $repo/releases/tag/v$version"
