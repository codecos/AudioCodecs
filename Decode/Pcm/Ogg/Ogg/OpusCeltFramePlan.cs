internal enum OpusCeltBandwidth
{
    Narrowband,
    Wideband,
    SuperWideband,
    Fullband
}

internal readonly record struct OpusCeltFramePlan(
    int Configuration,
    int Channels,
    OpusCeltBandwidth Bandwidth,
    int FrameSamples,
    int LongBlocks,
    int FftSize,
    int StartBand,
    int EndBand,
    int FrameBytes)
{
    internal int BandCount => EndBand - StartBand;
    internal int BitBudget => checked(FrameBytes * 8);
}

internal static class OpusCeltFramePlanner
{
    private const int FirstCeltConfiguration = 16;
    private const int LastCeltConfiguration = 31;
    private const int FullbandEndBand = 21;

    internal static bool TryCreate(
        int configuration,
        int channels,
        int frameBytes,
        out OpusCeltFramePlan plan,
        out string error)
    {
        plan = default;
        error = string.Empty;
        if ((uint)(configuration - FirstCeltConfiguration) > LastCeltConfiguration - FirstCeltConfiguration)
        {
            error = "configuration is not CELT-only";
            return false;
        }

        if (channels is < 1 or > 2)
        {
            error = "CELT mapping family 0 supports mono or stereo only";
            return false;
        }

        if (frameBytes < 0)
        {
            error = "frame length is negative";
            return false;
        }

        var longBlocks = configuration & 3;
        var frameSamples = 120 << longBlocks;
        var bandwidth = configuration switch
        {
            < 20 => OpusCeltBandwidth.Narrowband,
            < 24 => OpusCeltBandwidth.Wideband,
            < 28 => OpusCeltBandwidth.SuperWideband,
            _ => OpusCeltBandwidth.Fullband
        };
        var endBand = bandwidth switch
        {
            OpusCeltBandwidth.Narrowband => 13,
            OpusCeltBandwidth.Wideband => 17,
            OpusCeltBandwidth.SuperWideband => 19,
            _ => FullbandEndBand
        };
        plan = new OpusCeltFramePlan(
            configuration,
            channels,
            bandwidth,
            frameSamples,
            longBlocks,
            frameSamples,
            0,
            endBand,
            frameBytes);
        return true;
    }
}
