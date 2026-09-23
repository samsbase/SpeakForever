# Builds Speak Forever: publishes the app and the CLI into .\publish, then (with -Installer) the
# installer into .\dist. Both apps are self-contained: no .NET or Windows App SDK to install.
# -Repository owner/repo: where the app looks for updates (the GitHub Action passes its own repository).
param([switch]$Installer, [string]$Repository)
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot 'publish'
$repoArg = if ($Repository) { "-p:GitHubRepository=$Repository" } else { $null }
$version = ([xml](Get-Content "$PSScriptRoot\Directory.Build.props")).Project.PropertyGroup.Version | Where-Object { $_ }

# Nothing is published unless the tests pass.
dotnet test --project "$PSScriptRoot\tests\SpeakForever.Core.Tests" -c Release
if ($LASTEXITCODE) { exit $LASTEXITCODE }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish "$PSScriptRoot\app\Gui" -c Release -p:Platform=x64 -r win-x64 --self-contained -o $out $repoArg
if ($LASTEXITCODE) { exit $LASTEXITCODE }
dotnet publish "$PSScriptRoot\app\Cli" -c Release -r win-x64 --self-contained -o $out $repoArg
if ($LASTEXITCODE) { exit $LASTEXITCODE }

# The speech engine's packages carry builds for every platform; x64 Windows needs only its own.
foreach ($unused in 'runtimes\vulkan\linux-x64', 'runtimes\win-arm64', 'runtimes\win-x86') {
    $path = Join-Path $out $unused
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}

Write-Host "`nPublished Speak Forever $version to $out"
Write-Host "  SpeakForever.exe      the app"
Write-Host "  SpeakForeverCli.exe   headless mode and setup checks"

if ($Installer) {
    $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw 'Inno Setup 6 is needed for the installer: winget install JRSoftware.InnoSetup' }

    # Microsoft's Visual C++ runtime installer isn't kept in the repo; fetch it once and check it's Microsoft-signed.
    $redist = "$PSScriptRoot\installer\redist\vc_redist.x64.exe"
    if (-not (Test-Path $redist)) {
        New-Item -ItemType Directory -Force (Split-Path $redist) | Out-Null
        Invoke-WebRequest 'https://aka.ms/vs/17/release/vc_redist.x64.exe' -OutFile $redist
    }
    $signature = Get-AuthenticodeSignature $redist
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
        Remove-Item $redist
        throw "vc_redist.x64.exe isn't validly signed by Microsoft ($($signature.Status)); deleted it."
    }
    & $iscc "/DAppVersion=$version" "$PSScriptRoot\installer\SpeakForever.iss"
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    Write-Host "`nInstaller: $(Join-Path $PSScriptRoot "dist\SpeakForever-Setup-$version.exe")"
}
