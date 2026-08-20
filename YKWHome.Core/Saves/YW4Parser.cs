namespace YKWHome.Core.Saves;

/// <summary>
/// Parser for Yo-kai Watch 4 (PS4/Switch) save files.
/// YW4 uses flat binary with hardcoded offsets - no encryption, no section tree.
/// Based on AYw4SaveEditor's SaveFileParams.cs offsets.
/// </summary>
public static class YW4Parser
{
    // ── Offsets ──────────────────────────────────────────────
    private const int MagicOffset = 0x00;
    private const int MoneyOffset = 203;
    private const int MoneySize = 4;
    private const int ConsumablesOffset = 76_579;  // 0x12B23
    private const int ConsumableEntrySize = 54;
    private const int ConsumableMaxCount = 500;
    private const int EquipmentOffset = 103_587;  // 0x194A3
    private const int EquipmentEntrySize = 63;
    private const int EquipmentMaxCount = 1000;
    private const int PartyOffset = 166_627;  // 0x28AFB
    private const int PartyEntrySize = 469;
    private const int PartyMaxCount = 6;
    private const int UserYokaiOffset = 169_449;  // 0x29659
    private const int YokaiEntrySize = 469;
    private const int YokaiMaxCount = 400;
    private const int GenericSoulOffset = 958_227;  // 0xE9F23
    private const int GenericSoulEntrySize = 54;
    private const int GenericSoulMaxCount = 100;
    private const int YokaiSoulOffset = 963_635;  // 0xEB22B
    private const int YokaiSoulEntrySize = 80;
    private const int YokaiSoulMaxCount = 500;
    private const int YokaiCountOffset = 946_497;  // 0xE7141
    private const int ConsumableCountOffset = 166_587;  // 0x28AA3

    // ── Character name offsets ────────────────────────────────
    private static readonly (int offset, string name)[] CharacterNames =
    [
        (282, "Nate"),
        (318, "Katie"),
        (354, "Summer"),
        (390, "Touma"),
        (426, "Akinori"),
        (462, "Jack"),
    ];

    /// <summary>Verify the file has YW4 magic bytes.</summary>
    public static bool IsYW4File(byte[] data) =>
        data.Length >= 2 && data[0] == 0xFF && data[1] == 0xEE;

    /// <summary>Parse a YW4 USERDATA00/data.bin file.</summary>
    public static GameInfo Parse(string filePath)
    {
        byte[] data = File.ReadAllBytes(filePath);
        return Parse(data, filePath);
    }

    /// <summary>Parse YW4 save data from bytes.</summary>
    public static GameInfo Parse(byte[] data, string? filePath = null)
    {
        if (!IsYW4File(data))
            throw new InvalidDataException("Not a YW4 save file (missing 0xEEFF magic)");

        var info = new GameInfo
        {
            GameId = GameType.YW4,
            GameName = "Yo-kai Watch 4",
            Platform = Platform.Switch,
            YokaiSize = YokaiEntrySize,
            LevelOffset = 180,
        };

        // Parse misc data
        float posX = BitConverter.ToSingle(data, 131);
        float posY = BitConverter.ToSingle(data, 135);
        float posZ = BitConverter.ToSingle(data, 139);
        int money = BitConverter.ToInt32(data, MoneyOffset);

        // Parse character names
        var names = new Dictionary<string, string>();
        foreach (var (offset, name) in CharacterNames)
            names[name] = ReadString(data, offset, 24);

        // Parse party
        for (int i = 0; i < PartyMaxCount; i++)
        {
            int entryOff = PartyOffset + i * PartyEntrySize;
            var entry = ParseYokaiEntry(data, entryOff, i);
            if (entry.Variant > 0)
            {
                entry.IsTeamMember = true;
                entry.Name = YokaiNames.ResolveBySignature(entry.Signature ?? "");
                info.TeamList.Add(entry);
            }
        }

        // Parse user yokai (Medallium)
        int yokaiCount = BitConverter.ToInt32(data, YokaiCountOffset);
        for (int i = 0; i < YokaiMaxCount; i++)
        {
            int entryOff = UserYokaiOffset + i * YokaiEntrySize;
            var entry = ParseYokaiEntry(data, entryOff, i);
            if (entry.Variant > 0)
            {
                entry.Name = YokaiNames.ResolveBySignature(entry.Signature ?? "");
                info.YokaiList.Add(entry);
            }
        }

        info.YokaiCount = info.YokaiList.Count;
        return info;
    }

    /// <summary>Parse a single 469-byte Yo-kai/character entry.</summary>
    private static YokaiEntry ParseYokaiEntry(byte[] data, int offset, int slot)
    {
        int id1 = BitConverter.ToUInt16(data, offset);
        int id2 = BitConverter.ToUInt16(data, offset + 2);

        if (id2 == 0 && id1 == 0)
            return new YokaiEntry { Slot = slot };

        string signature = FormatSignature(data, offset + 72);
        string[] skills = new string[6];
        for (int s = 0; s < 6; s++)
            skills[s] = FormatSignature(data, offset + 84 + s * 4);

        return new YokaiEntry
        {
            Slot = slot,
            YokaiId = id1,
            Variant = id2,
            Name = ReadString(data, offset + 28, 24),
            Signature = signature,
            Skills = skills,
            XP = BitConverter.ToInt32(data, offset + 132),
            HP = BitConverter.ToInt32(data, offset + 144),
            YP = BitConverter.ToInt32(data, offset + 156),
            Level = BitConverter.ToInt32(data, offset + 180),
            HpPlus = BitConverter.ToUInt16(data, offset + 214),
            YpPlus = BitConverter.ToUInt16(data, offset + 216),
            StPlus = BitConverter.ToUInt16(data, offset + 218),
            SpPlus = BitConverter.ToUInt16(data, offset + 220),
            PaPlus = BitConverter.ToUInt16(data, offset + 222),
            SaPlus = BitConverter.ToUInt16(data, offset + 224),
            RawBytes = data.Skip(offset).Take(YokaiEntrySize).ToArray(),
        };
    }

    /// <summary>Read a null-terminated UTF-8 string.</summary>
    private static string ReadString(byte[] data, int offset, int maxLen)
    {
        int end = offset;
        while (end < offset + maxLen && end < data.Length && data[end] != 0)
            end++;
        return System.Text.Encoding.UTF8.GetString(data, offset, end - offset);
    }

    /// <summary>Format 4 bytes as dash-separated hex signature.</summary>
    private static string FormatSignature(byte[] data, int offset) =>
        $"{data[offset]:X2}-{data[offset + 1]:X2}-{data[offset + 2]:X2}-{data[offset + 3]:X2}";
}
