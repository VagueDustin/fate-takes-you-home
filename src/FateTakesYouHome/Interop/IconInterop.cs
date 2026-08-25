// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FateTakesYouHome.Interop;

/// <summary>
/// Converts WPF bitmaps into Win32 icon handles.
/// </summary>
/// <remarks>
/// The obvious route — <c>System.Drawing.Icon</c> — would pull in <c>System.Drawing.Common</c> for
/// one function, so the ICONIMAGE payload is assembled by hand instead. It is a fixed layout:
/// a <c>BITMAPINFOHEADER</c> whose height is doubled, the colour bitmap bottom-up, then a 1-bit
/// AND mask. With a 32-bit colour bitmap the alpha channel does the masking, so the mask is left
/// blank — but it must still be present and correctly padded or the icon comes out garbled.
/// </remarks>
internal static class IconInterop
{
    private const int BitmapInfoHeaderSize = 40;

    /// <summary>
    /// Creates an <c>HICON</c> from a bitmap. The caller owns the handle and must destroy it.
    /// </summary>
    public static IntPtr CreateHIcon(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Straight (non-premultiplied) BGRA is what the icon format expects. Handing it Pbgra32
        // produces an icon with dark fringing everywhere the source is partially transparent.
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();

        int width = converted.PixelWidth;
        int height = converted.PixelHeight;
        int colourStride = width * 4;

        byte[] topDown = new byte[colourStride * height];
        converted.CopyPixels(topDown, colourStride, 0);

        // DIBs are stored bottom-up.
        byte[] bottomUp = new byte[topDown.Length];
        for (int row = 0; row < height; row++)
        {
            Buffer.BlockCopy(
                topDown,
                row * colourStride,
                bottomUp,
                (height - 1 - row) * colourStride,
                colourStride);
        }

        // Each mask row is padded to a 4-byte boundary.
        int maskStride = ((width + 31) / 32) * 4;
        int maskBytes = maskStride * height;

        byte[] payload = new byte[BitmapInfoHeaderSize + bottomUp.Length + maskBytes];

        WriteHeader(payload, width, height, bottomUp.Length);
        Buffer.BlockCopy(bottomUp, 0, payload, BitmapInfoHeaderSize, bottomUp.Length);

        // The AND mask stays zeroed: every pixel opaque, alpha decides visibility.

        GCHandle pin = GCHandle.Alloc(payload, GCHandleType.Pinned);
        try
        {
            IntPtr icon = NativeMethods.CreateIconFromResourceEx(
                pin.AddrOfPinnedObject(),
                payload.Length,
                fIcon: true,
                NativeMethods.IconResourceVersion,
                width,
                height,
                NativeMethods.LR_DEFAULTCOLOR);

            if (icon == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "Windows refused to build the tray icon.",
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            }

            return icon;
        }
        finally
        {
            pin.Free();
        }
    }

    private static void WriteHeader(byte[] buffer, int width, int height, int imageBytes)
    {
        var span = buffer.AsSpan(0, BitmapInfoHeaderSize);

        BitConverter.TryWriteBytes(span[0..], BitmapInfoHeaderSize);   // biSize
        BitConverter.TryWriteBytes(span[4..], width);                  // biWidth

        // Doubled: the header describes the colour bitmap and the mask stacked together.
        BitConverter.TryWriteBytes(span[8..], height * 2);             // biHeight

        BitConverter.TryWriteBytes(span[12..], (short)1);              // biPlanes
        BitConverter.TryWriteBytes(span[14..], (short)32);             // biBitCount
        BitConverter.TryWriteBytes(span[16..], 0);                     // biCompression = BI_RGB
        BitConverter.TryWriteBytes(span[20..], imageBytes);            // biSizeImage
        BitConverter.TryWriteBytes(span[24..], 0);                     // biXPelsPerMeter
        BitConverter.TryWriteBytes(span[28..], 0);                     // biYPelsPerMeter
        BitConverter.TryWriteBytes(span[32..], 0);                     // biClrUsed
        BitConverter.TryWriteBytes(span[36..], 0);                     // biClrImportant
    }

    /// <summary>
    /// The pixel size the notification area wants for an icon on a given display.
    /// </summary>
    /// <remarks>
    /// <c>GetSystemMetricsForDpi</c> exists from Windows 10 1607; on anything older the
    /// non-DPI-aware metric is scaled by hand, which is close enough for an icon.
    /// </remarks>
    public static int GetTrayIconSize(uint dpi)
    {
        try
        {
            int size = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXSMICON, dpi);
            if (size > 0)
            {
                return size;
            }
        }
        catch (EntryPointNotFoundException)
        {
            // Pre-1607. Fall through.
        }

        int baseline = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
        if (baseline <= 0)
        {
            baseline = 16;
        }

        return (int)Math.Round(baseline * (dpi / 96d));
    }
}
