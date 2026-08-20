using System.IO.Hashing;

namespace YKWHome.Core.Crypto;

/// <summary>
/// IeCCode - Level-5's self-reciprocal Xorshift128 stream cipher.
/// Encryption and decryption are the same operation (self-reciprocal).
/// </summary>
public class YWCipher
{
    private readonly byte[] _table = new byte[256];
    private readonly Xorshift128 _rng;

    /// <summary>First 256 odd primes (3..1621).</summary>
    private static readonly uint[] OddPrimes = GenerateOddPrimes();

    public YWCipher(uint seed, int count)
    {
        for (int i = 0; i < 256; i++) _table[i] = (byte)i;
        _rng = new Xorshift128(seed);

        for (int i = 0; i < count; i++)
        {
            uint r = _rng.Next(0x10000);
            int r1 = (int)(r & 0xFF);
            int r2 = (int)((r >> 8) & 0xFF);
            if (r1 != r2)
            {
                byte a = _table[r1], b = _table[r2];
                (_table[a], _table[b]) = (_table[b], _table[a]);
            }
        }
    }

    /// <summary>Encrypt/decrypt data (self-reciprocal - same operation).</summary>
    public byte[] Process(byte[] data)
    {
        var output = new byte[data.Length];
        uint ka = 0;
        for (int i = 0; i < data.Length; i++)
        {
            if ((i & 0xFF) == 0)
                ka = OddPrimes[_table[(i & 0xFF00) >> 8]];
            int idx = (int)(ka * (uint)(i + 1)) & 0xFF;
            output[i] = (byte)(data[i] ^ _table[idx]);
        }
        return output;
    }

    /// <summary>Get the next PRNG value (used for AES key derivation).</summary>
    public uint NextKey(uint divisor = 0) => _rng.Next(divisor);

    private static uint[] GenerateOddPrimes()
    {
        var primes = new List<uint>();
        for (uint n = 3; primes.Count < 256; n += 2)
        {
            if (IsPrime(n)) primes.Add(n);
        }
        return primes.ToArray();
    }

    private static bool IsPrime(uint n)
    {
        if (n < 2) return false;
        if (n < 4) return true;
        if (n % 2 == 0 || n % 3 == 0) return false;
        for (uint i = 5; i * i <= n; i += 6)
            if (n % i == 0 || n % (i + 2) == 0) return false;
        return true;
    }
}

/// <summary>
/// YW1-style IeCCode processing (encrypt/decrypt).
/// Handles the CRC-32 tail segment.
/// </summary>
public static class YWProc
{
    public static int LastIecSeed { get; private set; }

    /// <summary>
    /// Process a YW1-style IeCCode encrypted file.
    /// </summary>
    /// <param name="data">Raw file data.</param>
    /// <param name="encrypt">If true, encrypt; if false, decrypt.</param>
    /// <returns>Processed data.</returns>
    public static byte[] Process(byte[] data, bool encrypt)
    {
        // Extract tail: CRC-32 (4 bytes) + seed (4 bytes)
        uint storedCrc = BitConverter.ToUInt32(data, data.Length - 8);
        uint seed = BitConverter.ToUInt32(data, data.Length - 4);
        LastIecSeed = (int)seed;

        byte[] payload = data[..^8];

        if (!encrypt)
        {
            // Verify CRC-32 on encrypted data BEFORE decrypting
            uint calculatedCrc = Crc32.HashToUInt32(payload);
            if (calculatedCrc != storedCrc)
                throw new InvalidDataException(
                    $"CRC-32 mismatch: expected {storedCrc:X8}, got {calculatedCrc:X8}");
        }

        // Encrypt/decrypt (self-reciprocal)
        var cipher = new YWCipher(seed, 0x1000);
        byte[] processed = cipher.Process(payload);

        if (encrypt)
        {
            // Calculate CRC-32 on encrypted data AFTER encrypting
            uint newCrc = Crc32.HashToUInt32(processed);
            var result = new byte[processed.Length + 8];
            processed.CopyTo(result, 0);
            BitConverter.TryWriteBytes(result.AsSpan(processed.Length), newCrc);
            BitConverter.TryWriteBytes(result.AsSpan(processed.Length + 4), seed);
            return result;
        }

        return processed;
    }

    /// <summary>
    /// Encrypt a plaintext payload for on-disk YW1 format.
    /// </summary>
    public static byte[] Encrypt(byte[] payload, uint seed)
    {
        var cipher = new YWCipher(seed, 0x1000);
        byte[] encrypted = cipher.Process(payload);
        uint crc = Crc32.HashToUInt32(encrypted);
        var result = new byte[encrypted.Length + 8];
        encrypted.CopyTo(result, 0);
        BitConverter.TryWriteBytes(result.AsSpan(encrypted.Length), crc);
        BitConverter.TryWriteBytes(result.AsSpan(encrypted.Length + 4), seed);
        return result;
    }
}
