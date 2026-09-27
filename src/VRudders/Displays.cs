using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace VRudders;

internal sealed class AxisTrack : Control
{
    double value;
    bool active;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Value { get => value; set { this.value = Math.Clamp(value, -1, 1); Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Active { get => active; set { active = value; Invalidate(); } }
    public AxisTrack() { Height = 55; DoubleBuffered = true; Margin = new(3, 0, 3, 0); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        int left = 12, right = Width - 12, middle = Width / 2, y = 17;
        using var line = new Pen(Color.FromArgb(192, 202, 213), 3);
        g.DrawLine(line, left, y, right, y); g.DrawLine(line, middle, y - 7, middle, y + 7);
        using var brush = new SolidBrush(active ? Color.FromArgb(0, 123, 167) : Color.Gray);
        float x = left + (float)((value + 1) / 2) * (right - left); g.FillEllipse(brush, x - 7, y - 7, 14, 14);
        TextRenderer.DrawText(g, "−100%", Font, new Point(0, 32), Color.DimGray);
        TextRenderer.DrawText(g, "0", Font, new Point(middle - 5, 32), Color.DimGray);
        TextRenderer.DrawText(g, "+100%", Font, new Rectangle(right - 100, 32, 100, 20), Color.DimGray, TextFormatFlags.Right);
    }
}

internal sealed class CurveDisplay : Control
{
    Axis axis = new("Z", 0, 65535, 2);
    Tuning tuning = new();
    Calibration? calibration;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public double Input { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public double Output { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Live { get; set; }
    public CurveDisplay() { DoubleBuffered = true; MinimumSize = new(280, 320); }
    public void SetResponse(Axis a, Tuning t, Calibration? c) { axis = a; tuning = t; calibration = c; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using var heading = new Font(Font, FontStyle.Bold);
        TextRenderer.DrawText(g, "Yaw response", heading, new Point(12, 6), ForeColor);
        var rect = new RectangleF(44, 48, Width - 64, Math.Min(Height - 150, Width - 64));
        if (rect.Width <= 0 || rect.Height <= 0) return;
        PointF Map(double x, double y) => new(rect.Left + (float)((x + 1) * 0.5) * rect.Width, rect.Bottom - (float)((y + 1) * 0.5) * rect.Height);
        using var grid = new Pen(Color.FromArgb(219, 226, 233)); using var diagonal = new Pen(Color.Silver) { DashStyle = DashStyle.Dash };
        for (int i = 0; i <= 4; i++) { float x = rect.Left + rect.Width * i / 4, y = rect.Top + rect.Height * i / 4; g.DrawLine(grid, x, rect.Top, x, rect.Bottom); g.DrawLine(grid, rect.Left, y, rect.Right, y); }
        g.DrawLine(diagonal, Map(-1, -1), Map(1, 1));
        PointF[] points = new PointF[201];
        for (int i = 0; i < points.Length; i++)
        {
            double x = i / 100.0 - 1; uint raw = (uint)Math.Round(axis.Min + (x + 1) / 2 * (axis.Max - axis.Min));
            points[i] = Map(x, YawProcessor.Shape(YawProcessor.Input(raw, axis, tuning, calibration), tuning));
        }
        using var curvePen = new Pen(Color.FromArgb(0, 123, 167), 2.5f); g.DrawLines(curvePen, points);
        if (Live)
        {
            PointF dot = Map(Input, Output); using var dotBrush = new SolidBrush(Color.FromArgb(224, 122, 40));
            g.FillEllipse(dotBrush, dot.X - 6, dot.Y - 6, 12, 12);
        }
        TextRenderer.DrawText(g, "+100", Font, new Point(0, (int)rect.Top - 9), Color.DimGray);
        TextRenderer.DrawText(g, "−100", Font, new Point(0, (int)rect.Bottom - 9), Color.DimGray);
        TextRenderer.DrawText(g, "Physical axis  −100%  →  +100%", Font, new Point((int)rect.Left, (int)rect.Bottom + 12), Color.DimGray);
        string note = "Line: calibrated response target\nDot: live output, including smoothing / trim\nDashed: original linear response";
        TextRenderer.DrawText(g, note, Font, new Rectangle(12, (int)rect.Bottom + 45, Width - 20, 90), Color.DimGray, TextFormatFlags.WordBreak);
    }
}
