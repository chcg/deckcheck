# Package the Notepad++ plugin: editors\notepadpp\out\deckcheck-npp-x64.zip with deckcheck.dll at the zip root (the
# Plugins Admin validator looks for <folder-name>.dll there), DeckcheckNpp.dll, engine\ (the frozen engine) and the
# licence. Prints the zip's size and sha256 (the plugin list's "id").
#   powershell -ExecutionPolicy Bypass -File tools\npp\package.ps1 -Version 0.1.0
param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = "Stop"
$repo = Resolve-Path "$PSScriptRoot\..\.."
Push-Location $repo
try {
    powershell -ExecutionPolicy Bypass -File editors\notepadpp\build.ps1 -Version $Version | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "plugin build failed" }
    if (-not (Test-Path build-engine\dist\deckcheck\deckcheck.exe)) { throw "build the engine first: tools\freeze\build_engine.ps1" }
    $stage = "editors\notepadpp\out\stage"
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
    New-Item -ItemType Directory $stage | Out-Null
    Copy-Item editors\notepadpp\out\deckcheck\* $stage
    Copy-Item -Recurse build-engine\dist\deckcheck "$stage\engine"
    Copy-Item LICENSE "$stage\LICENSE.txt"
    $zip = "editors\notepadpp\out\deckcheck-npp-x64.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path "$stage\*" -DestinationPath $zip
    $item = Get-Item $zip
    "{0}  {1:N1} MB  sha256 {2}" -f $item.Name, ($item.Length / 1MB), (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
} finally { Pop-Location }
