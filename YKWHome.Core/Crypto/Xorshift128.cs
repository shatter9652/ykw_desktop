namespace YKWHome.Core.Crypto;

/// <summary>
/// Xorshift128 PRNG - Level-5's PRNG used by IeCCode.
/// Exact port of the Python implementation from yw_decrypt.py.
/// </summary>
public class Xorshift128
{
    private uint[] _state;

    public Xorshift128(uint seed = 0)
    {
        _state = [0x6C078966, 0xDD5254A5, 0xB9523B81, 0x03DF95B3];
        if (seed == 0) return;

        const uint mul = 0x6C078966 - 1;
        uint val = seed ^ (seed >> 30);
        _state[0] = (val * mul + 1) & 0xFFFFFFFF;
        val = _state[0] ^ (_state[0] >> 30);
        _state[1] = (val * mul + 2) & 0xFFFFFFFF;
        val = _state[1] ^ (_state[1] >> 30);
        _state[2] = (val * mul + 3) & 0xFFFFFFFF;
    }

    public uint Next(uint divisor = 0)
    {
        uint t = (_state[0] ^ (_state[0] << 11)) & 0xFFFFFFFF;
        _state[0] = _state[1];
        _state[1] = _state[2];
        _state[2] = _state[3];
        _state[3] = (_state[3] ^ (_state[3] >> 19) ^ t ^ (t >> 8)) & 0xFFFFFFFF;
        return divisor > 0 ? _state[3] % divisor : _state[3];
    }
}
