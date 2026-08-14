function Get-FloatMateDotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $candidates = @(
        (Join-Path $RepositoryRoot ".tools\dotnet\dotnet.exe"),
        (Join-Path $env:LOCALAPPDATA "FloatMateDev\dotnet\dotnet.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    return $null
}

function Test-FloatMateDotNet8Sdk {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DotNetPath
    )

    $sdks = & $DotNetPath --list-sdks 2>$null
    return [bool]($sdks | Where-Object { $_ -match '^8\.' })
}
