using System.Security.Cryptography;

namespace YKWHome.Core.Crypto;

/// <summary>
/// AES-CCM (Counter with CBC-MAC) for YW2/YW3/YWB encryption.
/// Layout: nonce(12) + padding(4) + MAC(16) + ciphertext
/// Ported from Python yw_decrypt.py CCMCipher (matches PyCryptodome + NIST SP 800-38C).
/// </summary>
public class CCMCipher
{
    private readonly byte[] _key;
    private const int M = 16; // Tag length

    public CCMCipher(byte[] key)
    {
        _key = key;
    }

    /// <summary>
    /// Decrypt AES-CCM layer. Data is MAC(16) + ciphertext.
    /// </summary>
    public byte[] Decrypt(byte[] data, byte[] nonce)
    {
        // L' = L - 1, where L = 15 - nonce_length (NIST SP 800-38C)
        int L = 15 - nonce.Length;
        int lPrime = L - 1;

        // Extract MAC (first 16 bytes)
        byte[] mac = new byte[M];
        Array.Copy(data, 0, mac, 0, M);

        // Decrypt MAC using CTR with counter=0
        byte[] macPlain = AesCtr(_key, nonce, lPrime, mac, 0);

        // Decrypt message using CTR with counter=1
        byte[] ciphertext = new byte[data.Length - M];
        Array.Copy(data, M, ciphertext, 0, ciphertext.Length);
        byte[] plaintext = AesCtr(_key, nonce, lPrime, ciphertext, 1);

        // Verify MAC
        byte[] computedMac = CalculateMac(nonce, plaintext, lPrime);
        if (!CryptographicOperations.FixedTimeEquals(macPlain, computedMac))
            throw new CryptographicException("AES-CCM authentication failed");

        return plaintext;
    }

    /// <summary>
    /// Encrypt with AES-CCM. Returns: MAC(16) + ciphertext (nonce is handled by caller).
    /// </summary>
    public byte[] Encrypt(byte[] data, byte[] nonce)
    {
        int L = 15 - nonce.Length;
        int lPrime = L - 1;

        // Calculate MAC over plaintext
        byte[] macPlain = CalculateMac(nonce, data, lPrime);

        // Encrypt MAC with CTR counter=0
        byte[] macEncrypted = AesCtr(_key, nonce, lPrime, macPlain, 0);

        // Encrypt message with CTR counter=1
        byte[] ciphertext = AesCtr(_key, nonce, lPrime, data, 1);

        // Return MAC + ciphertext (caller prepends nonce)
        var result = new byte[M + ciphertext.Length];
        macEncrypted.CopyTo(result, 0);
        ciphertext.CopyTo(result, M);
        return result;
    }

    /// <summary>
    /// Calculate CBC-MAC per NIST SP 800-38C Section 2.2.
    /// B_0 = flags || nonce || length
    /// flags = M'*8 + L' where M' = (t-2)/2 = 7, L' = L-1
    /// </summary>
    private static byte[] CalculateMac(byte[] nonce, byte[] data, int lPrime)
    {
        // M' = (M - 2) / 2 = 7
        byte flag = (byte)((8 * ((M - 2) / 2)) + lPrime);

        // B_0 = flag || nonce || length (3 bytes big-endian)
        byte[] b0 = new byte[16];
        b0[0] = flag;
        Array.Copy(nonce, 0, b0, 1, nonce.Length);
        int len = data.Length;
        b0[13] = (byte)((len >> 16) & 0xFF);
        b0[14] = (byte)((len >> 8) & 0xFF);
        b0[15] = (byte)(len & 0xFF);

        byte[] x = AesEcbEncryptStatic(_key, b0);

        // Process data in 16-byte blocks (pad last block with zeros)
        for (int i = 0; i < data.Length; i += 16)
        {
            byte[] block = new byte[16];
            int blockLen = Math.Min(16, data.Length - i);
            Array.Copy(data, i, block, 0, blockLen);

            for (int j = 0; j < 16; j++) x[j] ^= block[j];
            x = AesEcbEncryptStatic(_key, x);
        }

        return x;
    }

    /// <summary>
    /// AES-CTR mode: counter block = L' || nonce || counter
    /// For L'=2: [L'(1)] [nonce(12)] [counter(3)] = 16 bytes total
    /// Counter occupies last 3 bytes (bytes 13-15) but fits in L'=2 range.
    /// </summary>
    private static byte[] AesCtr(byte[] key, byte[] nonce, int lPrime, byte[] data, uint initialCounter)
    {
        // Build counter block: L' || nonce || counter (big-endian 3 bytes)
        byte[] counterBlock = new byte[16];
        counterBlock[0] = (byte)lPrime;
        Array.Copy(nonce, 0, counterBlock, 1, nonce.Length);
        // Counter in last 3 bytes (bytes 13-15), big-endian
        counterBlock[13] = (byte)((initialCounter >> 16) & 0xFF);
        counterBlock[14] = (byte)((initialCounter >> 8) & 0xFF);
        counterBlock[15] = (byte)(initialCounter & 0xFF);

        byte[] output = new byte[data.Length];
        byte[] stream = new byte[16];

        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var encryptor = aes.CreateEncryptor();

        for (int i = 0; i < data.Length; i += 16)
        {
            // Encrypt counter block to get keystream
            encryptor.TransformBlock(counterBlock, 0, 16, stream, 0);

            // XOR keystream with data
            int blockLen = Math.Min(16, data.Length - i);
            for (int j = 0; j < blockLen; j++)
                output[i + j] = (byte)(data[i + j] ^ stream[j]);

            // Increment counter (big-endian, last 3 bytes)
            for (int j = 15; j >= 13; j--)
            {
                if (++counterBlock[j] != 0) break;
            }
        }

        return output;
    }

    private static byte[] AesEcbEncryptStatic(byte[] key, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(data, 0, data.Length);
    }
}
