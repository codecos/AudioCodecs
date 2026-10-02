using System.Runtime.CompilerServices;

/// <summary>
/// Opus entropy decoder state. Range-coded bytes are consumed from the front
/// of a frame and raw bits are consumed from the end, as required by Opus.
/// </summary>
internal ref struct OpusRangeDecoder
{
    private const int SymbolBits = 8;
    private const uint CodeTop = 1u << 31;
    private const uint CodeBottom = CodeTop >> SymbolBits;
    private const int CodeExtra = 7;
    private const int SymbolMax = 255;

    private ReadOnlySpan<byte> _buffer;
    private int _offset;
    private int _endOffset;
    private uint _range;
    private uint _value;
    private uint _ext;
    private int _remainder;
    private uint _endWindow;
    private int _endBits;
    private int _totalBits;
    private bool _error;

    internal OpusRangeDecoder(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _offset = 0;
        _endOffset = 0;
        _range = 1u << CodeExtra;
        _value = 0;
        _ext = 0;
        _remainder = ReadByte();
        _value = _range - 1 - ((uint)_remainder >> (SymbolBits - CodeExtra));
        _endWindow = 0;
        _endBits = 0;
        _totalBits = 32 + 1 - ((32 - CodeExtra) / SymbolBits * SymbolBits);
        _error = false;
        Normalize();
    }

    internal bool HasError => _error;
    internal int BitsUsed => _totalBits - IntegerLog(_range);

    internal uint Decode(uint frequencyTotal)
    {
        if (frequencyTotal == 0)
        {
            _error = true;
            return 0;
        }

        _ext = _range / frequencyTotal;
        var scaled = _value / _ext;
        return frequencyTotal - Math.Min(scaled + 1, frequencyTotal);
    }

    internal void Update(uint low, uint high, uint frequencyTotal)
    {
        if (frequencyTotal == 0 || low >= high || high > frequencyTotal)
        {
            _error = true;
            return;
        }

        var lower = _ext * (frequencyTotal - high);
        _value -= lower;
        _range = low > 0
            ? _ext * (high - low)
            : _range - lower;
        Normalize();
    }

    internal uint DecodeBit(int logProbability)
    {
        if ((uint)logProbability > 31)
        {
            _error = true;
            return 0;
        }

        var range = _range;
        var distance = _value;
        var split = range >> logProbability;
        var result = distance < split;
        if (!result)
        {
            _value = distance - split;
        }

        _range = result ? split : range - split;
        Normalize();
        return result ? 1u : 0u;
    }

    internal uint ReadBits(int bitCount)
    {
        if ((uint)bitCount > 25)
        {
            _error = true;
            return 0;
        }

        var window = _endWindow;
        var available = _endBits;
        while (available < bitCount)
        {
            if (_offset + _endOffset >= _buffer.Length && available <= 24)
            {
                // Opus permits implicit zero fill at the end of a truncated
                // packet, but the caller must still be able to observe error.
                _error = true;
            }

            window |= (uint)ReadByteFromEnd() << available;
            available += SymbolBits;
        }

        var mask = bitCount == 0 ? 0u : (1u << bitCount) - 1;
        var result = window & mask;
        _endWindow = window >> bitCount;
        _endBits = available - bitCount;
        _totalBits += bitCount;
        return result;
    }

    private void Normalize()
    {
        while (_range <= CodeBottom)
        {
            _totalBits += SymbolBits;
            _range <<= SymbolBits;
            var symbol = _remainder;
            _remainder = ReadByte();
            symbol = (symbol << SymbolBits | _remainder) >> (SymbolBits - CodeExtra);
            _value = ((_value << SymbolBits) + (SymbolMax & ~(uint)symbol)) & (CodeTop - 1u);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ReadByte()
    {
        if ((uint)_offset >= (uint)_buffer.Length)
        {
            _error = true;
            return 0;
        }

        return _buffer[_offset++];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ReadByteFromEnd()
    {
        _endOffset++;
        var index = _buffer.Length - _endOffset;
        if (index < 0 || index < _offset)
        {
            _error = true;
            return 0;
        }

        return _buffer[index];
    }

    private static int IntegerLog(uint value)
    {
        if (value == 0)
        {
            return 0;
        }

        return 32 - System.Numerics.BitOperations.LeadingZeroCount(value);
    }
}
