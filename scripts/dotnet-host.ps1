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

function Get-DotnetProjectDllPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,
        [string]$Configuration = 'Release'
    )

    $projectDir = Split-Path -Parent $ProjectPath
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
    return Join-Path $projectDir "bin\$Configuration\net8.0\$projectName.dll"
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
