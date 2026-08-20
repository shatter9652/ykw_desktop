using System.Reflection;

namespace YKWHome.Core.Saves;

/// <summary>
/// Loads yokai icons from embedded resources (primary) or disk (fallback).
/// Embedded resource naming: YKWHome.Core.Icons.{GAME_DIR}.{filename}
/// </summary>
public class IconService
{
    private readonly Assembly _assembly;
    private readonly Dictionary<string, string> _embeddedIcons = [];
    private readonly Dictionary<string, string> _crcToIcon = [];
    private readonly string? _diskBase;

    // Game icon directories (matching embedded resource paths)
    private static readonly Dictionary<string, string> GameIconDirs = new()
    {
        ["yw2"] = "YKW2/pngs",
        ["yw3"] = "YKW3/pngs",
        ["ykb"] = "YKWB/base_png",
        ["b2"] = "B2/base_pngs",
    };

    // Fallback order when icon is missing from a game's folder
    private static readonly string[] FallbackOrder = ["yw3", "yw2", "ykb", "b2"];

    public IconService()
    {
        _assembly = Assembly.GetExecutingAssembly();

        // Index all embedded icons
        foreach (string name in _assembly.GetManifestResourceNames())
        {
            if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                // Extract the relative path from the resource name
                // e.g., "YKWHome.Core.Icons.YKW3.pngs.c001000.00.png" -> "YKW3/pngs/c001000.00.png"
                string relative = name["YKWHome.Core.Icons.".Length..].Replace('.', '/').Replace('_', ' ');
                // Fix path separators and restore dots in filenames
                string fixedPath = FixResourcePath(name);
                _embeddedIcons[fixedPath] = name;
            }
        }

        // Also try disk fallback
        _diskBase = FindIconsDirectory();
        LoadCrcToIconMap();

        Console.WriteLine($"[icons] Embedded: {_embeddedIcons.Count} icons");
        Console.WriteLine($"[icons] Disk fallback: {_diskBase ?? "none"}");
    }

    /// <summary>Get the file path for a yokai icon.</summary>
    public string? GetIconPath(string game, int yokaiId)
    {
        string? dir = GameIconDirs.GetValueOrDefault(game);
        if (dir == null) return null;

        string filename = game switch
        {
            "yw2" or "ykb" => $"y{yokaiId + 100}000.00.png",
            "yw3" => $"c{yokaiId}000.00.png",
            "b2" => $"y{yokaiId + 100}000.00.png",
            _ => "",
        };

        if (string.IsNullOrEmpty(filename)) return null;

        // Try embedded resources first
        string? embedded = FindEmbeddedIcon(dir, filename);
        if (embedded != null) return $"embedded:{embedded}";

        // Try disk fallback
        if (_diskBase != null)
        {
            string diskPath = Path.Combine(_diskBase, dir, filename);
            if (File.Exists(diskPath)) return diskPath;
        }

        // Fallback to other games
        foreach (string fallback in FallbackOrder)
        {
            if (fallback == game) continue;
            string? fallbackDir = GameIconDirs.GetValueOrDefault(fallback);
            if (fallbackDir == null) continue;

            embedded = FindEmbeddedIcon(fallbackDir, filename);
            if (embedded != null) return $"embedded:{embedded}";

            if (_diskBase != null)
            {
                string fallbackPath = Path.Combine(_diskBase, fallbackDir, filename);
                if (File.Exists(fallbackPath)) return fallbackPath;
            }
        }

        return null;
    }

    /// <summary>Get icon path by CRC32 ID (used by YW1/Blasters).</summary>
    public string? GetIconByCrc32(int crc32)
    {
        if (_crcToIcon.TryGetValue(crc32.ToString("X8"), out var iconName))
        {
            foreach (var (_, dir) in GameIconDirs)
            {
                string? embedded = FindEmbeddedIcon(dir, $"{iconName}.00.png");
                if (embedded != null) return $"embedded:{embedded}";

                if (_diskBase != null)
                {
                    string path = Path.Combine(_diskBase, dir, $"{iconName}.00.png");
                    if (File.Exists(path)) return path;
                }
            }
        }
        return null;
    }

    /// <summary>Get icon bytes from the path returned by GetIconPath.</summary>
    public byte[]? GetIconBytes(string? iconPath)
    {
        if (string.IsNullOrEmpty(iconPath)) return null;

        if (iconPath.StartsWith("embedded:"))
        {
            string resourceName = iconPath["embedded:".Length..];
            using var stream = _assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return null;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        if (File.Exists(iconPath))
            return File.ReadAllBytes(iconPath);

        return null;
    }

    /// <summary>Check if icons are available.</summary>
    public bool HasIcons => _embeddedIcons.Count > 0 || (_diskBase != null && Directory.Exists(_diskBase));

    /// <summary>Count of embedded icons.</summary>
    public int EmbeddedIconCount => _embeddedIcons.Count;

    private string? FindEmbeddedIcon(string dir, string filename)
    {
        // Try multiple resource name patterns
        // Pattern 1: YKWHome.Core.Icons.{dir}.{filename} (dots as separators)
        // Pattern 2: YKWHome.Core.Icons.{dir_with_underscores}.{filename}

        string[] patterns =
        [
            $"YKWHome.Core.Icons.{dir}.{filename}",
            $"YKWHome.Core.Icons.{dir.Replace('/', '.')}.{filename}",
        ];

        foreach (string pattern in patterns)
        {
            if (_embeddedIcons.TryGetValue(pattern, out var name))
                return name;
        }

        // Brute force search
        string search = $"{dir}/{filename}";
        foreach (var kv in _embeddedIcons)
        {
            if (kv.Key.EndsWith(search, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }

        return null;
    }

    /// <summary>Fix resource path: dots in filenames (like c001000.00.png) get mangled.</summary>
    private static string FixResourcePath(string resourceName)
    {
        // "YKWHome.Core.Icons.YKW3.pngs.c001000.00.png" -> "YKW3/pngs/c001000.00.png"
        string prefix = "YKWHome.Core.Icons.";
        if (!resourceName.StartsWith(prefix)) return resourceName;

        string rest = resourceName[prefix.Length..];
        // Resource names use dots as separators, but filenames have dots too.
        // Convention: directory structure is fixed depth (game/pngs), rest is filename.
        // We know game dirs are single tokens (YKW3, YKW2, YKWB, B2) and pngs dirs.
        // Split and take first 2 as dirs, join the rest as filename.
        var parts = rest.Split('.');
        if (parts.Length >= 3)
        {
            string game = parts[0];
            string subdir = parts[1];
            string filename = string.Join(".", parts[2..]);
            return $"{game}/{subdir}/{filename}";
        }
        return rest;
    }

    private void LoadCrcToIconMap()
    {
        string mapPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "resources", "data", "crc32_to_icon.json"
        );
        if (!File.Exists(mapPath)) return;

        try
        {
            var json = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(File.ReadAllText(mapPath));
            if (json == null) return;
            foreach (var kv in json)
                _crcToIcon[kv.Key] = kv.Value.GetString() ?? "";
        }
        catch { /* ignore parse errors */ }
    }

    private static string? FindIconsDirectory()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Icons"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "YoKaiIcons"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "YKWHome", "YoKaiIcons"),
        ];

        foreach (string candidate in candidates)
        {
            string resolved = Path.GetFullPath(candidate);
            if (Directory.Exists(resolved) && Directory.Exists(Path.Combine(resolved, "YKW3")))
                return resolved;
        }
        return null;
    }
}
