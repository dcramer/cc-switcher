using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CcSwitcher;

sealed class Profile
{
    /// <summary>Stable; names the profile's folder, so it doesn't change on rename.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#D97757";

    /// <summary>True for the one profile that is Claude's own default data folder, i.e. the existing sign-in.</summary>
    public bool UsesDefaultData { get; set; }

    /// <summary>Give Claude Code sessions started from this profile their own CLAUDE_CONFIG_DIR.</summary>
    public bool SeparateCodeConfig { get; set; }

    /// <summary>Everything CC Switcher keeps for this profile; removed along with it.</summary>
    [JsonIgnore]
    public string Folder => Path.Combine(Settings.ProfilesRoot, Id);

    [JsonIgnore]
    public string DataDir => Path.Combine(Folder, "data");

    [JsonIgnore]
    public string CodeConfigDir => Path.Combine(Folder, "claude-code");

    /// <summary>The profile's tinted icon, used by its shortcuts and windows.</summary>
    [JsonIgnore]
    public string IconPath => Path.Combine(Settings.IconDir, Id + ".ico");
}

sealed class Settings
{
    /// <summary>Overrides auto-detection of the Claude Desktop executable.</summary>
    public string? ClaudePath { get; set; }
    public bool CloseAfterLaunch { get; set; }
    public string? LastProfileId { get; set; }
    public List<Profile> Profiles { get; set; } = [];

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Everything lives in one folder in the user profile rather than AppData. Claude Desktop is
    // an MSIX package: Windows redirects its AppData writes (and those of anything started from
    // inside it) into the package's private cache, where a profile's data would be invisible to
    // us and wiped when Claude is reinstalled.
    public static string HomeDir =>
        Environment.GetEnvironmentVariable("CC_SWITCHER_HOME") is { Length: > 0 } home
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cc-switcher");

    public static string FilePath => Path.Combine(HomeDir, "settings.json");
    public static string IconDir => Path.Combine(HomeDir, "icons");
    public static string ProfilesRoot => Path.Combine(HomeDir, "profiles");

    public static Settings Load()
    {
        if (!File.Exists(FilePath))
        {
            var initial = CreateInitial();
            initial.Save();
            return initial;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions) ?? CreateInitial();
            // Hand-edited settings can leave a color out; drawing copes with that, so only real ones are brought up to date.
            foreach (var profile in settings.Profiles.Where(p => p.Color is not null))
                profile.Color = IconFactory.Current(profile.Color);
            return settings;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Couldn't read {FilePath}:\n{ex.Message}\n\nFix or delete the file and try again.");
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(HomeDir);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temp, FilePath, overwrite: true);
    }

    public Profile? Find(string idOrName) =>
        Profiles.FirstOrDefault(p => p.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase))
        ?? Profiles.FirstOrDefault(p => p.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase));

    public Profile Add(string name, string color, bool separateCodeConfig)
    {
        var profile = new Profile { Id = UniqueId(name), Name = name, Color = color, SeparateCodeConfig = separateCodeConfig };
        Profiles.Add(profile);
        return profile;
    }

    // Also avoids folders left on disk, so a new profile never inherits an old one's sign-in.
    string UniqueId(string name)
    {
        var slug = Slug(name);
        var id = slug;
        for (var n = 2; Profiles.Any(p => p.Id == id) || Directory.Exists(Path.Combine(ProfilesRoot, id)); n++)
            id = $"{slug}-{n}";
        return id;
    }

    static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
                sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length > 0 ? slug : "profile";
    }

    static Settings CreateInitial() => new()
    {
        Profiles =
        [
            new Profile { Id = "personal", Name = "Personal", Color = "#D97757", UsesDefaultData = true },
            new Profile { Id = "work", Name = "Work", Color = "#6A9BCC" },
        ],
    };
}
