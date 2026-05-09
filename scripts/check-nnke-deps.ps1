#!/usr/bin/env pwsh
# scripts/check-nnke-deps.ps1
#
# CI guard: verifies that dotnet-ananke does NOT reference any cloud-platform
# SDKs. Platform SDK references must only appear in dotnet-ananke-platform and
# the Ananke.Federation.* packages.
#
# Exit 0 = clean, Exit 1 = violation found.

param(
    [string]$ProjectPath = "$PSScriptRoot/../src/dotnet-ananke/dotnet-ananke.csproj"
)

$forbidden = @(
    'Azure\.',
    'Microsoft\.Azure\.',
    'Google\.Cloud\.',
    'Google\.Apis\.',
    'Anthropic\.'
)

$content = Get-Content -Path $ProjectPath -Raw -ErrorAction Stop

$violations = @()
foreach ($pattern in $forbidden) {
    $matches = [regex]::Matches($content, "<PackageReference[^>]+Include=""($pattern[^""]+)""", 'IgnoreCase')
    foreach ($m in $matches) {
        $violations += $m.Groups[1].Value
    }
}

if ($violations.Count -gt 0) {
    Write-Error "nnke dependency boundary violated. The following cloud-SDK packages must not appear in dotnet-ananke.csproj:"
    $violations | ForEach-Object { Write-Error "  - $_" }
    Write-Error "Move them to dotnet-ananke-platform or an Ananke.Federation.* package."
    exit 1
}

Write-Host "check-nnke-deps: OK — no cloud-SDK references in dotnet-ananke.csproj"
exit 0
