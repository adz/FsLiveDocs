#!/bin/bash

# Config
THEME=${1:-dracula}
PROJECTS=(
  "src/FsLiveDocs.Core/FsLiveDocs.Core.fsproj"
  "src/FsLiveDocs.Runner/FsLiveDocs.Runner.fsproj"
  "src/FsLiveDocs.Renderer/FsLiveDocs.Renderer.fsproj"
  "src/FsLiveDocs.Cli/FsLiveDocs.Cli.fsproj"
  "samples/DeepReference/Acme.Docs/Acme.Docs.fsproj"
)

echo -e "\033[1;34m--- FsLiveDocs: Documentation Preview ---\033[0m"

# Kill previous server if running
pkill -f livedocs || true

# 1. Rebuild the tool to ensure latest changes are included
echo -e "\033[1;32m=> Rebuilding livedocs tool...\033[0m"
./scripts/publish.sh > /dev/null

# 2. Generate the Verify-based snapshot test project
echo -e "\033[1;32m=> Generating snapshot test project...\033[0m"
./artifacts/livedocs generate-tests "${PROJECTS[@]}"

# 3. Build and run the generated snapshot tests
echo -e "\033[1;32m=> Building snapshot test project...\033[0m"
dotnet build tests/FsLiveDocs.SnapshotTests/FsLiveDocs.SnapshotTests.fsproj --nologo -v minimal
echo -e "\033[1;32m=> Verifying docstrings via Verify...\033[0m"
dotnet test tests/FsLiveDocs.SnapshotTests/FsLiveDocs.SnapshotTests.fsproj --no-build --no-restore --nologo -v minimal
# 4. Build Documentation Site
echo -e "\033[1;32m=> Building static site (Theme: $THEME)...\033[0m"
./artifacts/livedocs build "${PROJECTS[@]}" --theme "$THEME"

# 5. Preview
echo -e "\033[1;32m=> Starting preview server...\033[0m"
./artifacts/livedocs watch "${PROJECTS[@]}" --theme "$THEME"
