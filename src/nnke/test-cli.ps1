<#
.SYNOPSIS
    Smoke-tests every nnke CLI command and option variant.

.DESCRIPTION
    Builds nnke from source, then runs every command and significant option
    combination, validates exit codes and (for --json variants) validates
    the JSON shape.  Scaffold commands create projects in a temp directory
    and immediately build them to confirm generated code compiles.

    Run from the repository src/ directory:
        powershell -ExecutionPolicy Bypass .\nnke\test-cli.ps1

    Flags
        -SkipBuild      Skip the initial dotnet build of nnke
        -SkipScaffold   Skip scaffold+build tests (faster smoke run)
        -Verbose        Print every command before running it
#>
param(
    [switch]$SkipBuild,
    [switch]$SkipScaffold,
    [switch]$Verbose
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

$Script:Passed  = 0
$Script:Failed  = 0
$Script:Skipped = 0
$Script:Results = [System.Collections.Generic.List[PSObject]]::new()

function Write-Header([string]$title) {
    Write-Host ""
    Write-Host "  -- $title" -ForegroundColor Cyan
}

function Invoke-Nnke {
    param(
        [string]   $Label,
        [string[]] $CliArgs,
        [int]      $ExpectExit   = 0,
        [string]   $ExpectOutput = $null,
        [switch]   $JsonShape,
        [string[]] $JsonKeys     = @(),
        [switch]   $Skip
    )

    if ($Skip) {
        $Script:Skipped++
        $Script:Results.Add([PSCustomObject]@{ Label=$Label; Status='SKIP'; Detail='' })
        Write-Host "  [SKIP] $Label" -ForegroundColor DarkGray
        return
    }

    if ($Verbose) { Write-Host "  $ nnke $($CliArgs -join ' ')" -ForegroundColor DarkGray }

    $output = & dotnet run --project nnke --no-build -- @CliArgs 2>&1
    $exit   = $LASTEXITCODE
    $stdout = $output -join "`n"

    $ok     = $true
    $detail = @()

    if ($exit -ne $ExpectExit) {
        $ok = $false
        $detail += "exit=$exit (expected $ExpectExit)"
    }

    if ($ExpectOutput -and $stdout -notmatch [regex]::Escape($ExpectOutput)) {
        $ok = $false
        $detail += "missing text: '$ExpectOutput'"
    }

    if ($JsonShape) {
        try {
            $json = $stdout | ConvertFrom-Json -ErrorAction Stop
            foreach ($key in $JsonKeys) {
                if ($null -eq $json.$key) {
                    $ok = $false
                    $detail += "JSON missing key: $key"
                }
            }
        } catch {
            $ok = $false
            $detail += "stdout is not valid JSON"
        }
    }

    $status = if ($ok) { 'PASS' } else { 'FAIL' }
    if ($ok) { $Script:Passed++ } else { $Script:Failed++ }
    $color  = if ($status -eq 'PASS') { 'Green' } elseif ($status -eq 'FAIL') { 'Red' } else { 'DarkGray' }
    $suffix = if ($detail.Count -gt 0) { " -- $($detail -join '; ')" } else { '' }
    Write-Host "  [$status] $Label$suffix" -ForegroundColor $color

    if (-not $ok -and -not $Verbose) {
        ($stdout -split "`n") | Select-Object -First 12 | ForEach-Object { Write-Host "         $_" -ForegroundColor DarkYellow }
    }

    $Script:Results.Add([PSCustomObject]@{ Label=$Label; Status=$status; Detail=($detail -join '; ') })
}

function Invoke-ScaffoldAndBuild {
    param(
        [string]   $Label,
        [string[]] $ScaffoldArgs,
        [string]   $ProjectDir
    )

    if ($SkipScaffold) {
        $Script:Skipped++
        $Script:Results.Add([PSCustomObject]@{ Label=$Label; Status='SKIP'; Detail='--SkipScaffold' })
        Write-Host "  [SKIP] $Label" -ForegroundColor DarkGray
        return
    }

    if ($Verbose) { Write-Host "  $ nnke $($ScaffoldArgs -join ' ')" -ForegroundColor DarkGray }

    $scaffoldOut  = & dotnet run --project nnke --no-build -- @ScaffoldArgs 2>&1
    $scaffoldExit = $LASTEXITCODE

    if ($scaffoldExit -ne 0) {
        $Script:Failed++
        $Script:Results.Add([PSCustomObject]@{ Label=$Label; Status='FAIL'; Detail="scaffold exit=$scaffoldExit" })
        Write-Host "  [FAIL] $Label -- scaffold failed (exit $scaffoldExit)" -ForegroundColor Red
        ($scaffoldOut -join "`n" -split "`n") | Select-Object -First 8 | ForEach-Object { Write-Host "         $_" -ForegroundColor DarkYellow }
        return
    }

    $buildOut  = & dotnet build $ProjectDir --nologo -v q 2>&1
    $buildExit = $LASTEXITCODE
    $ok        = $buildExit -eq 0
    $status    = if ($ok) { 'PASS' } else { 'FAIL' }
    $detail    = if ($ok) { '' } else { "dotnet build exit=$buildExit" }

    if ($ok) { $Script:Passed++ } else { $Script:Failed++ }
    $color  = if ($ok) { 'Green' } else { 'Red' }
    $suffix = if ($detail) { " -- $detail" } else { '' }
    Write-Host "  [$status] $Label$suffix" -ForegroundColor $color

    if (-not $ok) {
        ($buildOut -join "`n" -split "`n") | Select-Object -First 12 | ForEach-Object { Write-Host "         $_" -ForegroundColor DarkYellow }
    }

    $Script:Results.Add([PSCustomObject]@{ Label=$Label; Status=$status; Detail=$detail })
}

# ---- Setup ------------------------------------------------------------------

$SrcDir = $PSScriptRoot | Split-Path
if (-not (Test-Path (Join-Path $SrcDir 'Ananke.slnx'))) {
    $SrcDir = $PSScriptRoot
}
Set-Location $SrcDir

Write-Host ""
Write-Host "  nnke CLI smoke-test" -ForegroundColor White
Write-Host "  Working dir: $SrcDir" -ForegroundColor DarkGray

if (-not $SkipBuild) {
    Write-Host ""
    Write-Host "  Building nnke..." -ForegroundColor DarkGray
    $buildResult = & dotnet build nnke --nologo -v q 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  [FATAL] nnke build failed -- aborting" -ForegroundColor Red
        $buildResult | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkYellow }
        exit 1
    }
    Write-Host "  Build OK" -ForegroundColor Green
}

$TmpDir = Join-Path ([System.IO.Path]::GetTempPath()) "nnke-test-$(Get-Random)"
New-Item -ItemType Directory -Path $TmpDir -Force | Out-Null
Write-Host "  Temp dir: $TmpDir" -ForegroundColor DarkGray

$FixtureDir = Join-Path $TmpDir 'fixtures'
New-Item -ItemType Directory -Path $FixtureDir -Force | Out-Null

$ManifestYml = Join-Path $FixtureDir 'test.ananke.yml'
@'
name: test-workflow
models:
  primary:
    provider: openai
    model: gpt-4.1-mini
jobs:
  extract:
    type: agent
    model: primary
    prompt: "Extract data from: {{input}}"
  transform:
    type: agent
    model: primary
    prompt: "Transform: {{input}}"
  load:
    type: agent
    model: primary
    prompt: "Load: {{input}}"
connections:
  - extract -> transform
  - transform -> load
  - load -> End
'@ | Set-Content -Path $ManifestYml -Encoding UTF8

$BrokenYml = Join-Path $FixtureDir 'broken.ananke.yml'
@'
name: broken-workflow
models: {}
jobs:
  orphan:
    type: agent
    model: undefined-alias
    prompt: "orphan"
connections: []
'@ | Set-Content -Path $BrokenYml -Encoding UTF8

# ---- 1. Root / help ---------------------------------------------------------

Write-Header "Root / help"
Invoke-Nnke -Label "nnke --help"    -CliArgs @('--help')   -ExpectOutput 'new'
Invoke-Nnke -Label "nnke (no args)" -CliArgs @()           -ExpectOutput 'new' -ExpectExit 1
Invoke-Nnke -Label "nnke --version" -CliArgs @('--version')

# ---- 2. schema --------------------------------------------------------------

Write-Header "schema"
Invoke-Nnke -Label "nnke schema"        -CliArgs @('schema')          -JsonShape -JsonKeys @('tool','commands')
Invoke-Nnke -Label "nnke schema --json" -CliArgs @('schema','--json') -JsonShape -JsonKeys @('tool','commands')

# ---- 3. patterns ------------------------------------------------------------

Write-Header "patterns"
Invoke-Nnke -Label "nnke patterns"            -CliArgs @('patterns')                  -ExpectOutput 'etl'
Invoke-Nnke -Label "nnke patterns --json"     -CliArgs @('patterns','--json')         -JsonShape -JsonKeys @('status','manifestPatterns','agenticPatterns')
Invoke-Nnke -Label "nnke patterns etl"        -CliArgs @('patterns','etl')            -ExpectOutput 'ETL'
Invoke-Nnke -Label "nnke patterns etl --json" -CliArgs @('patterns','etl','--json')   -JsonShape -JsonKeys @('status','key','title')
Invoke-Nnke -Label "nnke patterns sequential" -CliArgs @('patterns','sequential')     -ExpectOutput 'Sequential'
Invoke-Nnke -Label "nnke patterns router"     -CliArgs @('patterns','router')         -ExpectOutput 'Router'
Invoke-Nnke -Label "nnke patterns unknown"    -CliArgs @('patterns','does-not-exist') -ExpectOutput 'Unknown pattern'

# ---- 4. explain -------------------------------------------------------------

Write-Header "explain"
Invoke-Nnke -Label "nnke explain (list)"                 -CliArgs @('explain')                               -ExpectOutput 'ANANKE_'
Invoke-Nnke -Label "nnke explain --json (list)"          -CliArgs @('explain','--json')                      -JsonShape -JsonKeys @('status','codes')
Invoke-Nnke -Label "nnke explain ANANKE_TOPO_003"        -CliArgs @('explain','ANANKE_TOPO_003')             -ExpectOutput 'Fork'
Invoke-Nnke -Label "nnke explain ANANKE_TOPO_003 --json" -CliArgs @('explain','ANANKE_TOPO_003','--json')    -JsonShape -JsonKeys @('status','code','title')
Invoke-Nnke -Label "nnke explain ANANKE_MANIFEST_001"    -CliArgs @('explain','ANANKE_MANIFEST_001')         -ExpectOutput 'parse'
Invoke-Nnke -Label "nnke explain unknown-code"           -CliArgs @('explain','ANANKE_FAKE_999')             -ExpectOutput 'Unknown diagnostic'

# ---- 5. docs ----------------------------------------------------------------

Write-Header "docs"
Invoke-Nnke -Label "nnke docs (list)"             -CliArgs @('docs')                              -ExpectOutput 'getting-started'
Invoke-Nnke -Label "nnke docs --list"             -CliArgs @('docs','--list')                     -ExpectOutput 'getting-started'
Invoke-Nnke -Label "nnke docs --list --json"      -CliArgs @('docs','--list','--json')            -JsonShape -JsonKeys @('status','topics')
Invoke-Nnke -Label "nnke docs --search agents"    -CliArgs @('docs','--search','agents')          -ExpectOutput 'result'
Invoke-Nnke -Label "nnke docs --search --json"    -CliArgs @('docs','--search','agents','--json') -JsonShape -JsonKeys @('status')
Invoke-Nnke -Label "nnke docs 01-getting-started" -CliArgs @('docs','01-getting-started')         -ExpectOutput 'getting started'
Invoke-Nnke -Label "nnke docs unknown-topic"      -CliArgs @('docs','no-such-topic')              -ExpectOutput 'not found'

# ---- 6. validate (top-level alias) ------------------------------------------

Write-Header "validate"
Invoke-Nnke -Label "nnke validate valid.yml"     -CliArgs @('validate', $ManifestYml)                       -ExpectOutput 'valid'
Invoke-Nnke -Label "nnke validate valid --json"  -CliArgs @('validate', $ManifestYml, '--json')             -JsonShape -JsonKeys @('status','workflow')
Invoke-Nnke -Label "nnke validate broken.yml"    -CliArgs @('validate', $BrokenYml)                         -ExpectOutput 'ANANKE_'
Invoke-Nnke -Label "nnke validate broken --json" -CliArgs @('validate', $BrokenYml, '--json')               -JsonShape -JsonKeys @('status','errors')
Invoke-Nnke -Label "nnke validate missing-file"  -CliArgs @('validate', (Join-Path $FixtureDir 'nope.yml')) -ExpectOutput 'not found'

# ---- 7. diagram (top-level alias) -------------------------------------------

$DiagramOut = Join-Path $TmpDir 'diagram.mmd'
Write-Header "diagram"
Invoke-Nnke -Label "nnke diagram (stdout)"      -CliArgs @('diagram', $ManifestYml)                                 -ExpectOutput 'graph TD'
Invoke-Nnke -Label "nnke diagram --output file" -CliArgs @('diagram', $ManifestYml, '--output', $DiagramOut)        -ExpectOutput 'written'
Invoke-Nnke -Label "nnke diagram --json"        -CliArgs @('diagram', $ManifestYml, '--json')                       -JsonShape -JsonKeys @('status','diagram')
Invoke-Nnke -Label "nnke diagram missing-file"  -CliArgs @('diagram', (Join-Path $FixtureDir 'nope.yml'))           -ExpectOutput 'not found'

# ---- 8. manifest (grouped subcommands) --------------------------------------

Write-Header "manifest validate / diagram"
Invoke-Nnke -Label "nnke manifest validate valid"  -CliArgs @('manifest','validate', $ManifestYml)           -ExpectOutput 'valid'
Invoke-Nnke -Label "nnke manifest validate --json" -CliArgs @('manifest','validate', $ManifestYml, '--json') -JsonShape -JsonKeys @('status','workflow')
Invoke-Nnke -Label "nnke manifest diagram stdout"  -CliArgs @('manifest','diagram', $ManifestYml)            -ExpectOutput 'graph TD'
Invoke-Nnke -Label "nnke manifest diagram --json"  -CliArgs @('manifest','diagram', $ManifestYml, '--json')  -JsonShape -JsonKeys @('status','diagram')

# ---- 9. inspect -------------------------------------------------------------

Write-Header "inspect"
Invoke-Nnke -Label "nnke inspect (CWD)"          -CliArgs @('inspect')                                     -ExpectOutput 'Project:'
Invoke-Nnke -Label "nnke inspect (explicit dir)" -CliArgs @('inspect', $FixtureDir)                        -ExpectOutput 'Project:'
Invoke-Nnke -Label "nnke inspect --json"         -CliArgs @('inspect', $FixtureDir, '--json')              -JsonShape -JsonKeys @('status','projectDir')
Invoke-Nnke -Label "nnke inspect missing-dir"    -CliArgs @('inspect', (Join-Path $TmpDir 'nope'))         -ExpectOutput 'not found'

# ---- 10. new quickstart -----------------------------------------------------

$QsDir    = Join-Path $TmpDir 'test-qs'
$QsAntDir = Join-Path $TmpDir 'test-qs-ant'
Write-Header "new quickstart"
Invoke-Nnke -Label "nnke new quickstart --help" -CliArgs @('new','quickstart','--help') -ExpectOutput 'provider'
Invoke-ScaffoldAndBuild `
    -Label        "new quickstart (openai)" `
    -ScaffoldArgs @('new','quickstart','test-qs','--output',$QsDir) `
    -ProjectDir   $QsDir
Invoke-Nnke -Label "nnke new quickstart --json" `
    -CliArgs  @('new','quickstart','test-qs2','--output',(Join-Path $TmpDir 'test-qs2'),'--json') `
    -JsonShape -JsonKeys @('status','projectDir','files')
Invoke-ScaffoldAndBuild `
    -Label        "new quickstart --provider anthropic" `
    -ScaffoldArgs @('new','quickstart','test-qs-ant','--provider','anthropic','--output',$QsAntDir) `
    -ProjectDir   $QsAntDir

# ---- 11. new chatbox --------------------------------------------------------

$CbDir       = Join-Path $TmpDir 'test-cb'
$CbGoogleDir = Join-Path $TmpDir 'test-cb-google'
Write-Header "new chatbox"
Invoke-Nnke -Label "nnke new chatbox --help" -CliArgs @('new','chatbox','--help') -ExpectOutput 'provider'
Invoke-ScaffoldAndBuild `
    -Label        "new chatbox (openai)" `
    -ScaffoldArgs @('new','chatbox','test-cb','--output',$CbDir) `
    -ProjectDir   $CbDir
Invoke-Nnke -Label "nnke new chatbox --json" `
    -CliArgs  @('new','chatbox','test-cb2','--output',(Join-Path $TmpDir 'test-cb2'),'--json') `
    -JsonShape -JsonKeys @('status','projectDir','files')
Invoke-ScaffoldAndBuild `
    -Label        "new chatbox --provider google" `
    -ScaffoldArgs @('new','chatbox','test-cb-google','--provider','google','--output',$CbGoogleDir) `
    -ProjectDir   $CbGoogleDir

# ---- 12. new workflow -------------------------------------------------------

Write-Header "new workflow"
Invoke-Nnke -Label "nnke new workflow --help" -CliArgs @('new','workflow','--help') -ExpectOutput 'pattern'

$ManifestPatternList = @('etl','sequential','fan-out','sub-workflow')
$CodePatternList     = @('router','review-critique','iterative-refinement','human-in-the-loop','handoff','streaming-chat','organic-host')

foreach ($p in $ManifestPatternList) {
    $dir = Join-Path $TmpDir "wf-$p"
    Invoke-ScaffoldAndBuild `
        -Label        "new workflow --pattern $p" `
        -ScaffoldArgs @('new','workflow',"wf-$p",'--pattern',$p,'--output',$dir) `
        -ProjectDir   $dir
}
foreach ($p in $CodePatternList) {
    $dir = Join-Path $TmpDir "wf-$p"
    Invoke-ScaffoldAndBuild `
        -Label        "new workflow --pattern $p" `
        -ScaffoldArgs @('new','workflow',"wf-$p",'--pattern',$p,'--output',$dir) `
        -ProjectDir   $dir
}

foreach ($prov in @('anthropic','google')) {
    $dir = Join-Path $TmpDir "wf-etl-$prov"
    Invoke-ScaffoldAndBuild `
        -Label        "new workflow etl --provider $prov" `
        -ScaffoldArgs @('new','workflow',"wf-etl-$prov",'--pattern','etl','--provider',$prov,'--output',$dir) `
        -ProjectDir   $dir
}

Invoke-Nnke -Label "nnke new workflow --json" `
    -CliArgs @('new','workflow','wf-json-test','--output',(Join-Path $TmpDir 'wf-json-test'),'--json') `
    -JsonShape -JsonKeys @('status','projectDir','pattern')
Invoke-Nnke -Label "nnke new workflow unknown-pattern" `
    -CliArgs @('new','workflow','wf-bad','--pattern','does-not-exist') `
    -ExpectOutput 'Unknown pattern'

# ---- 13. new pattern --------------------------------------------------------

$PatDir = Join-Path $TmpDir 'pat-router'
Write-Header "new pattern"
Invoke-Nnke -Label "nnke new pattern --list"        -CliArgs @('new','pattern','--list')          -ExpectOutput 'router'
Invoke-Nnke -Label "nnke new pattern --list --json" -CliArgs @('new','pattern','--list','--json') -JsonShape -JsonKeys @('status','patterns')
Invoke-Nnke -Label "nnke new pattern --help"        -CliArgs @('new','pattern','--help')          -ExpectOutput 'pattern'
Invoke-ScaffoldAndBuild `
    -Label        "new pattern router" `
    -ScaffoldArgs @('new','pattern','pat-router','--pattern','router','--output',$PatDir) `
    -ProjectDir   $PatDir
Invoke-Nnke -Label "nnke new pattern (no name)" -CliArgs @('new','pattern') -ExpectOutput 'name'

# ---- 14. kernel -------------------------------------------------------------

$FakeSnapshot = Join-Path $FixtureDir 'fake-snapshot.yml'
'kernelId: test-kernel' | Set-Content -Path $FakeSnapshot -Encoding UTF8

Write-Header "kernel"
Invoke-Nnke -Label "nnke kernel --help"            -CliArgs @('kernel','--help')                                     -ExpectOutput 'status'
Invoke-Nnke -Label "nnke kernel status (bad file)" -CliArgs @('kernel','status',$FakeSnapshot)                       -ExpectOutput ''
Invoke-Nnke -Label "nnke kernel status --json"     -CliArgs @('kernel','status',$FakeSnapshot,'--json')              -JsonShape
Invoke-Nnke -Label "nnke kernel history (bad)"     -CliArgs @('kernel','history',$FakeSnapshot)                      -ExpectOutput ''
Invoke-Nnke -Label "nnke kernel status missing"    -CliArgs @('kernel','status',(Join-Path $FixtureDir 'nope.yml'))  -ExpectOutput 'not found'

# ---- 15. mesh ---------------------------------------------------------------

Write-Header "mesh"
Invoke-Nnke -Label "nnke mesh --help"          -CliArgs @('mesh','--help')                                              -ExpectOutput 'status'
Invoke-Nnke -Label "nnke mesh status missing"  -CliArgs @('mesh','status',(Join-Path $FixtureDir 'nope.yml'))           -ExpectOutput 'not found'
Invoke-Nnke -Label "nnke mesh status --json"   -CliArgs @('mesh','status',(Join-Path $FixtureDir 'nope.yml'),'--json')  -JsonShape
Invoke-Nnke -Label "nnke mesh trace missing"   -CliArgs @('mesh','trace','test-cell',(Join-Path $FixtureDir 'nope.yml'))   -ExpectOutput 'not found'
Invoke-Nnke -Label "nnke mesh inspect missing"  -CliArgs @('mesh','inspect',(Join-Path $FixtureDir 'nope.yml'))         -ExpectOutput 'not found'
Invoke-Nnke -Label "nnke mesh lineage missing" -CliArgs @('mesh','lineage','test-cell',(Join-Path $FixtureDir 'nope.yml')) -ExpectOutput 'not found'

# ---- 16. serve / mcp-server (help only) ------------------------------------

Write-Header "serve / mcp-server (help only)"
Invoke-Nnke -Label "nnke serve --help"      -CliArgs @('serve','--help')      -ExpectOutput ''
Invoke-Nnke -Label "nnke mcp-server --help" -CliArgs @('mcp-server','--help') -ExpectOutput ''

# ---- Summary ----------------------------------------------------------------

Write-Host ""
Write-Host "  -----------------------------------------------------" -ForegroundColor DarkGray
Write-Host "  Results: " -NoNewline
Write-Host "$($Script:Passed) passed" -ForegroundColor Green -NoNewline
Write-Host "  " -NoNewline
if ($Script:Failed -gt 0) {
    Write-Host "$($Script:Failed) failed" -ForegroundColor Red -NoNewline
    Write-Host "  " -NoNewline
}
if ($Script:Skipped -gt 0) {
    Write-Host "$($Script:Skipped) skipped" -ForegroundColor DarkGray -NoNewline
}
Write-Host ""

if ($Script:Failed -gt 0) {
    Write-Host ""
    Write-Host "  Failed tests:" -ForegroundColor Red
    $Script:Results | Where-Object { $_.Status -eq 'FAIL' } | ForEach-Object {
        $d = if ($_.Detail) { " ($($_.Detail))" } else { '' }
        Write-Host "    - $($_.Label)$d" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "  Cleaning up $TmpDir ..." -ForegroundColor DarkGray
Remove-Item -Recurse -Force $TmpDir -ErrorAction SilentlyContinue

Write-Host ""
if ($Script:Failed -gt 0) {
    Write-Host "  FAILED ($($Script:Failed) test(s))" -ForegroundColor Red
    exit 1
} else {
    Write-Host "  All tests passed." -ForegroundColor Green
    exit 0
}
