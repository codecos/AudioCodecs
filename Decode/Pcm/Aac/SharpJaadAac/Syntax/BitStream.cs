using System;
using System.Runtime.CompilerServices;
using SharpJaad.AAC;

namespace SharpJaad.AAC.Syntax
{
    public unsafe class BitStream
    {
        private const int WORD_BITS = 32;
        private const int WORD_BYTES = 4;
        private static readonly uint[] BitMasks =
        {
            0x00000000u, 0x00000001u, 0x00000003u, 0x00000007u, 0x0000000Fu, 0x0000001Fu, 0x0000003Fu, 0x0000007Fu,
            0x000000FFu, 0x000001FFu, 0x000003FFu, 0x000007FFu, 0x00000FFFu, 0x00001FFFu, 0x00003FFFu, 0x00007FFFu,
            0x0000FFFFu, 0x0001FFFFu, 0x0003FFFFu, 0x0007FFFFu, 0x000FFFFFu, 0x001FFFFFu, 0x003FFFFFu, 0x007FFFFFu,
            0x00FFFFFFu, 0x01FFFFFFu, 0x03FFFFFFu, 0x07FFFFFFu, 0x0FFFFFFFu, 0x1FFFFFFFu, 0x3FFFFFFFu, 0x7FFFFFFFu,
            0xFFFFFFFFu
        };

        private byte[] _buffer;
        private byte* _externalBuffer;
        private int _bufferEnd;
        private int _pos; //offset in the buffer array
        private uint _cache; //current 4 bytes, that are read from the buffer
        protected int _bitsCached; //remaining bits in current cache
        protected int _position; //number of total bits read

        public BitStream()
        { }

        public BitStream(byte[] data)
        {
            SetData(data);
        }

        public void Destroy()
        {
            Reset();
            _buffer = null;
            _bufferEnd = 0;
        }

        public void SetData(byte[] data)
        {
            SetData(data, 0, data.Length);
        }

        public void SetData(byte[] data, int offset, int count)
        {
            Reset();

            _buffer = data;
            _bufferEnd = offset + count;

            int shift = count % WORD_BYTES;
            _bitsCached = 8 * shift;

            for (int i = 0; i < shift; ++i)
            {
                uint c = data[offset + i];
                _cache <<= 8;
                _cache |= c;
            }

            _pos = offset + shift;
        }

        internal void SetExternalData(byte* data, int count)
        {
            Reset();
            _externalBuffer = data;
            _bufferEnd = count;

            int shift = count % WORD_BYTES;
            _bitsCached = 8 * shift;
            for (int i = 0; i < shift; ++i)
            {
                _cache <<= 8;
                _cache |= data[i];
            }

            _pos = shift;
        }

        internal void ClearExternalData()
        {
            _externalBuffer = null;
            Reset();
        }

        public void ByteAlign()
        {
            int toFlush = _bitsCached & 7;
            if (toFlush > 0) SkipBits(toFlush);
        }

        public void Reset()
        {
            _pos = 0;
            _bitsCached = 0;
            _cache = 0;
            _position = 0;
        }

        public int GetPosition()
        {
            return _position;
        }

        public int GetBitsLeft()
        {
            return 8 * (_bufferEnd - _pos) + _bitsCached;
        }

        /**
		 * Reads the next four bytes.
		 * @param peek if true, the stream pointer will not be increased
		 */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected uint ReadCache(bool peek)
        {
            if (_pos > _bufferEnd - WORD_BYTES) throw new AACException("end of stream", true);
            uint i;
            if (_externalBuffer != null)
            {
                i = ((uint)_externalBuffer[_pos] << 24)
                    | ((uint)_externalBuffer[_pos + 1] << 16)
                    | ((uint)_externalBuffer[_pos + 2] << 8)
                    | _externalBuffer[_pos + 3];
            }
            else
            {
                i = ((uint)_buffer[_pos] << 24)
                    | ((uint)_buffer[_pos + 1] << 16)
                    | ((uint)_buffer[_pos + 2] << 8)
                    | _buffer[_pos + 3];
            }
            if (!peek) _pos += WORD_BYTES;
            return i;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadBits(int n)
        {
            int result;
            if (_bitsCached >= n)
            {
                _bitsCached -= n;
                result = (int)((_cache >> _bitsCached) & BitMasks[n]);
                _position += n;
            }
            else
            {
                _position += n;
                uint c = _cache & BitMasks[_bitsCached];
                int left = n - _bitsCached;
                _cache = ReadCache(false);
                _bitsCached = WORD_BITS - left;
                result = (int)(((_cache >> _bitsCached) & BitMasks[left]) | (c << left));
            }
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadBit()
        {
            int i;
            if (_bitsCached > 0)
            {
                _bitsCached--;
                i = (int)((_cache >> _bitsCached) & 1u);
                _position++;
            }
            else
            {
                _cache = ReadCache(false);
                _bitsCached = WORD_BITS - 1;
                _position++;
                i = (int)((_cache >> _bitsCached) & 1u);
            }
            return i;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ReadBool()
        {
            if (_bitsCached > 0)
            {
                _bitsCached--;
                _position++;
                return ((_cache >> _bitsCached) & 1u) != 0;
            }

            _cache = ReadCache(false);
            _bitsCached = WORD_BITS - 1;
            _position++;
            return ((_cache >> _bitsCached) & 1u) != 0;
        }

        public int PeekBits(int n)
        {
            int ret;
            if (_bitsCached >= n)
            {
                ret = (int)((_cache >> (_bitsCached - n)) & BitMasks[n]);
            }
            else
            {
                uint c = _cache & BitMasks[_bitsCached];
                n -= _bitsCached;
                ret = (int)(((ReadCache(true) >> (WORD_BITS - n)) & BitMasks[n]) | (c << n));
            }
            return ret;
        }

        public int PeekBit()
        {
            int ret;
            if (_bitsCached > 0)
            {
                ret = (int)((_cache >> (_bitsCached - 1)) & 1u);
            }
            else
            {
                uint word = ReadCache(true);
                ret = (int)((word >> (WORD_BITS - 1)) & 1u);
            }
            return ret;
        }

        public void SkipBits(int n)
        {
            _position += n;
            if (n <= _bitsCached)
            {
                _bitsCached -= n;
            }
            else
            {
                n -= _bitsCached;
                while (n >= WORD_BITS)
                {
                    n -= WORD_BITS;
                    ReadCache(false);
                }
                if (n > 0)
                {
                    _cache = ReadCache(false);
                    _bitsCached = WORD_BITS - n;
                }
                else
                {
                    _cache = 0;
                    _bitsCached = 0;
                }
            }
        }

        public void SkipBit()
        {
            _position++;
            if (_bitsCached > 0)
            {
                _bitsCached--;
            }
            else
            {
                _cache = ReadCache(false);
                _bitsCached = WORD_BITS - 1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int MaskBits(int n)
        {
            return (int)BitMasks[n];
        }
    }
}
