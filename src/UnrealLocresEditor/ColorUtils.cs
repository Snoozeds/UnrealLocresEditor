using Avalonia.Media;
using System;

public static class ColorUtils
{
    public static Color ChangeColorLuminosity(Color color, double factor)
    {
        var hsl = ColorToHSL(color);
        hsl.L = Math.Clamp(hsl.L + factor, 0, 1);
        return HSLToColor(hsl);
    }

    // Helper method to convert RGB to HSL
    public static (double H, double S, double L) ColorToHSL(Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double h = 0, s = 0, l = (max + min) / 2;

        if (max != min)
        {
            double delta = max - min;
            s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);

            if (max == r)
                h = (g - b) / delta + (g < b ? 6 : 0);
            else if (max == g)
                h = (b - r) / delta + 2;
            else
                h = (r - g) / delta + 4;

            h /= 6;
        }

        return (h, s, l);
    }

    // Helper method to convert HSL to RGB
    public static Color HSLToColor((double H, double S, double L) hsl)
    {
        double r, g, b;

        if (hsl.S == 0)
        {
            r = g = b = hsl.L * 255.0;
        }
        else
        {
            double hueToRGB = (hsl.H + 1 / 3.0) % 1;
            double temp2 = hsl.L < 0.5 ? hsl.L * (1 + hsl.S) : (hsl.L + hsl.S) - hsl.L * hsl.S;
            double temp1 = 2 * hsl.L - temp2;

            r = 255 * HueToRGB(temp1, temp2, hueToRGB);
            g = 255 * HueToRGB(temp1, temp2, hsl.H);
            b = 255 * HueToRGB(temp1, temp2, hsl.H - 1 / 3.0);
        }

        return Color.FromArgb(255, (byte)r, (byte)g, (byte)b);
    }

    // Helper method to calculate RGB value based on HSL
    private static double HueToRGB(double temp1, double temp2, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1 / 6.0) return temp1 + (temp2 - temp1) * 6 * t;
        if (t < 1 / 2.0) return temp2;
        if (t < 2 / 3.0) return temp1 + (temp2 - temp1) * (2 / 3.0 - t) * 6;
        return temp1;
    }
}
