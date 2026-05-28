#!/bin/bash
set -ex

# Config
THEME=${1:-dracula}
PROJECTS=(
  "src/FsLiveDocs.Core/FsLiveDocs.Core.fsproj"
  "src/FsLiveDocs.Runner/FsLiveDocs.Runner.fsproj"
  "src/FsLiveDocs.Renderer/FsLiveDocs.Renderer.fsproj"
  "src/FsLiveDocs.Cli/FsLiveDocs.Cli.fsproj"
)

echo "--- FsLiveDocs: Documentation Preview ---"

# Kill previous server if running
pkill -f livedocs || true

# 1. Rebuild the tool to ensure latest changes are included
echo "=> Rebuilding livedocs tool..."
./scripts/publish.sh

# 2. Run Verification Tests
echo "=> Verifying docstrings..."
./artifacts/livedocs test "${PROJECTS[@]}"

# 3. Build Documentation Site
echo "=> Building static site (Theme: $THEME)..."
./artifacts/livedocs build "${PROJECTS[@]}" --theme "$THEME"

# 4. Preview
echo "=> Starting preview server..."
./artifacts/livedocs watch "${PROJECTS[@]}" --theme "$THEME"
