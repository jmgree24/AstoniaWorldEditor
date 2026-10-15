param([switch]$Test, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework C# compiler is required.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot 'bin' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$references = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll', '/r:System.Web.Extensions.dll')
$references += "/resource:$(Join-Path $PSScriptRoot 'assets\art-categories.json'),Astonia.Editor.ArtCategories.json"
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /warn:4 "/win32icon:$(Join-Path $PSScriptRoot 'assets\editor.ico')" "/out:$(Join-Path $OutputDirectory 'AstoniaWorldEditor.exe')" @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Editor compilation failed.' }
Write-Output "Built $(Join-Path $OutputDirectory 'AstoniaWorldEditor.exe')"
if ($Test) {
    & $compiler /nologo /target:exe /platform:x64 /optimize+ /main:AutotileTests "/out:$(Join-Path $OutputDirectory 'AutotileTests.exe')" @references @sources (Join-Path $PSScriptRoot 'tests\AutotileTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Autotile test compilation failed.' }
    & (Join-Path $OutputDirectory 'AutotileTests.exe') (Join-Path (Split-Path $PSScriptRoot) 'Astonia_Windows_CPP_Port_Phase3')
    if ($LASTEXITCODE -ne 0) { throw 'Autotile autofill checks failed.' }
    $modelSources = @('MapDocument.cs','AssetCatalog.cs','EditorResources.cs','LegacyPreset.cs','AutotileSets.cs','SightPreview.cs','ConnectedTiles.cs','EditorPreferences.cs','TemplateStore.cs','ZoneSavePlan.cs','ArtCategories.cs','SpriteEnhancer.cs','SpriteEffects.cs') | ForEach-Object { Join-Path $PSScriptRoot "src\$_" }
    & $compiler /nologo /target:exe /platform:x64 /optimize+ "/out:$(Join-Path $OutputDirectory 'ResourceTests.exe')" @references @modelSources (Join-Path $PSScriptRoot 'tests\ResourceTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Resource test compilation failed.' }
    & (Join-Path $OutputDirectory 'ResourceTests.exe') (Join-Path (Split-Path $PSScriptRoot) 'Astonia_Windows_CPP_Port_Phase3')
    if ($LASTEXITCODE -ne 0) { throw 'Resource, preset or sight checks failed.' }
    & $compiler /nologo /target:exe /platform:x64 /optimize+ /main:ParityTests "/out:$(Join-Path $OutputDirectory 'ParityTests.exe')" @references @sources (Join-Path $PSScriptRoot 'tests\ParityTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Reference parity test compilation failed.' }
    & (Join-Path $OutputDirectory 'ParityTests.exe') (Join-Path (Split-Path $PSScriptRoot) 'Astonia_Windows_CPP_Port_Phase3') $PSScriptRoot
    if ($LASTEXITCODE -ne 0) { throw 'Reference parity, linked-zone or PNG checks failed.' }
    & $compiler /nologo /target:exe /platform:x64 /optimize+ "/out:$(Join-Path $OutputDirectory 'SpriteCoverageTests.exe')" @references @modelSources (Join-Path $PSScriptRoot 'tests\SpriteCoverageTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Sprite coverage test compilation failed.' }
    & (Join-Path $OutputDirectory 'SpriteCoverageTests.exe') (Join-Path (Split-Path $PSScriptRoot) 'Astonia_Windows_CPP_Port_Phase3')
    if ($LASTEXITCODE -ne 0) { throw 'Map sprite coverage checks failed.' }
    & $compiler /nologo /target:exe /platform:x64 /optimize+ "/out:$(Join-Path $OutputDirectory 'MapEditorTests.exe')" @references @modelSources (Join-Path $PSScriptRoot 'tests\MapTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & (Join-Path $OutputDirectory 'MapEditorTests.exe') (Join-Path (Split-Path $PSScriptRoot) 'Astonia_Windows_CPP_Port_Phase3\server\zones')
    if ($LASTEXITCODE -ne 0) { throw 'Map compatibility checks failed.' }
    & $compiler /nologo /target:exe /platform:x64 /optimize+ /main:CanvasTests "/out:$(Join-Path $OutputDirectory 'CanvasTests.exe')" @references @sources (Join-Path $PSScriptRoot 'tests\CanvasTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Canvas test compilation failed.' }
    & (Join-Path $OutputDirectory 'CanvasTests.exe') (Join-Path (Split-Path $PSScriptRoot) 'Astonia_Windows_CPP_Port_Phase3')
    if ($LASTEXITCODE -ne 0) { throw 'Canvas interaction checks failed.' }
    & $compiler /nologo /target:exe /platform:x64 /optimize+ /main:LayoutTests "/out:$(Join-Path $OutputDirectory 'LayoutTests.exe')" @references @sources (Join-Path $PSScriptRoot 'tests\LayoutTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Layout test compilation failed.' }
    & (Join-Path $OutputDirectory 'LayoutTests.exe') (Join-Path (Split-Path $PSScriptRoot) 'Astonia_Windows_CPP_Port_Phase3')
    if ($LASTEXITCODE -ne 0) { throw 'Layout checks failed.' }
}
