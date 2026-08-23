<#
.SYNOPSIS
    Captures a window belonging to a running process to a PNG.

.DESCRIPTION
    A development aid. Uses PrintWindow with PW_RENDERFULLCONTENT so the capture works even when
    the window is partly obscured or off screen, which a plain BitBlt of the desktop cannot do.

    Kept in the repository because verifying that a WPF window actually looks right is otherwise a
    manual step, and manual steps are the ones that stop happening.

.PARAMETER ProcessName
    Process to capture, without the .exe.

.PARAMETER Out
    Destination PNG path.

.PARAMETER TitleLike
    Optional wildcard filter on the window title, for a process with several windows.

.PARAMETER Index
    Which matching window to take when more than one qualifies. Zero-based.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ProcessName,
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$TitleLike = '*',
    [int]$Index = 0
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class WindowCapture
{
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(
        IntPtr hwnd, int attribute, out RECT value, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    // PW_RENDERFULLCONTENT. Without it a hardware-composited window captures as black.
    public const uint RenderFullContent = 0x00000002;

    // DWMWA_EXTENDED_FRAME_BOUNDS: the visible bounds, excluding the invisible resize border
    // that GetWindowRect includes and that would otherwise show up as a transparent margin.
    public const int ExtendedFrameBounds = 9;
}
'@

# Process.MainWindowHandle is no use here: a tool window — which every tray flyout is — is
# excluded from it by design. Enumerating top-level windows and filtering by process id finds
# them all.
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class WindowEnum
{
    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int count);

    public static List<string> ForProcess(uint processId)
    {
        var found = new List<string>();

        EnumWindows((hwnd, _) =>
        {
            uint owner;
            GetWindowThreadProcessId(hwnd, out owner);

            if (owner != processId || !IsWindowVisible(hwnd))
            {
                return true;
            }

            var title = new StringBuilder(512);
            GetWindowTextW(hwnd, title, title.Capacity);

            found.Add(hwnd.ToInt64() + "|" + title.ToString());
            return true;
        }, IntPtr.Zero);

        return found;
    }
}
'@

$processes = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)

if ($processes.Count -eq 0) {
    throw "No process named '$ProcessName' is running."
}

$candidates = @()
foreach ($p in $processes) {
    foreach ($entry in [WindowEnum]::ForProcess([uint32]$p.Id)) {
        $parts = $entry.Split('|', 2)
        $title = $parts[1]

        if ($title -like $TitleLike) {
            $candidates += [pscustomobject]@{
                Handle = [IntPtr][int64]$parts[0]
                Title  = $title
            }
        }
    }
}

if ($candidates.Count -eq 0) {
    throw "No visible window matching '$TitleLike' was found for '$ProcessName'."
}

if ($Index -ge $candidates.Count) {
    $titles = ($candidates | ForEach-Object { "'" + $_.Title + "'" }) -join ', '
    throw "Asked for window $Index but only $($candidates.Count) matched: $titles"
}

$target = $candidates[$Index]
$hwnd = $target.Handle

$rect = New-Object WindowCapture+RECT
$size = [System.Runtime.InteropServices.Marshal]::SizeOf([type]'WindowCapture+RECT')

if ([WindowCapture]::DwmGetWindowAttribute($hwnd, [WindowCapture]::ExtendedFrameBounds, [ref]$rect, $size) -ne 0) {
    [void][WindowCapture]::GetWindowRect($hwnd, [ref]$rect)
}

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top

if ($width -le 0 -or $height -le 0) {
    throw "The window reported a zero-sized rectangle ($width x $height)."
}

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$hdc = $graphics.GetHdc()

try {
    $ok = [WindowCapture]::PrintWindow($hwnd, $hdc, [WindowCapture]::RenderFullContent)
} finally {
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
}

if (-not $ok) {
    $bitmap.Dispose()
    throw 'PrintWindow refused to draw the window.'
}

$directory = Split-Path -Parent $Out
if ($directory -and -not (Test-Path $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$bitmap.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()

"Captured '$($target.Title)' at ${width}x${height} to $Out"
