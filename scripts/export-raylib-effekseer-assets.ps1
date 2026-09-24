param(
    [string[]]$EmitterIds = @(),
    [ValidateSet("Write", "Check")]
    [string]$Mode = "Write"
)

$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
$authoringPath = Join-Path $repo "mods/showcases/performer_raylib_micro_showcases/PerformerRaylibMicroShowcasesMod/assets/Presentation/Authoring/raylib-micro-showcases.authoring.json"
$assetRoot = Join-Path $repo "mods/showcases/performer_raylib_micro_showcases/PerformerRaylibMicroShowcasesMod/assets/Presentation/Effekseer"
$tool = Join-Path $repo "output/external/Effekseer1.80.6Win/Effekseer1.80.6Win/Tool/bin/Effekseer.exe"
$nativeBuild = Join-Path $repo "src/Build/build-effekseer-native.ps1"
$nativeRoot = Join-Path $repo "src/Libraries/Effekseer/runtimes/win-x64/native"
$nativeLibrary = Join-Path $nativeRoot "ludots_effekseer.dll"
$generator = Join-Path $repo "scripts/generate-raylib-micro-assets.mjs"
$projectGenerator = Join-Path $repo "scripts/generate-raylib-effekseer-projects.mjs"
$textureGenerator = Join-Path $repo "scripts/generate-raylib-effekseer-signature-textures.ps1"
$stagingParent = Join-Path $repo "output/effekseer-export"
$stagingRoot = Join-Path $stagingParent ([guid]::NewGuid().ToString("N"))

foreach ($required in @($authoringPath, $assetRoot, $tool, $nativeBuild, $generator, $projectGenerator, $textureGenerator)) {
    if (!(Test-Path -LiteralPath $required)) {
        throw "Required Effekseer export input does not exist: $required"
    }
}

& $textureGenerator -Mode $Mode

if ($Mode -eq "Check") {
    & node $projectGenerator --authoring $authoringPath --check
} else {
    & node $projectGenerator --authoring $authoringPath
}
if ($LASTEXITCODE -ne 0) {
    throw "Effekseer project source $($Mode.ToLowerInvariant()) failed with exit code $LASTEXITCODE."
}

$authoring = Get-Content -LiteralPath $authoringPath -Raw -Encoding UTF8 | ConvertFrom-Json
$EmitterIds = @($EmitterIds | ForEach-Object { $_ -split "," } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($EmitterIds.Count -gt 0) {
    $emitters = @($authoring.emitters | Where-Object { $EmitterIds -contains [string]$_.id })
    $missing = @($EmitterIds | Where-Object { $authoring.emitters.id -notcontains $_ })
    if ($missing.Count -gt 0) {
        throw "Unknown emitter id(s): $($missing -join ', ')"
    }
} else {
    $emitters = @($authoring.emitters)
}

if ($emitters.Count -eq 0) {
    throw "No emitter assets were selected for export."
}

$nodeTypeByAssetKind = @{
    SpriteEmitter = 2
    RibbonEmitter = 3
    RingEmitter = 4
    ModelEmitter = 5
    TrackEmitter = 6
}

foreach ($emitter in $emitters) {
    if (!$nodeTypeByAssetKind.ContainsKey([string]$emitter.assetKind)) {
        throw "Emitter '$($emitter.id)' declares unsupported concrete assetKind '$($emitter.assetKind)'."
    }
    if ([IO.Path]::GetFileName([string]$emitter.sourceFile) -ne [string]$emitter.sourceFile) {
        throw "Emitter '$($emitter.id)' sourceFile must be a basename."
    }
}

& $nativeBuild
if ($LASTEXITCODE -ne 0) {
    throw "Effekseer native validator build failed with exit code $LASTEXITCODE."
}
if (!(Test-Path -LiteralPath $nativeLibrary)) {
    throw "Effekseer native validator was not produced: $nativeLibrary"
}

if (!("LudotsEffekseerAssetValidator" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class LudotsEffekseerAssetValidator
{
    [DllImport("ludots_effekseer.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ludots_effekseer_validate_asset")]
    private static extern int ValidateAsset(IntPtr pathUtf8, int expectedNodeType);

    [DllImport("ludots_effekseer.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ludots_effekseer_get_last_error")]
    private static extern IntPtr GetLastError(IntPtr context);

    public static string Validate(string fullPath, int expectedNodeType)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(fullPath + "\0");
        GCHandle pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            if (ValidateAsset(pin.AddrOfPinnedObject(), expectedNodeType) != 0)
            {
                return null;
            }
            IntPtr error = GetLastError(IntPtr.Zero);
            return error == IntPtr.Zero ? "Native validator returned no error text." : Marshal.PtrToStringAnsi(error);
        }
        finally
        {
            pin.Free();
        }
    }
}
"@
}

$assetRoot = (Resolve-Path -LiteralPath $assetRoot).Path
$stagingParent = [IO.Path]::GetFullPath($stagingParent)
$stagingRoot = [IO.Path]::GetFullPath($stagingRoot)
if (![IO.Path]::GetDirectoryName($stagingRoot).Equals($stagingParent, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use an export staging directory outside the expected parent: $stagingRoot"
}
[IO.Directory]::CreateDirectory($stagingRoot) | Out-Null
foreach ($resourceDirectoryName in @("textures", "models")) {
    $resourceDirectory = Join-Path $assetRoot $resourceDirectoryName
    if (Test-Path -LiteralPath $resourceDirectory) {
        Copy-Item -LiteralPath $resourceDirectory -Destination $stagingRoot -Recurse
    }
}
foreach ($emitter in $emitters) {
    $projectFile = [IO.Path]::ChangeExtension([string]$emitter.sourceFile, ".efkproj")
    $projectPath = Join-Path $assetRoot $projectFile
    if (!(Test-Path -LiteralPath $projectPath)) {
        throw "Effekseer authoring source does not exist for '$($emitter.id)': $projectPath"
    }
    Copy-Item -LiteralPath $projectPath -Destination (Join-Path $stagingRoot $projectFile)
}

$priorPath = $env:PATH
$env:PATH = "$nativeRoot;$priorPath"
try {
    foreach ($emitter in $emitters) {
        $sourceFile = [string]$emitter.sourceFile
        $projectFile = [IO.Path]::ChangeExtension($sourceFile, ".efkproj")
        $projectPath = Join-Path $stagingRoot $projectFile
        if (!(Test-Path -LiteralPath $projectPath)) {
            throw "Effekseer authoring source does not exist for '$($emitter.id)': $projectPath"
        }

        $temporaryPath = Join-Path $stagingRoot $sourceFile
        Write-Host "== Exporting $($emitter.id) as $($emitter.assetKind)"

        $process = Start-Process -FilePath $tool `
            -WorkingDirectory $stagingRoot `
            -ArgumentList @("-cui", "-in", $projectFile, "-o", $temporaryPath) `
            -PassThru `
            -WindowStyle Hidden
        if (!$process.WaitForExit(60000)) {
            $process | Stop-Process -Force
            throw "Effekseer export timed out for '$($emitter.id)'."
        }
        if ($process.ExitCode -ne 0) {
            throw "Effekseer export failed for '$($emitter.id)' with exit code $($process.ExitCode)."
        }

        $priorLength = -1L
        $stableChecks = 0
        for ($attempt = 0; $attempt -lt 50 -and $stableChecks -lt 3; $attempt++) {
            if (Test-Path -LiteralPath $temporaryPath) {
                $length = (Get-Item -LiteralPath $temporaryPath).Length
                if ($length -gt 0 -and $length -eq $priorLength) { $stableChecks++ } else { $stableChecks = 0 }
                $priorLength = $length
            }
            Start-Sleep -Milliseconds 100
        }
        if ($stableChecks -lt 3) {
            throw "Effekseer output did not stabilize for '$($emitter.id)'."
        }

        $validationError = [LudotsEffekseerAssetValidator]::Validate(
            $temporaryPath,
            [int]$nodeTypeByAssetKind[[string]$emitter.assetKind])
        if ($null -ne $validationError) {
            throw "Native validation failed for '$($emitter.id)': $validationError"
        }
    }

    if ($Mode -eq "Check") {
        foreach ($emitter in $emitters) {
            $sourceFile = [string]$emitter.sourceFile
            $stagedPath = Join-Path $stagingRoot $sourceFile
            $publishedPath = Join-Path $assetRoot $sourceFile
            if (!(Test-Path -LiteralPath $publishedPath)) {
                throw "Published Effekseer asset does not exist for '$($emitter.id)': $publishedPath"
            }
            $stagedHash = (Get-FileHash -LiteralPath $stagedPath -Algorithm SHA256).Hash
            $publishedHash = (Get-FileHash -LiteralPath $publishedPath -Algorithm SHA256).Hash
            if ($stagedHash -ne $publishedHash) {
                throw "Effekseer source/output drift for '$($emitter.id)': staged=$stagedHash published=$publishedHash"
            }
        }
        & node $generator --authoring $authoringPath --validate-only
        if ($LASTEXITCODE -ne 0) { throw "Authoring validation failed with exit code $LASTEXITCODE." }
    } else {
        $authoringBackup = Join-Path $stagingRoot "authoring.backup.json"
        Copy-Item -LiteralPath $authoringPath -Destination $authoringBackup
        $publishedBackups = @{}
        foreach ($emitter in $emitters) {
            $sourceFile = [string]$emitter.sourceFile
            $publishedPath = Join-Path $assetRoot $sourceFile
            if (Test-Path -LiteralPath $publishedPath) {
                $backupPath = Join-Path $stagingRoot "backup-$sourceFile"
                Copy-Item -LiteralPath $publishedPath -Destination $backupPath
                $publishedBackups[$sourceFile] = $backupPath
            }
        }

        try {
            foreach ($emitter in $emitters) {
                $sourceFile = [string]$emitter.sourceFile
                Copy-Item -LiteralPath (Join-Path $stagingRoot $sourceFile) -Destination (Join-Path $assetRoot $sourceFile) -Force
            }
            & node $generator --authoring $authoringPath --refresh-emitter-hashes --validate-only
            if ($LASTEXITCODE -ne 0) { throw "Authoring hash refresh failed with exit code $LASTEXITCODE." }
            & node $generator --authoring $authoringPath
            if ($LASTEXITCODE -ne 0) { throw "Raylib micro showcase generation failed with exit code $LASTEXITCODE." }
        } catch {
            Copy-Item -LiteralPath $authoringBackup -Destination $authoringPath -Force
            foreach ($emitter in $emitters) {
                $sourceFile = [string]$emitter.sourceFile
                $publishedPath = Join-Path $assetRoot $sourceFile
                if ($publishedBackups.ContainsKey($sourceFile)) {
                    Copy-Item -LiteralPath $publishedBackups[$sourceFile] -Destination $publishedPath -Force
                } elseif (Test-Path -LiteralPath $publishedPath) {
                    Remove-Item -LiteralPath $publishedPath -Force
                }
            }
            throw
        }
    }
} finally {
    $env:PATH = $priorPath
    if ([IO.Directory]::Exists($stagingRoot)) {
        if (![IO.Path]::GetDirectoryName($stagingRoot).Equals($stagingParent, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to delete export staging outside the expected parent: $stagingRoot"
        }
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}

Write-Host "$Mode completed for $($emitters.Count) concrete Effekseer emitter asset(s)."
