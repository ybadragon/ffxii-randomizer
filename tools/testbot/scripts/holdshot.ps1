param([int]$Vk, [int]$Ms = 3000, [string]$Out = "$PSScriptRoot\..\..\..\work\bot\game\hold.png")
# holdshot.ps1 -Vk <virtual-key code> [-Ms 3000]: holds one key down (scan code, as the game reads keys) and takes a
# screenshot of the game while it is still held, then releases it. For finding what a key does (e.g. the flee key).
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class H {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] i, int size);
 [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint type);
 public struct RECT { public int L, T, R, B; }
 [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public KEYBDINPUT ki; public ulong pad; }
 [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort vk; public ushort scan; public uint flags; public uint time; public IntPtr extra; }
 public static void Focus(IntPtr h) { if (GetForegroundWindow() == h) return; keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); SetForegroundWindow(h); System.Threading.Thread.Sleep(400); }
 public static void Send(ushort vk, bool up) {
   uint ext = (vk >= 0x21 && vk <= 0x2E) || vk == 0xA3 || vk == 0xA5 ? 1u : 0u;
   var i = new INPUT[1]; i[0].type = 1; i[0].ki.scan = (ushort)MapVirtualKey(vk, 0); i[0].ki.flags = 8u | ext | (up ? 2u : 0u);
   SendInput(1, i, Marshal.SizeOf(typeof(INPUT)));
 } }
"@
$p = Get-Process FFXII_TZA
[H]::Focus($p.MainWindowHandle)
[H]::Send([uint16]$Vk, $false)
try {
  Start-Sleep -Milliseconds $Ms
  $r = New-Object H+RECT; [H]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
  $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T); $g = [Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size); $bmp.Save($Out); $g.Dispose(); $bmp.Dispose()
} finally { [H]::Send([uint16]$Vk, $true) }
"held 0x$('{0:X}' -f $Vk) for $Ms ms; shot $Out"
