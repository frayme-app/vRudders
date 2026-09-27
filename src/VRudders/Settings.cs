using System.Text.Json;

namespace VRudders;

internal sealed record FlightProfile(string Id, string Name, Tuning Tuning)
{
    public const string BaselineId = "working-passthrough";
    public static FlightProfile Baseline => new(BaselineId, "VRudders · Original response", new());
    public override string ToString() => Name;
    public void Validate()
    {
        if (!Guid.TryParse(Id, out _) || string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || Name.Any(char.IsControl) || Tuning == null)
            throw new InvalidDataException("Profile name or ID is invalid.");
        Tuning.Validate();
    }
}

internal sealed class UserSettings
{
    public int Version { get; set; } = 2;
    public string DeviceKey { get; set; } = "";
    public string Axis { get; set; } = "Z";
    public string SelectedProfile { get; set; } = FlightProfile.BaselineId;
    public bool MinimizeToTray { get; set; }
    public List<FlightProfile> Profiles { get; set; } = [];
    public Dictionary<string, Calibration> Calibrations { get; set; } = [];

    public void Validate()
    {
        if (Version is not (1 or 2) || DeviceKey == null || DeviceKey.Length > 512 || Axis is not ("X" or "Y" or "Z" or "R" or "U" or "V") ||
            Profiles == null || Profiles.Count > 100 || Calibrations == null || Calibrations.Count > 100 || SelectedProfile == null)
            throw new InvalidDataException("Settings format or limits are invalid.");
        foreach (var p in Profiles)
        {
            if (p == null) throw new InvalidDataException("Empty profile."); p.Validate();
            if (Version == 1 && p.Tuning.Curve < 0) throw new InvalidDataException("Aggressive curves require profile format 2.");
        }
        if (Profiles.Select(p => p.Id).Distinct().Count() != Profiles.Count) throw new InvalidDataException("Duplicate profile IDs.");
        foreach (var (key, c) in Calibrations)
            if (key.Length > 600 || c == null || c.Minimum >= c.Center || c.Center >= c.Maximum)
                throw new InvalidDataException("Invalid device calibration.");
    }
}

internal static class SettingsStore
{
    internal static string? TestDirectory { get; set; }
    public static string DirectoryPath => TestDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRudders");
    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public static UserSettings Load(out string? error)
    {
        error = null;
        if (!File.Exists(FilePath)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<UserSettings>(ReadBounded(FilePath), Json) ?? throw new InvalidDataException("Empty settings.");
            settings.Validate(); settings.Version = 2; return settings;
        }
        catch (Exception ex) { error = "Saved settings were not loaded: " + ex.Message + " The existing file has been kept."; return new(); }
    }

    public static void Save(UserSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(DirectoryPath);
        if (File.Exists(FilePath))
        {
            using var previous = JsonDocument.Parse(ReadBounded(FilePath));
            if (previous.RootElement.TryGetProperty("Version", out var version) && version.GetInt32() == 1)
            {
                string backup = Path.Combine(DirectoryPath, "settings-v1-backup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + ".json");
                File.Copy(FilePath, backup, false);
            }
        }
        settings.Version = 2;
        AtomicWrite(FilePath, JsonSerializer.Serialize(settings, Json));
    }

    public static FlightProfile Import(string path)
    {
        var envelope = JsonSerializer.Deserialize<ProfileFile>(ReadBounded(path), Json) ?? throw new InvalidDataException("Empty profile.");
        if (envelope.Version is not (1 or 2) || envelope.Profile == null) throw new InvalidDataException("Unsupported profile format.");
        envelope.Profile.Validate();
        if (envelope.Version == 1 && envelope.Profile.Tuning.Curve < 0) throw new InvalidDataException("Aggressive curves require profile format 2.");
        return envelope.Profile with { Id = Guid.NewGuid().ToString("D") };
    }
    public static void Export(string path, FlightProfile profile)
    {
        if (profile.Id == FlightProfile.BaselineId) profile = profile with { Id = Guid.NewGuid().ToString("D") };
        profile.Validate();
        AtomicWrite(path, JsonSerializer.Serialize(new ProfileFile(2, profile), Json));
    }
    static string ReadBounded(string path)
    {
        if (new FileInfo(path).Length > 256 * 1024) throw new InvalidDataException("Settings/profile file exceeds 256 KB.");
        return File.ReadAllText(path);
    }
    static void AtomicWrite(string path, string text)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.Write(text); writer.Flush(); stream.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    sealed record ProfileFile(int Version, FlightProfile Profile);
}
