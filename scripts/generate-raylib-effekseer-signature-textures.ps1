param(
    [ValidateSet("Write", "Check")]
    [string]$Mode = "Write"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$repo = Split-Path -Parent $PSScriptRoot
$assetRoot = Join-Path $repo "mods/showcases/performer_raylib_micro_showcases/PerformerRaylibMicroShowcasesMod/assets/Presentation/Effekseer"
$publishedRoot = Join-Path $assetRoot "textures/signatures"
$temporaryParent = Join-Path $repo "output/effekseer-signature-textures"
$temporaryRoot = Join-Path $temporaryParent ([guid]::NewGuid().ToString("N"))
$outputRoot = if ($Mode -eq "Write") { $publishedRoot } else { $temporaryRoot }

$textureNames = @(
    "semantic_explosion",
    "semantic_lightning",
    "semantic_slash",
    "semantic_shield",
    "semantic_heal",
    "semantic_burn",
    "semantic_poison",
    "semantic_frost",
    "semantic_target_lock",
    "semantic_warning_cone",
    "semantic_buff",
    "semantic_debuff_warning",
    "semantic_stun",
    "semantic_silence",
    "semantic_channeling",
    "semantic_interact_pickup"
)

function New-Pen([int]$Alpha, [float]$Width) {
    $pen = [Drawing.Pen]::new([Drawing.Color]::FromArgb($Alpha, 255, 255, 255), $Width)
    $pen.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    return $pen
}

function Draw-Lines($Graphics, [Drawing.PointF[]]$Points, [bool]$Closed = $false, [float]$Width = 8) {
    foreach ($layer in @(@{ A = 38; W = $Width + 14 }, @{ A = 90; W = $Width + 6 }, @{ A = 245; W = $Width })) {
        $pen = New-Pen $layer.A $layer.W
        try {
            if ($Closed) { $Graphics.DrawPolygon($pen, $Points) } else { $Graphics.DrawLines($pen, $Points) }
        } finally {
            $pen.Dispose()
        }
    }
}

function Draw-Ellipse($Graphics, [float]$X, [float]$Y, [float]$Width, [float]$Height, [float]$Stroke = 7) {
    foreach ($layer in @(@{ A = 38; W = $Stroke + 14 }, @{ A = 90; W = $Stroke + 6 }, @{ A = 245; W = $Stroke })) {
        $pen = New-Pen $layer.A $layer.W
        try { $Graphics.DrawEllipse($pen, $X, $Y, $Width, $Height) } finally { $pen.Dispose() }
    }
}

function Fill-Polygon($Graphics, [Drawing.PointF[]]$Points) {
    $glow = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(70, 255, 255, 255))
    $core = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(225, 255, 255, 255))
    try {
        $Graphics.FillPolygon($glow, $Points)
        $Graphics.FillPolygon($core, $Points)
    } finally {
        $glow.Dispose()
        $core.Dispose()
    }
}

function Draw-Texture([string]$Name, [string]$Path) {
    $bitmap = [Drawing.Bitmap]::new(128, 128, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        switch ($Name) {
            "semantic_explosion" {
                foreach ($angle in 0, 45, 90, 135, 180, 225, 270, 315) {
                    $radians = $angle * [Math]::PI / 180
                    $points = [Drawing.PointF[]]@(
                        [Drawing.PointF]::new(64 + 22 * [Math]::Cos($radians), 64 + 22 * [Math]::Sin($radians)),
                        [Drawing.PointF]::new(64 + 51 * [Math]::Cos($radians), 64 + 51 * [Math]::Sin($radians)))
                    Draw-Lines $graphics $points $false 6
                }
                Draw-Ellipse $graphics 43 43 42 42 9
            }
            "semantic_lightning" {
                Fill-Polygon $graphics ([Drawing.PointF[]]@(
                    [Drawing.PointF]::new(72, 10), [Drawing.PointF]::new(39, 65), [Drawing.PointF]::new(59, 65),
                    [Drawing.PointF]::new(48, 116), [Drawing.PointF]::new(91, 53), [Drawing.PointF]::new(69, 53)))
            }
            "semantic_slash" {
                foreach ($layer in @(@{ A = 42; W = 23 }, @{ A = 105; W = 14 }, @{ A = 250; W = 7 })) {
                    $pen = New-Pen $layer.A $layer.W
                    try { $graphics.DrawArc($pen, 18, 14, 98, 98, 205, 112) } finally { $pen.Dispose() }
                }
            }
            "semantic_shield" {
                Draw-Lines $graphics ([Drawing.PointF[]]@(
                    [Drawing.PointF]::new(64, 12), [Drawing.PointF]::new(103, 27), [Drawing.PointF]::new(96, 78),
                    [Drawing.PointF]::new(64, 114), [Drawing.PointF]::new(32, 78), [Drawing.PointF]::new(25, 27))) $true 7
            }
            "semantic_heal" {
                Draw-Lines $graphics ([Drawing.PointF[]]@([Drawing.PointF]::new(64, 22), [Drawing.PointF]::new(64, 106))) $false 13
                Draw-Lines $graphics ([Drawing.PointF[]]@([Drawing.PointF]::new(22, 64), [Drawing.PointF]::new(106, 64))) $false 13
            }
            "semantic_burn" {
                Fill-Polygon $graphics ([Drawing.PointF[]]@(
                    [Drawing.PointF]::new(67, 9), [Drawing.PointF]::new(83, 42), [Drawing.PointF]::new(79, 59),
                    [Drawing.PointF]::new(96, 47), [Drawing.PointF]::new(104, 78), [Drawing.PointF]::new(88, 108),
                    [Drawing.PointF]::new(61, 118), [Drawing.PointF]::new(29, 101), [Drawing.PointF]::new(22, 70),
                    [Drawing.PointF]::new(43, 38), [Drawing.PointF]::new(47, 70), [Drawing.PointF]::new(63, 55)))
            }
            "semantic_poison" {
                Draw-Ellipse $graphics 18 55 42 42 8
                Draw-Ellipse $graphics 56 35 54 54 8
                Draw-Ellipse $graphics 46 79 35 35 7
                Draw-Ellipse $graphics 25 19 20 20 5
            }
            "semantic_frost" {
                foreach ($angle in 0, 60, 120) {
                    $radians = $angle * [Math]::PI / 180
                    $dx = 47 * [Math]::Cos($radians)
                    $dy = 47 * [Math]::Sin($radians)
                    Draw-Lines $graphics ([Drawing.PointF[]]@([Drawing.PointF]::new(64 - $dx, 64 - $dy), [Drawing.PointF]::new(64 + $dx, 64 + $dy))) $false 5
                }
                Draw-Ellipse $graphics 55 55 18 18 4
            }
            "semantic_target_lock" {
                Draw-Ellipse $graphics 39 39 50 50 6
                foreach ($points in @(
                    @([Drawing.PointF]::new(18, 45), [Drawing.PointF]::new(18, 18), [Drawing.PointF]::new(45, 18)),
                    @([Drawing.PointF]::new(83, 18), [Drawing.PointF]::new(110, 18), [Drawing.PointF]::new(110, 45)),
                    @([Drawing.PointF]::new(110, 83), [Drawing.PointF]::new(110, 110), [Drawing.PointF]::new(83, 110)),
                    @([Drawing.PointF]::new(45, 110), [Drawing.PointF]::new(18, 110), [Drawing.PointF]::new(18, 83)))) {
                    Draw-Lines $graphics ([Drawing.PointF[]]$points) $false 6
                }
            }
            "semantic_warning_cone" {
                Fill-Polygon $graphics ([Drawing.PointF[]]@([Drawing.PointF]::new(64, 102), [Drawing.PointF]::new(17, 28), [Drawing.PointF]::new(111, 28)))
                Draw-Ellipse $graphics 56 94 16 16 4
            }
            "semantic_buff" {
                Draw-Lines $graphics ([Drawing.PointF[]]@([Drawing.PointF]::new(25, 82), [Drawing.PointF]::new(64, 43), [Drawing.PointF]::new(103, 82))) $false 10
                Draw-Lines $graphics ([Drawing.PointF[]]@([Drawing.PointF]::new(25, 108), [Drawing.PointF]::new(64, 69), [Drawing.PointF]::new(103, 108))) $false 10
            }
            "semantic_debuff_warning" {
                Draw-Lines $graphics ([Drawing.PointF[]]@([Drawing.PointF]::new(25, 25), [Drawing.PointF]::new(103, 103))) $false 11
                Draw-Lines $graphics ([Drawing.PointF[]]@([Drawing.PointF]::new(103, 25), [Drawing.PointF]::new(25, 103))) $false 11
            }
            "semantic_stun" {
                foreach ($angle in 0, 45, 90, 135, 180, 225, 270, 315) {
                    $radians = $angle * [Math]::PI / 180
                    $x = 64 + 42 * [Math]::Cos($radians)
                    $y = 64 + 42 * [Math]::Sin($radians)
                    Draw-Ellipse $graphics ($x - 5) ($y - 5) 10 10 5
                }
                Draw-Ellipse $graphics 43 43 42 42 5
            }
            "semantic_silence" {
                Draw-Ellipse $graphics 18 18 92 92 8
                Draw-Lines $graphics ([Drawing.PointF[]]@([Drawing.PointF]::new(30, 98), [Drawing.PointF]::new(98, 30))) $false 10
            }
            "semantic_channeling" {
                Draw-Ellipse $graphics 24 24 80 80 7
                Fill-Polygon $graphics ([Drawing.PointF[]]@([Drawing.PointF]::new(64, 38), [Drawing.PointF]::new(82, 64), [Drawing.PointF]::new(64, 90), [Drawing.PointF]::new(46, 64)))
            }
            "semantic_interact_pickup" {
                Draw-Lines $graphics ([Drawing.PointF[]]@([Drawing.PointF]::new(64, 16), [Drawing.PointF]::new(64, 82))) $false 10
                Fill-Polygon $graphics ([Drawing.PointF[]]@([Drawing.PointF]::new(31, 69), [Drawing.PointF]::new(64, 111), [Drawing.PointF]::new(97, 69)))
            }
            default { throw "Unknown signature texture: $Name" }
        }

        $directory = [IO.Path]::GetDirectoryName($Path)
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
try {
    foreach ($name in $textureNames) {
        Draw-Texture $name (Join-Path $outputRoot "$name.png")
    }

    if ($Mode -eq "Check") {
        foreach ($name in $textureNames) {
            $generated = Join-Path $temporaryRoot "$name.png"
            $published = Join-Path $publishedRoot "$name.png"
            if (!(Test-Path -LiteralPath $published)) { throw "Published signature texture is missing: $published" }
            if ((Get-FileHash -LiteralPath $generated -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $published -Algorithm SHA256).Hash) {
                throw "Signature texture source/output drift: $name.png"
            }
        }
    }
} finally {
    if ($Mode -eq "Check" -and [IO.Directory]::Exists($temporaryRoot)) {
        $resolvedParent = [IO.Path]::GetFullPath($temporaryParent)
        $resolvedTemporary = [IO.Path]::GetFullPath($temporaryRoot)
        if (![IO.Path]::GetDirectoryName($resolvedTemporary).Equals($resolvedParent, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to delete texture staging outside the expected parent: $resolvedTemporary"
        }
        Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
    }
}

Write-Host "$Mode completed for $($textureNames.Count) Effekseer signature texture(s)."
