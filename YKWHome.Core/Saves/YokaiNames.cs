using System.Text.Json;

namespace YKWHome.Core.Saves;

/// <summary>
/// Yo-kai name resolution from ID/Signature.
/// </summary>
public static class YokaiNames
{
    private static Dictionary<int, string>? _nameDb;
    private static Dictionary<int, string>? _crcNameDb;

    public static void Load(string basePath)
    {
        var dataDir = Path.Combine(basePath, "resources", "data");

        // Sequential ID → name (YW2/YW3)
        var namePath = Path.Combine(dataDir, "yokai_names.json");
        if (File.Exists(namePath))
        {
            var json = File.ReadAllText(namePath);
            var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            _nameDb = new Dictionary<int, string>();
            if (raw != null)
                foreach (var kv in raw)
                    if (int.TryParse(kv.Key, out int id))
                        _nameDb[id] = kv.Value.GetString() ?? "";
        }

        // CRC32 → name (B1)
        var crcPath = Path.Combine(dataDir, "crc32_yokai_map.json");
        if (File.Exists(crcPath))
        {
            var json = File.ReadAllText(crcPath);
            var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            _crcNameDb = new Dictionary<int, string>();
            if (raw != null)
                foreach (var kv in raw)
                    if (int.TryParse(kv.Key, System.Globalization.NumberStyles.HexNumber, null, out int id))
                        _crcNameDb[id] = kv.Value.GetString() ?? "";
        }
    }

    public static string Resolve(GameType game, int yokaiId)
    {
        // Try CRC32 map first
        if (_crcNameDb != null && _crcNameDb.TryGetValue(yokaiId, out var name))
        {
            // Extract English name from "日本語 (English)" format
            var match = System.Text.RegularExpressions.Regex.Match(name, @"\(([^)]+)\)$");
            return match.Success ? match.Groups[1].Value.Trim() : name;
        }

        // Try sequential ID
        if (_nameDb != null && _nameDb.TryGetValue(yokaiId, out name))
        {
            var match = System.Text.RegularExpressions.Regex.Match(name, @"\(([^)]+)\)$");
            return match.Success ? match.Groups[1].Value.Trim() : name;
        }

        return $"Yo-kai #{yokaiId}";
    }

    public static string ResolveBySignature(string signature) =>
        YW4SignatureMap.TryGetValue(signature, out var name) ? name : $"Unknown ({signature})";

    /// <summary>Signature → name map for YW4 (from GetYokai.cs).</summary>
    public static readonly Dictionary<string, string> YW4SignatureMap = new()
    {
        ["74-22-A1-F1"] = "Nate",
        ["C5-E6-31-09"] = "Katie",
        ["64-FE-81-43"] = "Summer",
        ["B4-84-21-04"] = "Touma",
        ["D4-D7-E1-7E"] = "Akinori",
        ["C4-0B-C1-CC"] = "Jack",
        ["0C-55-64-6B"] = "Jibanyan",
        ["A9-86-38-A0"] = "Komasan",
        ["19-AF-58-9D"] = "Komajiro",
        ["72-5D-50-AA"] = "Hungramps",
        ["A2-27-F0-ED"] = "Dimmy",
        ["12-0E-90-D0"] = "Tattletell",
        ["02-D2-B0-62"] = "Dismarelda",
        ["B2-FB-D0-5F"] = "Hidabat",
        ["62-81-70-18"] = "Frostina",
        ["D2-A8-10-25"] = "Insomni",
        ["03-3F-40-A7"] = "Blizzaria",
        ["42-0E-5B-BE"] = "Damona",
        ["67-A7-6C-5C"] = "Little Charrmer",
        ["07-F4-AC-26"] = "Roughraff",
        ["B7-DD-CC-1B"] = "Mochismo",
        ["A7-01-EC-A9"] = "Blazion",
        ["17-28-8C-94"] = "Sgt. Burly",
        ["C7-52-2C-D3"] = "Venoct",
        ["86-63-37-CA"] = "Illuminoct",
        ["45-30-1A-E1"] = "Shadow Venoct",
        ["77-7B-4C-EE"] = "Shogunyan",
        ["A6-EC-1C-6C"] = "Snartle",
        ["16-C5-7C-51"] = "Arachnus",
        ["1A-17-F5-F0"] = "Komashura",
        ["79-FC-98-E7"] = "Noko",
        ["69-20-B8-55"] = "Hovernyan",
        ["08-9E-88-EA"] = "Reuknight",
        ["49-AF-93-F3"] = "Corptain",
        ["B8-B7-E8-D7"] = "Toadal Dude",
        ["6C-06-A4-11"] = "Silver Lining",
        ["DC-2F-C4-2C"] = "Manjimutt",
        ["AD-4D-D4-21"] = "Kyubi",
        ["EC-7C-CF-38"] = "Darkyubi",
        ["1D-64-B4-1C"] = "Master Nyada",
        ["D4-36-A1-0C"] = "Noway",
        ["B4-65-61-76"] = "Sandmeh",
        ["04-4C-01-4B"] = "Mimikin",
        ["14-90-21-F9"] = "Mirapo",
        ["15-7D-D1-3C"] = "Robonyan",
        ["54-4C-CA-25"] = "Goldenyan",
        ["EF-47-B8-9B"] = "Jibanyan (Lightside)",
        ["8A-20-04-23"] = "Jibanyan (Shadowside)",
        ["3B-F6-F4-5D"] = "Himoji (Lightside)",
        ["5E-91-48-E5"] = "Himoji (Shadowside)",
        ["3A-1B-04-98"] = "Pakkun (Lightside)",
        ["5F-7C-B8-20"] = "Pakkun (Shadowside)",
        ["3F-3D-18-DC"] = "Komasan (Lightside)",
        ["5A-5A-A4-64"] = "Komasan (Shadowside)",
        ["9F-C8-58-53"] = "Merameraion (Lightside)",
        ["FA-AF-E4-EB"] = "Merameraion (Shadowside)",
        ["F1-1C-4C-20"] = "Orochi (Lightside)",
        ["94-7B-F0-98"] = "Orochi (Shadowside)",
        ["83-C6-F1-40"] = "Enma",
        ["7C-98-71-8C"] = "Enma Awakened",
        ["1D-26-41-33"] = "Yami Enma",
        ["8B-B4-84-21"] = "Touma",
        ["AE-F5-79-4C"] = "Gargaros",
        ["7A-44-35-8A"] = "Ogralus",
        ["CA-6D-55-B7"] = "Orcanos",
        ["BF-C4-A9-3B"] = "Gilgaros",
        ["BB-0F-45-BA"] = "McKraken",
        ["47-AB-9D-34"] = "Nurarihyon",
        ["33-EF-91-7D"] = "Lord Ananta",
        ["57-77-BD-86"] = "Douketsu",
        ["E7-5E-DD-BB"] = "Shutendoji",
        ["01-99-3D-FB"] = "Shuka Natsume (Summer)",
        ["8F-14-78-E1"] = "Micchy (Lightside)",
        ["EA-73-C4-59"] = "Micchy (Shadowside)",
        ["FA-BD-84-6D"] = "Jinta (Lightside)",
        ["9F-DA-38-D5"] = "Jinta (Shadowside)",
        ["52-51-A1-C2"] = "Micchy Hyper (Lightside)",
        ["37-36-1D-7A"] = "Micchy Hyper (Shadowside)",
    };
}
