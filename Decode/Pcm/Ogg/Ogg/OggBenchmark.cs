using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using ManagedBass;

internal static class OggBenchmark
{
    private readonly record struct FloatPairSummary(int FirstBitMismatch, int MaxIndex);

    private readonly record struct Pcm32PairSummary(int FirstMismatch, int MaxIndex);

    internal static void Run(string path, int iterations, Action<string, int, Func<DecodeResult>> benchmark)
    {
        PrintProbe(path);
        WarmUp(path);

        benchmark($"Manual Ogg hdr x{iterations}", iterations, () => OggVorbisManualDecoder.DecodeHeaders(path));
        benchmark($"Manual packets x{iterations}", iterations, () => OggVorbisManualDecoder.DecodePacketBytes(path));
        benchmark($"Manual Float32 x{iterations}", iterations, () => OggVorbisManualDecoder.DecodeToFloat32(path));
        benchmark($"Manual PCM32 x{iterations}", iterations, () => OggVorbisManualDecoder.DecodeToPcm32(path));
    }

    internal static DecodeResult DecodeBassToFloat32(string path)
    {
        return DecodeBass(path, pcm32: false);
    }

    internal static DecodeResult DecodeBassToPcm32(string path)
    {
        return DecodeBass(path, pcm32: true);
    }

    internal static void CompareFloat32Prefix(string path, int sampleCount)
    {
        if (sampleCount <= 0)
        {
            sampleCount = 262_144;
        }

        var info = OggVorbisManualDecoder.ReadInfo(path);
        var expectedSamples = info.TotalPcmFrames > 0
            ? Math.Min((long)sampleCount, checked(info.TotalPcmFrames * Math.Max(1, info.Channels)))
            : sampleCount;
        var compareSamples = checked((int)Math.Min(int.MaxValue, expectedSamples));
        var manual = OggVorbisManualDecoder.DecodeToFloat32Prefix(path, compareSamples);
        var bass = DecodeBassFloat32Prefix(path, compareSamples);

        Console.WriteLine($"ogg_compare_rate={info.SampleRate}");
        Console.WriteLine($"ogg_compare_channels={info.Channels}");
        Console.WriteLine($"ogg_compare_requested_samples={sampleCount}");
        PrintSignalStats("ogg_compare_manual", manual);
        PrintSignalStats("ogg_compare_bass", bass);
        var pairSummary = PrintPairStats("ogg_compare", manual, "manual", bass, "bass", info.Channels);
        PrintPacketOwnerSummary("ogg_compare", path, info.Channels, pairSummary.FirstBitMismatch, pairSummary.MaxIndex);
        PrintFloatBestLagSummary("ogg_compare_best_lag", manual, bass, info.Channels, maxFrameLag: 4096);
    }

    internal static void CompareFloat32TriplePrefix(string path, int sampleCount)
    {
        if (sampleCount <= 0)
        {
            sampleCount = 262_144;
        }

        var info = OggVorbisManualDecoder.ReadInfo(path);
        var expectedSamples = info.TotalPcmFrames > 0
            ? Math.Min((long)sampleCount, checked(info.TotalPcmFrames * Math.Max(1, info.Channels)))
            : sampleCount;
        var compareSamples = checked((int)Math.Min(int.MaxValue, expectedSamples));
        var manual = OggVorbisManualDecoder.DecodeToFloat32Prefix(path, compareSamples);
        var bass = DecodeBassFloat32Prefix(path, compareSamples);
        var ffmpegAvailable = TryDecodeFfmpegFloat32Prefix(path, compareSamples, out var ffmpeg, out var ffmpegStatus);

        Console.WriteLine($"ogg_compare_rate={info.SampleRate}");
        Console.WriteLine($"ogg_compare_channels={info.Channels}");
        Console.WriteLine($"ogg_compare_requested_samples={sampleCount}");
        Console.WriteLine($"ogg_compare_ffmpeg_status={ffmpegStatus}");
        PrintSignalStats("ogg_compare_manual", manual);
        PrintSignalStats("ogg_compare_bass", bass);
        var manualBassSummary = PrintPairStats("ogg_compare_mb", manual, "manual", bass, "bass", info.Channels);
        PrintPacketOwnerSummary("ogg_compare_mb", path, info.Channels, manualBassSummary.FirstBitMismatch, manualBassSummary.MaxIndex);
        PrintFloatBestLagSummary("ogg_compare_mb_best_lag", manual, bass, info.Channels, maxFrameLag: 4096);

        if (!ffmpegAvailable)
        {
            return;
        }

        PrintSignalStats("ogg_compare_ffmpeg", ffmpeg);
        PrintPairStats("ogg_compare_mf", manual, "manual", ffmpeg, "ffmpeg", info.Channels);
        PrintPairStats("ogg_compare_bf", bass, "bass", ffmpeg, "ffmpeg", info.Channels);
        PrintFloatBestLagSummary("ogg_compare_mf_best_lag", manual, ffmpeg, info.Channels, maxFrameLag: 4096);
        PrintFloatBestLagSummary("ogg_compare_bf_best_lag", bass, ffmpeg, info.Channels, maxFrameLag: 4096);
    }

    internal static void ComparePcm32Prefix(string path, int sampleCount)
    {
        if (sampleCount <= 0)
        {
            sampleCount = 262_144;
        }

        var info = OggVorbisManualDecoder.ReadInfo(path);
        var expectedSamples = info.TotalPcmFrames > 0
            ? Math.Min((long)sampleCount, checked(info.TotalPcmFrames * Math.Max(1, info.Channels)))
            : sampleCount;
        var compareSamples = checked((int)Math.Min(int.MaxValue, expectedSamples));
        var manual = OggVorbisManualDecoder.DecodeToPcm32Prefix(path, compareSamples);
        var bass = DecodeBassPcm32Prefix(path, compareSamples);

        Console.WriteLine($"ogg_compare_pcm32_rate={info.SampleRate}");
        Console.WriteLine($"ogg_compare_pcm32_channels={info.Channels}");
        Console.WriteLine($"ogg_compare_pcm32_requested_samples={sampleCount}");
        PrintPcm32SignalStats("ogg_compare_pcm32_manual", manual);
        PrintPcm32SignalStats("ogg_compare_pcm32_bass", bass);
        var pairSummary = PrintPcm32PairStats("ogg_compare_pcm32", manual, "manual", bass, "bass", info.Channels);
        PrintPacketOwnerSummary("ogg_compare_pcm32", path, info.Channels, pairSummary.FirstMismatch, pairSummary.MaxIndex);
    }

    internal static void PrintDebugPackets(string path, int startAudioPacket, int packetCount)
    {
        var packets = OggVorbisManualDecoder.DebugFloat32Packets(path, startAudioPacket, packetCount);
        Console.WriteLine($"ogg_debug_packet_start={startAudioPacket}");
        Console.WriteLine($"ogg_debug_packet_count={packets.Length}");
        for (var i = 0; i < packets.Length; i++)
        {
            var packet = packets[i];
            Console.WriteLine(
                $"ogg_debug_packet[{i}] audio_index={packet.AudioPacketIndex} granule={packet.GranulePosition} mode={packet.ModeNumber} block={packet.BlockSize} prev={(packet.PreviousWindowFlag ? 1 : 0)} next={(packet.NextWindowFlag ? 1 : 0)} floors_present={packet.FloorPresentChannels} floors_absent={packet.FloorAbsentChannels} floor_max={packet.FloorMaxAbs:R} residue_pre_coupling_max={packet.ResidueMaxAbsBeforeCoupling:R} residue_post_coupling_max={packet.ResidueMaxAbsAfterCoupling:R} residue_post_floor_max={packet.ResidueMaxAbsAfterFloor:R} residue_max_book={packet.ResidueMaxBookIndexUsed} residue_lookup_max={packet.ResidueLookupVectorMaxAbsUsed:R} imdct_max={packet.ImdctMaxAbs:R} windowed_max={packet.WindowedMaxAbs:R} output_max={packet.OutputMaxAbs:R} output_frames={packet.OutputFramesProduced} output_frame_start={packet.OutputFrameStart} output_frame_end={packet.OutputFrameEnd}");
        }
    }

    internal static void PrintProfile(string path)
    {
        var profile = OggVorbisManualDecoder.ProfileDecodeToFloat32(path);
        var totalCodebookDecodes = profile.CodebookFastPathHits + profile.CodebookTreeDecodes;
        var codebookFastHitRate = totalCodebookDecodes == 0 ? 0d : profile.CodebookFastPathHits * 100d / totalCodebookDecodes;
        var codebookTreeAverageBits = profile.CodebookTreeDecodes == 0 ? 0d : profile.CodebookTreeBits / (double)profile.CodebookTreeDecodes;
        Console.WriteLine($"ogg_profile_audio_packets={profile.AudioPacketCount}");
        Console.WriteLine($"ogg_profile_output_samples={profile.OutputSamples}");
        Console.WriteLine($"ogg_profile_total_ms={profile.TotalMs:F2}");
        Console.WriteLine($"ogg_profile_container_ms={profile.ContainerMs:F2}");
        Console.WriteLine($"ogg_profile_audio_packet_ms={profile.AudioPacketMs:F2}");
        Console.WriteLine($"ogg_profile_packet_header_ms={profile.PacketHeaderMs:F2}");
        Console.WriteLine($"ogg_profile_residue_setup_ms={profile.ResidueSetupMs:F2}");
        Console.WriteLine($"ogg_profile_reset_ms={profile.ResetMs:F2}");
        Console.WriteLine($"ogg_profile_floor_ms={profile.FloorMs:F2}");
        Console.WriteLine($"ogg_profile_residue_ms={profile.ResidueMs:F2}");
        Console.WriteLine($"ogg_profile_coupling_ms={profile.CouplingMs:F2}");
        Console.WriteLine($"ogg_profile_apply_floor_ms={profile.ApplyFloorMs:F2}");
        Console.WriteLine($"ogg_profile_imdct_ms={profile.ImdctMs:F2}");
        Console.WriteLine($"ogg_profile_window_ms={profile.WindowMs:F2}");
        Console.WriteLine($"ogg_profile_output_ms={profile.OutputMs:F2}");
        Console.WriteLine($"ogg_profile_output_fast_ms={profile.OutputFastPathMs:F2}");
        Console.WriteLine($"ogg_profile_output_scalar_ms={profile.OutputScalarMs:F2}");
        Console.WriteLine($"ogg_profile_output_fast_packets={profile.OutputFastPathPackets}");
        Console.WriteLine($"ogg_profile_output_scalar_packets={profile.OutputScalarPackets}");
        Console.WriteLine($"ogg_profile_codebook_fast_hits={profile.CodebookFastPathHits}");
        Console.WriteLine($"ogg_profile_codebook_tree_decodes={profile.CodebookTreeDecodes}");
        Console.WriteLine($"ogg_profile_codebook_fast_misses={profile.CodebookFastPathMisses}");
        Console.WriteLine($"ogg_profile_codebook_short_reads={profile.CodebookFastPathShortReads}");
        Console.WriteLine($"ogg_profile_codebook_tree_bits={profile.CodebookTreeBits}");
        Console.WriteLine($"ogg_profile_codebook_fast_hit_rate={codebookFastHitRate:F2}");
        Console.WriteLine($"ogg_profile_codebook_tree_avg_bits={codebookTreeAverageBits:F2}");
        Console.WriteLine($"ogg_profile_overhead_ms={profile.OverheadMs:F2}");

        PrintProfilePercent("ogg_profile_pct_packet_header", profile.PacketHeaderMs, profile.AudioPacketMs);
        PrintProfilePercent("ogg_profile_pct_residue_setup", profile.ResidueSetupMs, profile.AudioPacketMs);
        PrintProfilePercent("ogg_profile_pct_residue", profile.ResidueMs, profile.AudioPacketMs);
        PrintProfilePercent("ogg_profile_pct_imdct", profile.ImdctMs, profile.AudioPacketMs);
        PrintProfilePercent("ogg_profile_pct_window", profile.WindowMs, profile.AudioPacketMs);
        PrintProfilePercent("ogg_profile_pct_output", profile.OutputMs, profile.AudioPacketMs);
        PrintProfilePercent("ogg_profile_pct_output_fast", profile.OutputFastPathMs, profile.AudioPacketMs);
        PrintProfilePercent("ogg_profile_pct_output_scalar", profile.OutputScalarMs, profile.AudioPacketMs);
        PrintProfilePercent("ogg_profile_pct_floor", profile.FloorMs, profile.AudioPacketMs);
        PrintProfilePercent("ogg_profile_pct_apply_floor", profile.ApplyFloorMs, profile.AudioPacketMs);
        PrintProfilePercent("ogg_profile_pct_coupling", profile.CouplingMs, profile.AudioPacketMs);
        PrintProfilePercent("ogg_profile_pct_reset", profile.ResetMs, profile.AudioPacketMs);
        PrintProfilePercent("ogg_profile_pct_overhead", profile.OverheadMs, profile.AudioPacketMs);
    }

    internal static void PrintProbe(string path)
    {
        var info = OggVorbisManualDecoder.ReadInfo(path);
        Console.WriteLine($"OGG Vorbis: rate={info.SampleRate} channels={info.Channels} frames={info.TotalPcmFrames} audioPackets={info.AudioPacketCount}");
        Console.WriteLine($"bitrate: max={FormatBitrate(info.BitrateMaximum)} nominal={FormatBitrate(info.BitrateNominal)} min={FormatBitrate(info.BitrateMinimum)}");
        Console.WriteLine($"blocksize: small={info.BlockSizeSmall} large={info.BlockSizeLarge}");
        Console.WriteLine($"ogg: serial=0x{info.SerialNumber:X8} pages={info.PageCount} packets={info.PacketCount} continuedPages={info.ContinuedPageCount} maxPacketBytes={info.MaxPacketBytes} maxPagePayloadBytes={info.MaxPagePayloadBytes}");
        Console.WriteLine($"comments: vendor=\"{info.Vendor}\" count={info.Comments.Length}");
        for (var i = 0; i < Math.Min(4, info.Comments.Length); i++)
        {
            Console.WriteLine($"comment[{i}]={info.Comments[i]}");
        }

        var setup = info.Setup;
        Console.WriteLine($"setup: codebooks={setup.CodebookCount} floors={setup.FloorCount} residues={setup.ResidueCount} mappings={setup.MappingCount} modes={setup.ModeCount} times={setup.TimeCount}");
        Console.WriteLine($"setup-codebook-max: entries={setup.MaxCodebookEntries} dimensions={setup.MaxCodebookDimensions} framing={setup.FramingBit}");
        PrintTopCodebookLookupRanges(setup.Codebooks, 8);
        var audio = info.AudioPacketSummary;
        Console.WriteLine($"audio-packets: parsed={audio.ParsedAudioPackets} short={audio.ShortBlockPackets} long={audio.LongBlockPackets} prevWindowSet={audio.PreviousWindowSetPackets} nextWindowSet={audio.NextWindowSetPackets} floorsPresent={audio.FloorPresentChannels} floorsAbsent={audio.FloorAbsentChannels} floorValues={audio.DecodedFloorValues} floorCurveValues={audio.MaterializedFloorValues} floorHash=0x{audio.FloorCurveHash:X16} residueClasswords={audio.DecodedResidueClasswords} residueVectors={audio.DecodedResidueVectors} residueValues={audio.MaterializedResidueValues} residueHash=0x{audio.ResidueValueHash:X16}");
        if (audio.ModePacketCounts.Length > 0)
        {
            Console.WriteLine($"audio-modes: {FormatModeCounts(audio.ModePacketCounts)}");
        }
    }

    private static void WarmUp(string path)
    {
        _ = OggVorbisManualDecoder.DecodeHeaders(path);
        _ = OggVorbisManualDecoder.DecodePacketBytes(path);
        _ = OggVorbisManualDecoder.DecodeToFloat32(path);
        _ = OggVorbisManualDecoder.DecodeToPcm32(path);
    }

    private static DecodeResult DecodeBass(string path, bool pcm32)
    {
        EnsureBassInitialized();
        var stream = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
        if (stream == 0)
        {
            throw new InvalidOperationException($"BASS create OGG stream failed: {Bass.LastError}");
        }

        try
        {
            if (!Bass.ChannelGetInfo(stream, out var info))
            {
                throw new InvalidOperationException($"BASS get OGG info failed: {Bass.LastError}");
            }

            var channels = Math.Max(1, info.Channels);
            var buffer = new float[8192 * channels];
            var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                long sink = 0;
                long totalSamples = 0;

                while (true)
                {
                    var bytesRead = Bass.ChannelGetData(stream, pin.AddrOfPinnedObject(), buffer.Length * sizeof(float));
                    if (bytesRead <= 0)
                    {
                        break;
                    }

                    var sampleCount = bytesRead / sizeof(float);
                    for (var i = 0; i < sampleCount; i++)
                    {
                        sink = pcm32
                            ? unchecked(sink ^ FloatToPcm32(buffer[i]))
                            : unchecked(sink ^ BitConverter.SingleToInt32Bits(buffer[i]));
                    }

                    totalSamples += sampleCount;
                }

                return new DecodeResult(totalSamples, sink);
            }
            finally
            {
                pin.Free();
            }
        }
        finally
        {
            Bass.StreamFree(stream);
        }
    }

    private static float[] DecodeBassFloat32Prefix(string path, int maxSamples)
    {
        if (maxSamples <= 0)
        {
            return Array.Empty<float>();
        }

        EnsureBassInitialized();

        var stream = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
        if (stream == 0)
        {
            throw new InvalidOperationException($"BASS create OGG stream failed: {Bass.LastError}");
        }

        try
        {
            var samples = new float[maxSamples];
            var pin = GCHandle.Alloc(samples, GCHandleType.Pinned);
            try
            {
                var offset = 0;
                while (offset < samples.Length)
                {
                    var bytesRead = Bass.ChannelGetData(
                        stream,
                        IntPtr.Add(pin.AddrOfPinnedObject(), offset * sizeof(float)),
                        (samples.Length - offset) * sizeof(float));
                    if (bytesRead <= 0)
                    {
                        break;
                    }

                    offset += bytesRead / sizeof(float);
                }

                if (offset == samples.Length)
                {
                    return samples;
                }

                var result = new float[offset];
                Array.Copy(samples, result, result.Length);
                return result;
            }
            finally
            {
                pin.Free();
            }
        }
        finally
        {
            Bass.StreamFree(stream);
        }
    }

    private static bool TryDecodeFfmpegFloat32Prefix(string path, int maxSamples, out float[] samples, out string status)
    {
        samples = Array.Empty<float>();
        if (maxSamples <= 0)
        {
            status = "disabled";
            return false;
        }

        var ffmpegExe = ResolveFfmpegExecutable();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegExe,
                Arguments = $"-v error -nostdin -i \"{path}\" -map a:0 -vn -sn -dn -f f32le -acodec pcm_f32le pipe:1",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            if (!process.Start())
            {
                status = "start-failed";
                return false;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            status = $"missing:{ex.GetType().Name}";
            return false;
        }

        var targetBytes = checked(maxSamples * sizeof(float));
        var rented = ArrayPool<byte>.Shared.Rent(targetBytes);
        var offset = 0;
        try
        {
            while (offset < targetBytes)
            {
                var read = process.StandardOutput.BaseStream.Read(rented, offset, targetBytes - offset);
                if (read <= 0)
                {
                    break;
                }

                offset += read;
            }

            var filledPrefix = offset >= targetBytes;
            if (filledPrefix && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.WaitForExit();
            var stderr = process.StandardError.ReadToEnd().Trim();
            if (!filledPrefix && process.ExitCode != 0)
            {
                status = string.IsNullOrWhiteSpace(stderr)
                    ? $"exit-{process.ExitCode}"
                    : $"exit-{process.ExitCode}:{SanitizeStatus(stderr)}";
                return false;
            }

            var sampleBytes = offset - (offset % sizeof(float));
            var sampleCount = sampleBytes / sizeof(float);
            samples = new float[sampleCount];
            MemoryMarshal.Cast<byte, float>(rented.AsSpan(0, sampleBytes)).CopyTo(samples);
            status = filledPrefix ? $"ok-prefix:{ffmpegExe}" : $"ok:{ffmpegExe}";
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
            process.Dispose();
        }
    }

    private static string ResolveFfmpegExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("APP1_FFMPEG_EXE");
        return string.IsNullOrWhiteSpace(configured) ? "ffmpeg" : configured.Trim();
    }

    private static int[] DecodeBassPcm32Prefix(string path, int maxSamples)
    {
        var floatSamples = DecodeBassFloat32Prefix(path, maxSamples);
        if (floatSamples.Length == 0)
        {
            return Array.Empty<int>();
        }

        var pcm32 = new int[floatSamples.Length];
        for (var i = 0; i < floatSamples.Length; i++)
        {
            pcm32[i] = FloatToPcm32(floatSamples[i]);
        }

        return pcm32;
    }

    private static void EnsureBassInitialized()
    {
        if (!Bass.Init(0, 44100) && Bass.LastError != Errors.Already)
        {
            throw new InvalidOperationException($"BASS init failed: {Bass.LastError}");
        }
    }

    private static void PrintSignalStats(string prefix, ReadOnlySpan<float> samples)
    {
        var maxAbs = 0d;
        var rms = 0d;
        for (var i = 0; i < samples.Length; i++)
        {
            var value = samples[i];
            var abs = Math.Abs((double)value);
            if (abs > maxAbs)
            {
                maxAbs = abs;
            }

            rms += (double)value * value;
        }

        if (samples.Length > 0)
        {
            rms = Math.Sqrt(rms / samples.Length);
        }

        Console.WriteLine($"{prefix}_samples={samples.Length}");
        Console.WriteLine($"{prefix}_hash=0x{OggVorbisManualDecoder.HashFloat32Samples(samples):X16}");
        Console.WriteLine($"{prefix}_max_abs={maxAbs:G17}");
        Console.WriteLine($"{prefix}_rms={rms:G17}");
    }

    private static void PrintProfilePercent(string prefix, double partMs, double totalMs)
    {
        var pct = totalMs <= double.Epsilon ? 0d : (partMs * 100d) / totalMs;
        Console.WriteLine($"{prefix}={pct:F2}");
    }

    private static void PrintPcm32SignalStats(string prefix, ReadOnlySpan<int> samples)
    {
        long maxAbs = 0;
        var rms = 0d;
        for (var i = 0; i < samples.Length; i++)
        {
            var value = samples[i];
            var abs = Math.Abs((long)value);
            if (abs > maxAbs)
            {
                maxAbs = abs;
            }

            rms += (double)value * value;
        }

        if (samples.Length > 0)
        {
            rms = Math.Sqrt(rms / samples.Length);
        }

        Console.WriteLine($"{prefix}_samples={samples.Length}");
        Console.WriteLine($"{prefix}_hash=0x{OggVorbisManualDecoder.HashPcm32Samples(samples):X16}");
        Console.WriteLine($"{prefix}_max_abs={maxAbs}");
        Console.WriteLine($"{prefix}_rms={rms:G17}");
    }

    private static void PrintFloatBestLagSummary(string prefix, ReadOnlySpan<float> manual, ReadOnlySpan<float> bass, int channels, int maxFrameLag)
    {
        var safeChannels = Math.Max(1, channels);
        var maxSampleLag = Math.Max(0, maxFrameLag) * safeChannels;
        var bestLag = 0;
        var bestScale = 0.0;
        var bestRms = double.PositiveInfinity;
        var bestMeanAbs = 0.0;
        var bestComparedSamples = 0;

        for (var lag = -maxSampleLag; lag <= maxSampleLag; lag += safeChannels)
        {
            var manualStart = lag < 0 ? -lag : 0;
            var bassStart = lag > 0 ? lag : 0;
            var sampleCount = Math.Min(manual.Length - manualStart, bass.Length - bassStart);
            sampleCount -= sampleCount % safeChannels;
            if (sampleCount <= safeChannels * 256)
            {
                continue;
            }

            double sumManualBass = 0;
            double sumManualSquared = 0;
            for (var i = 0; i < sampleCount; i++)
            {
                var manualValue = manual[manualStart + i];
                sumManualBass += manualValue * bass[bassStart + i];
                sumManualSquared += manualValue * manualValue;
            }

            var scale = sumManualSquared <= double.Epsilon ? 0 : sumManualBass / sumManualSquared;
            double sumAbsDiff = 0;
            double sumSquaredDiff = 0;
            for (var i = 0; i < sampleCount; i++)
            {
                var diff = (manual[manualStart + i] * scale) - bass[bassStart + i];
                sumAbsDiff += Math.Abs(diff);
                sumSquaredDiff += diff * diff;
            }

            var rms = Math.Sqrt(sumSquaredDiff / sampleCount);
            if (rms < bestRms)
            {
                bestRms = rms;
                bestLag = lag;
                bestScale = scale;
                bestMeanAbs = sumAbsDiff / sampleCount;
                bestComparedSamples = sampleCount;
            }
        }

        Console.WriteLine($"{prefix}_frames={bestLag / safeChannels}");
        Console.WriteLine($"{prefix}_samples={bestLag}");
        Console.WriteLine($"{prefix}_scale={bestScale:G9}");
        Console.WriteLine($"{prefix}_compared_samples={bestComparedSamples}");
        Console.WriteLine($"{prefix}_mean_abs_diff={bestMeanAbs:G9}");
        Console.WriteLine($"{prefix}_rms_diff={bestRms:G9}");
    }

    private static FloatPairSummary PrintPairStats(string prefix, ReadOnlySpan<float> left, string leftName, ReadOnlySpan<float> right, string rightName, int channels)
    {
        var count = Math.Min(left.Length, right.Length);
        var maxAbs = 0d;
        var meanAbs = 0d;
        var rms = 0d;
        var firstBitMismatch = -1;
        var firstAbove1e6 = -1;
        var firstAbove1e4 = -1;
        var firstLeftAbove1e4 = -1;
        var maxIndex = -1;

        for (var i = 0; i < count; i++)
        {
            var leftSample = left[i];
            var rightSample = right[i];
            var leftAbs = Math.Abs((double)leftSample);
            if (firstLeftAbove1e4 < 0 && leftAbs >= 1e-4d)
            {
                firstLeftAbove1e4 = i;
            }

            var diff = (double)leftSample - rightSample;
            var abs = Math.Abs(diff);
            meanAbs += abs;
            rms += diff * diff;
            if (abs > maxAbs)
            {
                maxAbs = abs;
                maxIndex = i;
            }

            if (firstBitMismatch < 0 && BitConverter.SingleToInt32Bits(leftSample) != BitConverter.SingleToInt32Bits(rightSample))
            {
                firstBitMismatch = i;
            }

            if (firstAbove1e6 < 0 && abs >= 1e-6d)
            {
                firstAbove1e6 = i;
            }

            if (firstAbove1e4 < 0 && abs >= 1e-4d)
            {
                firstAbove1e4 = i;
            }
        }

        if (count > 0)
        {
            meanAbs /= count;
            rms = Math.Sqrt(rms / count);
        }

        Console.WriteLine($"{prefix}_left={leftName}");
        Console.WriteLine($"{prefix}_right={rightName}");
        Console.WriteLine($"{prefix}_samples={count}");
        Console.WriteLine($"{prefix}_left_samples={left.Length}");
        Console.WriteLine($"{prefix}_right_samples={right.Length}");
        Console.WriteLine($"{prefix}_max_abs={maxAbs:G17}");
        Console.WriteLine($"{prefix}_mean_abs={meanAbs:G17}");
        Console.WriteLine($"{prefix}_rms={rms:G17}");
        Console.WriteLine($"{prefix}_first_bit_mismatch={firstBitMismatch}");
        Console.WriteLine($"{prefix}_first_abs_ge_1e-6={firstAbove1e6}");
        Console.WriteLine($"{prefix}_first_abs_ge_1e-4={firstAbove1e4}");
        Console.WriteLine($"{prefix}_first_{leftName}_abs_ge_1e-4={firstLeftAbove1e4}");
        Console.WriteLine($"{prefix}_max_abs_index={maxIndex}");
        if (firstBitMismatch >= 0)
        {
            PrintSampleWindow($"{prefix}_first", left, right, channels, firstBitMismatch, 4);
        }

        if (maxIndex >= 0 && maxIndex != firstBitMismatch)
        {
            PrintSampleWindow($"{prefix}_max", left, right, channels, maxIndex, 4);
        }

        return new FloatPairSummary(firstBitMismatch, maxIndex);
    }

    private static Pcm32PairSummary PrintPcm32PairStats(string prefix, ReadOnlySpan<int> left, string leftName, ReadOnlySpan<int> right, string rightName, int channels)
    {
        var count = Math.Min(left.Length, right.Length);
        long maxAbs = 0;
        var meanAbs = 0d;
        var rms = 0d;
        var mismatchCount = 0;
        var firstMismatch = -1;
        var maxIndex = -1;

        for (var i = 0; i < count; i++)
        {
            var diff = (long)left[i] - right[i];
            var abs = Math.Abs(diff);
            meanAbs += abs;
            rms += (double)diff * diff;
            if (abs > maxAbs)
            {
                maxAbs = abs;
                maxIndex = i;
            }

            if (left[i] != right[i])
            {
                mismatchCount++;
                if (firstMismatch < 0)
                {
                    firstMismatch = i;
                }
            }
        }

        if (count > 0)
        {
            meanAbs /= count;
            rms = Math.Sqrt(rms / count);
        }

        Console.WriteLine($"{prefix}_left={leftName}");
        Console.WriteLine($"{prefix}_right={rightName}");
        Console.WriteLine($"{prefix}_samples={count}");
        Console.WriteLine($"{prefix}_left_samples={left.Length}");
        Console.WriteLine($"{prefix}_right_samples={right.Length}");
        Console.WriteLine($"{prefix}_mismatch_count={mismatchCount}");
        Console.WriteLine($"{prefix}_exact_match={(mismatchCount == 0 ? 1 : 0)}");
        Console.WriteLine($"{prefix}_max_abs={maxAbs}");
        Console.WriteLine($"{prefix}_mean_abs={meanAbs:G17}");
        Console.WriteLine($"{prefix}_rms={rms:G17}");
        Console.WriteLine($"{prefix}_first_mismatch={firstMismatch}");
        Console.WriteLine($"{prefix}_max_abs_index={maxIndex}");
        if (firstMismatch >= 0)
        {
            PrintPcm32SampleWindow($"{prefix}_first", left, right, channels, firstMismatch, 4);
        }

        if (maxIndex >= 0 && maxIndex != firstMismatch)
        {
            PrintPcm32SampleWindow($"{prefix}_max", left, right, channels, maxIndex, 4);
        }

        return new Pcm32PairSummary(firstMismatch, maxIndex);
    }

    private static void PrintPacketOwnerSummary(string prefix, string path, int channels, int firstSampleIndex, int maxSampleIndex)
    {
        if (channels <= 0)
        {
            return;
        }

        var targetFrames = new long[2];
        var targetCount = 0;
        if (firstSampleIndex >= 0)
        {
            targetFrames[targetCount++] = firstSampleIndex / channels;
        }

        var maxFrameIndex = maxSampleIndex >= 0 ? maxSampleIndex / channels : -1;
        if (maxFrameIndex >= 0 && (targetCount == 0 || targetFrames[0] != maxFrameIndex))
        {
            targetFrames[targetCount++] = maxFrameIndex;
        }

        if (targetCount == 0)
        {
            return;
        }

        var owners = targetCount == 1
            ? OggVorbisManualDecoder.FindOwningPacketsForFrames(path, targetFrames[0])
            : OggVorbisManualDecoder.FindOwningPacketsForFrames(path, targetFrames[0], targetFrames[1]);
        if (firstSampleIndex >= 0)
        {
            PrintPacketOwner($"{prefix}_first_packet", owners, firstSampleIndex / channels);
        }

        if (maxFrameIndex >= 0)
        {
            PrintPacketOwner($"{prefix}_max_packet", owners, maxFrameIndex);
        }
    }

    private static void PrintPacketOwner(string prefix, ReadOnlySpan<OggVorbisFramePacketOwnerInfo> owners, long frameIndex)
    {
        for (var i = 0; i < owners.Length; i++)
        {
            var owner = owners[i];
            if (owner.FrameIndex != frameIndex)
            {
                continue;
            }

            Console.WriteLine($"{prefix}_frame={frameIndex}");
            if (!owner.Found)
            {
                Console.WriteLine($"{prefix}_found=0");
                return;
            }

            var packet = owner.Packet;
            Console.WriteLine($"{prefix}_found=1");
            Console.WriteLine($"{prefix}_audio_index={packet.AudioPacketIndex}");
            Console.WriteLine($"{prefix}_mode={packet.ModeNumber}");
            Console.WriteLine($"{prefix}_block={packet.BlockSize}");
            Console.WriteLine($"{prefix}_prev={(packet.PreviousWindowFlag ? 1 : 0)}");
            Console.WriteLine($"{prefix}_next={(packet.NextWindowFlag ? 1 : 0)}");
            Console.WriteLine($"{prefix}_output_frame_start={packet.OutputFrameStart}");
            Console.WriteLine($"{prefix}_output_frame_end={packet.OutputFrameEnd}");
            Console.WriteLine($"{prefix}_output_max={packet.OutputMaxAbs:R}");
            Console.WriteLine($"{prefix}_floor_max={packet.FloorMaxAbs:R}");
            Console.WriteLine($"{prefix}_residue_post_floor_max={packet.ResidueMaxAbsAfterFloor:R}");
            return;
        }

        Console.WriteLine($"{prefix}_frame={frameIndex}");
        Console.WriteLine($"{prefix}_found=0");
    }

    private static string SanitizeStatus(string value)
    {
        const int maxLength = 96;
        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (singleLine.Length <= maxLength)
        {
            return singleLine;
        }

        return singleLine[..maxLength];
    }

    private static void PrintSampleWindow(string prefix, ReadOnlySpan<float> manual, ReadOnlySpan<float> reference, int channels, int centerSample, int radiusFrames)
    {
        var safeChannels = Math.Max(1, channels);
        var centerFrame = centerSample / safeChannels;
        var firstFrame = Math.Max(0, centerFrame - radiusFrames);
        var lastFrame = Math.Min((Math.Min(manual.Length, reference.Length) - 1) / safeChannels, centerFrame + radiusFrames);
        for (var frame = firstFrame; frame <= lastFrame; frame++)
        {
            for (var channel = 0; channel < safeChannels; channel++)
            {
                var sampleIndex = (frame * safeChannels) + channel;
                if (sampleIndex >= manual.Length || sampleIndex >= reference.Length)
                {
                    return;
                }

                var diff = (double)manual[sampleIndex] - reference[sampleIndex];
                Console.WriteLine($"{prefix}_sample[{sampleIndex}] frame={frame} ch={channel} manual={manual[sampleIndex]:R} reference={reference[sampleIndex]:R} diff={diff:G17}");
            }
        }
    }

    private static void PrintPcm32SampleWindow(string prefix, ReadOnlySpan<int> manual, ReadOnlySpan<int> reference, int channels, int centerSample, int radiusFrames)
    {
        var safeChannels = Math.Max(1, channels);
        var centerFrame = centerSample / safeChannels;
        var firstFrame = Math.Max(0, centerFrame - radiusFrames);
        var lastFrame = Math.Min((Math.Min(manual.Length, reference.Length) - 1) / safeChannels, centerFrame + radiusFrames);
        for (var frame = firstFrame; frame <= lastFrame; frame++)
        {
            for (var channel = 0; channel < safeChannels; channel++)
            {
                var sampleIndex = (frame * safeChannels) + channel;
                if (sampleIndex >= manual.Length || sampleIndex >= reference.Length)
                {
                    return;
                }

                var diff = (long)manual[sampleIndex] - reference[sampleIndex];
                Console.WriteLine($"{prefix}_sample[{sampleIndex}] frame={frame} ch={channel} manual={manual[sampleIndex]} reference={reference[sampleIndex]} diff={diff}");
            }
        }
    }

    private static int FloatToPcm32(float value)
    {
        if (value >= 1f)
        {
            return int.MaxValue;
        }

        if (value <= -1f)
        {
            return int.MinValue;
        }

        return value >= 0f
            ? (int)MathF.Round(value * 2147483647f)
            : (int)MathF.Round(value * 2147483648f);
    }

    private static string FormatBitrate(int bitrate)
    {
        return bitrate <= 0
            ? "unset"
            : (bitrate / 1000.0).ToString("F1", CultureInfo.InvariantCulture) + " kbps";
    }

    private static string FormatModeCounts(int[] counts)
    {
        var parts = new string[counts.Length];
        for (var i = 0; i < counts.Length; i++)
        {
            parts[i] = $"{i}:{counts[i]}";
        }

        return string.Join(' ', parts);
    }

    private static void PrintTopCodebookLookupRanges(OggVorbisCodebookInfo[] codebooks, int maxCount)
    {
        if (codebooks.Length == 0 || maxCount <= 0)
        {
            return;
        }

        var stats = new List<(int Index, int LookupType, int Dimensions, int Entries, double Min, double Max, double MaxAbs)>(codebooks.Length);
        for (var i = 0; i < codebooks.Length; i++)
        {
            var vectors = codebooks[i].LookupVectors;
            if (vectors.Length == 0)
            {
                continue;
            }

            var min = double.PositiveInfinity;
            var max = double.NegativeInfinity;
            var maxAbs = 0d;
            for (var j = 0; j < vectors.Length; j++)
            {
                var value = vectors[j];
                min = Math.Min(min, value);
                max = Math.Max(max, value);
                maxAbs = Math.Max(maxAbs, Math.Abs((double)value));
            }

            stats.Add((i, codebooks[i].LookupType, codebooks[i].Dimensions, codebooks[i].Entries, min, max, maxAbs));
        }

        stats.Sort((left, right) => right.MaxAbs.CompareTo(left.MaxAbs));
        var count = Math.Min(maxCount, stats.Count);
        for (var i = 0; i < count; i++)
        {
            var item = stats[i];
            Console.WriteLine($"setup-codebook[{item.Index}]: lookupType={item.LookupType} dims={item.Dimensions} entries={item.Entries} min={item.Min:G17} max={item.Max:G17} maxAbs={item.MaxAbs:G17}");
        }
    }
}
