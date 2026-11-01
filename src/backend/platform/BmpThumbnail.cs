using System;
using System.IO;
using Microsoft.Xna.Framework;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// Reads a 24- or 32-bit BI_RGB BMP produced by ffmpeg.
    /// Black pixels become alpha 192, matching the Windows System.Drawing path.
    /// </summary>
    public static class BmpThumbnail
    {
        public static Color[] Read(string path, int expectedWidth, int expectedHeight)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 54 || data[0] != (byte)'B' || data[1] != (byte)'M')
                throw new InvalidDataException("Not a BMP: " + path);
            int pixelOffset = BitConverter.ToInt32(data, 10);
            int dibSize = BitConverter.ToInt32(data, 14);
            if (dibSize < 40)
                throw new InvalidDataException("Unsupported BMP header size " + dibSize);
            int width = BitConverter.ToInt32(data, 18);
            int heightRaw = BitConverter.ToInt32(data, 22);
            bool topDown = heightRaw < 0;
            int height = Math.Abs(heightRaw);
            short bpp = BitConverter.ToInt16(data, 28);
            int compression = BitConverter.ToInt32(data, 30);
            if (compression != 0 || (bpp != 24 && bpp != 32))
                throw new InvalidDataException("Unsupported BMP bpp=" + bpp + " compression=" + compression);
            if (width != expectedWidth || height != expectedHeight)
                throw new InvalidDataException("BMP size " + width + "x" + height + " != " + expectedWidth + "x" + expectedHeight);
            int stride = ((width * bpp + 31) / 32) * 4;
            var colors = new Color[width * height];
            int pixelBytes = bpp / 8;
            for (int y = 0; y < height; y++)
            {
                int srcY = topDown ? y : (height - 1 - y);
                int row = pixelOffset + (srcY * stride);
                for (int x = 0; x < width; x++)
                {
                    int i = row + (x * pixelBytes);
                    byte b = data[i];
                    byte g = data[i + 1];
                    byte r = data[i + 2];
                    byte a = bpp == 32 ? data[i + 3] : (byte)255;
                    if (r == 0 && g == 0 && b == 0)
                        a = 192;
                    colors[x + (y * width)] = new Color(r, g, b, a);
                }
            }
            return colors;
        }
    }
}
