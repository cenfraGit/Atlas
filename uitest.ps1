# end to end smoke test: starts Atlas, drives it with real keystrokes and
# saves a screenshot of each step. the self-checks cannot see the window: the
# clipboard paste was broken and every one of them still passed.
#
#   powershell -STA -File uitest.ps1 [-Repo <path>]

param(
    [string]$Repo = $PSScriptRoot,
    [string]$OutDir = "$env:TEMP\atlas_uitest"
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class AtlasWin {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@

$fails = 0
function Check([bool]$ok, [string]$what) {
    if ($ok) { "  ok   $what" } else { $script:fails++; "  FAIL $what" }
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
Get-ChildItem $OutDir -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force

$images = Join-Path $Repo '.atlas\images'
$before = (Get-ChildItem $images -ErrorAction SilentlyContinue).Count

# a picture to paste. this is the path avalonia's own clipboard cannot see
$src = New-Object System.Drawing.Bitmap 640, 360
$g0 = [System.Drawing.Graphics]::FromImage($src)
$g0.Clear([System.Drawing.Color]::FromArgb(20, 30, 60))
$g0.FillRectangle([System.Drawing.Brushes]::Orange, 40, 40, 240, 120)
$g0.Dispose()
[System.Windows.Forms.Clipboard]::SetImage($src)

$p = Start-Process -FilePath dotnet -ArgumentList 'run', '--', $Repo `
    -WorkingDirectory (Join-Path $PSScriptRoot 'app') -PassThru
Start-Sleep -Seconds 16
$app = Get-Process Atlas -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $app -or $app.MainWindowHandle -eq [IntPtr]::Zero) { "no window - did it build?"; exit 1 }
$h = $app.MainWindowHandle

function Shot([string]$name) {
    $r = New-Object AtlasWin+RECT
    [void][AtlasWin]::GetWindowRect($h, [ref]$r)
    $bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
    $bmp.Save("$OutDir\$name.png", [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

function Key([string]$keys, [double]$wait = 1.2) {
    [void][AtlasWin]::SetForegroundWindow($h)
    Start-Sleep -Milliseconds 250
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Seconds $wait
}

[void][AtlasWin]::SetForegroundWindow($h)
Start-Sleep -Seconds 1
Shot "01-map"

Key "o"; Shot "02-boards-panel"
Key "{ENTER}" 2.0; Shot "03-board-open"
Key "e"; Shot "04-board-edit"

Key "^v" 2.5; Shot "05-image-pasted"
$pasted = (Get-ChildItem $images -ErrorAction SilentlyContinue).Count
Check ($pasted -eq $before + 1) "ctrl+V writes one image into .atlas/images"

Key "^z" 1.5; Shot "06-image-undone"
# alt+left leaves a board, not Escape: Escape closes what is open, and
# leaving is going somewhere rather than closing something
Key "%{LEFT}" 2.5; Shot "07-back-to-map"
$after = (Get-ChildItem $images -ErrorAction SilentlyContinue).Count
Check ($after -eq $before) "leaving the board prunes the image undo threw away"

Key "p" 3.0; Shot "08-pull-requests"
Key "{ESC}"; Key "b" 1.5; Shot "09-bookmarks"
Key "{ESC}"; Key "/" 1.5; Shot "10-search"
Key "{ESC}"

Stop-Process -Id $app.Id -Force
""
if ($fails -eq 0) { "PASS - shots in $OutDir" } else { "$fails FAILURES - shots in $OutDir"; exit 1 }
