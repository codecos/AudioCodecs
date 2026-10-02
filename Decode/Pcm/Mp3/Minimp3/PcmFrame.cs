namespace Minimp3;

public sealed class PcmFrame
{
    public PcmFrame(short[] samples, int sampleRate, int channels)
    {
        Samples = samples;
        SampleRate = sampleRate;
        Channels = channels;
    }

    public short[] Samples { get; }
    public int SampleRate { get; }
    public int Channels { get; }
}
