namespace YKWHome.Core.Saves;

/// <summary>
/// Represents a single Yo-kai in a save file.
/// </summary>
public class YokaiEntry
{
    public int Slot { get; set; }
    public int YokaiId { get; set; }
    public int Variant { get; set; }
    public int Level { get; set; }
    public string Name { get; set; } = "";
    public string Nickname { get; set; } = "";
    public bool IsTeamMember { get; set; }
    public byte[] RawBytes { get; set; } = [];

    // YW4 specific
    public int HP { get; set; }
    public int YP { get; set; }
    public int XP { get; set; }
    public string? Signature { get; set; }
    public string[] Skills { get; set; } = [];
    public int HpPlus { get; set; }
    public int YpPlus { get; set; }
    public int StPlus { get; set; }
    public int SpPlus { get; set; }
    public int PaPlus { get; set; }
    public int SaPlus { get; set; }

    public string? IconPath { get; set; }

    public override string ToString() =>
        string.IsNullOrEmpty(Name) ? $"Yo-kai #{YokaiId}" : Name;
}

/// <summary>
/// Information about a detected game.
/// </summary>
public class GameInfo
{
    public GameType GameId { get; set; }
    public string GameName { get; set; } = "";
    public string IconPath { get; set; } = "";
    public int YokaiSize { get; set; }
    public int LevelOffset { get; set; }
    public int IdOffset { get; set; }
    public bool IdIsCrc32 { get; set; }
    public Platform Platform { get; set; }

    /// <summary>Yokai count, populated after parsing.</summary>
    public int YokaiCount { get; set; }

    public List<YokaiEntry> YokaiList { get; set; } = [];
    public List<YokaiEntry> TeamList { get; set; } = [];
}
