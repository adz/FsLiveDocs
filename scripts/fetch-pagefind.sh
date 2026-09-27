#!/usr/bin/env bash
set -euo pipefail

VERSION=1.5.2
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="${2:-$ROOT/artifacts/pagefind}"
MODE="${1:-current}"

platforms=(
  "linux-x64|x86_64-unknown-linux-musl|afb824a9e7f64905a934900481cea5be679c03975e527329e0e5e6cc70f5feda|pagefind"
  "linux-arm64|aarch64-unknown-linux-musl|f50ec608bcbf431cebd84e0efa3a5b041ee63df2ad81f138023e0cfd2f509424|pagefind"
  "osx-x64|x86_64-apple-darwin|26f51b4ba921897142338fb13b836420696e251fe197b1782ea7981de311156d|pagefind"
  "osx-arm64|aarch64-apple-darwin|7286f394a349bd37677d44a65a20078a02b1747da0b0814d83403bf86be17abe|pagefind"
  "win-x64|x86_64-pc-windows-msvc|fab125d5e8e2d3481ffe7d36dec6e101f54a6581cd51cf5e2e09220d4bc78e9c|pagefind.exe"
  "win-arm64|aarch64-pc-windows-msvc|c4d869293a9cd5c14c2dff7e7f568b27a5f4acb1124a2097b638cffc0d82b840|pagefind.exe"
)

current_rid() {
  local os arch
  case "$(uname -s)" in Linux) os=linux ;; Darwin) os=osx ;; MINGW*|MSYS*|CYGWIN*) os=win ;; *) echo "Unsupported operating system: $(uname -s)" >&2; exit 1 ;; esac
  case "$(uname -m)" in x86_64|amd64) arch=x64 ;; aarch64|arm64) arch=arm64 ;; *) echo "Unsupported architecture: $(uname -m)" >&2; exit 1 ;; esac
  printf '%s-%s' "$os" "$arch"
}

fetch() {
  IFS='|' read -r rid upstream checksum executable <<<"$1"
  [[ "$MODE" == all || "$MODE" == "$rid" ]] || return 0
  local directory="$DEST/$rid" target="$DEST/$rid/$executable"
  [[ -x "$target" || "$rid" == win-* && -f "$target" ]] && return 0
  mkdir -p "$directory"
  local archive
  archive="$(mktemp)"
  trap 'rm -f "$archive"' RETURN
  local url="https://github.com/Pagefind/pagefind/releases/download/v${VERSION}/pagefind-v${VERSION}-${upstream}.tar.gz"
  echo "Fetching Pagefind ${VERSION} for ${rid}..."
  curl --fail --location --silent --show-error "$url" --output "$archive"
  echo "$checksum  $archive" | sha256sum --check --status || { echo "Pagefind checksum mismatch for $rid" >&2; exit 1; }
  tar -xzf "$archive" -C "$directory"
  if [[ "$rid" == win-* && -f "$directory/pagefind" ]]; then mv "$directory/pagefind" "$target"; fi
  [[ "$rid" == win-* ]] || chmod +x "$target"
  printf '%s\n' "$VERSION" > "$directory/VERSION"
}

if [[ "$MODE" == current ]]; then MODE="$(current_rid)"; fi
for platform in "${platforms[@]}"; do fetch "$platform"; done
