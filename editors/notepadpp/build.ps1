# Build the Notepad++ plugin with tools that ship with Windows (.NET Framework csc and ilasm); nothing to install.
#   powershell -ExecutionPolicy Bypass -File editors\notepadpp\build.ps1 [-Version 0.1.0] [-Test]
# Output: editors\notepadpp\out\deckcheck\{deckcheck.dll, DeckcheckNpp.dll}. -Test also builds and runs the unit tests.
param([string]$Version = "0.1.0", [switch]$Test)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Resolve-Path "$here\..\.."
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$out = "$here\out"
$plugin = "$out\deckcheck"
if (-not ($Version -match '^\d+(\.\d+){0,3}$')) { throw "bad version $Version" }
$parts = @($Version.Split(".")) + @("0", "0", "0", "0") | Select-Object -First 4
$v4 = $parts -join "."

if (Test-Path $plugin) { Remove-Item -Recurse -Force $plugin }
New-Item -ItemType Directory -Force $plugin | Out-Null

function Check($what) { if ($LASTEXITCODE -ne 0) { throw "$what failed ($LASTEXITCODE)" } }

Set-Content -Encoding ASCII "$out\Version.g.cs" "[assembly: System.Reflection.AssemblyVersion(""$v4"")]"
$refs = "/r:System.Windows.Forms.dll", "/r:System.Drawing.dll", "/r:System.Web.Extensions.dll"
$src = @(Get-ChildItem "$here\src\*.cs" | ForEach-Object { $_.FullName }) + "$out\Version.g.cs"
& "$fw\csc.exe" /nologo /target:library /platform:x64 /optimize /warnaserror- "/out:$plugin\DeckcheckNpp.dll" $refs $src
Check "csc"

Push-Location $repo
try { python -m tools.npp.version_res "$out\version.res" $v4 deckcheck; Check "version_res" } finally { Pop-Location }
$il = (Get-Content -Raw "$here\src\Exports.il").Replace("@VERSION@", $v4.Replace(".", ":"))
Set-Content -Encoding UTF8 "$out\Exports.il" $il
& "$fw\ilasm.exe" "$out\Exports.il" /DLL /PE64 /X64 /QUIET "/RESOURCE=$out\version.res" "/OUTPUT=$plugin\deckcheck.dll"
Check "ilasm"

if ($Test) {
    $tests = "$out\tests"
    New-Item -ItemType Directory -Force $tests | Out-Null
    & "$fw\csc.exe" /nologo /target:exe /platform:x64 "/out:$tests\FakeEngine.exe" "$here\tests\FakeEngine.cs"
    Check "csc FakeEngine"
    Copy-Item "$plugin\DeckcheckNpp.dll" $tests
    & "$fw\csc.exe" /nologo /target:exe /platform:x64 "/out:$tests\Run.exe" "/r:$tests\DeckcheckNpp.dll" $refs "$here\tests\Run.cs"
    Check "csc Run"
    Push-Location $repo
    try { & "$tests\Run.exe" "$here\tests\data" "$tests\FakeEngine.exe"; Check "unit tests" } finally { Pop-Location }
}
Get-ChildItem $plugin | Select-Object Name, Length
