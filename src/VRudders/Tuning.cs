namespace VRudders;

internal sealed record Calibration(uint Minimum, uint Center, uint Maximum)
{
    public void Validate(Axis axis)
    {
        double minimumTravel = (axis.Max - axis.Min) * 0.05;
        if (Minimum < axis.Min || Maximum > axis.Max || Center <= Minimum || Center >= Maximum ||
            Center - Minimum < minimumTravel || Maximum - Center < minimumTravel)
            throw new InvalidDataException("Capture both pedal stops and a center at least 5% of the axis range from each stop.");
    }
    public double Normalize(uint raw) => raw < Center
        ? Math.Clamp(((double)raw - Center) / (Center - Minimum), -1, 0)
        : Math.Clamp(((double)raw - Center) / (Maximum - Center), 0, 1);
}

internal sealed record Tuning
{
    public bool Bypass { get; init; } = true;
    public bool Reverse { get; init; }
    public bool UseCalibration { get; init; }
    public double CenterDeadZone { get; init; }
    public double LeftEndZone { get; init; }
    public double RightEndZone { get; init; }
    public double Curve { get; init; }
    public double Sensitivity { get; init; } = 1;
    public double SmoothingMs { get; init; }
    public bool PrecisionEnabled { get; init; }
    public int PrecisionKey { get; init; } = 0x77; // F8, held.
    public double PrecisionCurve { get; init; } = 0.8;
    public bool TrimEnabled { get; init; }

    public void Validate()
    {
        static void Range(double v, double min, double max, string name)
        {
            if (!double.IsFinite(v) || v < min || v > max) throw new InvalidDataException($"Invalid {name}: expected {min}–{max}.");
        }
        Range(CenterDeadZone, 0, 0.2, "center dead zone");
        Range(LeftEndZone, 0, 0.3, "left end zone"); Range(RightEndZone, 0, 0.3, "right end zone");
        Range(Curve, -1, 1, "response curve"); Range(PrecisionCurve, 0, 1, "precision curve");
        Range(Sensitivity, 0.1, 1, "sensitivity"); Range(SmoothingMs, 0, 200, "smoothing time");
        if (PrecisionKey < 0x70 || PrecisionKey > 0x7B || PrecisionKey is 0x78 or 0x79 or 0x7A)
            throw new InvalidDataException("Precision key must be F1–F8 or F12. F9–F11 are reserved for trim.");
    }
}

internal sealed class YawProcessor
{
    double? filtered;
    double precisionMix;
    double trim;
    double targetTrim;
    public double Trim => trim;
    public bool PrecisionActive => precisionMix > 0;
    public void Reset() { filtered = null; precisionMix = trim = targetTrim = 0; }
    public void ChangeTrim(double amount) => targetTrim = Math.Clamp(targetTrim + amount, -0.2, 0.2);
    public void ResetTrim() => targetTrim = 0;

    public static double Input(uint raw, Axis axis, Tuning tuning, Calibration? calibration)
    {
        double value = !tuning.Bypass && tuning.UseCalibration && calibration != null ? calibration.Normalize(raw) : Joysticks.Normalize(raw, axis);
        return tuning.Reverse ? -value : value;
    }

    public static double Shape(double value, Tuning tuning, double precisionMix = 0)
    {
        value = Math.Clamp(value, -1, 1);
        if (tuning.Bypass) return value;
        double magnitude = Math.Abs(value);
        double end = 1 - (value < 0 ? tuning.LeftEndZone : tuning.RightEndZone);
        double span = end - tuning.CenterDeadZone;
        double t = Math.Clamp((magnitude - tuning.CenterDeadZone) / span, 0, 1);
        double curve = tuning.Curve + (Math.Max(tuning.Curve, tuning.PrecisionCurve) - tuning.Curve) * Math.Clamp(precisionMix, 0, 1);
        // Positive bias softens the center; negative bias amplifies small input.
        // Both branches are monotonic, meet at linear, and retain exact endpoints.
        double shaped = curve >= 0 ? (1 - curve) * t + curve * t * t * t
            : (1 + curve) * t - curve * (1 - (1 - t) * (1 - t) * (1 - t));
        return Math.CopySign(shaped * tuning.Sensitivity, value);
    }

    public double Process(uint raw, Axis axis, Tuning tuning, Calibration? calibration, double seconds, bool precisionHeld)
    {
        double value = Input(raw, axis, tuning, calibration);
        // Exact baseline path: no filtering, calibration, dead zones, curve, or trim.
        if (tuning.Bypass) { Reset(); return value; }
        seconds = double.IsFinite(seconds) ? Math.Clamp(seconds, 0, 0.1) : 0;
        double precisionTarget = tuning.PrecisionEnabled && precisionHeld ? 1 : 0;
        precisionMix = MoveTowards(precisionMix, precisionTarget, seconds / 0.3);
        value = Shape(value, tuning, precisionMix);
        if (tuning.SmoothingMs > 0 && filtered.HasValue)
            value = filtered.Value + (value - filtered.Value) * (1 - Math.Exp(-seconds * 1000 / tuning.SmoothingMs));
        filtered = value;
        if (!tuning.TrimEnabled) targetTrim = trim = 0;
        trim = MoveTowards(trim, targetTrim, seconds * 0.1);
        return Math.Clamp(value + trim, -1, 1);
    }

    static double MoveTowards(double value, double target, double step) => value + Math.Clamp(target - value, -step, step);
}
