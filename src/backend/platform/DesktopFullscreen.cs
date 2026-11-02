using System;
using System.Collections.Generic;
using System.Reflection;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// Which display a DesktopGL window is on, so fullscreen covers that display
    /// and not the primary one. The choice is the display with the largest overlap.
    /// </summary>
    public static class DesktopFullscreen
    {
        public readonly struct DisplayBounds
        {
            public DisplayBounds(int x, int y, int width, int height)
            {
                X = x;
                Y = y;
                Width = width;
                Height = height;
            }

            public int X { get; }
            public int Y { get; }
            public int Width { get; }
            public int Height { get; }
        }

        /// <summary>
        /// Index of the display the window is on. Ties keep the earlier display.
        /// No overlap uses the display whose center is nearest the window center.
        /// An empty list returns -1.
        /// </summary>
        public static int ChooseIndex(int windowX, int windowY, int windowWidth, int windowHeight, IReadOnlyList<DisplayBounds> displays)
        {
            if (displays == null || displays.Count == 0)
                return -1;
            int best = 0;
            long bestArea = Overlap(windowX, windowY, windowWidth, windowHeight, displays[0]);
            for (int i = 1; i < displays.Count; i++)
            {
                long area = Overlap(windowX, windowY, windowWidth, windowHeight, displays[i]);
                if (area > bestArea)
                {
                    best = i;
                    bestArea = area;
                }
            }
            if (bestArea > 0)
                return best;
            int cx = windowX + Math.Max(windowWidth, 0) / 2;
            int cy = windowY + Math.Max(windowHeight, 0) / 2;
            long bestDistance = DistanceSquared(cx, cy, displays[0]);
            best = 0;
            for (int i = 1; i < displays.Count; i++)
            {
                long distance = DistanceSquared(cx, cy, displays[i]);
                if (distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>
        /// SDL display bounds. Does not move the window. False when SDL is not ready.
        /// </summary>
        public static bool TryReadDisplays(out DisplayBounds[] displays)
        {
            displays = Array.Empty<DisplayBounds>();
            try
            {
                Assembly asm = typeof(Microsoft.Xna.Framework.GraphicsDeviceManager).Assembly;
                Type? sdl = asm.GetType("Sdl");
                Type? display = sdl?.GetNestedType("Display", BindingFlags.Public | BindingFlags.NonPublic);
                Type? rectType = sdl?.GetNestedType("Rectangle", BindingFlags.Public | BindingFlags.NonPublic);
                if (display == null || rectType == null)
                    return false;
                MethodInfo? countMethod = display.GetMethod("GetNumVideoDisplays", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo? boundsMethod = display.GetMethod("GetBounds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (countMethod == null || boundsMethod == null)
                    return false;
                object? countValue = countMethod.Invoke(null, null);
                if (countValue is not int count || count <= 0)
                    return false;
                var list = new DisplayBounds[count];
                FieldInfo? fieldX = rectType.GetField("X");
                FieldInfo? fieldY = rectType.GetField("Y");
                FieldInfo? fieldW = rectType.GetField("Width");
                FieldInfo? fieldH = rectType.GetField("Height");
                if (fieldX == null || fieldY == null || fieldW == null || fieldH == null)
                    return false;
                for (int i = 0; i < count; i++)
                {
                    object rect = Activator.CreateInstance(rectType)!;
                    object[] args = { i, rect };
                    boundsMethod.Invoke(null, args);
                    object filled = args[1]!;
                    list[i] = new DisplayBounds(
                        (int)fieldX.GetValue(filled)!,
                        (int)fieldY.GetValue(filled)!,
                        (int)fieldW.GetValue(filled)!,
                        (int)fieldH.GetValue(filled)!);
                }
                displays = list;
                return true;
            }
            catch
            {
                return false;
            }
        }

        static long Overlap(int windowX, int windowY, int windowWidth, int windowHeight, DisplayBounds display)
        {
            int width = Math.Max(windowWidth, 0);
            int height = Math.Max(windowHeight, 0);
            int left = Math.Max(windowX, display.X);
            int top = Math.Max(windowY, display.Y);
            int right = Math.Min(windowX + width, display.X + Math.Max(display.Width, 0));
            int bottom = Math.Min(windowY + height, display.Y + Math.Max(display.Height, 0));
            int overlapWidth = right - left;
            int overlapHeight = bottom - top;
            if (overlapWidth <= 0 || overlapHeight <= 0)
                return 0;
            return (long)overlapWidth * overlapHeight;
        }

        static long DistanceSquared(int x, int y, DisplayBounds display)
        {
            long centerX = display.X + (long)display.Width / 2;
            long centerY = display.Y + (long)display.Height / 2;
            long dx = x - centerX;
            long dy = y - centerY;
            return dx * dx + dy * dy;
        }
    }
}
