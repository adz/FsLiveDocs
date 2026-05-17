#!/bin/bash
set -e

# Config
THEME=${1:-dracula}
PROJECTS=(
  "src/FsLiveDocs.Core/FsLiveDocs.Core.fsproj"
  "src/FsLiveDocs.Runner/FsLiveDocs.Runner.fsproj"
  "src/FsLiveDocs.Renderer/FsLiveDocs.Renderer.fsproj"
  "src/FsLiveDocs.Cli/FsLiveDocs.Cli.fsproj"
)

echo "--- FsLiveDocs: Documentation Builder ---"

# 1. Build and Publish the livedocs tool if not exists or forced
if [ ! -f "./artifacts/livedocs" ]; then
  echo "=> Building livedocs tool..."
  ./scripts/publish.sh
fi

# 2. Run Verification Tests
echo "=> Verifying docstrings..."
./artifacts/livedocs test "${PROJECTS[@]}"

# 3. Build Documentation Site
echo "=> Building static site (Theme: $THEME)..."
./artifacts/livedocs build "${PROJECTS[@]}" --theme "$THEME"

# 4. Preview
echo "=> Starting preview server..."
./artifacts/livedocs watch "${PROJECTS[@]}" --theme "$THEME"
