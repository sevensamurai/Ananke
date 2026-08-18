# UTF-8 BOM check/fix. A BOM breaks tooling that expects plain UTF-8, so CI runs this in
# -Check mode and fails the build on any finding.
#
# Usage: pwsh -File scripts/fix-encoding.ps1 [-Check]   (-Check = report only, exit 1 on findings)
#
# The file list comes from git, not a directory walk, which is what keeps this honest:
#   --cached                   tracked files
#   --others --exclude-standard  new files not yet added, minus anything .gitignore covers
# Together that is exactly the set a commit could contain. Build output (obj/, bin/) is
# gitignored, so it is structurally unreachable — no skip-list to get wrong. NuGet writes
# genuinely BOM-prefixed files into obj/, and the previous directory-walk version reported
# 170 of them as failures on Linux because its skip pattern only matched Windows separators.
param([switch]$Check)

$ErrorActionPreference = 'Stop'

# Resolve the repo from the script's own location so the result does not depend on cwd.
$root = & git -C $PSScriptRoot rev-parse --show-toplevel 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($root)) {
    Write-Error "Not inside a git working tree (or git is not installed) — this script enumerates files with 'git ls-files'."
    exit 2
}

$files = (& git -C $root ls-files -z --cached --others --exclude-standard) -split "`0" |
    Where-Object { $_ }

$hits = @()
$binary = @()

foreach ($rel in $files) {
    $path = Join-Path $root $rel

    # Tracked-but-deleted paths still appear in --cached.
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }

    # Cheap 3-byte probe: only files that actually start with a BOM are read in full.
    $head = [byte[]]::new(3)
    $stream = [System.IO.File]::OpenRead($path)
    try { $read = $stream.Read($head, 0, 3) } finally { $stream.Dispose() }

    if ($read -lt 3 -or $head[0] -ne 0xEF -or $head[1] -ne 0xBB -or $head[2] -ne 0xBF) { continue }

    $bytes = [System.IO.File]::ReadAllBytes($path)

    # Those three bytes are also a legal start to a binary blob. A NUL says this is not text,
    # and stripping bytes off a binary would corrupt it — report instead of touching it.
    if ([Array]::IndexOf($bytes, [byte]0) -ge 0) { $binary += $rel; continue }

    $hits += $rel

    if (-not $Check) {
        # Not $bytes[3..($bytes.Length - 1)] — for a file that is nothing but a BOM that is the
        # descending range 3..2, which yields a stray 0xBF byte instead of an empty file.
        $rest = [byte[]]::new($bytes.Length - 3)
        [Array]::Copy($bytes, 3, $rest, 0, $rest.Length)
        [System.IO.File]::WriteAllBytes($path, $rest)
    }
}

if ($binary.Count -gt 0) {
    Write-Warning "Skipped $($binary.Count) file(s) that open with the BOM bytes but contain NUL (binary):"
    $binary | ForEach-Object { "  $_" }
}

if ($hits.Count -gt 0) {
    $verb = $Check ? 'BOM found in' : 'BOM stripped from'
    $hits | ForEach-Object { "$verb $_" }
    if ($Check) {
        "Done. $($hits.Count) file(s) with a BOM."
        exit 1
    }
}

"Done. $($hits.Count) file(s) affected."
