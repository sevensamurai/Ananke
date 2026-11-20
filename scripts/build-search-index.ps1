#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds docs/search-index.json, which the documentation site's search reads.

.DESCRIPTION
    One entry per page listed in docs/site.json, in its order:

      path, title, section, tags   as site.json gives them
      headings                     the page's ## and ### headings, each with the anchor
                                   docs/index.html gives it, so a result can open the section
      keywords                     the words that say most about the page

    The keywords are the page's most frequent words. A word in a heading counts three times and a
    word in inline code twice, because headings and API names are what people search for. Fenced
    code blocks, link targets, addresses, HTML comments and common English words are left out.

    The file is built, never edited: .gitignore covers it, website.yml builds it before each deploy,
    and the pull-request build runs this script so a broken run fails before the merge. Without the
    file the site searches site.json's titles and tags alone.

    A page listed in site.json that does not exist under docs/ fails the run.

.PARAMETER OutFile
    Where to write the index. Defaults to docs/search-index.json.

.EXAMPLE
    pwsh -File scripts/build-search-index.ps1
#>
param([string]$OutFile)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$KeywordCount = 30
$HeadingWeight = 3
$InlineCodeWeight = 2

# Common English words, and words most pages use, that say nothing about what a page is about.
$Stopwords = [System.Collections.Generic.HashSet[string]]::new([string[]]@(
    -split @'
a about above after again against all almost along already also although always am among an and
another any anything are around as at available back be because been before being below best
better between both but by can cannot could did different do does doing done down during e.g each
easy either else enough etc even every everything example few first following for found from
further get gets getting give given gives go goes going good got had has have having he her here
how however i.e if in instead into is it its itself just keep keeps know last later least less let
like likely made main make makes many may me might more most much must my need needed needs never
new next no nor not note now of off often on once one only or other others otherwise our out over
own part per rather really right same see seen should shown simple simply since so some something
still such sure take takes than that the their them then there these they thing things this those
though through thus to too two under unless until up upon us use used uses using usually very via
want was way ways we well were what when where whether which while who whose why will with within
without would yet you your
'@
))

# Resolve the repo from the script's own location so the result does not depend on cwd.
$root = & git -C $PSScriptRoot rev-parse --show-toplevel 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($root)) {
    Write-Error 'Not inside a git working tree (or git is not installed).'
    exit 2
}
$docs = Join-Path $root 'docs'
if (-not $OutFile) { $OutFile = Join-Path $docs 'search-index.json' }

# The id docs/index.html gives a heading (its slugify): lower-case, tags removed, every character
# outside [A-Za-z0-9_], whitespace and '-' removed, whitespace runs turned into '-'. The page passes
# the whole heading line, '#' marks included, so this takes the line too.
function Get-HeadingAnchor([string]$line) {
    $slug = $line.ToLowerInvariant()
    $slug = $slug -replace '<[^>]+>', ''
    $slug = $slug -replace '[^A-Za-z0-9_\s-]', ''
    return ($slug.Trim() -replace '\s+', '-')
}

# A heading as a reader sees it: link targets, code ticks and emphasis marks removed.
function Get-HeadingText([string]$markdown) {
    $text = $markdown -replace '!?\[([^\]]*)\]\([^)]*\)', '$1'
    $text = $text -replace '[`*]', ''
    $text = $text -replace '(^|\s)_+|_+(\s|$)', '$1$2'
    return ($text -replace '\s+', ' ').Trim()
}

function Get-Words([string]$text) {
    $text = $text.ToLowerInvariant() -replace 'https?://\S+', ' '
    # Words, keeping names such as c#, c++, Ananke.Postgres and multi-agent together.
    foreach ($match in [regex]::Matches($text, "[\p{L}\p{N}]+(?:[.'’-][\p{L}\p{N}]+)*[+#]*")) {
        $word = $match.Value
        if ($word.Length -gt 1 -and $word -match '\p{L}' -and $word -notmatch "['’]" -and -not $Stopwords.Contains($word)) {
            $word
        }
    }
}

function Get-PageIndex([string]$markdown) {
    $markdown = $markdown -replace "`r`n", "`n"
    # Fenced code and HTML comments carry no words a reader searches for.
    $prose = [regex]::Replace($markdown, '(?ms)^[ \t]*(`{3,}|~{3,})[^\n]*\n.*?^[ \t]*\1[ \t]*$', '')
    $prose = [regex]::Replace($prose, '(?s)<!--.*?-->', '')

    $scores = @{}
    $add = {
        param([string]$text, [int]$weight)
        foreach ($word in Get-Words $text) { $scores[$word] = ($scores[$word] ?? 0) + $weight }
    }

    $headings = [System.Collections.Generic.List[object]]::new()
    foreach ($match in [regex]::Matches($prose, '(?m)^(#{2,3})[ \t]+(.+?)[ \t]*$')) {
        $line = $match.Value
        $text = Get-HeadingText ($match.Groups[2].Value -replace '[ \t]+#+$', '')
        $headings.Add([ordered]@{ text = $text; anchor = Get-HeadingAnchor $line })
        & $add $text ($HeadingWeight - 1)
    }

    foreach ($match in [regex]::Matches($prose, '`([^`\n]+)`')) {
        & $add $match.Groups[1].Value ($InlineCodeWeight - 1)
    }

    # Links and images keep their text; their targets go.
    $text = $prose -replace '!?\[([^\]]*)\]\([^)]*\)', '$1' -replace '<[^>]+>', ' '
    & $add $text 1

    $keywords = $scores.GetEnumerator() |
        Sort-Object -Property @{ Expression = 'Value'; Descending = $true }, @{ Expression = 'Key'; Descending = $false } |
        Select-Object -First $KeywordCount |
        ForEach-Object Key

    return @{ headings = @($headings); keywords = @($keywords) }
}

$site = Get-Content -Raw -Encoding utf8 (Join-Path $docs 'site.json') | ConvertFrom-Json
$pages = [System.Collections.Generic.List[object]]::new()
$missing = @()
foreach ($section in $site.sections) {
    foreach ($item in $section.items) {
        $file = Join-Path $docs $item.path
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { $missing += $item.path; continue }
        $index = Get-PageIndex (Get-Content -Raw -Encoding utf8 -LiteralPath $file)
        $tags = if ($item.PSObject.Properties['tags']) { @($item.tags) } else { @() }
        $pages.Add([ordered]@{
            path     = $item.path
            title    = $item.title
            section  = $section.title
            tags     = $tags
            headings = $index.headings
            keywords = $index.keywords
        })
    }
}

if ($missing) {
    Write-Error "site.json lists pages that are not under docs/: $($missing -join ', ')"
    exit 1
}

$json = [ordered]@{ pages = @($pages) } | ConvertTo-Json -Depth 6 -Compress
[System.IO.File]::WriteAllText($OutFile, "$json`n", [System.Text.UTF8Encoding]::new($false))

$headingCount = ($pages | ForEach-Object { $_.headings.Count } | Measure-Object -Sum).Sum
Write-Host "Wrote $OutFile`: $($pages.Count) pages, $headingCount headings."
