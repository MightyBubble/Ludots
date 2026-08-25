Set-StrictMode -Version Latest

function Get-DotnetCommand {
    $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    if (-not [string]::IsNullOrWhiteSpace($localAppData)) {
        $bundledDotnet = Join-Path $localAppData 'X28L\sdk\Game\Plugins\UnrealMono\dotnet\sdk\dotnet.bat'
        if (Test-Path $bundledDotnet) {
            return $bundledDotnet
        }
    }

    return 'dotnet'
}

function Get-ProjectTargetFramework {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath
    )

    # 跟随 csproj 声明的 TFM，避免硬编码；多目标取第一个
    $match = Select-String -Path $ProjectPath -Pattern '<TargetFrameworks?>\s*([^<]+)' |
        Select-Object -First 1
    if ($match -and $match.Matches.Count -gt 0) {
        return ($match.Matches[0].Groups[1].Value.Trim() -split ';' | Select-Object -First 1)
    }

    throw "Cannot resolve TargetFramework from project: $ProjectPath"
}

function Get-DotnetProjectDllPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,
        [string]$Configuration = 'Release'
    )

    $projectDir = Split-Path -Parent $ProjectPath
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
    $targetFramework = Get-ProjectTargetFramework -ProjectPath $ProjectPath
    return Join-Path $projectDir "bin\$Configuration\$targetFramework\$projectName.dll"
}

function Build-DotnetProject {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,
        [string]$Configuration = 'Release'
    )

    $dotnet = Get-DotnetCommand
    & $dotnet build $ProjectPath -c $Configuration -nologo '-clp:ErrorsOnly' | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed: $ProjectPath"
    }

    $dllPath = Get-DotnetProjectDllPath -ProjectPath $ProjectPath -Configuration $Configuration
    if (-not (Test-Path $dllPath)) {
        throw "Project output dll not found: $dllPath"
    }

    return [pscustomobject]@{
        Dotnet = $dotnet
        DllPath = $dllPath
    }
}

function Invoke-DotnetProject {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,
        [string[]]$Arguments = @(),
        [string]$Configuration = 'Release'
    )

    $build = Build-DotnetProject -ProjectPath $ProjectPath -WorkingDirectory $WorkingDirectory -Configuration $Configuration
    & $build.Dotnet exec --roll-forward Major $build.DllPath @Arguments | Out-Host
    return $LASTEXITCODE
}

function Start-DotnetProject {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,
        [string[]]$Arguments = @(),
        [string]$Configuration = 'Release',
        [switch]$PassThru,
        [string]$WindowStyle,
        [string]$RedirectStandardOutput,
        [string]$RedirectStandardError
    )

    $build = Build-DotnetProject -ProjectPath $ProjectPath -WorkingDirectory $WorkingDirectory -Configuration $Configuration
    $startArgs = @{
        FilePath = $build.Dotnet
        WorkingDirectory = $WorkingDirectory
        ArgumentList = @('exec', '--roll-forward', 'Major', $build.DllPath) + $Arguments
    }

    if ($PassThru) {
        $startArgs.PassThru = $true
    }

    if ($PSBoundParameters.ContainsKey('WindowStyle') -and -not [string]::IsNullOrWhiteSpace($WindowStyle)) {
        $startArgs.WindowStyle = $WindowStyle
    }

    if ($PSBoundParameters.ContainsKey('RedirectStandardOutput') -and -not [string]::IsNullOrWhiteSpace($RedirectStandardOutput)) {
        $startArgs.RedirectStandardOutput = $RedirectStandardOutput
    }

    if ($PSBoundParameters.ContainsKey('RedirectStandardError') -and -not [string]::IsNullOrWhiteSpace($RedirectStandardError)) {
        $startArgs.RedirectStandardError = $RedirectStandardError
    }

    return Start-Process @startArgs
}
