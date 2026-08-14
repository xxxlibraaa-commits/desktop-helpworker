param(
    [switch]$NoDesktopShortcut,
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
$repositoryRoot = $PSScriptRoot
. (Join-Path $repositoryRoot "scripts\Common.ps1")

if ($env:OS -ne "Windows_NT") {
    throw "FloatMate currently supports Windows only."
}

$dotnet = Get-FloatMateDotNet -RepositoryRoot $repositoryRoot
$hasDotNet8 = $dotnet -and (Test-FloatMateDotNet8Sdk -DotNetPath $dotnet)

if (-not $hasDotNet8) {
    $installDirectory = Join-Path $repositoryRoot ".tools\dotnet"
    $installer = Join-Path $env:TEMP "floatmate-dotnet-install.ps1"

    Write-Host ".NET 8 SDK was not found. Downloading the official Microsoft installer..."
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest "https://dot.net/v1/dotnet-install.ps1" -OutFile $installer

    try {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer `
            -Channel 8.0 `
            -Quality GA `
            -InstallDir $installDirectory `
            -NoPath
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    finally {
        if (Test-Path -LiteralPath $installer) {
            Remove-Item -LiteralPath $installer -Force
        }
    }

    $dotnet = Join-Path $installDirectory "dotnet.exe"
}

Write-Host "Using SDK: $dotnet"
& (Join-Path $repositoryRoot "build.ps1") -FrameworkDependent:$FrameworkDependent
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $NoDesktopShortcut) {
    $desktop = [Environment]::GetFolderPath("Desktop")
    $shortcutPath = Join-Path $desktop "FloatMate.lnk"
    $launcher = Join-Path $repositoryRoot "start-floatmate.cmd"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $launcher
    $shortcut.WorkingDirectory = $repositoryRoot
    $shortcut.Description = "Start the FloatMate Windows desktop assistant"
    $shortcut.Save()
    Write-Host "Desktop shortcut created: $shortcutPath"
}

Write-Host ""
Write-Host "FloatMate is ready. Run start-floatmate.cmd to launch it." -ForegroundColor Green
