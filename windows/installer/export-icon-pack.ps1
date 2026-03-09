param(
    [string]$OutputDirectory,
    [string]$SourceSvgPath = (Join-Path $PSScriptRoot "..\windows-client-ui\assets\owl-face.svg")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.IO;

public static class OwlProtectIconWriter
{
    public static void WriteIco(string destinationPath, string[] pngPaths)
    {
        using (var stream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)pngPaths.Length);

            var imageData = new byte[pngPaths.Length][];
            var offset = 6 + (16 * pngPaths.Length);
            for (var index = 0; index < pngPaths.Length; index++)
            {
                imageData[index] = File.ReadAllBytes(pngPaths[index]);
                using (var image = System.Drawing.Image.FromFile(pngPaths[index]))
                {
                    var width = image.Width >= 256 ? 0 : image.Width;
                    var height = image.Height >= 256 ? 0 : image.Height;
                    writer.Write((byte)width);
                    writer.Write((byte)height);
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write(imageData[index].Length);
                    writer.Write(offset);
                    offset += imageData[index].Length;
                }
            }

            foreach (var bytes in imageData)
            {
                writer.Write(bytes);
            }
        }
    }
}
"@

function Get-Palette([string]$StateName) {
    switch ($StateName) {
        "healthy" { return @{ Face = [System.Drawing.Color]::FromArgb(255, 34, 120, 86); Eyes = [System.Drawing.Color]::FromArgb(255, 88, 202, 133) } }
        "pending-approval" { return @{ Face = [System.Drawing.Color]::FromArgb(255, 34, 104, 170); Eyes = [System.Drawing.Color]::FromArgb(255, 239, 181, 56) } }
        "degraded" { return @{ Face = [System.Drawing.Color]::FromArgb(255, 191, 120, 42); Eyes = [System.Drawing.Color]::FromArgb(255, 239, 181, 56) } }
        "admin-disconnected" { return @{ Face = [System.Drawing.Color]::FromArgb(255, 178, 61, 49); Eyes = [System.Drawing.Color]::FromArgb(255, 214, 88, 75) } }
        default { return @{ Face = [System.Drawing.Color]::FromArgb(255, 79, 95, 107); Eyes = [System.Drawing.Color]::FromArgb(255, 160, 165, 170) } }
    }
}

function New-OwlBitmap {
    param(
        [int]$Size,
        [System.Drawing.Color]$FaceColor,
        [System.Drawing.Color]$EyeColor
    )

    $bitmap = New-Object System.Drawing.Bitmap $Size, $Size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $scale = $Size / 32.0
    $faceBrush = New-Object System.Drawing.SolidBrush $FaceColor
    $eyeBrush = New-Object System.Drawing.SolidBrush $EyeColor
    $beakBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 215, 167, 49))

    $leftEar = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF (7 * $scale), (8 * $scale)),
        (New-Object System.Drawing.PointF (13 * $scale), (2 * $scale)),
        (New-Object System.Drawing.PointF (16 * $scale), (13 * $scale))
    )
    $rightEar = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF (25 * $scale), (8 * $scale)),
        (New-Object System.Drawing.PointF (19 * $scale), (2 * $scale)),
        (New-Object System.Drawing.PointF (16 * $scale), (13 * $scale))
    )
    $beak = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF (16 * $scale), (18 * $scale)),
        (New-Object System.Drawing.PointF (13.5 * $scale), (24 * $scale)),
        (New-Object System.Drawing.PointF (18.5 * $scale), (24 * $scale))
    )

    $graphics.FillPolygon($faceBrush, $leftEar)
    $graphics.FillPolygon($faceBrush, $rightEar)
    $graphics.FillEllipse($faceBrush, 5 * $scale, 8 * $scale, 22 * $scale, 19 * $scale)
    $graphics.FillEllipse($eyeBrush, 10 * $scale, 12 * $scale, 4 * $scale, 7 * $scale)
    $graphics.FillEllipse($eyeBrush, 18 * $scale, 12 * $scale, 4 * $scale, 7 * $scale)
    $graphics.FillPolygon($beakBrush, $beak)

    $beakBrush.Dispose()
    $eyeBrush.Dispose()
    $faceBrush.Dispose()
    $graphics.Dispose()

    return $bitmap
}

$null = New-Item -ItemType Directory -Force -Path $OutputDirectory
Copy-Item -Path $SourceSvgPath -Destination (Join-Path $OutputDirectory "owl-face.svg") -Force

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$iconSizes = @(16, 32, 48, 64, 256)
$states = @("disconnected", "pending-approval", "healthy", "degraded", "admin-disconnected")

foreach ($state in $states) {
    $palette = Get-Palette $state
    $stateDirectory = Join-Path $OutputDirectory $state
    $null = New-Item -ItemType Directory -Force -Path $stateDirectory
    $pngPaths = @()
    foreach ($size in $sizes) {
        $bitmap = New-OwlBitmap -Size $size -FaceColor $palette.Face -EyeColor $palette.Eyes
        $pngPath = Join-Path $stateDirectory ("owlprotect-{0}-{1}.png" -f $state, $size)
        $bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
        if ($iconSizes -contains $size) {
            $pngPaths += $pngPath
        }
    }

    [OwlProtectIconWriter]::WriteIco((Join-Path $stateDirectory ("owlprotect-{0}.ico" -f $state)), $pngPaths)
}

@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    sourceSvg = "owl-face.svg"
    states = $states
    sizes = $sizes
} | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $OutputDirectory "manifest.json")
