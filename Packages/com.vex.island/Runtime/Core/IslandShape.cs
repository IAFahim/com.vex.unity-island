using System;

namespace Vex.Island
{
    /// <summary>
    /// Scanline rounded-rect used for XShape / Win32 window regions.
    /// Coalesces identical insets into taller rectangles.
    /// </summary>
    public static class IslandShape
    {
        public static int BuildRoundedRect(int width, int height, int radius, int[] xywh)
        {
            if (width <= 0 || height <= 0 || xywh == null)
                return 0;

            var maxR = Math.Min(width, height) / 2;
            if (radius < 0)
                radius = 0;
            if (radius > maxR)
                radius = maxR;

            var count = 0;
            var runY = 0;
            var runInset = InsetForRow(0, height, radius);
            var runH = 1;

            for (var y = 1; y < height; y++)
            {
                var inset = InsetForRow(y, height, radius);
                if (inset == runInset)
                {
                    runH++;
                    continue;
                }

                if (!Write(xywh, ref count, runInset, runY, width - 2 * runInset, runH))
                    return count;
                runY = y;
                runInset = inset;
                runH = 1;
            }

            Write(xywh, ref count, runInset, runY, width - 2 * runInset, runH);
            return count;
        }

        public static bool Contains(int[] xywh, int count, int px, int py)
        {
            for (var i = 0; i < count; i++)
            {
                var o = i * 4;
                var x = xywh[o];
                var y = xywh[o + 1];
                var w = xywh[o + 2];
                var h = xywh[o + 3];
                if (px >= x && py >= y && px < x + w && py < y + h)
                    return true;
            }

            return false;
        }

        static int InsetForRow(int y, int height, int radius)
        {
            if (radius <= 0)
                return 0;
            double yy;
            if (y < radius)
                yy = radius - y - 0.5;
            else if (y >= height - radius)
                yy = y - (height - radius) + 0.5;
            else
                return 0;

            if (yy < 0)
                yy = 0;
            if (yy > radius)
                yy = radius;
            return (int)Math.Ceiling(radius - Math.Sqrt((double)radius * radius - yy * yy));
        }

        static bool Write(int[] xywh, ref int count, int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0)
                return true;
            var o = count * 4;
            if (o + 4 > xywh.Length)
                return false;
            xywh[o] = x;
            xywh[o + 1] = y;
            xywh[o + 2] = w;
            xywh[o + 3] = h;
            count++;
            return true;
        }
    }
}
