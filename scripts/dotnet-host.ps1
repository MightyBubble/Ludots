Set-StrictMode -Version Latest

function Repair-WindowsProcessEnvironment {
    # Git Bash 启动 Windows PowerShell 时会把 TEMP/HOME 换成 /tmp、/home/...，并可能不带 PATHEXT。
    if ($env:OS -ne 'Windows_NT') {
        return
    }

    if ([string]::IsNullOrWhiteSpace($env:PATHEXT)) {
        $env:PATHEXT = '.COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC'
    }

    $tempPoisoned = (Test-PosixProcessPath $env:TEMP) -or (Test-PosixProcessPath $env:TMP)
    if ($tempPoisoned) {
        $temp = [Environment]::GetEnvironmentVariable('TEMP', 'User')
        if (-not (Test-WindowsProcessPath $temp)) {
            $temp = [Environment]::GetEnvironmentVariable('TEMP', 'Machine')
        }

        if (-not (Test-WindowsProcessPath $temp)) {
            $local = [Environment]::GetFolderPath('LocalApplicationData')
            if ([string]::IsNullOrWhiteSpace($local)) {
                throw 'Windows TEMP/TMP are POSIX paths and no Windows temp directory is available.'
            }

            $temp = Join-Path $local 'Temp'
        }

        $env:TEMP = $temp
        $env:TMP = $temp
    }

    if (Test-PosixProcessPath $env:HOME) {
        $profile = [Environment]::GetFolderPath('UserProfile')
        if ([string]::IsNullOrWhiteSpace($profile) -or (Test-PosixProcessPath $profile)) {
            throw 'Windows HOME is a POSIX path and UserProfile is unavailable.'
        }

        $env:HOME = $profile
    }
}

function Test-PosixProcessPath {
    param([string]$Value)
    return -not [string]::IsNullOrWhiteSpace($Value) -and ($Value.StartsWith('/') -or $Value.StartsWith('~'))
}

function Test-WindowsProcessPath {
    param([string]$Value)
    return -not [string]::IsNullOrWhiteSpace($Value) -and -not (Test-PosixProcessPath $Value)
}

Repair-WindowsProcessEnvironment

function Get-DotnetCommand {
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
