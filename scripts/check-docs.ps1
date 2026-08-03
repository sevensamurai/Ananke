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

    SCOPE NOTE (2026-08-01). Path checking and identifier checking have deliberately
    DIFFERENT default scopes, because they fail differently:

      * A broken path is ALWAYS wrong -- there is no legitimate reason for a doc to link
        at a file that does not exist. So paths are checked everywhere in scope,
        including src/demos/** and PLAN-*.md.
      * An unknown identifier is often legitimate in tutorial/demo prose (placeholder
        types, illustrative names). So identifier checking still skips demos and
        PLAN-*.md unless -IncludeExamples.

    This split exists because the earlier default (roots 'src' + 'docs', demos excluded
    wholesale) meant the repo-root README.md was never scanned at all -- so 34 broken
    links in the front-door README, plus 16 more across demo READMEs, reported "clean".
    The path machinery was working; it was simply never pointed at them.

.PARAMETER Path
    One or more roots to scan for Markdown. Defaults to 'src', 'docs' and 'releases'.
    Repo-root Markdown (README.md, ARCHITECTURE.md, MAP.md, ...) is ALWAYS scanned --
    it is the front door and the most expensive place to have a broken link.
    internals/ is excluded by default; see -IncludeInternals.

.PARAMETER IgnoreFile
    Path to a newline-delimited allow-list of identifiers to skip (e.g. doc-only or
    planned types). Defaults to scripts/check-docs-ignore.txt next to this script.

.PARAMETER IncludeCodeBlocks
    Also scan fenced code blocks (noisier; good for periodic deep audits).

.PARAMETER IncludeExamples
    Also apply IDENTIFIER checking to src/demos/** and PLAN-*.md. Their path references
    are checked either way -- see the scope note above.

.PARAMETER IncludeInternals
    Also scan internals/. Off by default: it is a historical ADR archive, and a plan
    written in April that references a file since renamed is expected staleness, not
    drift to fix. Roughly 890 such references exist, which would drown the 66 live ones.
    Use this for a deliberate archive audit, not in CI.

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
    [switch]   $IncludeInternals,
    [switch]   $ShowContext
)

$ErrorActionPreference = 'Stop'

# --- Locate the solution root (this script lives in <root>/scripts) ---------------
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root      = Split-Path -Parent $scriptDir

if (-not $Path) {
    $Path = @('src', 'docs', 'releases')
    if ($IncludeInternals) { $Path += 'internals' }
}
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
# Project names are real things a doc may legitimately name (`AgenticWebDemo`,
# `Ananke.Orchestration`), but they are not necessarily C# identifiers -- a demo using
# top-level statements declares no namespace, so its name appears in no .cs file. Add
# every .csproj basename so naming a real project passes, while naming a project that
# does NOT exist still fails. That distinction is the point: it is what catches a README
# advertising a demo that was deleted.
$projFiles = Get-ChildItem -Path $csRoot -Recurse -Filter '*.csproj' -File |
    Where-Object { $_.FullName -notmatch '[\\/](obj|bin|\.vs)[\\/]' }
foreach ($p in $projFiles) {
    [void] $known.Add([System.IO.Path]::GetFileNameWithoutExtension($p.Name))
}

Write-Host ("  {0} C# files, {1} projects, {2} distinct identifiers" -f $csFiles.Count, $projFiles.Count, $known.Count) -ForegroundColor DarkGray

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
# Build artefacts are never docs. Demos and PLAN-*.md ARE included here so their path
# references get checked; identifier checking is suppressed for them below.
$excludeDir = '[\\/](obj|bin|\.vs|node_modules)[\\/]'

# Repo-root Markdown is always in scope -- README.md is the front door, and it is the
# single most expensive place in the repo to carry a broken link.
$mdFiles = @(Get-ChildItem -Path $root -Filter '*.md' -File)
foreach ($r in $scanRoots) {
    $mdFiles += Get-ChildItem -Path $r -Recurse -Filter '*.md' -File |
        Where-Object { $_.FullName -notmatch $excludeDir }
}
$mdFiles = @($mdFiles | Sort-Object FullName -Unique)

# Identifier checking is suppressed on three kinds of file; path checking is not
# suppressed anywhere, because a link that does not resolve is wrong in any file.
#
#   demos/, PLAN-*.md : tutorial prose legitimately names placeholder or aspirational
#                       types that were never meant to exist in source.
#   releases/*.md     : a shipped release note is a HISTORICAL RECORD of that version.
#                       If v0.3.0 shipped `InterruptableSession` and it was later
#                       renamed, the v0.3.0 note is still correct about v0.3.0 --
#                       "fixing" it would falsify the changelog. Their links are still
#                       checked, because a broken link is broken whenever it is clicked.
$exampleFileRegex  = [regex] '[\\/]demos[\\/]'
$historicalRegex   = [regex] '[\\/]releases[\\/]'
function Test-ScanIdentifiers($file) {
    if ($IncludeExamples) { return $true }
    if ($exampleFileRegex.IsMatch($file.FullName))  { return $false }
    if ($historicalRegex.IsMatch($file.FullName))   { return $false }
    if ($file.Name -match '^(PLAN|CHANGELOG)')      { return $false }
    return $true
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
    $scanIdents = Test-ScanIdentifiers $f
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
            if ($scanIdents) {
                foreach ($m in $tokenRegex.Matches($span)) {
                    $tok = $m.Value
                    if (-not (Test-IsCandidate $tok)) { continue }
                    if ($known.Contains($tok))         { continue }
                    if ($ignore.Contains($tok))        { continue }
                    $findings.Add([pscustomobject]@{
                        Token = $tok; File = $rel; Line = $lineNo; Text = $line.Trim()
                    })
                }
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
