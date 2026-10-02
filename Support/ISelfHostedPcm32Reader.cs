using System;

namespace No_Glass_Player.AudioDecodeCore.Decode.Pcm;

public interface ISelfHostedPcm32Reader : IDisposable
{
    int SampleRate { get; }
    double DurationSeconds { get; }
    void Seek(TimeSpan position);
    int ReadStereoPcm32Frames(Span<byte> destination);
    int ReadStereoPcm32Frames(Span<int> destination);
    int ReadStereoWorkFrames(Span<long> destination);
}
