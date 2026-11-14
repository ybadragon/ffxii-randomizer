param([string]$Steps = "wait:45,shot:t0", [string]$Dir = "$PSScriptRoot\..\..\..\work\bot\game", [switch]$Keep)
# Drives the game: steps are wait:<s>, key:<name>, shot:<name>. Launches it first unless it is already running.
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class G {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
 // Windows only lets a background process take focus right after input, so tap Alt first.
 public static bool Focus(IntPtr h) { if (GetForegroundWindow() == h) return true; keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); SetForegroundWindow(h); System.Threading.Thread.Sleep(400); return GetForegroundWindow() == h; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] i, int size);
 [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint type);
 public struct RECT { public int L, T, R, B; }
 [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public KEYBDINPUT ki; public ulong pad; }
 [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort vk; public ushort scan; public uint flags; public uint time; public IntPtr extra; }
 public static int HoldMs = 120;
 static void Send(ushort vk, bool up) {
   uint ext = (vk >= 0x25 && vk <= 0x28) ? 1u : 0u;
   var i = new INPUT[1]; i[0].type = 1; i[0].ki.scan = (ushort)MapVirtualKey(vk, 0); i[0].ki.flags = 8u | ext | (up ? 2u : 0u);
   SendInput(1, i, Marshal.SizeOf(typeof(INPUT)));
 }
 // holds every key of a chord together for HoldMs
 public static void Chord(ushort[] vks) {
   foreach (var v in vks) Send(v, false);
   System.Threading.Thread.Sleep(HoldMs);
   foreach (var v in vks) Send(v, true);
 }
 public static void Key(ushort vk) { Chord(new ushort[] { vk }); } }
"@
New-Item -ItemType Directory -Force $Dir | Out-Null
$p = Get-Process FFXII_TZA -ErrorAction SilentlyContinue
if (-not $p) { Start-Process "steam://rungameid/595520"; for ($i = 0; $i -lt 60 -and -not $p; $i++) { Start-Sleep 1; $p = Get-Process FFXII_TZA -ErrorAction SilentlyContinue } }
$keys = @{ tab = 0x09; backspace = 0x08; shift = 0x10; ctrl = 0x11; enter = 0x0D; esc = 0x1B; up = 0x26; down = 0x28; left = 0x25; right = 0x27; space = 0x20; w = 0x57; a = 0x41; s = 0x53; d = 0x44 }
foreach ($step in $Steps -split ',') {
  $kind, $arg = $step -split ':'
  $p = Get-Process FFXII_TZA -ErrorAction SilentlyContinue
  if (-not $p) { "game exited before step $step"; break }
  switch ($kind) {
    'wait' { Start-Sleep ([int]$arg) }
    'key'  { $names, $ms = $arg -split '/'
             $vks = [UInt16[]]@($names -split '\+' | ForEach-Object { if ($keys.ContainsKey($_)) { $keys[$_] } else { [int][char]$_.ToUpper() } })
             [G]::HoldMs = if ($ms) { [int]$ms } else { 120 }
             "focused: $([G]::Focus($p.MainWindowHandle))"; Start-Sleep -Milliseconds 300; [G]::Chord($vks); Start-Sleep -Milliseconds 700 }
    'shot' { [G]::Focus($p.MainWindowHandle) | Out-Null
             $r = New-Object G+RECT; [G]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
             $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T); $g = [Drawing.Graphics]::FromImage($bmp)
             $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size); $bmp.Save("$Dir\$arg.png"); $g.Dispose(); $bmp.Dispose(); "shot $arg ($($p.MainWindowTitle))" }
  }
}
if (-not $Keep) { Get-Process FFXII_TZA -ErrorAction SilentlyContinue | Stop-Process -Force }
