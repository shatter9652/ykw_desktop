namespace YKWHome.Core.Saves;

/// <summary>Supported game types.</summary>
public enum GameType
{
    Unknown,
    YW1,      // Yo-kai Watch 1 (3DS / Switch)
    YW2,      // Yo-kai Watch 2 (Psychic Specters / Fleshy Souls / Bony Spirits)
    YW3,      // Yo-kai Watch 3
    YWB,      // Yo-kai Watch Blasters
    YWB2,     // Yo-kai Watch Blasters 2
    YW4,      // Yo-kai Watch 4 (PS4 / Switch)
    YW1S,     // Yo-kai Watch 1 for Smartphone
}

/// <summary>Platform detection from file structure.</summary>
public enum Platform
{
    Unknown,
    ThreeDS,
    Switch,
    PS4,
    Smartphone,
}
