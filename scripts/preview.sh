#!/bin/bash

# Config
THEME=${1:-dracula}
PROJECTS=(
  "src/FsLiveDocs.Core/FsLiveDocs.Core.fsproj"
  "src/FsLiveDocs.Runner/FsLiveDocs.Runner.fsproj"
  "src/FsLiveDocs.Renderer/FsLiveDocs.Renderer.fsproj"
  "src/FsLiveDocs.Cli/FsLiveDocs.Cli.fsproj"
)

echo -e "\033[1;34m--- FsLiveDocs: Documentation Preview ---\033[0m"

# Kill previous server if running
pkill -f livedocs || true

# 1. Rebuild the tool to ensure latest changes are included
echo -e "\033[1;32m=> Rebuilding livedocs tool...\033[0m"
./scripts/publish.sh > /dev/null

# 2. Run Verification Tests
echo -e "\033[1;32m=> Verifying docstrings...\033[0m"
./artifacts/livedocs test "${PROJECTS[@]}"

# 3. Build Documentation Site
echo -e "\033[1;32m=> Building static site (Theme: $THEME)...\033[0m"
./artifacts/livedocs build "${PROJECTS[@]}" --theme "$THEME"

# 4. Preview
echo -e "\033[1;32m=> Starting preview server...\033[0m"
./artifacts/livedocs watch "${PROJECTS[@]}" --theme "$THEME"
