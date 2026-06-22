#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Pre-PR documentation drift check for the Ananke solution.

.DESCRIPTION
    Catches stale type/API names in Markdown docs *before* they are committed.

    The check is deliberately simple and dependency-free: it builds a set of every
    PascalCase identifier that appears anywhere in the C# source under src/, then
    scans curated Markdown (per-project README.md / ARCHITECTURE.md and the docs/
    tree) for backtick-quoted identifiers that exist NOWHERE in the source.

    That single rule -- "if a doc names a type/method, it must appear in the code" --
    is exactly the failure mode that bites LLM-facing docs: a renamed or typo'd type
    such as `GoogleAgentModel` (real class: `GeminiAgentModel`). Any external type the
    codebase genuinely uses (HttpClient, QdrantClient, ...) appears in source and so
    passes automatically; only genuinely-absent names are flagged.

    By default only INLINE `code` spans are inspected -- that is where curated type
    inventories live and where drift hides. Fenced ```code``` blocks (full samples,
    full of local variables and tutorial placeholder types) are skipped unless you
    pass -IncludeCodeBlocks. Demo projects and PLAN-*.md working notes are skipped
    unless you pass -IncludeExamples.

    A second, independent check verifies REFERENCED FILE PATHS: any inline `code` span
    or Markdown link target that looks like a repo path (e.g. `src/Foo/Bar.cs`, or a
    relative link like `../demos/FooDemo/`) is resolved and checked for existence. Bare
    paths rooted at src/, docs/, or internals/ resolve from the repo root; anything else
    (including any path starting with `./` or `../`) resolves relative to the Markdown
    file that contains it, matching normal Markdown link semantics. This catches the
    other half of doc drift: a cross-referenced file that was since renamed or moved.

    This is NOT a semantic check. It does not verify signatures, namespaces, or that a
    type is used correctly -- only that the identifier or path exists. It is a fast
    first guard, not a substitute for review or compilation.

.PARAMETER Path
    One or more roots to scan for Markdown. Defaults to 'src' and 'docs'.

.PARAMETER IgnoreFile
    Path to a newline-delimited allow-list of identifiers to skip (e.g. doc-only or
    planned types). Defaults to scripts/check-docs-ignore.txt next to this script.

.PARAMETER IncludeCodeBlocks
    Also scan fenced code blocks (noisier; good for periodic deep audits).

.PARAMETER IncludeExamples
    Also scan src/demos/** and PLAN-*.md files (skipped by default).

.PARAMETER ShowContext
    Print the surrounding line for each finding.

.EXAMPLE
    pwsh scripts/check-docs.ps1
    # Windows PowerShell 5.1:
    powershell -File scripts/check-docs.ps1

.NOTES
    Exit code 0 = clean, 1 = drift found (suitable for a pre-push hook or CI later).
#>
[CmdletBinding()]
param(
    [string[]] $Path,
    [string]   $IgnoreFile,
    [switch]   $IncludeCodeBlocks,
    [switch]   $IncludeExamples,
    [switch]   $ShowContext
)

$ErrorActionPreference = 'Stop'

# --- Locate the solution root (this script lives in <root>/scripts) ---------------
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root      = Split-Path -Parent $scriptDir

if (-not $Path) { $Path = @('src', 'docs') }
$scanRoots = $Path | ForEach-Object {
    if ([System.IO.Path]::IsPathRooted($_)) { $_ } else { Join-Path $root $_ }
} | Where-Object { Test-Path $_ }

if (-not $IgnoreFile) { $IgnoreFile = Join-Path $scriptDir 'check-docs-ignore.txt' }

# --- Token rules ------------------------------------------------------------------
# Tokenizer splits identifiers out of code spans. A "candidate" type/API reference is
# then: PascalCase (uppercase first, case-SENSITIVE), >= 3 chars, has a lowercase
# letter (excludes acronyms LLM/MCP/API), and has no underscore (excludes config keys
# like Anthropic__ApiKey and snake_case -- C# type names do not use underscores).
$tokenRegex = [regex] '[A-Za-z][A-Za-z0-9_]+'
function Test-IsCandidate([string] $t) {
    if ($t.Length -lt 3)      { return $false }
    if ($t -cnotmatch '^[A-Z]') { return $false }   # case-SENSITIVE: starts uppercase
    if ($t -cnotmatch '[a-z]')  { return $false }   # has a lowercase letter
    if ($t -match '_')          { return $false }   # no underscore
    return $true
}

# --- Build the known-identifier set from C# source --------------------------------
Write-Host 'Building known-identifier set from src/**/*.cs ...' -ForegroundColor DarkGray
$known   = [System.Collections.Generic.HashSet[string]]::new()
$csRoot  = Join-Path $root 'src'
$csFiles = Get-ChildItem -Path $csRoot -Recurse -Filter '*.cs' -File |
    Where-Object { $_.FullName -notmatch '[\\/](obj|bin|\.vs)[\\/]' }

$blockComment = [regex] '/\*[\s\S]*?\*/'
$lineComment  = [regex] '//[^\r\n]*'
foreach ($f in $csFiles) {
    # File basename: ARCHITECTURE.md legitimately references source files by name.
    [void] $known.Add([System.IO.Path]::GetFileNameWithoutExtension($f.Name))
    $text = [System.IO.File]::ReadAllText($f.FullName)
    # Strip comments: a stale type name that lingers only in a comment must NOT mask
    # the same stale name in the docs (this is exactly how GoogleAgentModel hid).
    $text = $blockComment.Replace($text, ' ')
    $text = $lineComment.Replace($text, ' ')
    foreach ($m in $tokenRegex.Matches($text)) { [void] $known.Add($m.Value) }
}
Write-Host ("  {0} C# files, {1} distinct identifiers" -f $csFiles.Count, $known.Count) -ForegroundColor DarkGray

# --- Load the ignore list ----------------------------------------------------------
$ignore = [System.Collections.Generic.HashSet[string]]::new()
if (Test-Path $IgnoreFile) {
    foreach ($line in Get-Content $IgnoreFile) {
        # Strip a trailing inline '# ...' comment, then the whole line if it's comment-only.
        $hashIndex = $line.IndexOf('#')
        $t = if ($hashIndex -ge 0) { $line.Substring(0, $hashIndex).Trim() } else { $line.Trim() }
        if ($t) { [void] $ignore.Add($t) }
    }
}

# --- Select Markdown files ---------------------------------------------------------
$excludeDir = if ($IncludeExamples) { '[\\/](obj|bin|\.vs|node_modules)[\\/]' }
              else                  { '[\\/](obj|bin|\.vs|node_modules|demos)[\\/]' }

$mdFiles = foreach ($r in $scanRoots) {
    Get-ChildItem -Path $r -Recurse -Filter '*.md' -File |
        Where-Object {
            $_.FullName -notmatch $excludeDir -and
            ($IncludeExamples -or $_.Name -notmatch '^(PLAN|CHANGELOG)')
        }
}

# --- Path reference rules ----------------------------------------------------------
# A "path-looking" span: at least one path separator, ends in a recognized extension.
$pathExt          = 'cs|csproj|md|yml|yaml|json|sln|slnx|ps1|sh'
$pathLikeRegex    = [regex] "^[\w.\-]+(?:[\\/][\w.\-]+)+\.($pathExt)$"
$rootRelPathRegex = [regex] '^(?:src|docs|internals)[\\/]'
$mdLinkRegex      = [regex] '\[([^\]]*)\]\(([^)\s]+)\)'

function Test-DocPathReference([string] $target, [string] $fileDir) {
    $clean = ($target -replace '#.*$', '').Trim()
    if (-not $clean) { return $true }                    # pure #anchor link
    if ($clean -match '^[a-zA-Z][a-zA-Z0-9+.\-]*://') { return $true }   # external URL
    if ($clean -match '^mailto:') { return $true }

    # Primary resolution: repo-root-relative for bare src/docs/internals paths, otherwise
    # relative to the file containing the reference (standard Markdown link semantics).
    $primary = if ($rootRelPathRegex.IsMatch($clean) -and -not $clean.StartsWith('.')) {
        Join-Path $root $clean
    } else {
        Join-Path $fileDir $clean
    }
    if (Test-Path $primary) { return $true }

    # Fallback: some prose writes a repo-relative path as plain descriptive text rather
    # than a navigable link (e.g. the backtick-quoted label half of a Markdown link, or
    # a "see src/Foo/Bar.cs"-style mention without a leading ./ or ../). Try repo-root
    # resolution too before declaring it broken.
    if ($primary -ne (Join-Path $root $clean)) {
        if (Test-Path (Join-Path $root $clean)) { return $true }
    }
    return $false
}

# --- Scan --------------------------------------------------------------------------
$inlineCode    = [regex] '`([^`]+)`'
$fenceLine     = [regex] '^\s*```'
$findings      = [System.Collections.Generic.List[object]]::new()
$pathFindings  = [System.Collections.Generic.List[object]]::new()
$mdCount       = 0

foreach ($f in $mdFiles) {
    $mdCount++
    $rel      = $f.FullName.Substring($root.Length).TrimStart('\','/')
    # ARCHITECTURE.md fenced blocks ARE the curated type inventory (dependency trees,
    # abstraction maps) -- always scan them. Elsewhere, fenced code is opt-in.
    $scanFenced = $IncludeCodeBlocks -or ($f.Name -ieq 'ARCHITECTURE.md')
    $inFenced   = $false
    $lineNo     = 0
    foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
        $lineNo++
        if ($fenceLine.IsMatch($line)) { $inFenced = -not $inFenced; continue }

        # Choose the spans to inspect on this line.
        $spans = @()
        if ($inFenced) {
            if ($scanFenced) { $spans += $line }   # whole line is code
        } else {
            foreach ($m in $inlineCode.Matches($line)) { $spans += $m.Groups[1].Value }
        }

        foreach ($span in $spans) {
            foreach ($m in $tokenRegex.Matches($span)) {
                $tok = $m.Value
                if (-not (Test-IsCandidate $tok)) { continue }
                if ($known.Contains($tok))         { continue }
                if ($ignore.Contains($tok))        { continue }
                $findings.Add([pscustomobject]@{
                    Token = $tok; File = $rel; Line = $lineNo; Text = $line.Trim()
                })
            }

            # Path-looking inline code spans, e.g. `src/Foo/Bar.cs`.
            if (-not $inFenced -and $pathLikeRegex.IsMatch($span) -and -not $ignore.Contains($span)) {
                if (-not (Test-DocPathReference $span $f.DirectoryName)) {
                    $pathFindings.Add([pscustomobject]@{
                        PathRef = $span; File = $rel; Line = $lineNo; Text = $line.Trim()
                    })
                }
            }
        }

        # Markdown link targets, e.g. [text](../demos/FooDemo/) -- checked on raw lines
        # (not fenced code) regardless of -IncludeCodeBlocks.
        if (-not $inFenced) {
            foreach ($m in $mdLinkRegex.Matches($line)) {
                $target = $m.Groups[2].Value
                if ($ignore.Contains($target)) { continue }
                if (-not (Test-DocPathReference $target $f.DirectoryName)) {
                    $pathFindings.Add([pscustomobject]@{
                        PathRef = $target; File = $rel; Line = $lineNo; Text = $line.Trim()
                    })
                }
            }
        }
    }
}

# --- Report ------------------------------------------------------------------------
Write-Host ''
Write-Host ("Scanned {0} Markdown files under: {1}" -f $mdCount, ($scanRoots -join ', '))

if ($findings.Count -eq 0 -and $pathFindings.Count -eq 0) {
    Write-Host 'No documentation drift found. [OK]' -ForegroundColor Green
    exit 0
}

if ($findings.Count -gt 0) {
    $byToken = $findings | Group-Object Token | Sort-Object Name
    Write-Host ''
    Write-Host ("Found {0} unknown identifier(s) across {1} occurrence(s):" -f $byToken.Count, $findings.Count) -ForegroundColor Yellow
    Write-Host '(each names a type/API that appears in NO .cs file - likely renamed, typo''d, or removed)' -ForegroundColor DarkGray
    Write-Host ''

    foreach ($g in $byToken) {
        Write-Host ("  {0}" -f $g.Name) -ForegroundColor Red
        foreach ($occ in ($g.Group | Sort-Object File, Line)) {
            Write-Host ("      {0}:{1}" -f $occ.File, $occ.Line)
            if ($ShowContext) { Write-Host ("        | {0}" -f $occ.Text) -ForegroundColor DarkGray }
        }
    }

    Write-Host ''
    Write-Host 'If an identifier is a genuine external/planned name (not a code symbol), add it to:' -ForegroundColor DarkGray
    Write-Host ("  {0}" -f ($IgnoreFile.Substring($root.Length).TrimStart('\','/'))) -ForegroundColor DarkGray
}

if ($pathFindings.Count -gt 0) {
    $byPath = $pathFindings | Group-Object PathRef | Sort-Object Name
    Write-Host ''
    Write-Host ("Found {0} broken path reference(s) across {1} occurrence(s):" -f $byPath.Count, $pathFindings.Count) -ForegroundColor Yellow
    Write-Host '(each names a file/link target that does not resolve - likely renamed, moved, or typo''d)' -ForegroundColor DarkGray
    Write-Host ''

    foreach ($g in $byPath) {
        Write-Host ("  {0}" -f $g.Name) -ForegroundColor Red
        foreach ($occ in ($g.Group | Sort-Object File, Line)) {
            Write-Host ("      {0}:{1}" -f $occ.File, $occ.Line)
            if ($ShowContext) { Write-Host ("        | {0}" -f $occ.Text) -ForegroundColor DarkGray }
        }
    }
}

exit 1
