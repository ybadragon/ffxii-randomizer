param([string]$Title = "FFXII trip viewer", [string]$Out)
# winshot.ps1 -Title <window title> -Out <png>: screenshot of one window (e.g. the trip viewer) for checking it.
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W {
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
 public struct RECT { public int L, T, R, B; } }
"@
$h = [IntPtr](Get-Process | Where-Object MainWindowTitle -eq $Title | Select-Object -First 1).MainWindowHandle
if ($h -eq [IntPtr]::Zero) { "window '$Title' not found"; exit 1 }
$r = New-Object W+RECT; [W]::GetWindowRect($h, [ref]$r) | Out-Null
$bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T); $g = [Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc(); [W]::PrintWindow($h, $dc, 2) | Out-Null; $g.ReleaseHdc($dc)
$bmp.Save($Out); $g.Dispose(); $bmp.Dispose(); "saved $Out"
