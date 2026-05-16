#!/bin/bash
set -e
echo "Publishing FsLiveDocs..."
mise x -- dotnet publish src/FsLiveDocs.Cli/FsLiveDocs.Cli.fsproj -c Release -o ./artifacts
echo "Published to ./artifacts/livedocs"
