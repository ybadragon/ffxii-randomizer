using System.Drawing;
using System.Drawing.Imaging;

namespace Ff12MapData;

/// <summary>
/// PS2 TIM2 pictures inside a file. Layout: "TIM2" + version + format + count (16 bytes); per picture: totalSize u32,
/// clutSize u32, imageSize u32, headerSize u16, clutColors u16, picFormat u8, mipmaps u8, clutType u8, imageType u8,
/// width u16, height u16, ..., then the image and the palette (CLUT). imageType: 1 = 16 bpp, 2 = 24, 3 = 32, 4 = 4-bit,
/// 5 = 8-bit. 8-bit palettes are stored in the PS2's swizzled (CSM1) order unless clutType bit 7 says otherwise.
/// </summary>
public static class Tim2
{
    public sealed record Picture(int Offset, int Width, int Height, Func<Bitmap> Decode);

    /// <summary>Every complete picture in <paramref name="data"/>, in file order (decoded only when asked).</summary>
    public static List<Picture> Find(byte[] data)
    {
        var list = new List<Picture>();
        for (int at = 0; at + 64 < data.Length; at++)
        {
            if (data[at] != 'T' || data[at + 1] != 'I' || data[at + 2] != 'M' || data[at + 3] != '2') continue;
            int ph = at + 16, hdr = BitConverter.ToUInt16(data, ph + 12), w = BitConverter.ToUInt16(data, ph + 20), h = BitConverter.ToUInt16(data, ph + 22);
            uint imgSize = BitConverter.ToUInt32(data, ph + 8);
            if (w == 0 || h == 0 || ph + hdr + imgSize > data.Length) continue;
            int start = at;
            list.Add(new Picture(start, w, h, () => Decode(data, start)));
            at += 15;
        }
        return list;
    }

    static Bitmap Decode(byte[] bytes, int at)
    {
        int ph = at + 16;
        uint imgSize = BitConverter.ToUInt32(bytes, ph + 8);
        int hdr = BitConverter.ToUInt16(bytes, ph + 12), clutColors = BitConverter.ToUInt16(bytes, ph + 14);
        int clutType = bytes[ph + 18], imgType = bytes[ph + 19], w = BitConverter.ToUInt16(bytes, ph + 20), h = BitConverter.ToUInt16(bytes, ph + 22);
        int img = ph + hdr, clut = img + (int)imgSize;
        // a palette cut short by the end of the file reads as zeros
        if (clut + clutColors * 4 > bytes.Length) Array.Resize(ref bytes, clut + clutColors * 4);
        int Clut(int i)
        {
            if ((clutType & 3) == 3) { int o = clut + i * 4; return Argb(Math.Min(255, bytes[o + 3] * 2), bytes[o], bytes[o + 1], bytes[o + 2]); }
            if ((clutType & 3) == 2) { int o = clut + i * 3; return Argb(255, bytes[o], bytes[o + 1], bytes[o + 2]); }
            int v = BitConverter.ToUInt16(bytes, clut + i * 2);
            return Argb((v & 0x8000) != 0 ? 255 : 0, (v & 31) * 8, ((v >> 5) & 31) * 8, ((v >> 10) & 31) * 8);
        }
        int Unswizzle(int i) => (clutType & 0x80) != 0 ? i : (i & ~0x18) | ((i & 0x08) << 1) | ((i & 0x10) >> 1);
        var px = new int[w * h];
        for (int i = 0; i < px.Length; i++)
            px[i] = imgType switch
            {
                5 => Clut(clutColors == 256 ? Unswizzle(bytes[img + i]) : bytes[img + i]),
                4 => Clut((i & 1) == 0 ? bytes[img + i / 2] & 15 : bytes[img + i / 2] >> 4),
                3 => Argb(Math.Min(255, bytes[img + i * 4 + 3] * 2), bytes[img + i * 4], bytes[img + i * 4 + 1], bytes[img + i * 4 + 2]),
                2 => Argb(255, bytes[img + i * 3], bytes[img + i * 3 + 1], bytes[img + i * 3 + 2]),
                _ => Rgb555(BitConverter.ToUInt16(bytes, img + i * 2)),
            };
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        System.Runtime.InteropServices.Marshal.Copy(px, 0, data.Scan0, px.Length);
        bmp.UnlockBits(data);
        return bmp;
    }

    static int Argb(int a, int r, int g, int b) => (a << 24) | (r << 16) | (g << 8) | b;
    static int Rgb555(int v) => Argb(255, (v & 31) * 8, ((v >> 5) & 31) * 8, ((v >> 10) & 31) * 8);
}
