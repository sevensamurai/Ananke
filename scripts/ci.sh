#!/usr/bin/env bash
# The check before a pull request (card verify) and before publishing (pipeline.yml's build job):
# the encoding, internal-refs and docs checks, the docs search index, the release notes for the
# version, then restore, build, whitespace check and test of src/Ananke.slnx in Release; then it packs
# every package into artifacts/ci/packages, checks each package's version and dependency versions,
# and builds the two consumers in scripts/consumers from that folder only. Exits non-zero at the
# first failure. The tests that start or need a container (category Containers: Redis, MQTT, Qdrant,
# Postgres, Memgraph) are explicit, left out of the test step unless --containers is given, and then
# fail the run when any of them is skipped.
# The format step checks whitespace only; style and analyzer rules already fail the build
# (EnforceCodeStyleInBuild, TreatWarningsAsErrors), and --format adds the full dotnet format check.
#   bash scripts/ci.sh [--containers] [--format]
# Needs pwsh, unzip and the .NET SDK pinned by global.json (uses ~/.dotnet when present).
set -euo pipefail

containers=0
full_format=0
for arg in "$@"; do
  case "$arg" in
    --containers) containers=1 ;;
    --format) full_format=1 ;;
    *) echo "ci: unknown argument $arg (usage: bash scripts/ci.sh [--containers] [--format])" >&2; exit 2 ;;
  esac
done

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
if [[ -x "$HOME/.dotnet/dotnet" ]]; then
  export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
fi
command -v pwsh >/dev/null || { echo "ci: pwsh not found (sudo snap install powershell --classic)" >&2; exit 1; }
command -v dotnet >/dev/null || { echo "ci: dotnet not found (the SDK global.json pins)" >&2; exit 1; }
command -v unzip >/dev/null || { echo "ci: unzip not found (sudo apt install unzip)" >&2; exit 1; }
# Without Docker every container-backed test is ignored, so the run would pass having tested none.
if (( containers )) && ! docker info >/dev/null 2>&1; then
  echo "ci: --containers needs Docker running (docker info failed)" >&2; exit 1
fi

step() { echo; echo "ci: $*"; }

echo "ci: Ananke $(git rev-parse --short HEAD) on $(git branch --show-current || true), SDK $(dotnet --version)"

step "encoding (no UTF-8 BOM)"
pwsh -NoProfile -File scripts/fix-encoding.ps1 -Check

step "internal document references"
bash scripts/check-internal-refs.sh

step "documentation drift"
pwsh -NoProfile -File scripts/check-docs.ps1

step "docs search index"
index="$(mktemp)"
trap 'rm -f "$index"' EXIT
pwsh -NoProfile -File scripts/build-search-index.ps1 -OutFile "$index"

step "release notes"
version="$(grep -oP '(?<=<VersionPrefix>)[^<]+' src/Directory.Build.props || true)"
[[ -n "$version" ]] || { echo "ci: no VersionPrefix in src/Directory.Build.props" >&2; exit 1; }
[[ -f "releases/v$version.md" ]] || {
  echo "ci: version is $version but releases/v$version.md is missing (see releases/TEMPLATE.md)" >&2; exit 1; }
echo "releases/v$version.md"

step "restore"
dotnet restore src/Ananke.slnx -nologo -v quiet

step "build"
dotnet build src/Ananke.slnx -c Release --no-restore -nologo -v quiet -clp:ErrorsOnly

if (( full_format )); then
  step "format (whitespace, style, analyzers)"
  dotnet format src/Ananke.slnx --verify-no-changes --no-restore
else
  step "format (whitespace)"
  dotnet format whitespace src/Ananke.slnx --verify-no-changes
fi

step "test"
# The console logger at minimal names each failing test, with its message and stack trace.
dotnet test src/Ananke.slnx -c Release --no-build -nologo -v quiet --logger "console;verbosity=minimal"
if (( containers )); then
  step "container-backed tests (category Containers)"
  # A skipped test here tested nothing (a fixture that ignored it), so it fails the run like a failed one.
  results="$(mktemp -d)"
  trap 'rm -f "$index"; rm -rf "$results"' EXIT
  dotnet test src/Ananke.slnx -c Release --no-build -nologo -v quiet --logger "console;verbosity=minimal" \
    --logger trx --results-directory "$results" --filter TestCategory=Containers
  compgen -G "$results/*.trx" >/dev/null || { echo "ci: the Containers run wrote no test results" >&2; exit 1; }
  skipped="$(grep -ohP '<UnitTestResult [^>]*testName="\K[^"]+(?="[^>]*outcome="NotExecuted")' "$results"/*.trx)" \
    || (( $? == 1 )) || { echo "ci: could not read the Containers test results" >&2; exit 1; }
  skipped="$(sort -u <<<"$skipped")"
  [[ -z "$skipped" ]] || {
    echo "ci: --containers skipped these Containers tests:" >&2; echo "$skipped" | sed 's/^/  /' >&2; exit 1; }
  echo "no Containers test skipped"
fi

step "pack"
ci="$root/artifacts/ci"
packages="$ci/packages"
# The consumers restore into a cache of their own (scripts/consumers/Directory.Build.props), kept
# between runs for the other packages; its Ananke packages go, so none is older than this pack.
cache="$root/artifacts/consumer-packages"
rm -rf "$ci" "$cache"/ananke* "$cache"/nnke
dotnet pack src/Ananke.slnx -c Release --no-build -nologo -v quiet -o "$packages"
ls "$packages"/*.nupkg | xargs -n1 basename

# Every package is the version, depends on the other Ananke packages at exactly it, and on
# Microsoft.Extensions.* at the version src/Directory.Packages.props pins.
ms="$(grep -oP '(?<=<MsExtensionsVersion>)[^<]+' src/Directory.Packages.props || true)"
[[ -n "$ms" ]] || { echo "ci: no MsExtensionsVersion in src/Directory.Packages.props" >&2; exit 1; }
for nupkg in "$packages"/*.nupkg; do
  name="$(basename "$nupkg")"
  [[ "$name" == *".$version.nupkg" ]] || { echo "ci: $name is not version $version" >&2; exit 1; }
  nuspec="$(unzip -p "$nupkg" '*.nuspec')"
  wrong="$(grep -oP 'id="Ananke[^"]*" version="(?!\Q['"$version"']\E")[^"]*"' <<<"$nuspec" || true)"
  [[ -z "$wrong" ]] || { echo "ci: $name depends on $wrong, not [$version]" >&2; exit 1; }
  wrong="$(grep -oP 'id="Microsoft\.Extensions\.[^"]*" version="(?!\Q'"$ms"'\E")[^"]*"' <<<"$nuspec" || true)"
  [[ -z "$wrong" ]] || { echo "ci: $name depends on $wrong, not $ms" >&2; exit 1; }
done
echo "every package $version, Ananke dependencies at [$version], Microsoft.Extensions.* at $ms"

# A consumer whose build fails, or which shows one of the warnings it must not, prints its log.
consumer() {
  local name="$1" log="$ci/$1.log"
  step "consumer $name (from artifacts/ci/packages)"
  local built=0
  dotnet build "scripts/consumers/$name/$name.csproj" -c Release -nologo -v minimal \
    -p:AnankeVersion="$version" >"$log" 2>&1 || built=$?
  if grep -q 'ANANKE_ASYNC_001' "$log"; then
    cat "$log" >&2; echo "ci: $name gets ANANKE_ASYNC_001, this repository's own rule" >&2; exit 1
  fi
  (( built == 0 )) || { cat "$log" >&2; echo "ci: $name does not build" >&2; exit 1; }
  for package in $(grep -oP '(?<=PackageReference Include=")Ananke[^"]*' "scripts/consumers/$name/$name.csproj"); do
    local id="${package,,}"
    [[ -d "$cache/$id/$version" ]] || {
      echo "ci: $name took $package from somewhere other than artifacts/ci/packages" >&2; exit 1; }
  done
}

consumer SerenShaped
bin="$ci/consumers/SerenShaped/bin/Release/net10.0"
[[ -f "$bin/Ananke.StateMachine.dll" ]] || { echo "ci: no Ananke.StateMachine.dll in $bin" >&2; exit 1; }
[[ ! -e "$bin/Ananke.Orchestration.dll" ]] || {
  echo "ci: Ananke.Orchestration.dll is in SerenShaped's output: a state-machine package depends on it" >&2; exit 1; }
echo "no Ananke.Orchestration.dll in its output"

consumer ZenziqueShaped
grep -q 'warning ANNKE002.*gpt-4o' "$ci/ZenziqueShaped.log" || {
  cat "$ci/ZenziqueShaped.log" >&2; echo "ci: ZenziqueShaped gets no ANNKE002 for \"gpt-4o\"" >&2; exit 1; }
echo "ANNKE002 for the deprecated \"gpt-4o\""

echo
echo "ci: passed"
