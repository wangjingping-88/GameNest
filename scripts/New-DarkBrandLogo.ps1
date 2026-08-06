[CmdletBinding()]
param(
    [string]$SourcePath = (Join-Path $PSScriptRoot '..\docs\GameNest_Logo_Horizontal.png'),
    [string]$DestinationPath = (Join-Path $PSScriptRoot '..\src\GameNest.App\Assets\Brand\GameNest_Logo_Horizontal_Dark.png')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$resolvedSource = (Resolve-Path -LiteralPath $SourcePath).Path
$destinationDirectory = Split-Path -Parent $DestinationPath
if (-not (Test-Path -LiteralPath $destinationDirectory)) {
    New-Item -ItemType Directory -Path $destinationDirectory | Out-Null
}

$sourceImage = [System.Drawing.Bitmap]::FromFile($resolvedSource)
try {
    if ($sourceImage.Width -ne 1612 -or $sourceImage.Height -ne 457) {
        throw "Source logo must be 1612x457; actual size is $($sourceImage.Width)x$($sourceImage.Height)."
    }

    $outputImage = [System.Drawing.Bitmap]::new(
        $sourceImage.Width,
        $sourceImage.Height,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($outputImage)
        try {
            $graphics.DrawImageUnscaled($sourceImage, 0, 0)
        }
        finally {
            $graphics.Dispose()
        }

        $bounds = [System.Drawing.Rectangle]::new(0, 0, $outputImage.Width, $outputImage.Height)
        $bitmapData = $outputImage.LockBits(
            $bounds,
            [System.Drawing.Imaging.ImageLockMode]::ReadWrite,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $byteCount = [Math]::Abs($bitmapData.Stride) * $bitmapData.Height
            $pixels = New-Object byte[] $byteCount
            [System.Runtime.InteropServices.Marshal]::Copy($bitmapData.Scan0, $pixels, 0, $byteCount)

            for ($y = 0; $y -lt $outputImage.Height; $y++) {
                for ($x = 0; $x -lt $outputImage.Width; $x++) {
                    $offset = ($y * $bitmapData.Stride) + ($x * 4)
                    $blue = [int]$pixels[$offset]
                    $green = [int]$pixels[$offset + 1]
                    $red = [int]$pixels[$offset + 2]
                    $alpha = [int]$pixels[$offset + 3]
                    if ($alpha -eq 0) {
                        continue
                    }

                    $maximumChannel = [Math]::Max($red, [Math]::Max($green, $blue))
                    $isGameWordmark = $x -ge 455 -and $x -le 1110 -and $y -ge 90 -and $y -le 370
                    $isCubeDetail = $x -ge 165 -and $x -le 330 -and $y -ge 95 -and $y -le 315
                    if ($maximumChannel -ge 140 -or (-not $isGameWordmark -and -not $isCubeDetail)) {
                        continue
                    }

                    $verticalProgress = $y / [double]$outputImage.Height
                    $sourceDetail = [Math]::Min(14, [Math]::Round($maximumChannel / 10.0))
                    if ($isGameWordmark) {
                        $targetRed = 202 - [Math]::Round(28 * $verticalProgress) + $sourceDetail
                        $targetGreen = 214 - [Math]::Round(25 * $verticalProgress) + $sourceDetail
                        $targetBlue = 228 - [Math]::Round(18 * $verticalProgress) + $sourceDetail
                    }
                    else {
                        $targetRed = 132 - [Math]::Round(20 * $verticalProgress) + $sourceDetail
                        $targetGreen = 151 - [Math]::Round(18 * $verticalProgress) + $sourceDetail
                        $targetBlue = 176 - [Math]::Round(12 * $verticalProgress) + $sourceDetail
                    }

                    $pixels[$offset] = [byte][Math]::Min(255, [Math]::Max(0, $targetBlue))
                    $pixels[$offset + 1] = [byte][Math]::Min(255, [Math]::Max(0, $targetGreen))
                    $pixels[$offset + 2] = [byte][Math]::Min(255, [Math]::Max(0, $targetRed))
                }
            }

            [System.Runtime.InteropServices.Marshal]::Copy($pixels, 0, $bitmapData.Scan0, $byteCount)
        }
        finally {
            $outputImage.UnlockBits($bitmapData)
        }

        $outputImage.Save($DestinationPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $outputImage.Dispose()
    }
}
finally {
    $sourceImage.Dispose()
}

Write-Host "Generated dark-theme logo: $DestinationPath"
