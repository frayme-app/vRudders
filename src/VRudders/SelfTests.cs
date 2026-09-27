namespace VRudders;

internal static class SelfTests
{
    public static string Run()
    {
        var axis = new Axis("Z", 0, 65535, 2);
        var processor = new YawProcessor();
        var calibration = new Calibration(1000, 29000, 64000);
        var tuned = new Tuning { Bypass = false, CenterDeadZone = .04, LeftEndZone = .08, RightEndZone = .08, Curve = .7 };
        calibration.Validate(axis); tuned.Validate();
        Check(calibration.Normalize(1000) == -1 && calibration.Normalize(29000) == 0 && calibration.Normalize(64000) == 1, "asymmetric calibration endpoints");
        Near(calibration.Normalize(15000), -.5, "left calibration midpoint"); Near(calibration.Normalize(46500), .5, "right calibration midpoint");
        Check(calibration.Normalize(0) == -1 && calibration.Normalize(65535) == 1, "calibration clamping");
        Reject(() => new Calibration(1000, 1200, 64000).Validate(axis), "insufficient calibration travel");
        Reject(() => new Calibration(30000, 20000, 64000).Validate(axis), "invalid calibration order");
        Reject(() => (tuned with { CenterDeadZone = double.NaN }).Validate(), "non-finite settings");
        Reject(() => (tuned with { Sensitivity = 2 }).Validate(), "invalid sensitivity");
        Reject(() => (tuned with { PrecisionKey = 0x79 }).Validate(), "conflicting keys");

        foreach (bool reverse in new[] { false, true })
        {
            // Even non-default optional settings must have no effect in bypass.
            var bypass = tuned with { Bypass = true, Reverse = reverse, UseCalibration = true, SmoothingMs = 150, Sensitivity = .4, PrecisionEnabled = true, TrimEnabled = true };
            for (uint raw = 0; raw <= 65535; raw++)
            {
                processor.ChangeTrim(.1);
                ushort actual = Joysticks.ToVirtual(processor.Process(raw, axis, bypass, calibration, .016, true));
                ushort expected = (ushort)(reverse ? 65535 - raw : raw);
                Check(actual == expected, "exact bypass conversion");
                Check(VirtualOutput.Encode(actual, true).SequenceEqual(new byte[] { 2, 1, (byte)expected, (byte)(expected >> 8), 1 }), "exact bypass packet");
            }
        }
        Check(Joysticks.ToVirtual(0) == 32768, "neutral encoding");
        Check(VirtualOutput.Encode(0x1234, true).SequenceEqual(new byte[] { 2, 1, 0x34, 0x12, 1 }), "protocol encoding");
        Check(new Joystick(0, "VRudders POC", 0xDEED, 0xFEED, []).IsVirtual, "legacy virtual identity");
        Check(new Joystick(0, "VRudders Yaw", 0xDEED, 0xFEED, []).IsVirtual, "renamed virtual identity");
        Check(!new Joystick(0, "Other sample", 0xDEED, 0xFEED, []).IsVirtual && !new Joystick(0, "VRudders Yaw", 0x10F5, 0x7012, []).IsVirtual, "reject unrelated identity");

        foreach (double c in new[] { -1.0, -.5, 0.0, .5, 1.0 })
        foreach (double dz in new[] { 0.0, .04, .2 })
        foreach (double end in new[] { 0.0, .1, .3 })
        {
            var t = tuned with { Curve = c, CenterDeadZone = dz, LeftEndZone = end, RightEndZone = end };
            double previous = -1;
            for (int i = -10000; i <= 10000; i++)
            {
                double x = i / 10000.0, y = YawProcessor.Shape(x, t);
                Check(double.IsFinite(y) && y >= -1 && y <= 1 && y >= previous, "bounded monotonic curve");
                Near(y, -YawProcessor.Shape(-x, t), "curve symmetry"); previous = y;
            }
            Check(YawProcessor.Shape(0, t) == 0 && YawProcessor.Shape(-1, t) == -1 && YawProcessor.Shape(1, t) == 1, "center and full authority");
            Check(Math.Abs(YawProcessor.Shape(dz + 1e-8, t)) < 1e-6 && YawProcessor.Shape(dz, t) == 0, "dead-zone continuity");
        }
        Near(YawProcessor.Shape(1, tuned with { Sensitivity = .4 }), .4, "output strength");
        Near(YawProcessor.Shape(-.9, tuned with { LeftEndZone = .1 }), -1, "left travel endpoint");
        Near(YawProcessor.Shape(.8, tuned with { RightEndZone = .2 }), 1, "right travel endpoint");
        var aggressive = new Tuning { Bypass = false, Curve = -1 };
        Near(YawProcessor.Shape(.25, aggressive), .578125, "aggressive curve amplifies quarter travel");
        Near(YawProcessor.Shape(-.25, aggressive), -.578125, "aggressive left yaw");
        Near(YawProcessor.Shape(.25, aggressive with { Curve = 1 }), .015625, "existing soft curve preserved");
        Near(YawProcessor.Shape(.25, aggressive with { Curve = 0 }), .25, "linear midpoint between curve directions");

        double Filter(int steps)
        {
            processor.Reset(); var t = new Tuning { Bypass = false, SmoothingMs = 100 };
            processor.Process(0, axis, t, null, 0, false);
            double value = -1;
            for (int i = 0; i < steps; i++) value = processor.Process(65535, axis, t, null, .1 / steps, false);
            return value;
        }
        Near(Filter(10), Filter(20), "time-based smoothing independence"); Near(Filter(10), 1 - 2 / Math.E, "smoothing time constant");
        var precision = new Tuning { Bypass = false, PrecisionEnabled = true, PrecisionCurve = 1, TrimEnabled = true };
        processor.Reset();
        double before = processor.Process(49151, axis, precision, null, .016, false);
        double after = processor.Process(49151, axis, precision, null, .016, true);
        Check(after < before && before - after < .03, "precision blend avoids abrupt switch");
        for (int i = 0; i < 30; i++) processor.Process(65535, axis, precision, null, .016, true);
        Near(processor.Process(65535, axis, precision, null, .016, true), 1, "precision retains endpoints");
        processor.ChangeTrim(1);
        for (int i = 0; i < 30; i++) processor.Process(32768, axis, precision, null, .1, false);
        Near(processor.Trim, .2, "trim bounded at twenty percent");
        processor.Reset(); Check(processor.Trim == 0 && !processor.PrecisionActive, "stop resets temporal state");

        string folder = Path.Combine(Path.GetTempPath(), "VRudders-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string profilePath = Path.Combine(folder, "profile.json");
        try
        {
            var profile = new FlightProfile(Guid.NewGuid().ToString("D"), "Test aircraft", tuned);
            SettingsStore.Export(profilePath, profile);
            var imported = SettingsStore.Import(profilePath);
            Check(imported.Name == profile.Name && imported.Tuning == tuned && imported.Id != profile.Id, "profile round trip and unique import identity");
            string legacy = File.ReadAllText(profilePath).Replace("\"Version\": 2", "\"Version\": 1");
            File.WriteAllText(profilePath, legacy);
            Check(SettingsStore.Import(profilePath).Tuning == tuned, "legacy profile keeps its response");
            SettingsStore.Export(profilePath, profile with { Tuning = aggressive });
            Check(SettingsStore.Import(profilePath).Tuning.Curve == -1, "aggressive profile round trip");
            SettingsStore.TestDirectory = folder;
            var oldSettings = new UserSettings { Version = 1, Profiles = [profile], SelectedProfile = profile.Id };
            string oldText = System.Text.Json.JsonSerializer.Serialize(oldSettings);
            File.WriteAllText(SettingsStore.FilePath, oldText);
            var migrated = SettingsStore.Load(out var migrationError);
            Check(migrationError == null && migrated.Version == 2 && migrated.Profiles.Single() == profile, "settings migration preserves custom names and response");
            SettingsStore.Save(migrated);
            Check(File.ReadAllText(Directory.GetFiles(folder, "settings-v1-backup-*.json").Single()) == oldText, "legacy settings backup is exact");
            SettingsStore.TestDirectory = null;
            File.WriteAllText(profilePath, "{\"Version\":99,\"Profile\":null}"); Reject(() => SettingsStore.Import(profilePath), "future profile schema");
            File.WriteAllText(profilePath, "broken"); Reject(() => SettingsStore.Import(profilePath), "malformed profile");
            File.WriteAllText(profilePath, new string(' ', 256 * 1024 + 1)); Reject(() => SettingsStore.Import(profilePath), "oversized profile");
            var settings = new UserSettings { Profiles = [profile, profile] }; Reject(settings.Validate, "duplicate profile IDs");
        }
        finally { SettingsStore.TestDirectory = null; foreach (string file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
        return "PASS: all 65,536 Z values and encoded packets in both directions; calibration; soft/aggressive monotonic curves; dead-zone continuity; sensitivity; smoothing timing; precision transitions; trim/reset; profile validation, migration/backup and round-trip; legacy/new device identity. No driver writes.";
    }
    static void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException("Self-test failed: " + name); }
    static void Near(double actual, double expected, string name) => Check(Math.Abs(actual - expected) < 1e-10, name);
    static void Reject(Action action, string name)
    {
        try { action(); } catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException) { return; }
        throw new InvalidOperationException("Self-test failed: accepted " + name);
    }
}
