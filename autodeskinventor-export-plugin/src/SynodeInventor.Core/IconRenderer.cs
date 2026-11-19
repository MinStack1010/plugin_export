using System;

namespace Synode.Inventor.Core
{
    /// <summary>
    /// Draws the "Export to GLB" button icon procedurally (no image file, no System.Drawing needed):
    /// a rounded square in Synode blue with a white downward arrow (export).
    /// Returns 32-bit BGRA pixels with premultiplied alpha, top row first (top-down).
    /// </summary>
    public static class IconRenderer
    {
        private const int Samples = 4;

        public static uint[] Render(int size)
        {
            if (size < 8 || size > 256) throw new ArgumentOutOfRangeException(nameof(size));
            var pixels = new uint[size * size];
            const double bgR = 0x1E, bgG = 0x6F, bgB = 0xD9;

            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    int inBg = 0, inFg = 0;
                    for (int sy = 0; sy < Samples; sy++)
                    {
                        for (int sx = 0; sx < Samples; sx++)
                        {
                            double x = (px + (sx + 0.5) / Samples) / size;
                            double y = (py + (sy + 0.5) / Samples) / size;
                            if (InRoundedRect(x, y, 0.06, 0.18))
                            {
                                inBg++;
                                if (InArrow(x, y)) inFg++;
                            }
                        }
                    }
                    int total = Samples * Samples;
                    double alpha = (double)inBg / total;
                    if (alpha <= 0) { pixels[py * size + px] = 0; continue; }
                    double fgShare = inBg == 0 ? 0 : (double)inFg / inBg;
                    // Color (not premultiplied), then multiplied by alpha.
                    double r = bgR + (255 - bgR) * fgShare;
                    double g = bgG + (255 - bgG) * fgShare;
                    double b = bgB + (255 - bgB) * fgShare;
                    uint a8 = (uint)Math.Round(alpha * 255);
                    uint r8 = (uint)Math.Round(r * alpha);
                    uint g8 = (uint)Math.Round(g * alpha);
                    uint b8 = (uint)Math.Round(b * alpha);
                    pixels[py * size + px] = (a8 << 24) | (r8 << 16) | (g8 << 8) | b8;
                }
            }
            return pixels;
        }

        private static bool InRoundedRect(double x, double y, double margin, double radius)
        {
            double min = margin, max = 1 - margin;
            if (x < min || x > max || y < min || y > max) return false;
            double cx = Math.Clamp(x, min + radius, max - radius);
            double cy = Math.Clamp(y, min + radius, max - radius);
            double dx = x - cx, dy = y - cy;
            return dx * dx + dy * dy <= radius * radius;
        }

        private static bool InArrow(double x, double y)
        {
            // Arrow shaft.
            if (x >= 0.42 && x <= 0.58 && y >= 0.18 && y <= 0.52) return true;
            // Arrow head: triangle (0.24,0.48) (0.76,0.48) (0.5,0.74).
            if (y >= 0.48 && y <= 0.74)
            {
                double half = 0.26 * (0.74 - y) / 0.26;
                if (Math.Abs(x - 0.5) <= half) return true;
            }
            // Bottom bar.
            return x >= 0.22 && x <= 0.78 && y >= 0.79 && y <= 0.86;
        }
    }
}
