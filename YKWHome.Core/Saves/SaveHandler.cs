using YKWHome.Core.Crypto;

namespace YKWHome.Core.Saves;

/// <summary>
/// Unified save file handler - detects game type, decrypts, and extracts Yo-kai.
/// Port of the Python save_handler.py.
/// </summary>
public class SaveHandler
{
    public GameType GameType { get; private set; } = GameType.Unknown;
    public GameInfo? GameInfo { get; private set; }
    public byte[]? RawData { get; private set; }
    public byte[]? DecryptedData { get; private set; }
    public SectionParser? Parser { get; private set; }

    // Encryption state for write-back
    public int IecSeed { get; private set; }
    public byte[]? AesKey { get; private set; }
    public byte[]? CcmNonce { get; private set; }

    /// <summary>Detect game type from file path or contents.</summary>
    public static GameType DetectGame(string filePath)
    {
        if (!File.Exists(filePath)) return GameType.Unknown;

        byte[] header = new byte[64];
        using (var fs = File.OpenRead(filePath))
            fs.Read(header, 0, 64);

        string pathLower = filePath.ToLowerInvariant();

        // Path-based hints
        if (pathLower.Contains("yw4") || pathLower.Contains("userdata"))
        {
            if (header.Length >= 2 && header[0] == 0xFF && header[1] == 0xEE)
                return GameType.YW4;
        }

        // Check if it's a YW4 directory structure
        if (Directory.Exists(filePath))
        {
            if (File.Exists(Path.Combine(filePath, "USERDATA00", "data.bin")))
                return GameType.YW4;
            if (File.Exists(Path.Combine(filePath, "0", "USERDATA00", "data.bin")))
                return GameType.YW4;
        }

        // Check for IeCCode-only (YW1 style)
        bool hasCcm = header.Take(12).Any(b => b != 0);
        if (!hasCcm && header.Length >= 8)
            return GameType.YW1;

        // YW2+ with CCM
        if (hasCcm) return GameType.YW2;

        return GameType.Unknown;
    }

    /// <summary>Load and decrypt a save file.</summary>
    public GameInfo Load(string filePath, GameType? forceType = null)
    {
        // Handle YW4 directory structure
        if (Directory.Exists(filePath))
            return LoadYW4Directory(filePath);

        RawData = File.ReadAllBytes(filePath);
        GameType = forceType ?? DetectGame(filePath);

        return GameType switch
        {
            GameType.YW1 => LoadYW1(),
            GameType.YW2 => LoadYW2(filePath),
            GameType.YW3 => LoadYW3(filePath),
            GameType.YWB or GameType.YWB2 => LoadYWB(filePath),
            GameType.YW4 => LoadYW4File(filePath),
            _ => throw new InvalidDataException($"Unsupported game type: {GameType}"),
        };
    }

    private GameInfo LoadYW4Directory(string dirPath)
    {
        // Find USERDATA00/data.bin in the directory tree
        string? dataPath = FindYW4DataFile(dirPath);
        if (dataPath == null)
            throw new FileNotFoundException("No USERDATA00/data.bin found in directory");

        GameType = GameType.YW4;
        RawData = File.ReadAllBytes(dataPath);
        var info = YW4Parser.Parse(RawData, dataPath);
        GameInfo = info;
        return info;
    }

    private static string? FindYW4DataFile(string dirPath)
    {
        // Check slot 0 then slot 1
        for (int slot = 0; slot <= 1; slot++)
        {
            string path = Path.Combine(dirPath, slot.ToString(), "USERDATA00", "data.bin");
            if (File.Exists(path)) return path;
        }
        // Check direct path
        string direct = Path.Combine(dirPath, "USERDATA00", "data.bin");
        return File.Exists(direct) ? direct : null;
    }

    private GameInfo LoadYW1()
    {
        if (RawData == null) throw new InvalidOperationException("No data loaded");
        DecryptedData = YWProc.Process(RawData, encrypt: false);
        IecSeed = YWProc.LastIecSeed;
        ParseSections();
        var info = ExtractYokai(GameType.YW1, "Yo-kai Watch 1");
        GameInfo = info;
        return info;
    }

    private GameInfo LoadYW2(string filePath)
    {
        if (RawData == null) throw new InvalidOperationException("No data loaded");

        // Try fixed key first
        byte[] fixedKey = System.Text.Encoding.ASCII.GetBytes("5+NI8WVq09V7LI5w");
        try
        {
        DecryptedData = DecryptYW2(RawData, fixedKey);
        this.AesKey = fixedKey;
        }
        catch
        {
            // Derive from head.yw
            string? headPath = FindHeadFile(filePath);
            if (headPath == null)
                throw new FileNotFoundException("YW2 requires head.yw for key derivation");
            var (dec, key) = DecryptYW2FromHead(RawData, headPath);
            DecryptedData = dec;
            this.AesKey = key;
        }

        CcmNonce = RawData[..12];
        ParseSections();
        var info = ExtractYokai(GameType.YW2, "Yo-kai Watch 2");
        GameInfo = info;
        return info;
    }

    private GameInfo LoadYW3(string filePath)
    {
        if (RawData == null) throw new InvalidOperationException("No data loaded");
        string? headPath = FindHeadFile(filePath);
        if (headPath == null)
            throw new FileNotFoundException("YW3 requires head.yw for key derivation");
        var (dec, key) = DecryptYW3(RawData, headPath);
        DecryptedData = dec;
        this.AesKey = key;
        this.CcmNonce = RawData[..12];
        ParseSections();
        var info = ExtractYokai(GameType.YW3, "Yo-kai Watch 3");
        GameInfo = info;
        return info;
    }

    private GameInfo LoadYWB(string filePath)
    {
        if (RawData == null) throw new InvalidOperationException("No data loaded");
        string? headPath = FindHeadFile(filePath);
        if (headPath == null)
            throw new FileNotFoundException("Blasters requires head.yw for key derivation");
        var (dec, key) = DecryptYWB(RawData, headPath);
        DecryptedData = dec;
        this.AesKey = key;
        this.CcmNonce = RawData[..12];
        ParseSections();
        var info = ExtractYokai(GameType.YWB, "Yo-kai Watch Blasters");
        GameInfo = info;
        return info;
    }

    private GameInfo LoadYW4File(string filePath)
    {
        if (RawData == null) throw new InvalidOperationException("No data loaded");
        var info = YW4Parser.Parse(RawData, filePath);
        GameInfo = info;
        return info;
    }

    private void ParseSections()
    {
        if (DecryptedData == null) throw new InvalidOperationException("No decrypted data");
        // Find section tree start (skip non-section header bytes)
        int treeStart = 0;
        for (int i = 0; i < DecryptedData.Length - 8; i += 4)
        {
            uint word = BitConverter.ToUInt32(DecryptedData, i);
            if ((ushort)(word & 0xFFFF) == 0xFFFE)
            {
                treeStart = i;
                break;
            }
        }
        byte[] treeData = DecryptedData[treeStart..];
        Parser = new SectionParser(treeData, treeStart);
    }

    private GameInfo ExtractYokai(GameType game, string name)
    {
        var info = new GameInfo
        {
            GameId = game,
            GameName = name,
            YokaiSize = game switch
            {
                GameType.YW1 or GameType.YW2 => 0x5C,
                GameType.YW3 => 0x54,
                GameType.YWB or GameType.YWB2 => 0x4C,
                _ => 0x5C,
            },
            LevelOffset = game switch
            {
                GameType.YWB or GameType.YWB2 => 0x48,
                GameType.YW3 => 0x49,
                _ => 0x4F,
            },
        };

        if (Parser == null) return info;

        // Extract Yo-kai from section 0x07
        if (Parser.Sections.TryGetValue(0x07, out var sec07))
        {
            int entrySize = info.YokaiSize;
            int idOffset = 0x04;
            bool isCrc32 = true;

            int count = 0;
            for (int i = 0; i + entrySize <= sec07.Data.Length; i += entrySize)
            {
                int yokaiId = isCrc32
                    ? BitConverter.ToInt32(sec07.Data, i + idOffset)
                    : BitConverter.ToUInt16(sec07.Data, i + idOffset);

                if (yokaiId == 0) continue;

                int level = sec07.Data[i + info.LevelOffset];
                string yokaiName = YokaiNames.Resolve(game, yokaiId);

                info.YokaiList.Add(new YokaiEntry
                {
                    Slot = count,
                    YokaiId = yokaiId,
                    Level = level,
                    Name = yokaiName,
                    RawBytes = sec07.Data.Skip(i).Take(entrySize).ToArray(),
                });
                count++;
            }
            info.YokaiCount = count;
        }

        // Extract team from section 0x01
        if (Parser.Sections.TryGetValue(0x01, out var sec01))
        {
            // Team members are identified via GENERIC_HANDLE in section 0x01
            // For now, mark first 6 yokai as potential team
        }

        return info;
    }

    // ── CCM Decryption Helpers ──────────────────────────────

    private static byte[] DecryptYW2(byte[] data, byte[] key)
    {
        var ccm = new CCMCipher(key);
        byte[] nonce = data[..12];
        byte[] mac = data[0x10..0x20];
        byte[] payload = data[0x20..];

        // Reconstruct CCM format for decrypt
        var ccmData = new byte[16 + payload.Length];
        mac.CopyTo(ccmData, 0);
        payload.CopyTo(ccmData, 16);

        byte[] decrypted = ccm.Decrypt(ccmData, nonce);
        return YWProc.Process(decrypted, encrypt: false);
    }

    private (byte[] decrypted, byte[] key) DecryptYW2FromHead(byte[] data, string headPath)
    {
        byte[] headRaw = File.ReadAllBytes(headPath);
        byte[] headDecrypted = YWProc.Process(headRaw, encrypt: false);
        uint seed = BitConverter.ToUInt32(headDecrypted, 0x0C);

        var rng = new Xorshift128(seed);
        byte[] key = new byte[16];
        for (int i = 0; i < 16; i++)
            key[i] = (byte)(rng.Next(0x100) & 0xFF);

        return (DecryptYW2(data, key), key);
    }

    private (byte[] decrypted, byte[] key) DecryptYW3(byte[] data, string headPath)
    {
        byte[] headRaw = File.ReadAllBytes(headPath);
        byte[] headDecrypted = YWProc.Process(headRaw, encrypt: false);

        uint baseSeed = BitConverter.ToUInt32(headDecrypted, 0x0C);
        uint xorVal = GetYW3Xor(headDecrypted);
        uint seed = baseSeed ^ xorVal;
        int count = GetYW3Count(headDecrypted) & 0xFF;

        var cipher = new YWCipher(seed, count);
        byte[] key = new byte[16];
        for (int i = 0; i < 16; i++)
            key[i] = (byte)cipher.NextKey(0x100);

        return (DecryptYW2(data, key), key);
    }

    private (byte[] decrypted, byte[] key) DecryptYWB(byte[] data, string headPath)
    {
        byte[] headRaw = File.ReadAllBytes(headPath);
        byte[] headDecrypted = YWProc.Process(headRaw, encrypt: false);

        uint baseSeed = BitConverter.ToUInt32(headDecrypted, 0x0C);
        // Simplified B1 key derivation
        var rng = new Xorshift128(baseSeed);
        byte[] key = new byte[16];
        for (int i = 0; i < 16; i++)
            key[i] = (byte)rng.Next(0x100);

        return (DecryptYW2(data, key), key);
    }

    private static uint GetYW3Xor(byte[] headData)
    {
        uint r2 = BitConverter.ToUInt32(headData, 0x10);
        if (r2 != 0) r2--;
        int pos = (int)(r2 * 0xA8 + 0x20);
        if (pos == 0) return 0;
        pos += 8 + 0x30;
        if (pos + 4 > headData.Length) return 0;
        return BitConverter.ToUInt32(headData, pos);
    }

    private static int GetYW3Count(byte[] headData)
    {
        uint r2 = BitConverter.ToUInt32(headData, 0x10);
        if (r2 != 0) r2--;
        int pos = (int)(r2 * 0xA8 + 0x20);
        if (pos == 0) return 0;
        pos += 0x40;
        if (pos + 24 > headData.Length) return 0;
        long sum = 0;
        for (int i = 0; i < 6; i++)
            sum += BitConverter.ToUInt32(headData, pos + i * 4);
        return (int)(sum & 0xFF);
    }

    private static string? FindHeadFile(string filePath)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (dir == null) return null;

        foreach (string name in new[] { "head.yw", "head.yw_g" })
        {
            string path = Path.Combine(dir, name);
            if (File.Exists(path)) return path;
        }

        // Try parent directory
        dir = Path.GetDirectoryName(dir);
        if (dir != null)
        {
            foreach (string name in new[] { "head.yw", "head.yw_g" })
            {
                string path = Path.Combine(dir, name);
                if (File.Exists(path)) return path;
            }
        }

        return null;
    }

    // ── Encryption for Write-Back ────────────────────────────

    /// <summary>Re-encrypt the save data for writing back to disk.</summary>
    public byte[] ExportSave()
    {
        if (RawData == null) throw new InvalidOperationException("No save loaded");

        return GameType switch
        {
            GameType.YW4 => RawData, // YW4 is plaintext
            GameType.YW1 => YWProc.Process(DecryptedData!, encrypt: true),
            GameType.YW2 or GameType.YW3 or GameType.YWB or GameType.YWB2 =>
                EncryptWithCCM(DecryptedData!),
            _ => throw new NotSupportedException($"Export not supported for {GameType}"),
        };
    }

    private byte[] EncryptWithCCM(byte[] plaintext)
    {
        // IeCCode encrypt first (includes CRC32 + seed tail)
        byte[] iecEncrypted = YWProc.Process(plaintext, encrypt: true);

        if (AesKey == null || CcmNonce == null)
            throw new InvalidOperationException("AES key or nonce not available");

        var ccm = new CCMCipher(AesKey);
        byte[] ccmResult = ccm.Encrypt(iecEncrypted, CcmNonce);
        // ccmResult = MAC(16) + ciphertext

        // Build output: nonce(12) + padding(4) + MAC(16) + ciphertext
        var output = new byte[12 + 4 + ccmResult.Length];
        CcmNonce.CopyTo(output, 0);
        // Skip 4 bytes padding (already zero)
        ccmResult.CopyTo(output, 16);
        return output;
    }
}
