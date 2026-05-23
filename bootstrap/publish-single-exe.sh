#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

detect_rid() {
  local os
  local arch
  os="$(uname -s)"
  arch="$(uname -m)"

  case "$os:$arch" in
    Darwin:arm64) printf 'osx-arm64' ;;
    Darwin:x86_64) printf 'osx-x64' ;;
    Linux:x86_64) printf 'linux-x64' ;;
    Linux:amd64) printf 'linux-x64' ;;
    MINGW*:x86_64 | MSYS*:x86_64 | CYGWIN*:x86_64) printf 'win-x64' ;;
    *)
      printf 'Unsupported host for automatic RID detection: %s %s\n' "$os" "$arch" >&2
      printf 'Pass a RID explicitly, for example: win-x64, linux-x64, osx-arm64, osx-x64\n' >&2
      return 1
      ;;
  esac
}

rid="${1:-$(detect_rid)}"
output_dir="${2:-$repo_root/artifacts/release/$rid}"

"$repo_root/bootstrap/bootstrap.sh" \
  --self-contained-script bootstrap/SqlTestSupport.expand.sh \
  --self-contained-targets dist/SqlTestSupport.Directory.Build.targets \
  --self-contained-csharp bootstrap/SqlTestSupport.Bootstrap.cs

dotnet publish "$repo_root/tools/SqlTestSupport.ReleaseBootstrap/SqlTestSupport.ReleaseBootstrap.csproj" \
  -c Release \
  -r "$rid" \
  -o "$output_dir" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:DebugType=None \
  -p:DebugSymbols=false

executable_name="SqlTestSupport.Bootstrap"
case "$rid" in
  win*) executable_name="SqlTestSupport.Bootstrap.exe" ;;
esac

printf 'Published single-file bootstrap executable:\n  %s\n' "$output_dir/$executable_name"
