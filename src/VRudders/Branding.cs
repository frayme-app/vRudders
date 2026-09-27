using System.Drawing.Drawing2D;
using System.Reflection;

namespace VRudders;

internal static class Branding
{
    // Embedded assets work in a source build or self-contained install; no loose
    // image lookup, downloads, or work inside the forwarding timer is required.
    public static Icon AppIcon { get; } = LoadIcon();
    public static Image Helicopter { get; } = LoadImage("VRudders.Assets.Helicopter");
    static Stream Open(string name) => Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
        ?? throw new InvalidOperationException("Missing application artwork: " + name);
    static Icon LoadIcon()
    {
        using var stream = Open("VRudders.Assets.AppIcon");
        using var icon = new Icon(stream, 256, 256);
        return (Icon)icon.Clone();
    }
    static Image LoadImage(string name)
    {
        using var stream = Open(name);
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}

internal sealed class FlightHeader : Control
{
    public FlightHeader()
    {
        Dock = DockStyle.Fill; DoubleBuffered = true; Margin = new(0, 0, 0, 12);
        AccessibleName = "VRudders · Rudder Control";
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        float scale = DeviceDpi / 96f;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        using var backdrop = new LinearGradientBrush(ClientRectangle, Color.FromArgb(8, 24, 38), Color.FromArgb(5, 17, 30), 0f);
        g.FillRectangle(backdrop, ClientRectangle);
        // Fit the full aircraft vertically, retaining its rotor and muzzle flashes.
        float imageWidth = Height * Branding.Helicopter.Width / (float)Branding.Helicopter.Height;
        float imageLeft = Width - imageWidth;
        g.DrawImage(Branding.Helicopter, imageLeft, 0, imageWidth, Height);
        var fadeRect = new RectangleF(imageLeft, 0, imageWidth * .35f, Height);
        using var fade = new LinearGradientBrush(fadeRect, Color.FromArgb(5, 17, 30), Color.FromArgb(0, 5, 17, 30), 0f);
        g.FillRectangle(fade, fadeRect);
        float iconSize = 64 * scale, iconX = 24 * scale, iconY = (Height - iconSize) / 2;
        g.DrawIcon(Branding.AppIcon, new Rectangle((int)iconX, (int)iconY, (int)iconSize, (int)iconSize));
        int x = (int)(106 * scale), center = Height / 2;
        using var title = new Font("Segoe UI", 28, FontStyle.Bold);
        using var caption = new Font("Segoe UI", 9, FontStyle.Bold);
        using var subtitle = new Font("Segoe UI", 10);
        TextRenderer.DrawText(g, "RUDDER CONTROL", caption, new Point(x + 3, center - (int)(49 * scale)), Color.FromArgb(104, 215, 234), TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, "VRudders", title, new Point(x, center - (int)(28 * scale)), Color.White, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, "Your pedals. Your response.", subtitle, new Point(x + 3, center + (int)(26 * scale)), Color.FromArgb(190, 211, 225), TextFormatFlags.NoPadding);
        using var accent = new SolidBrush(Color.FromArgb(255, 170, 85));
        g.FillRectangle(accent, 24 * scale, Height - 3 * scale, 52 * scale, 3 * scale);
    }
}
