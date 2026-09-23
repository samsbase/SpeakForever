# Builds Voice Forever: publishes the app and the CLI into .\publish, then (with -Installer) the
# installer into .\dist. Both apps are self-contained: no .NET or Windows App SDK to install.
param([switch]$Installer)
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot 'publish'
$version = ([xml](Get-Content "$PSScriptRoot\Directory.Build.props")).Project.PropertyGroup.Version | Where-Object { $_ }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish "$PSScriptRoot\app\Gui" -c Release -p:Platform=x64 -r win-x64 --self-contained -o $out
if ($LASTEXITCODE) { exit $LASTEXITCODE }
dotnet publish "$PSScriptRoot\app\Cli" -c Release -r win-x64 --self-contained -o $out
if ($LASTEXITCODE) { exit $LASTEXITCODE }

# The speech engine's packages carry builds for every platform; x64 Windows needs only its own.
foreach ($unused in 'runtimes\vulkan\linux-x64', 'runtimes\win-arm64', 'runtimes\win-x86') {
    $path = Join-Path $out $unused
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}

Write-Host "`nPublished Voice Forever $version to $out"
Write-Host "  VoiceForever.exe      the app"
Write-Host "  VoiceForeverCli.exe   headless mode and setup checks"

if ($Installer) {
    $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw 'Inno Setup 6 is needed for the installer: winget install JRSoftware.InnoSetup' }
    & $iscc "/DAppVersion=$version" "$PSScriptRoot\installer\VoiceForever.iss"
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    Write-Host "`nInstaller: $(Join-Path $PSScriptRoot "dist\VoiceForever-Setup-$version.exe")"
}
