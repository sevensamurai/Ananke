# One-time + CI-checkable normalizer: strips UTF-8 BOMs from all tracked text files.
# Usage: pwsh scripts/fix-encoding.ps1 [-Check]   (-Check = report only, exit 1 on findings)
param([switch]$Check)

$extensions = '*.cs','*.csx','*.md','*.json','*.yml','*.yaml','*.xml','*.config','*.txt',
              '*.csproj','*.props','*.targets','*.slnx','*.nuspec','*.resx','*.sh','*.editorconfig'
$hits = @()

foreach ($ext in $extensions) {
    Get-ChildItem -Recurse -Filter $ext -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(\.git|obj|bin|node_modules|\.codegraph)\\' } |
        ForEach-Object {
            $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
            if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
                $hits += $_.FullName
                if (-not $Check) {
                    [System.IO.File]::WriteAllBytes($_.FullName, $bytes[3..($bytes.Length - 1)])
                }
            }
        }
}

if ($hits.Count -gt 0) {
    $verb = $Check ? 'BOM found in' : 'BOM stripped from'
    $hits | ForEach-Object { "$verb $_" }
    if ($Check) { exit 1 }
}

"Done. $($hits.Count) file(s) affected."
