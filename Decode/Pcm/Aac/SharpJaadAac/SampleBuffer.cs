namespace SharpJaad.AAC
{
    /// <summary>
    /// The SampleBuffer holds the decoded AAC frame. It contains the raw PCM data and its format.
    /// </summary>
    public unsafe class SampleBuffer
    {
        private nint _externalFloatPointer;
        private int _externalFloatCapacity;
        private nint _externalPcm32Pointer;
        private int _externalPcm32Capacity;
        public int SampleRate { get; private set; }
        public int Channels { get; private set; }
        public int BitsPerSample { get; private set; }
        public double Length { get; private set; }
        public double Bitrate { get; private set; }
        public double EncodedBitrate { get; private set; }
        public byte[] Data { get; private set; }
        public float[] FloatData { get; private set; }
        public int[] Pcm32Data { get; private set; }
        public int FramesPerChannel { get; private set; }
        public bool BigEndian { get; private set; }
        public bool PreferPcm32Output { get; set; }

        public ReadOnlySpan<float> FloatDataSpan => _externalFloatPointer != 0
            ? new ReadOnlySpan<float>((void*)_externalFloatPointer, checked(FramesPerChannel * Channels))
            : FloatData;

        public ReadOnlySpan<int> Pcm32DataSpan => _externalPcm32Pointer != 0
            ? new ReadOnlySpan<int>((void*)_externalPcm32Pointer, checked(FramesPerChannel * Channels))
            : Pcm32Data;

        public SampleBuffer()
        {
            Data = System.Array.Empty<byte>();
            FloatData = System.Array.Empty<float>();
            Pcm32Data = System.Array.Empty<int>();
            SampleRate = 0;
            Channels = 0;
            BitsPerSample = 0;
            FramesPerChannel = 0;
            BigEndian = true;
        }

        /// <summary>
        /// Sets the endianness for the data.
        /// </summary>
        /// <param name="bigEndian">if true the data will be in big endian, else in little endian</param>
        public void SetBigEndian(bool bigEndian)
        {
            if (bigEndian != BigEndian)
            {
                byte tmp;
                for (int i = 0; i < Data.Length; i += 2)
                {
                    tmp = Data[i];
                    Data[i] = Data[i + 1];
                    Data[i + 1] = tmp;
                }
                BigEndian = bigEndian;
            }
        }

        public void SetExternalFloatBuffer(nint pointer, int capacitySamples)
        {
            if (pointer == 0 || capacitySamples <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacitySamples));
            }

            _externalFloatPointer = pointer;
            _externalFloatCapacity = capacitySamples;
            FloatData = System.Array.Empty<float>();
        }

        public void SetExternalPcm32Buffer(nint pointer, int capacitySamples)
        {
            if (pointer == 0 || capacitySamples <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacitySamples));
            }

            _externalPcm32Pointer = pointer;
            _externalPcm32Capacity = capacitySamples;
            Pcm32Data = System.Array.Empty<int>();
        }

        public void ClearExternalBuffers()
        {
            _externalFloatPointer = 0;
            _externalFloatCapacity = 0;
            _externalPcm32Pointer = 0;
            _externalPcm32Capacity = 0;
            FramesPerChannel = 0;
        }

        internal Span<float> PrepareFloatData(int sampleCount)
        {
            if (_externalFloatPointer != 0)
            {
                if (sampleCount > _externalFloatCapacity)
                {
                    throw new ArgumentException("External Float32 output buffer is too small.");
                }

                return new Span<float>((void*)_externalFloatPointer, sampleCount);
            }

            if (FloatData.Length != sampleCount)
            {
                FloatData = new float[sampleCount];
            }

            return FloatData;
        }

        internal Span<int> PreparePcm32Data(int sampleCount)
        {
            if (_externalPcm32Pointer != 0)
            {
                if (sampleCount > _externalPcm32Capacity)
                {
                    throw new ArgumentException("External PCM32 output buffer is too small.");
                }

                return new Span<int>((void*)_externalPcm32Pointer, sampleCount);
            }

            if (Pcm32Data.Length != sampleCount)
            {
                Pcm32Data = new int[sampleCount];
            }

            return Pcm32Data;
        }

        internal void CommitFloatData(int sampleCount, int sampleRate, int channels, int bitsRead)
        {
            Data = System.Array.Empty<byte>();
            Pcm32Data = System.Array.Empty<int>();
            CommitFormat(sampleCount, sampleRate, channels, bitsRead);
        }

        internal void CommitPcm32Data(int sampleCount, int sampleRate, int channels, int bitsRead)
        {
            Data = System.Array.Empty<byte>();
            FloatData = System.Array.Empty<float>();
            CommitFormat(sampleCount, sampleRate, channels, bitsRead);
        }

        private void CommitFormat(int sampleCount, int sampleRate, int channels, int bitsRead)
        {
            SampleRate = sampleRate;
            Channels = channels;
            BitsPerSample = 32;
            FramesPerChannel = channels == 0 ? 0 : sampleCount / channels;
            BigEndian = false;

            if (sampleRate == 0 || FramesPerChannel == 0)
            {
                Length = 0;
                Bitrate = 0;
                EncodedBitrate = 0;
                return;
            }

            Length = FramesPerChannel / (double)sampleRate;
            Bitrate = sampleRate * BitsPerSample * channels;
            EncodedBitrate = bitsRead * sampleRate / (double)FramesPerChannel;
        }

        public void SetData(byte[] data, int sampleRate, int channels, int bitsPerSample, int bitsRead)
        {
            Data = data;
            FloatData = System.Array.Empty<float>();
            Pcm32Data = System.Array.Empty<int>();
            SampleRate = sampleRate;
            Channels = channels;
            BitsPerSample = bitsPerSample;
            FramesPerChannel = sampleRate == 0 || channels == 0 || bitsPerSample == 0
                ? 0
                : data.Length / ((bitsPerSample / 8) * channels);

            if (sampleRate == 0)
            {
                Length = 0;
                Bitrate = 0;
                EncodedBitrate = 0;
            }
            else
            {
                int bytesPerSample = bitsPerSample / 8; //usually 2
                int samplesPerChannel = data.Length / (bytesPerSample * channels); //=1024
                Length = samplesPerChannel / (double)sampleRate;
                Bitrate = samplesPerChannel * bitsPerSample * channels / Length;
                EncodedBitrate = bitsRead / Length;
            }
        }

        public void SetFloatData(float[] data, int sampleRate, int channels, int bitsRead)
        {
            FloatData = data;
            Data = System.Array.Empty<byte>();
            Pcm32Data = System.Array.Empty<int>();
            SampleRate = sampleRate;
            Channels = channels;
            BitsPerSample = 32;
            FramesPerChannel = channels == 0 ? 0 : data.Length / channels;
            BigEndian = false;

            if (sampleRate == 0 || FramesPerChannel == 0)
            {
                Length = 0;
                Bitrate = 0;
                EncodedBitrate = 0;
                return;
            }

            Length = FramesPerChannel / (double)sampleRate;
            Bitrate = sampleRate * BitsPerSample * channels;
            EncodedBitrate = bitsRead * sampleRate / (double)FramesPerChannel;
        }

        public void SetPcm32Data(int[] data, int sampleRate, int channels, int bitsRead)
        {
            Pcm32Data = data;
            Data = System.Array.Empty<byte>();
            FloatData = System.Array.Empty<float>();
            SampleRate = sampleRate;
            Channels = channels;
            BitsPerSample = 32;
            FramesPerChannel = channels == 0 ? 0 : data.Length / channels;
            BigEndian = false;

            if (sampleRate == 0 || FramesPerChannel == 0)
            {
                Length = 0;
                Bitrate = 0;
                EncodedBitrate = 0;
                return;
            }

            Length = FramesPerChannel / (double)sampleRate;
            Bitrate = sampleRate * BitsPerSample * channels;
            EncodedBitrate = bitsRead * sampleRate / (double)FramesPerChannel;
        }
    }
}
