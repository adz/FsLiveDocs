#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
target_repo="${1:-${FSLIVEDOCS_TARGET_REPO:-}}"

if [[ -z "$target_repo" ]]; then
  echo "Usage: $0 <target-repository>" >&2
  echo "Or set FSLIVEDOCS_TARGET_REPO." >&2
  exit 2
fi

target_repo="$(cd "$target_repo" 2>/dev/null && pwd || true)"
if [[ -z "$target_repo" || ! -f "$target_repo/.config/dotnet-tools.json" ]]; then
  echo "Target repo has no local tool manifest: $target_repo/.config/dotnet-tools.json" >&2
  exit 1
fi

project="$repo_root/src/FsLiveDocs.Cli/FsLiveDocs.Cli.fsproj"
package_dir="$repo_root/artifacts/packages"
product_version="$(grep -oPm1 '(?<=<Version>)[^<]+' "$project")"
IFS=. read -r major minor patch <<<"$product_version"
package_version="${FSLIVEDOCS_LOCAL_VERSION:-$major.$minor.$((patch + 1))-local.$(date -u +%Y%m%d%H%M%S)}"

mkdir -p "$package_dir"
echo "Packing FsLiveDocs $package_version..."
dotnet pack "$project" \
  -c Release \
  -o "$package_dir" \
  --nologo \
  -p:PackageVersion="$package_version"

echo "Installing in $target_repo..."
if grep -qi '"fslivedocs"' "$target_repo/.config/dotnet-tools.json"; then
  dotnet tool update FsLiveDocs \
    --tool-manifest "$target_repo/.config/dotnet-tools.json" \
    --version "$package_version" \
    --add-source "$package_dir"
else
  dotnet tool install FsLiveDocs \
    --tool-manifest "$target_repo/.config/dotnet-tools.json" \
    --version "$package_version" \
    --add-source "$package_dir"
fi

echo "Installed FsLiveDocs $package_version in $target_repo"
