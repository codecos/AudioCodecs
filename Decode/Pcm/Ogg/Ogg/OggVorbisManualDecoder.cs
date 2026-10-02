using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using No_Glass_Player.AudioDecodeCore.Decode.Pcm.Ogg;

internal readonly record struct OggVorbisInfo(
    int Channels,
    int SampleRate,
    int BitrateMaximum,
    int BitrateNominal,
    int BitrateMinimum,
    int BlockSizeSmall,
    int BlockSizeLarge,
    string Vendor,
    string[] Comments,
    OggVorbisSetupSummary Setup,
    OggVorbisAudioPacketSummary AudioPacketSummary,
    uint SerialNumber,
    int PageCount,
    int PacketCount,
    int AudioPacketCount,
    int MaxPacketBytes,
    int MaxPagePayloadBytes,
    int ContinuedPageCount,
    long TotalPcmFrames,
    long PacketByteHash);

internal readonly record struct OggVorbisSetupSummary(
    int CodebookCount,
    int TimeCount,
    int FloorCount,
    int ResidueCount,
    int MappingCount,
    int ModeCount,
    int MaxCodebookEntries,
    int MaxCodebookDimensions,
    OggVorbisCodebookInfo[] Codebooks,
    OggVorbisFloorInfo[] Floors,
    OggVorbisResidueInfo[] Residues,
    OggVorbisMappingInfo[] Mappings,
    OggVorbisModeInfo[] Modes,
    bool FramingBit);

internal readonly record struct OggVorbisCodebookInfo(
    int Dimensions,
    int Entries,
    int LookupType,
    long LookupValues,
    byte[] CodewordLengths,
    int[] DecodeZero,
    int[] DecodeOne,
    int[] DecodeEntries,
    int MaxCodewordLength,
    int FastLookupBitCount,
    byte[] FastLookupLengths,
    int[] FastLookupEntries,
    int[] LookupEntryOffsets,
    float[] LookupVectors,
    float LookupVectorMaxAbs);

internal readonly record struct OggVorbisFloorInfo(
    int Type,
    int ValueCount,
    int[] PartitionClasses,
    int[] ClassDimensions,
    int[] ClassSubclasses,
    int[] ClassMasterbooks,
    int[][] SubclassBooks,
    int Multiplier,
    int RangeBits,
    int[] XList);

internal readonly record struct OggVorbisResidueInfo(
    int Type,
    int Begin,
    int End,
    int PartitionSize,
    int Classifications,
    int Classbook,
    int[] Cascades,
    int[][] Books,
    OggVorbisResidueStageInfo[] StageInfos);

internal readonly record struct OggVorbisResidueStageInfo(
    bool IsUsed,
    int BookIndex,
    int Dimensions,
    int VectorCount);

internal readonly record struct OggVorbisMappingInfo(
    int Submaps,
    int CouplingSteps,
    int[] Magnitudes,
    int[] Angles,
    int[] Mux,
    int[] SubmapFloors,
    int[] SubmapResidues,
    int[][] SubmapChannels);

internal readonly record struct OggVorbisModeInfo(bool BlockFlag, int Mapping);

internal readonly record struct OggVorbisAudioPacketSummary(
    int ParsedAudioPackets,
    int ShortBlockPackets,
    int LongBlockPackets,
    int PreviousWindowSetPackets,
    int NextWindowSetPackets,
    int FloorPresentChannels,
    int FloorAbsentChannels,
    int DecodedFloorValues,
    int MaterializedFloorValues,
    long FloorCurveHash,
    int DecodedResidueClasswords,
    int DecodedResidueVectors,
    int MaterializedResidueValues,
    long ResidueValueHash,
    int[] ModePacketCounts,
    long ModePacketHash);

internal readonly record struct OggVorbisPacketDebugInfo(
    int AudioPacketIndex,
    long GranulePosition,
    int ModeNumber,
    bool BlockFlag,
    bool PreviousWindowFlag,
    bool NextWindowFlag,
    int BlockSize,
    int BlockSizeHalf,
    int FloorPresentChannels,
    int FloorAbsentChannels,
    float FloorMaxAbs,
    float ResidueMaxAbsBeforeCoupling,
    float ResidueMaxAbsAfterCoupling,
    float ResidueMaxAbsAfterFloor,
    int ResidueMaxBookIndexUsed,
    float ResidueLookupVectorMaxAbsUsed,
    float ImdctMaxAbs,
    float WindowedMaxAbs,
    float OutputMaxAbs,
    int OutputFramesProduced,
    long OutputFrameStart,
    long OutputFrameEnd);

internal readonly record struct OggVorbisFramePacketOwnerInfo(
    long FrameIndex,
    bool Found,
    OggVorbisPacketDebugInfo Packet);

internal readonly record struct OggVorbisProfileSummary(
    int AudioPacketCount,
    long OutputSamples,
    double TotalMs,
    double ContainerMs,
    double AudioPacketMs,
    double PacketHeaderMs,
    double ResidueSetupMs,
    double ResetMs,
    double FloorMs,
    double ResidueMs,
    double CouplingMs,
    double ApplyFloorMs,
    double ImdctMs,
    double ImdctButterfliesMs,
    double ImdctBitReverseMs,
    double WindowMs,
    double OutputMs,
    double OutputFastPathMs,
    double OutputScalarMs,
    int OutputFastPathPackets,
    int OutputScalarPackets,
    double ResidueType2Ms,
    double ResidueOtherMs,
    double ResidueType2WriteMs,
    double ResidueOtherWriteMs,
    long ResidueType2Vectors,
    long ResidueOtherVectors,
    long CodebookFastPathHits,
    long CodebookTreeDecodes,
    long CodebookFastPathMisses,
    long CodebookFastPathShortReads,
    long CodebookTreeBits,
    double OverheadMs);

internal static partial class OggVorbisManualDecoder
{
    private const long FnvOffsetBasis = 1469598103934665603L;
    private const long FnvPrime = 1099511628211L;
    private const int CodebookFastLookupBits = 10;

    private static readonly byte[] VorbisSignature = "vorbis"u8.ToArray();
    private const int FloorStepFlag = 0x8000;
    private const int FloorValueMask = 0x7FFF;
    private static readonly float[] FloorInverseDbTable =
    [
        1.0649863e-07f, 1.1341951e-07f, 1.2079015e-07f, 1.2863978e-07f,
        1.3699951e-07f, 1.4590251e-07f, 1.5538408e-07f, 1.6548181e-07f,
        1.7623575e-07f, 1.8768855e-07f, 1.9988561e-07f, 2.1287530e-07f,
        2.2670913e-07f, 2.4144197e-07f, 2.5713223e-07f, 2.7384213e-07f,
        2.9163793e-07f, 3.1059021e-07f, 3.3077411e-07f, 3.5226968e-07f,
        3.7516214e-07f, 3.9954229e-07f, 4.2550680e-07f, 4.5315863e-07f,
        4.8260743e-07f, 5.1396998e-07f, 5.4737065e-07f, 5.8294187e-07f,
        6.2082472e-07f, 6.6116941e-07f, 7.0413592e-07f, 7.4989464e-07f,
        7.9862701e-07f, 8.5052630e-07f, 9.0579828e-07f, 9.6466216e-07f,
        1.0273513e-06f, 1.0941144e-06f, 1.1652161e-06f, 1.2409384e-06f,
        1.3215816e-06f, 1.4074654e-06f, 1.4989305e-06f, 1.5963394e-06f,
        1.7000785e-06f, 1.8105592e-06f, 1.9282195e-06f, 2.0535261e-06f,
        2.1869758e-06f, 2.3290978e-06f, 2.4804557e-06f, 2.6416497e-06f,
        2.8133190e-06f, 2.9961443e-06f, 3.1908506e-06f, 3.3982101e-06f,
        3.6190449e-06f, 3.8542308e-06f, 4.1047004e-06f, 4.3714470e-06f,
        4.6555282e-06f, 4.9580707e-06f, 5.2802740e-06f, 5.6234160e-06f,
        5.9888572e-06f, 6.3780469e-06f, 6.7925283e-06f, 7.2339451e-06f,
        7.7040476e-06f, 8.2047000e-06f, 8.7378876e-06f, 9.3057248e-06f,
        9.9104632e-06f, 1.0554501e-05f, 1.1240392e-05f, 1.1970856e-05f,
        1.2748789e-05f, 1.3577278e-05f, 1.4459606e-05f, 1.5399272e-05f,
        1.6400004e-05f, 1.7465768e-05f, 1.8600792e-05f, 1.9809576e-05f,
        2.1096914e-05f, 2.2467911e-05f, 2.3928002e-05f, 2.5482978e-05f,
        2.7139006e-05f, 2.8902651e-05f, 3.0780908e-05f, 3.2781225e-05f,
        3.4911534e-05f, 3.7180282e-05f, 3.9596466e-05f, 4.2169667e-05f,
        4.4910090e-05f, 4.7828601e-05f, 5.0936773e-05f, 5.4246931e-05f,
        5.7772202e-05f, 6.1526565e-05f, 6.5524908e-05f, 6.9783085e-05f,
        7.4317983e-05f, 7.9147585e-05f, 8.4291040e-05f, 8.9768747e-05f,
        9.5602426e-05f, 1.0181521e-04f, 1.0843174e-04f, 1.1547824e-04f,
        1.2298267e-04f, 1.3097477e-04f, 1.3948625e-04f, 1.4855085e-04f,
        1.5820453e-04f, 1.6848555e-04f, 1.7943469e-04f, 1.9109536e-04f,
        2.0351382e-04f, 2.1673929e-04f, 2.3082423e-04f, 2.4582449e-04f,
        2.6179955e-04f, 2.7881276e-04f, 2.9693158e-04f, 3.1622787e-04f,
        3.3677814e-04f, 3.5866388e-04f, 3.8197188e-04f, 4.0679456e-04f,
        4.3323036e-04f, 4.6138411e-04f, 4.9136745e-04f, 5.2329927e-04f,
        5.5730621e-04f, 5.9352311e-04f, 6.3209358e-04f, 6.7317058e-04f,
        7.1691700e-04f, 7.6350630e-04f, 8.1312324e-04f, 8.6596457e-04f,
        9.2223983e-04f, 9.8217216e-04f, 1.0459992e-03f, 1.1139742e-03f,
        1.1863665e-03f, 1.2634633e-03f, 1.3455702e-03f, 1.4330129e-03f,
        1.5261382e-03f, 1.6253153e-03f, 1.7309374e-03f, 1.8434235e-03f,
        1.9632195e-03f, 2.0908006e-03f, 2.2266726e-03f, 2.3713743e-03f,
        2.5254795e-03f, 2.6895994e-03f, 2.8643847e-03f, 3.0505286e-03f,
        3.2487691e-03f, 3.4598925e-03f, 3.6847358e-03f, 3.9241906e-03f,
        4.1792066e-03f, 4.4507950e-03f, 4.7400328e-03f, 5.0480668e-03f,
        5.3761186e-03f, 5.7254891e-03f, 6.0975636e-03f, 6.4938176e-03f,
        6.9158225e-03f, 7.3652516e-03f, 7.8438871e-03f, 8.3536271e-03f,
        8.8964928e-03f, 9.4746370e-03f, 1.0090352e-02f, 1.0746080e-02f,
        1.1444421e-02f, 1.2188144e-02f, 1.2980198e-02f, 1.3823725e-02f,
        1.4722068e-02f, 1.5678791e-02f, 1.6697687e-02f, 1.7782797e-02f,
        1.8938423e-02f, 2.0169149e-02f, 2.1479854e-02f, 2.2875735e-02f,
        2.4362330e-02f, 2.5945531e-02f, 2.7631618e-02f, 2.9427276e-02f,
        3.1339626e-02f, 3.3376252e-02f, 3.5545228e-02f, 3.7855157e-02f,
        4.0315199e-02f, 4.2935108e-02f, 4.5725273e-02f, 4.8696758e-02f,
        5.1861348e-02f, 5.5231591e-02f, 5.8820850e-02f, 6.2643361e-02f,
        6.6714279e-02f, 7.1049749e-02f, 7.5666962e-02f, 8.0584227e-02f,
        8.5821044e-02f, 9.1398179e-02f, 9.7337747e-02f, 1.0366330e-01f,
        1.1039993e-01f, 1.1757434e-01f, 1.2521498e-01f, 1.3335215e-01f,
        1.4201813e-01f, 1.5124727e-01f, 1.6107617e-01f, 1.7154380e-01f,
        1.8269168e-01f, 1.9456402e-01f, 2.0720788e-01f, 2.2067342e-01f,
        2.3501402e-01f, 2.5028656e-01f, 2.6655159e-01f, 2.8387361e-01f,
        3.0232132e-01f, 3.2196786e-01f, 3.4289114e-01f, 3.6517414e-01f,
        3.8890521e-01f, 4.1417847e-01f, 4.4109412e-01f, 4.6975890e-01f,
        5.0028648e-01f, 5.3279791e-01f, 5.6742212e-01f, 6.0429640e-01f,
        6.4356699e-01f, 6.8538959e-01f, 7.2993007e-01f, 7.7736504e-01f,
        8.2788260e-01f, 8.8168307e-01f, 9.3897980e-01f, 1.0000000e+00f
    ];

    internal static OggVorbisInfo ReadInfo(string path)
    {
        OggVorbisIdentificationHeader? identification = null;
        OggVorbisCommentHeader? comment = null;
        OggVorbisSetupSummary? setup = null;
        var stats = new OggStats();

        ReadPackets(
            path,
            page =>
            {
                stats.PageCount++;
                stats.SerialNumber = page.SerialNumber;
                stats.MaxPagePayloadBytes = Math.Max(stats.MaxPagePayloadBytes, page.PayloadBytes);
                if ((page.HeaderType & 0x01) != 0)
                {
                    stats.ContinuedPageCount++;
                }

                if (page.GranulePosition >= 0)
                {
                    stats.LastGranulePosition = page.GranulePosition;
                }
            },
            (packet, packetIndex, _) =>
            {
                stats.PacketCount++;
                stats.MaxPacketBytes = Math.Max(stats.MaxPacketBytes, packet.Length);
                stats.PacketByteHash = HashPacket(stats.PacketByteHash, packet);

                if (packetIndex == 0)
                {
                    identification = ParseIdentificationHeader(packet);
                }
                else if (packetIndex == 1)
                {
                    comment = ParseCommentHeader(packet);
                }
                else if (packetIndex == 2)
                {
                    setup = ParseSetupHeader(packet, identification?.Channels ?? 0);
                }
                else
                {
                    stats.AudioPacketCount++;
                    if (setup is not null)
                    {
                        var audioPacket = ParseAudioPacketHeader(
                            packet,
                            setup.Value,
                            identification?.Channels ?? 0,
                            identification?.BlockSizeSmall ?? 0,
                            identification?.BlockSizeLarge ?? 0);
                        stats.ParsedAudioPacketCount++;
                        if (audioPacket.BlockFlag)
                        {
                            stats.LongBlockPackets++;
                        }
                        else
                        {
                            stats.ShortBlockPackets++;
                        }

                        if (audioPacket.PreviousWindowFlag)
                        {
                            stats.PreviousWindowSetPackets++;
                        }

                        if (audioPacket.NextWindowFlag)
                        {
                            stats.NextWindowSetPackets++;
                        }

                        stats.ModePacketCounts ??= new int[Math.Max(1, setup.Value.ModeCount)];
                        stats.ModePacketCounts[audioPacket.ModeNumber]++;
                        stats.FloorPresentChannels += audioPacket.FloorPresentChannels;
                        stats.FloorAbsentChannels += audioPacket.FloorAbsentChannels;
                        stats.DecodedFloorValues += audioPacket.DecodedFloorValues;
                        stats.MaterializedFloorValues += audioPacket.MaterializedFloorValues;
                        stats.FloorCurveHash = HashCombine(stats.FloorCurveHash, audioPacket.FloorCurveHash);
                        stats.DecodedResidueClasswords += audioPacket.DecodedResidueClasswords;
                        stats.DecodedResidueVectors += audioPacket.DecodedResidueVectors;
                        stats.MaterializedResidueValues += audioPacket.MaterializedResidueValues;
                        stats.ResidueValueHash = HashCombine(stats.ResidueValueHash, audioPacket.ResidueValueHash);
                        stats.ModePacketHash = HashModePacket(stats.ModePacketHash, audioPacket);
                    }
                }

                return true;
            });

        if (identification is null)
        {
            throw new InvalidDataException("No Vorbis identification packet found.");
        }

        if (comment is null)
        {
            throw new InvalidDataException("No Vorbis comment packet found.");
        }

        if (setup is null)
        {
            throw new InvalidDataException("No Vorbis setup packet found.");
        }

        return new OggVorbisInfo(
            identification.Value.Channels,
            identification.Value.SampleRate,
            identification.Value.BitrateMaximum,
            identification.Value.BitrateNominal,
            identification.Value.BitrateMinimum,
            identification.Value.BlockSizeSmall,
            identification.Value.BlockSizeLarge,
            comment.Value.Vendor,
            comment.Value.Comments,
            setup.Value,
            new OggVorbisAudioPacketSummary(
                stats.ParsedAudioPacketCount,
                stats.ShortBlockPackets,
                stats.LongBlockPackets,
                stats.PreviousWindowSetPackets,
                stats.NextWindowSetPackets,
                stats.FloorPresentChannels,
                stats.FloorAbsentChannels,
                stats.DecodedFloorValues,
                stats.MaterializedFloorValues,
                stats.FloorCurveHash,
                stats.DecodedResidueClasswords,
                stats.DecodedResidueVectors,
                stats.MaterializedResidueValues,
                stats.ResidueValueHash,
                stats.ModePacketCounts ?? Array.Empty<int>(),
                stats.ModePacketHash),
            stats.SerialNumber,
            stats.PageCount,
            stats.PacketCount,
            stats.AudioPacketCount,
            stats.MaxPacketBytes,
            stats.MaxPagePayloadBytes,
            stats.ContinuedPageCount,
            Math.Max(0, stats.LastGranulePosition),
            stats.PacketByteHash);
    }

    internal static bool TryReadPlaybackInfo(string path, out OggVorbisInfo info)
    {
        try
        {
            info = ReadPlaybackInfoCore(path);
            return true;
        }
        catch
        {
            info = default;
            return false;
        }
    }

    internal static bool TryReadBasicPlaybackInfo(
        string path,
        out int channels,
        out int sampleRate,
        out long totalPcmFrames)
    {
        channels = 0;
        sampleRate = 0;
        totalPcmFrames = 0;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.SequentialScan);
            Span<byte> header = stackalloc byte[27];
            if (!TryReadExact(stream, header)
                || !header[..4].SequenceEqual("OggS"u8)
                || header[4] != 0)
            {
                return false;
            }

            var segmentCount = header[26];
            if (segmentCount <= 0)
            {
                return false;
            }

            Span<byte> segments = stackalloc byte[255];
            stream.ReadExactly(segments[..segmentCount]);
            var packetLength = 0;
            var packetComplete = false;
            for (var i = 0; i < segmentCount; i++)
            {
                packetLength += segments[i];
                if (segments[i] < 255)
                {
                    packetComplete = true;
                    break;
                }
            }

            Span<byte> identificationPacket = stackalloc byte[64];
            if (!packetComplete || packetLength <= 0 || packetLength > identificationPacket.Length)
            {
                return false;
            }

            stream.ReadExactly(identificationPacket[..packetLength]);
            var identification = ParseIdentificationHeader(identificationPacket[..packetLength]);
            channels = identification.Channels;
            sampleRate = identification.SampleRate;
            totalPcmFrames = Math.Max(0L, ReadLastGranulePosition(path));
            return channels is 1 or 2 && sampleRate > 0 && totalPcmFrames > 0;
        }
        catch
        {
            channels = 0;
            sampleRate = 0;
            totalPcmFrames = 0;
            return false;
        }
    }

    private static OggVorbisInfo ReadPlaybackInfoCore(
        string path,
        OggReaderWorkspace? workspace = null,
        bool parseComments = true)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
        var ownsWorkspace = workspace is null;
        workspace ??= new OggReaderWorkspace();

        OggVorbisIdentificationHeader? identification = null;
        OggVorbisCommentHeader? comment = null;
        OggVorbisSetupSummary? setup = null;
        uint serialNumber = 0;
        var packetCount = 0;
        var maxPagePayloadBytes = 0;
        var continuedPageCount = 0;

        var packetLength = 0;

        try
        {
            workspace.PreparePacketWorkspace();
            Span<byte> header = stackalloc byte[27];

            while (packetCount < 3 && TryReadExact(stream, header))
            {
                if (header[0] != (byte)'O' || header[1] != (byte)'g' || header[2] != (byte)'g' || header[3] != (byte)'S')
                {
                    throw new InvalidDataException($"Invalid Ogg capture pattern at stream offset {stream.Position - header.Length}.");
                }

                if (header[4] != 0)
                {
                    throw new InvalidDataException($"Unsupported Ogg bitstream version: {header[4]}.");
                }

                var headerType = header[5];
                serialNumber = BinaryPrimitives.ReadUInt32LittleEndian(header[14..18]);
                var pageSegmentCount = header[26];
                var segmentSpan = workspace.SegmentTable[..pageSegmentCount];
                stream.ReadExactly(segmentSpan);

                var payloadBytes = 0;
                for (var i = 0; i < segmentSpan.Length; i++)
                {
                    payloadBytes += segmentSpan[i];
                }

                maxPagePayloadBytes = Math.Max(maxPagePayloadBytes, payloadBytes);
                if ((headerType & 0x01) != 0)
                {
                    continuedPageCount++;
                }

                var payloadSpan = workspace.PagePayload[..payloadBytes];
                stream.ReadExactly(payloadSpan);

                var payloadOffset = 0;
                for (var i = 0; i < segmentSpan.Length; i++)
                {
                    var segmentBytes = segmentSpan[i];
                    if (segmentBytes > 0)
                    {
                        var requiredPacketBytes = checked(packetLength + segmentBytes);
                        workspace.EnsurePacketCapacity(requiredPacketBytes);
                        payloadSpan.Slice(payloadOffset, segmentBytes)
                            .CopyTo(workspace.PacketBuffer.Slice(packetLength, segmentBytes));
                        packetLength = requiredPacketBytes;
                        payloadOffset += segmentBytes;
                    }

                    if (segmentBytes >= 255)
                    {
                        continue;
                    }

                    var packet = workspace.PacketBuffer[..packetLength];
                    switch (packetCount)
                    {
                        case 0:
                            identification = ParseIdentificationHeader(packet);
                            break;
                        case 1:
                            comment = parseComments
                                ? ParseCommentHeader(packet)
                                : new OggVorbisCommentHeader(string.Empty, Array.Empty<string>());
                            break;
                        case 2:
                            if (identification is null)
                            {
                                throw new InvalidDataException("Vorbis setup packet arrived before identification.");
                            }

                            var channels = identification.Value.Channels;
                            if (!workspace.TryGetCachedSetup(packet, channels, out var cachedSetup))
                            {
                                cachedSetup = ParseSetupHeader(packet, channels);
                                workspace.CacheSetup(packet, channels, cachedSetup);
                            }

                            setup = cachedSetup;
                            break;
                    }

                    packetCount++;
                    packetLength = 0;

                    if (packetCount >= 3)
                    {
                        break;
                    }
                }
            }

            if (packetLength != 0)
            {
                throw new InvalidDataException("Ogg stream ended inside a continued packet.");
            }
        }
        finally
        {
            if (ownsWorkspace)
            {
                workspace.Dispose();
            }
        }

        if (identification is null)
        {
            throw new InvalidDataException("No Vorbis identification packet found.");
        }

        if (comment is null)
        {
            throw new InvalidDataException("No Vorbis comment packet found.");
        }

        if (setup is null)
        {
            throw new InvalidDataException("No Vorbis setup packet found.");
        }

        var totalPcmFrames = ReadLastGranulePosition(path);
        return new OggVorbisInfo(
            identification.Value.Channels,
            identification.Value.SampleRate,
            identification.Value.BitrateMaximum,
            identification.Value.BitrateNominal,
            identification.Value.BitrateMinimum,
            identification.Value.BlockSizeSmall,
            identification.Value.BlockSizeLarge,
            comment.Value.Vendor,
            comment.Value.Comments,
            setup.Value,
            new OggVorbisAudioPacketSummary(
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                Array.Empty<int>(),
                0),
            serialNumber,
            0,
            packetCount,
            0,
            0,
            maxPagePayloadBytes,
            continuedPageCount,
            Math.Max(0, totalPcmFrames),
            0);
    }

    internal static DecodeResult DecodePacketBytes(string path)
    {
        long sink = 0;
        long packetBytes = 0;

        ReadPackets(
            path,
            page => { },
            (packet, _, _) =>
            {
                sink = HashPacket(sink, packet);
                packetBytes += packet.Length;
                return true;
            });

        return new DecodeResult(packetBytes, sink);
    }

    internal static DecodeResult DecodeHeaders(string path)
    {
        var info = ReadInfo(path);
        var sink = info.PacketByteHash
            ^ ((long)info.SampleRate << 32)
            ^ ((long)info.Channels << 24)
            ^ ((long)info.Setup.CodebookCount << 16)
            ^ info.Setup.ModeCount
            ^ info.AudioPacketSummary.ModePacketHash;
        return new DecodeResult(info.TotalPcmFrames * Math.Max(1, info.Channels), sink);
    }

    internal static DecodeResult DecodeToFloat32(string path)
    {
        OggVorbisIdentificationHeader? identification = null;
        OggVorbisSetupSummary? setup = null;
        OggVorbisFloat32Context? context = null;

        ReadPackets(
            path,
            page => { },
            (packet, packetIndex, granulePosition) =>
            {
                if (packetIndex == 0)
                {
                    identification = ParseIdentificationHeader(packet);
                    return true;
                }

                if (packetIndex == 1)
                {
                    _ = ParseCommentHeader(packet);
                    return true;
                }

                if (packetIndex == 2)
                {
                    if (identification is null)
                    {
                        throw new InvalidDataException("Vorbis setup packet arrived before identification.");
                    }

                    setup = ParseSetupHeader(packet, identification.Value.Channels);
                    context = new OggVorbisFloat32Context(
                        identification.Value.Channels,
                        identification.Value.BlockSizeSmall,
                        identification.Value.BlockSizeLarge,
                        setup.Value,
                        OggVorbisOutputTarget.Float32);
                    return true;
                }

                if (identification is null || setup is null || context is null)
                {
                    throw new InvalidDataException("Vorbis audio packet arrived before setup.");
                }

                context.DecodePacket(packet, granulePosition);
                return true;
            });

        return context?.ToDecodeResult() ?? new DecodeResult(0, 0);
    }

    internal static DecodeResult DecodeToPcm32(string path)
    {
        OggVorbisIdentificationHeader? identification = null;
        OggVorbisSetupSummary? setup = null;
        OggVorbisFloat32Context? context = null;

        ReadPackets(
            path,
            page => { },
            (packet, packetIndex, granulePosition) =>
            {
                if (packetIndex == 0)
                {
                    identification = ParseIdentificationHeader(packet);
                    return true;
                }

                if (packetIndex == 1)
                {
                    _ = ParseCommentHeader(packet);
                    return true;
                }

                if (packetIndex == 2)
                {
                    if (identification is null)
                    {
                        throw new InvalidDataException("Vorbis setup packet arrived before identification.");
                    }

                    setup = ParseSetupHeader(packet, identification.Value.Channels);
                    context = new OggVorbisFloat32Context(
                        identification.Value.Channels,
                        identification.Value.BlockSizeSmall,
                        identification.Value.BlockSizeLarge,
                        setup.Value,
                        OggVorbisOutputTarget.Pcm32);
                    return true;
                }

                if (identification is null || setup is null || context is null)
                {
                    throw new InvalidDataException("Vorbis audio packet arrived before setup.");
                }

                context.DecodePacket(packet, granulePosition);
                return true;
            });

        return context?.ToDecodeResult() ?? new DecodeResult(0, 0);
    }

    internal static float[] DecodeToFloat32Samples(string path)
    {
        var info = ReadInfo(path);
        var totalSamples = info.TotalPcmFrames * Math.Max(1, info.Channels);
        if (totalSamples <= 0)
        {
            return Array.Empty<float>();
        }

        if (totalSamples > int.MaxValue)
        {
            throw new NotSupportedException($"OGG sample capture exceeds supported size. samples={totalSamples}");
        }

        return DecodeToFloat32Prefix(path, (int)totalSamples);
    }

    internal static int[] DecodeToPcm32Samples(string path)
    {
        var info = ReadInfo(path);
        var totalSamples = info.TotalPcmFrames * Math.Max(1, info.Channels);
        if (totalSamples <= 0)
        {
            return Array.Empty<int>();
        }

        if (totalSamples > int.MaxValue)
        {
            throw new NotSupportedException($"OGG PCM32 sample capture exceeds supported size. samples={totalSamples}");
        }

        return DecodeToPcm32Prefix(path, (int)totalSamples);
    }

    internal static OggVorbisProfileSummary ProfileDecodeToFloat32(string path)
    {
        OggVorbisIdentificationHeader? identification = null;
        OggVorbisSetupSummary? setup = null;
        OggVorbisFloat32Context? context = null;
        var profile = new OggVorbisProfileTotals();
        var totalStart = Stopwatch.GetTimestamp();

        ReadPackets(
            path,
            page => { },
            (packet, packetIndex, granulePosition) =>
            {
                if (packetIndex == 0)
                {
                    identification = ParseIdentificationHeader(packet);
                    return true;
                }

                if (packetIndex == 1)
                {
                    _ = ParseCommentHeader(packet);
                    return true;
                }

                if (packetIndex == 2)
                {
                    if (identification is null)
                    {
                        throw new InvalidDataException("Vorbis setup packet arrived before identification.");
                    }

                    setup = ParseSetupHeader(packet, identification.Value.Channels);
                    context = new OggVorbisFloat32Context(
                        identification.Value.Channels,
                        identification.Value.BlockSizeSmall,
                        identification.Value.BlockSizeLarge,
                        setup.Value,
                        OggVorbisOutputTarget.Float32,
                        captureSampleLimit: 0,
                        profile);
                    return true;
                }

                if (identification is null || setup is null || context is null)
                {
                    throw new InvalidDataException("Vorbis audio packet arrived before setup.");
                }

                context.DecodePacket(packet, granulePosition);
                return true;
            });

        var totalTicks = Stopwatch.GetTimestamp() - totalStart;
        var result = context?.ToDecodeResult() ?? new DecodeResult(0, 0);
        var packetTicks = profile.PacketTicks;
        var stageTicks = profile.PacketHeaderTicks
            + profile.ResidueSetupTicks
            + profile.ResetTicks
            + profile.FloorTicks
            + profile.ResidueTicks
            + profile.CouplingTicks
            + profile.ApplyFloorTicks
            + profile.ImdctTicks
            + profile.WindowTicks
            + profile.OutputTicks;
        return new OggVorbisProfileSummary(
            profile.AudioPacketCount,
            result.Samples,
            StopwatchTicksToMilliseconds(totalTicks),
            StopwatchTicksToMilliseconds(Math.Max(0L, totalTicks - packetTicks)),
            StopwatchTicksToMilliseconds(packetTicks),
            StopwatchTicksToMilliseconds(profile.PacketHeaderTicks),
            StopwatchTicksToMilliseconds(profile.ResidueSetupTicks),
            StopwatchTicksToMilliseconds(profile.ResetTicks),
            StopwatchTicksToMilliseconds(profile.FloorTicks),
            StopwatchTicksToMilliseconds(profile.ResidueTicks),
            StopwatchTicksToMilliseconds(profile.CouplingTicks),
            StopwatchTicksToMilliseconds(profile.ApplyFloorTicks),
            StopwatchTicksToMilliseconds(profile.ImdctTicks),
            StopwatchTicksToMilliseconds(profile.ImdctButterfliesTicks),
            StopwatchTicksToMilliseconds(profile.ImdctBitReverseTicks),
            StopwatchTicksToMilliseconds(profile.WindowTicks),
            StopwatchTicksToMilliseconds(profile.OutputTicks),
            StopwatchTicksToMilliseconds(profile.OutputFastPathTicks),
            StopwatchTicksToMilliseconds(profile.OutputScalarTicks),
            profile.OutputFastPathPackets,
            profile.OutputScalarPackets,
            StopwatchTicksToMilliseconds(profile.ResidueType2Ticks),
            StopwatchTicksToMilliseconds(profile.ResidueOtherTicks),
            StopwatchTicksToMilliseconds(profile.ResidueType2WriteTicks),
            StopwatchTicksToMilliseconds(profile.ResidueOtherWriteTicks),
            profile.ResidueType2Vectors,
            profile.ResidueOtherVectors,
            profile.CodebookFastPathHits,
            profile.CodebookTreeDecodes,
            profile.CodebookFastPathMisses,
            profile.CodebookFastPathShortReads,
            profile.CodebookTreeBits,
            StopwatchTicksToMilliseconds(Math.Max(0L, packetTicks - stageTicks)));
    }

    internal static float[] DecodeToFloat32Prefix(string path, int maxSamples)
    {
        if (maxSamples <= 0)
        {
            return Array.Empty<float>();
        }

        OggVorbisIdentificationHeader? identification = null;
        OggVorbisSetupSummary? setup = null;
        OggVorbisFloat32Context? context = null;

        ReadPackets(
            path,
            page => { },
            (packet, packetIndex, granulePosition) =>
            {
                if (packetIndex == 0)
                {
                    identification = ParseIdentificationHeader(packet);
                    return true;
                }

                if (packetIndex == 1)
                {
                    _ = ParseCommentHeader(packet);
                    return true;
                }

                if (packetIndex == 2)
                {
                    if (identification is null)
                    {
                        throw new InvalidDataException("Vorbis setup packet arrived before identification.");
                    }

                    setup = ParseSetupHeader(packet, identification.Value.Channels);
                    context = new OggVorbisFloat32Context(
                        identification.Value.Channels,
                        identification.Value.BlockSizeSmall,
                        identification.Value.BlockSizeLarge,
                        setup.Value,
                        OggVorbisOutputTarget.Float32,
                        maxSamples);
                    return true;
                }

                if (identification is null || setup is null || context is null)
                {
                    throw new InvalidDataException("Vorbis audio packet arrived before setup.");
                }

                context.DecodePacket(packet, granulePosition);
                return !context.IsCaptureComplete;
            });

        return context?.ToCapturedSamples() ?? Array.Empty<float>();
    }

    internal static int[] DecodeToPcm32Prefix(string path, int maxSamples)
    {
        if (maxSamples <= 0)
        {
            return Array.Empty<int>();
        }

        OggVorbisIdentificationHeader? identification = null;
        OggVorbisSetupSummary? setup = null;
        OggVorbisFloat32Context? context = null;

        ReadPackets(
            path,
            page => { },
            (packet, packetIndex, granulePosition) =>
            {
                if (packetIndex == 0)
                {
                    identification = ParseIdentificationHeader(packet);
                    return true;
                }

                if (packetIndex == 1)
                {
                    _ = ParseCommentHeader(packet);
                    return true;
                }

                if (packetIndex == 2)
                {
                    if (identification is null)
                    {
                        throw new InvalidDataException("Vorbis setup packet arrived before identification.");
                    }

                    setup = ParseSetupHeader(packet, identification.Value.Channels);
                    context = new OggVorbisFloat32Context(
                        identification.Value.Channels,
                        identification.Value.BlockSizeSmall,
                        identification.Value.BlockSizeLarge,
                        setup.Value,
                        OggVorbisOutputTarget.Pcm32,
                        maxSamples);
                    return true;
                }

                if (identification is null || setup is null || context is null)
                {
                    throw new InvalidDataException("Vorbis audio packet arrived before setup.");
                }

                context.DecodePacket(packet, granulePosition);
                return !context.IsCaptureComplete;
            });

        return context?.ToCapturedPcm32Samples() ?? Array.Empty<int>();
    }

    internal static OggVorbisPacketDebugInfo[] DebugFloat32Packets(string path, int startAudioPacket, int packetCount)
    {
        if (packetCount <= 0)
        {
            return Array.Empty<OggVorbisPacketDebugInfo>();
        }

        if (startAudioPacket < 0)
        {
            startAudioPacket = 0;
        }

        OggVorbisIdentificationHeader? identification = null;
        OggVorbisSetupSummary? setup = null;
        OggVorbisFloat32Context? context = null;
        var debugPackets = new List<OggVorbisPacketDebugInfo>(packetCount);
        var audioPacketIndex = 0;
        var endAudioPacket = startAudioPacket + packetCount;

        ReadPackets(
            path,
            page => { },
            (packet, packetIndex, granulePosition) =>
            {
                if (packetIndex == 0)
                {
                    identification = ParseIdentificationHeader(packet);
                    return true;
                }

                if (packetIndex == 1)
                {
                    _ = ParseCommentHeader(packet);
                    return true;
                }

                if (packetIndex == 2)
                {
                    if (identification is null)
                    {
                        throw new InvalidDataException("Vorbis setup packet arrived before identification.");
                    }

                    setup = ParseSetupHeader(packet, identification.Value.Channels);
                    context = new OggVorbisFloat32Context(
                        identification.Value.Channels,
                        identification.Value.BlockSizeSmall,
                        identification.Value.BlockSizeLarge,
                        setup.Value,
                        OggVorbisOutputTarget.Float32);
                    return true;
                }

                if (identification is null || setup is null || context is null)
                {
                    throw new InvalidDataException("Vorbis audio packet arrived before setup.");
                }

                if (audioPacketIndex >= startAudioPacket && audioPacketIndex < endAudioPacket)
                {
                    debugPackets.Add(context.DecodePacketDebug(packet, granulePosition, audioPacketIndex));
                }
                else
                {
                    context.DecodePacket(packet, granulePosition);
                }

                audioPacketIndex++;
                return audioPacketIndex < endAudioPacket;
            });

        return debugPackets.ToArray();
    }

    internal static OggVorbisFramePacketOwnerInfo[] FindOwningPacketsForFrames(string path, params long[] frameIndices)
    {
        if (frameIndices is null || frameIndices.Length == 0)
        {
            return Array.Empty<OggVorbisFramePacketOwnerInfo>();
        }

        var owners = new OggVorbisFramePacketOwnerInfo[frameIndices.Length];
        var remaining = 0;
        for (var i = 0; i < frameIndices.Length; i++)
        {
            var frameIndex = frameIndices[i];
            var found = frameIndex < 0;
            owners[i] = new OggVorbisFramePacketOwnerInfo(frameIndex, found, default);
            if (!found)
            {
                remaining++;
            }
        }

        if (remaining == 0)
        {
            return owners;
        }

        OggVorbisIdentificationHeader? identification = null;
        OggVorbisSetupSummary? setup = null;
        OggVorbisFloat32Context? context = null;
        var audioPacketIndex = 0;

        ReadPackets(
            path,
            page => { },
            (packet, packetIndex, granulePosition) =>
            {
                if (packetIndex == 0)
                {
                    identification = ParseIdentificationHeader(packet);
                    return true;
                }

                if (packetIndex == 1)
                {
                    _ = ParseCommentHeader(packet);
                    return true;
                }

                if (packetIndex == 2)
                {
                    if (identification is null)
                    {
                        throw new InvalidDataException("Vorbis setup packet arrived before identification.");
                    }

                    setup = ParseSetupHeader(packet, identification.Value.Channels);
                    context = new OggVorbisFloat32Context(
                        identification.Value.Channels,
                        identification.Value.BlockSizeSmall,
                        identification.Value.BlockSizeLarge,
                        setup.Value,
                        OggVorbisOutputTarget.Float32);
                    return true;
                }

                if (identification is null || setup is null || context is null)
                {
                    throw new InvalidDataException("Vorbis audio packet arrived before setup.");
                }

                var debugPacket = context.DecodePacketDebug(packet, granulePosition, audioPacketIndex);
                if (debugPacket.OutputFramesProduced > 0 && debugPacket.OutputFrameStart >= 0 && debugPacket.OutputFrameEnd >= debugPacket.OutputFrameStart)
                {
                    for (var i = 0; i < owners.Length; i++)
                    {
                        if (owners[i].Found)
                        {
                            continue;
                        }

                        var frameIndex = owners[i].FrameIndex;
                        if (frameIndex < debugPacket.OutputFrameStart || frameIndex > debugPacket.OutputFrameEnd)
                        {
                            continue;
                        }

                        owners[i] = new OggVorbisFramePacketOwnerInfo(frameIndex, true, debugPacket);
                        remaining--;
                    }
                }

                audioPacketIndex++;
                return remaining > 0;
            });

        return owners;
    }

    internal static long HashFloat32Samples(ReadOnlySpan<float> samples) => HashFloatValues(samples);

    internal static long HashPcm32Samples(ReadOnlySpan<int> samples) => HashInt32Values(samples);

    private static double StopwatchTicksToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private static OggVorbisIdentificationHeader ParseIdentificationHeader(ReadOnlySpan<byte> packet)
    {
        ValidateVorbisHeader(packet, 0x01, "identification");

        if (packet.Length < 30)
        {
            throw new InvalidDataException("Vorbis identification packet is truncated.");
        }

        var version = BinaryPrimitives.ReadUInt32LittleEndian(packet[7..11]);
        if (version != 0)
        {
            throw new InvalidDataException($"Unsupported Vorbis version: {version}.");
        }

        var channels = packet[11];
        var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(packet[12..16]);
        var bitrateMaximum = BinaryPrimitives.ReadInt32LittleEndian(packet[16..20]);
        var bitrateNominal = BinaryPrimitives.ReadInt32LittleEndian(packet[20..24]);
        var bitrateMinimum = BinaryPrimitives.ReadInt32LittleEndian(packet[24..28]);
        var blockSizePacked = packet[28];
        var blockSizeSmall = 1 << (blockSizePacked & 0x0F);
        var blockSizeLarge = 1 << ((blockSizePacked >> 4) & 0x0F);
        var framing = (packet[29] & 0x01) != 0;

        if (channels <= 0)
        {
            throw new InvalidDataException("Vorbis channel count is zero.");
        }

        if (sampleRate <= 0)
        {
            throw new InvalidDataException("Vorbis sample rate is invalid.");
        }

        if (!framing)
        {
            throw new InvalidDataException("Vorbis identification framing bit is not set.");
        }

        if (blockSizeSmall < 64 || blockSizeLarge < blockSizeSmall)
        {
            throw new InvalidDataException($"Unsupported Vorbis block sizes: {blockSizeSmall}/{blockSizeLarge}.");
        }

        return new OggVorbisIdentificationHeader(
            channels,
            sampleRate,
            bitrateMaximum,
            bitrateNominal,
            bitrateMinimum,
            blockSizeSmall,
            blockSizeLarge);
    }

    private static OggVorbisCommentHeader ParseCommentHeader(ReadOnlySpan<byte> packet)
    {
        ValidateVorbisHeader(packet, 0x03, "comment");

        var offset = 7;
        var vendor = ReadVorbisString(packet, ref offset);
        var commentCount = ReadUInt32AsInt(packet, ref offset, "comment count");
        var comments = new string[commentCount];

        for (var i = 0; i < comments.Length; i++)
        {
            comments[i] = ReadVorbisString(packet, ref offset);
        }

        if (offset >= packet.Length || (packet[offset] & 0x01) == 0)
        {
            throw new InvalidDataException("Vorbis comment framing bit is not set.");
        }

        return new OggVorbisCommentHeader(vendor, comments);
    }

    private static OggVorbisSetupSummary ParseSetupHeader(ReadOnlySpan<byte> packet, int channels)
    {
        ValidateVorbisHeader(packet, 0x05, "setup");

        var reader = new VorbisBitReader(packet[7..]);
        var codebookCount = reader.ReadBitsInt(8) + 1;
        var codebooks = new OggVorbisCodebookInfo[codebookCount];
        var maxCodebookEntries = 0;
        var maxCodebookDimensions = 0;

        for (var i = 0; i < codebookCount; i++)
        {
            codebooks[i] = ParseCodebook(ref reader, ref maxCodebookEntries, ref maxCodebookDimensions);
        }

        var timeCount = reader.ReadBitsInt(6) + 1;
        for (var i = 0; i < timeCount; i++)
        {
            var timeType = reader.ReadBitsInt(16);
            if (timeType != 0)
            {
                throw new InvalidDataException($"Unsupported Vorbis time domain transform type: {timeType}.");
            }
        }

        var floorCount = reader.ReadBitsInt(6) + 1;
        var floors = new OggVorbisFloorInfo[floorCount];
        for (var i = 0; i < floorCount; i++)
        {
            floors[i] = ParseFloor(ref reader);
        }

        var residueCount = reader.ReadBitsInt(6) + 1;
        var residues = new OggVorbisResidueInfo[residueCount];
        for (var i = 0; i < residueCount; i++)
        {
            residues[i] = ParseResidue(ref reader);
        }

        var mappingCount = reader.ReadBitsInt(6) + 1;
        var mappings = new OggVorbisMappingInfo[mappingCount];
        for (var i = 0; i < mappingCount; i++)
        {
            mappings[i] = ParseMapping(ref reader, Math.Max(1, channels));
        }

        var modeCount = reader.ReadBitsInt(6) + 1;
        var modes = new OggVorbisModeInfo[modeCount];
        for (var i = 0; i < modeCount; i++)
        {
            var blockFlag = reader.ReadBitsInt(1) != 0;
            var windowType = reader.ReadBitsInt(16);
            var transformType = reader.ReadBitsInt(16);
            var mapping = reader.ReadBitsInt(8);

            if (windowType != 0 || transformType != 0)
            {
                throw new InvalidDataException($"Unsupported Vorbis mode transform: window={windowType} transform={transformType}.");
            }

            if ((uint)mapping >= (uint)mappings.Length)
            {
                throw new InvalidDataException($"Vorbis mode references invalid mapping index: {mapping}.");
            }

            modes[i] = new OggVorbisModeInfo(blockFlag, mapping);
        }

        var framingBit = reader.ReadBitsInt(1) != 0;
        if (!framingBit)
        {
            throw new InvalidDataException("Vorbis setup framing bit is not set.");
        }

        residues = FinalizeResidues(residues, codebooks);

        return new OggVorbisSetupSummary(
            codebookCount,
            timeCount,
            floorCount,
            residueCount,
            mappingCount,
            modeCount,
            maxCodebookEntries,
            maxCodebookDimensions,
            codebooks,
            floors,
            residues,
            mappings,
            modes,
            framingBit);
    }

    private static OggVorbisCodebookInfo ParseCodebook(ref VorbisBitReader reader, ref int maxEntries, ref int maxDimensions)
    {
        var sync = reader.ReadBitsInt(24);
        if (sync != 0x564342)
        {
            throw new InvalidDataException($"Invalid Vorbis codebook sync: 0x{sync:X6}.");
        }

        var dimensions = reader.ReadBitsInt(16);
        var entries = reader.ReadBitsInt(24);
        if (dimensions <= 0 || entries <= 0)
        {
            throw new InvalidDataException($"Invalid Vorbis codebook shape: dimensions={dimensions} entries={entries}.");
        }

        maxEntries = Math.Max(maxEntries, entries);
        maxDimensions = Math.Max(maxDimensions, dimensions);

        var lengths = new byte[entries];
        var ordered = reader.ReadBitsInt(1) != 0;
        if (ordered)
        {
            var currentLength = reader.ReadBitsInt(5) + 1;
            var currentEntry = 0;
            while (currentEntry < entries)
            {
                var bits = ILog(entries - currentEntry);
                var number = reader.ReadBitsInt(bits);
                if (number < 0 || currentEntry + number > entries)
                {
                    throw new InvalidDataException("Vorbis ordered codebook length run exceeds the entry count.");
                }

                for (var i = 0; i < number; i++)
                {
                    lengths[currentEntry++] = checked((byte)currentLength);
                }

                currentLength++;
            }
        }
        else
        {
            var sparse = reader.ReadBitsInt(1) != 0;
            for (var i = 0; i < entries; i++)
            {
                if (!sparse || reader.ReadBitsInt(1) != 0)
                {
                    lengths[i] = checked((byte)(reader.ReadBitsInt(5) + 1));
                }
            }
        }

        var lookupType = reader.ReadBitsInt(4);
        var huffmanTree = BuildCodebookHuffmanTree(lengths);
        var fastLookup = BuildCodebookFastLookup(huffmanTree.Zero, huffmanTree.One, huffmanTree.Entries, huffmanTree.MaxLength);
        if (lookupType == 0)
        {
            return new OggVorbisCodebookInfo(
                dimensions,
                entries,
                lookupType,
                0,
                lengths,
                huffmanTree.Zero,
                huffmanTree.One,
                huffmanTree.Entries,
                huffmanTree.MaxLength,
                fastLookup.BitCount,
                fastLookup.Lengths,
                fastLookup.Entries,
                Array.Empty<int>(),
                Array.Empty<float>(),
                0f);
        }

        if (lookupType is not 1 and not 2)
        {
            throw new InvalidDataException($"Unsupported Vorbis codebook lookup type: {lookupType}.");
        }

        var minimumValue = UnpackVorbisFloat32(reader.ReadBits(32));
        var deltaValue = UnpackVorbisFloat32(reader.ReadBits(32));
        var valueBits = reader.ReadBitsInt(4) + 1;
        var sequenceP = reader.ReadBitsInt(1) != 0;

        var lookupValues = lookupType == 1
            ? Lookup1Values(entries, dimensions)
            : checked((long)entries * dimensions);
        if (lookupValues > int.MaxValue)
        {
            throw new InvalidDataException($"Vorbis codebook lookup table is too large: {lookupValues}.");
        }

        var multiplicands = new int[(int)lookupValues];
        for (var i = 0; i < multiplicands.Length; i++)
        {
            multiplicands[i] = reader.ReadBitsInt(valueBits);
        }

        var lookupVectors = BuildCodebookLookupVectors(
            lookupType,
            dimensions,
            entries,
            (int)lookupValues,
            minimumValue,
            deltaValue,
            sequenceP,
            multiplicands);
        var lookupEntryOffsets = BuildCodebookLookupEntryOffsets(entries, dimensions, lookupVectors.Length);

        return new OggVorbisCodebookInfo(
            dimensions,
            entries,
            lookupType,
            lookupValues,
            lengths,
            huffmanTree.Zero,
            huffmanTree.One,
            huffmanTree.Entries,
            huffmanTree.MaxLength,
            fastLookup.BitCount,
            fastLookup.Lengths,
            fastLookup.Entries,
            lookupEntryOffsets,
            lookupVectors,
            MaxAbs(lookupVectors));
    }

    private static OggVorbisFloorInfo ParseFloor(ref VorbisBitReader reader)
    {
        var floorType = reader.ReadBitsInt(16);
        if (floorType == 0)
        {
            _ = reader.ReadBitsInt(8);
            _ = reader.ReadBitsInt(16);
            _ = reader.ReadBitsInt(16);
            _ = reader.ReadBitsInt(6);
            _ = reader.ReadBitsInt(8);
            var bookCount = reader.ReadBitsInt(4) + 1;
            for (var i = 0; i < bookCount; i++)
            {
                _ = reader.ReadBitsInt(8);
            }

            return new OggVorbisFloorInfo(
                floorType,
                0,
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int[]>(),
                0,
                0,
                Array.Empty<int>());
        }

        if (floorType != 1)
        {
            throw new InvalidDataException($"Unsupported Vorbis floor type: {floorType}.");
        }

        var partitionCount = reader.ReadBitsInt(5);
        var partitionClasses = new int[partitionCount];
        var maximumClass = -1;
        var valueCount = 2;
        for (var i = 0; i < partitionCount; i++)
        {
            var partitionClass = reader.ReadBitsInt(4);
            partitionClasses[i] = partitionClass;
            maximumClass = Math.Max(maximumClass, partitionClass);
        }

        var classCount = maximumClass + 1;
        var classDimensions = new int[classCount];
        var classSubclasses = new int[classCount];
        var classMasterbooks = new int[classCount];
        var subclassBooks = new int[classCount][];
        Array.Fill(classMasterbooks, -1);
        for (var i = 0; i <= maximumClass; i++)
        {
            classDimensions[i] = reader.ReadBitsInt(3) + 1;
            var subclass = reader.ReadBitsInt(2);
            classSubclasses[i] = subclass;
            if (subclass > 0)
            {
                classMasterbooks[i] = reader.ReadBitsInt(8);
            }

            var subclassBookCount = 1 << subclass;
            var books = new int[subclassBookCount];
            for (var j = 0; j < books.Length; j++)
            {
                books[j] = reader.ReadBitsInt(8) - 1;
            }

            subclassBooks[i] = books;
        }

        var multiplier = reader.ReadBitsInt(2) + 1;
        var rangeBits = reader.ReadBitsInt(4);
        var xList = new int[2 + partitionCount * 8];
        xList[0] = 0;
        xList[1] = 1 << rangeBits;
        var xIndex = 2;
        for (var i = 0; i < partitionCount; i++)
        {
            var partitionClass = partitionClasses[i];
            for (var j = 0; j < classDimensions[partitionClass]; j++)
            {
                xList[xIndex++] = reader.ReadBitsInt(rangeBits);
                valueCount++;
            }
        }

        if (xIndex != xList.Length)
        {
            Array.Resize(ref xList, xIndex);
        }

        return new OggVorbisFloorInfo(
            floorType,
            valueCount,
            partitionClasses,
            classDimensions,
            classSubclasses,
            classMasterbooks,
            subclassBooks,
            multiplier,
            rangeBits,
            xList);
    }

    private static OggVorbisResidueInfo ParseResidue(ref VorbisBitReader reader)
    {
        var residueType = reader.ReadBitsInt(16);
        if (residueType is < 0 or > 2)
        {
            throw new InvalidDataException($"Unsupported Vorbis residue type: {residueType}.");
        }

        var begin = reader.ReadBitsInt(24);
        var end = reader.ReadBitsInt(24);
        var partitionSize = reader.ReadBitsInt(24) + 1;
        var classifications = reader.ReadBitsInt(6) + 1;
        var classbook = reader.ReadBitsInt(8);

        var cascades = new int[classifications];
        var books = new int[classifications][];
        for (var i = 0; i < classifications; i++)
        {
            var lowBits = reader.ReadBitsInt(3);
            var highBits = reader.ReadBitsInt(1) != 0 ? reader.ReadBitsInt(5) : 0;
            cascades[i] = (highBits << 3) | lowBits;
        }

        for (var i = 0; i < classifications; i++)
        {
            books[i] = new int[8];
            Array.Fill(books[i], -1);
            for (var j = 0; j < 8; j++)
            {
                if ((cascades[i] & (1 << j)) != 0)
                {
                    books[i][j] = reader.ReadBitsInt(8);
                }
            }
        }

        return new OggVorbisResidueInfo(
            residueType,
            begin,
            end,
            partitionSize,
            classifications,
            classbook,
            cascades,
            books,
            new OggVorbisResidueStageInfo[checked(classifications * 8)]);
    }

    private static OggVorbisResidueInfo[] FinalizeResidues(
        OggVorbisResidueInfo[] residues,
        OggVorbisCodebookInfo[] codebooks)
    {
        var finalized = new OggVorbisResidueInfo[residues.Length];
        for (var residueIndex = 0; residueIndex < residues.Length; residueIndex++)
        {
            var residue = residues[residueIndex];
            var stageInfos = new OggVorbisResidueStageInfo[checked(residue.Classifications * 8)];
            for (var classification = 0; classification < residue.Classifications; classification++)
            {
                if ((uint)classification >= (uint)residue.Books.Length)
                {
                    throw new InvalidDataException($"Vorbis residue classification table is invalid: {classification}.");
                }

                var stageBooks = residue.Books[classification];
                if (stageBooks.Length < 8)
                {
                    throw new InvalidDataException($"Vorbis residue classification stagebook row is incomplete: {classification}.");
                }

                for (var pass = 0; pass < 8; pass++)
                {
                    var bookIndex = stageBooks[pass];
                    if (bookIndex < 0)
                    {
                        continue;
                    }

                    if ((uint)bookIndex >= (uint)codebooks.Length)
                    {
                        throw new InvalidDataException($"Vorbis residue references invalid stagebook: {bookIndex}.");
                    }

                    var book = codebooks[bookIndex];
                    if (book.Dimensions <= 0)
                    {
                        throw new InvalidDataException($"Vorbis residue stagebook has invalid dimensions: {book.Dimensions}.");
                    }

                    stageInfos[(classification << 3) + pass] = new OggVorbisResidueStageInfo(
                        true,
                        bookIndex,
                        book.Dimensions,
                        residue.PartitionSize / book.Dimensions);
                }
            }

            finalized[residueIndex] = residue with { StageInfos = stageInfos };
        }

        return finalized;
    }

    private static OggVorbisMappingInfo ParseMapping(ref VorbisBitReader reader, int channels)
    {
        var mappingType = reader.ReadBitsInt(16);
        if (mappingType != 0)
        {
            throw new InvalidDataException($"Unsupported Vorbis mapping type: {mappingType}.");
        }

        var submaps = reader.ReadBitsInt(1) != 0 ? reader.ReadBitsInt(4) + 1 : 1;
        var couplingSteps = 0;
        var magnitudes = Array.Empty<int>();
        var angles = Array.Empty<int>();
        if (reader.ReadBitsInt(1) != 0)
        {
            couplingSteps = reader.ReadBitsInt(8) + 1;
            magnitudes = new int[couplingSteps];
            angles = new int[couplingSteps];
            var channelBits = ILog(channels - 1);
            for (var i = 0; i < couplingSteps; i++)
            {
                var magnitude = reader.ReadBitsInt(channelBits);
                var angle = reader.ReadBitsInt(channelBits);
                if (magnitude == angle || magnitude >= channels || angle >= channels)
                {
                    throw new InvalidDataException($"Invalid Vorbis coupling pair: magnitude={magnitude} angle={angle} channels={channels}.");
                }

                magnitudes[i] = magnitude;
                angles[i] = angle;
            }
        }

        var reserved = reader.ReadBitsInt(2);
        if (reserved != 0)
        {
            throw new InvalidDataException($"Vorbis mapping reserved bits are non-zero: {reserved}.");
        }

        var mux = new int[channels];
        if (submaps > 1)
        {
            for (var i = 0; i < channels; i++)
            {
                mux[i] = reader.ReadBitsInt(4);
                if (mux[i] >= submaps)
                {
                    throw new InvalidDataException($"Vorbis mapping mux references invalid submap: {mux[i]}.");
                }
            }
        }

        var submapFloors = new int[submaps];
        var submapResidues = new int[submaps];
        for (var i = 0; i < submaps; i++)
        {
            _ = reader.ReadBitsInt(8);
            submapFloors[i] = reader.ReadBitsInt(8);
            submapResidues[i] = reader.ReadBitsInt(8);
        }

        return new OggVorbisMappingInfo(
            submaps,
            couplingSteps,
            magnitudes,
            angles,
            mux,
            submapFloors,
            submapResidues,
            BuildSubmapChannels(submaps, channels, mux));
    }

    private static int[][] BuildSubmapChannels(int submaps, int channels, int[] mux)
    {
        var counts = new int[submaps];
        for (var channel = 0; channel < channels; channel++)
        {
            var submap = mux.Length > channel ? mux[channel] : 0;
            counts[submap]++;
        }

        var result = new int[submaps][];
        for (var submap = 0; submap < submaps; submap++)
        {
            result[submap] = new int[counts[submap]];
        }

        Array.Clear(counts, 0, counts.Length);
        for (var channel = 0; channel < channels; channel++)
        {
            var submap = mux.Length > channel ? mux[channel] : 0;
            result[submap][counts[submap]++] = channel;
        }

        return result;
    }

    private static OggVorbisAudioPacketInfo ParseAudioPacketHeader(
        ReadOnlySpan<byte> packet,
        OggVorbisSetupSummary setup,
        int channels,
        int blockSizeSmall,
        int blockSizeLarge)
    {
        if (packet.IsEmpty)
        {
            throw new InvalidDataException("Vorbis audio packet is empty.");
        }

        var reader = new VorbisBitReader(packet);
        var packetType = reader.ReadBitsInt(1);
        if (packetType != 0)
        {
            throw new InvalidDataException($"Invalid Vorbis audio packet type bit: {packetType}.");
        }

        var modeBits = ILog(setup.ModeCount - 1);
        var modeNumber = reader.ReadBitsInt(modeBits);
        if ((uint)modeNumber >= (uint)setup.Modes.Length)
        {
            throw new InvalidDataException($"Vorbis audio packet references invalid mode: {modeNumber}.");
        }

        var mode = setup.Modes[modeNumber];
        var previousWindowFlag = false;
        var nextWindowFlag = false;
        if (mode.BlockFlag)
        {
            previousWindowFlag = reader.ReadBitsInt(1) != 0;
            nextWindowFlag = reader.ReadBitsInt(1) != 0;
        }

        var blockSizeHalf = (mode.BlockFlag ? blockSizeLarge : blockSizeSmall) >> 1;
        var mapping = setup.Mappings[mode.Mapping];
        var floorPresentChannels = 0;
        var floorAbsentChannels = 0;
        var decodedFloorValues = 0;
        var materializedFloorValues = 0;
        var floorCurveHash = 0L;
        var noResidue = new bool[channels];
        for (var channel = 0; channel < channels; channel++)
        {
            var submap = mapping.Mux.Length > channel ? mapping.Mux[channel] : 0;
            if ((uint)submap >= (uint)mapping.SubmapFloors.Length)
            {
                throw new InvalidDataException($"Vorbis packet references invalid submap: {submap}.");
            }

            var floorIndex = mapping.SubmapFloors[submap];
            if ((uint)floorIndex >= (uint)setup.Floors.Length)
            {
                throw new InvalidDataException($"Vorbis packet references invalid floor: {floorIndex}.");
            }

            var floorPacket = ParseFloorPacket(ref reader, setup.Floors[floorIndex], setup);
            if (floorPacket.Present)
            {
                floorPresentChannels++;
                noResidue[channel] = false;
            }
            else
            {
                floorAbsentChannels++;
                noResidue[channel] = true;
            }

            decodedFloorValues += floorPacket.DecodedValues;
            materializedFloorValues += floorPacket.MaterializedValues;
            floorCurveHash = HashCombine(floorCurveHash, floorPacket.CurveHash);
        }

        for (var i = 0; i < mapping.CouplingSteps; i++)
        {
            var magnitude = mapping.Magnitudes[i];
            var angle = mapping.Angles[i];
            if (!noResidue[magnitude] || !noResidue[angle])
            {
                noResidue[magnitude] = false;
                noResidue[angle] = false;
            }
        }

        var decodedResidueClasswords = 0;
        var decodedResidueVectors = 0;
        var materializedResidueValues = 0;
        var residueValueHash = 0L;
        for (var submap = 0; submap < mapping.Submaps; submap++)
        {
            var submapChannels = mapping.SubmapChannels[submap];
            if (submapChannels.Length == 0)
            {
                continue;
            }

            var residueIndex = mapping.SubmapResidues[submap];
            if ((uint)residueIndex >= (uint)setup.Residues.Length)
            {
                throw new InvalidDataException($"Vorbis packet references invalid residue: {residueIndex}.");
            }

            var residuePacket = ConsumeResiduePacket(
                ref reader,
                setup.Residues[residueIndex],
                setup,
                noResidue,
                submapChannels,
                blockSizeHalf);
            decodedResidueClasswords += residuePacket.DecodedClasswords;
            decodedResidueVectors += residuePacket.DecodedVectors;
            materializedResidueValues += residuePacket.MaterializedValues;
            residueValueHash = HashCombine(residueValueHash, residuePacket.ValueHash);
        }

        return new OggVorbisAudioPacketInfo(
            modeNumber,
            mode.BlockFlag,
            previousWindowFlag,
            nextWindowFlag,
            mode.Mapping,
            floorPresentChannels,
            floorAbsentChannels,
            decodedFloorValues,
            materializedFloorValues,
            floorCurveHash,
            decodedResidueClasswords,
            decodedResidueVectors,
            materializedResidueValues,
            residueValueHash);
    }

    private static OggVorbisFloorPacketInfo ParseFloorPacket(
        ref VorbisBitReader reader,
        OggVorbisFloorInfo floor,
        OggVorbisSetupSummary setup,
        float[]? curveValues = null,
        int curveOffset = 0,
        int curveLength = 0,
        bool collectStats = true,
        int[]? yValueScratch = null,
        OggVorbisProfileTotals? profile = null)
    {
        var present = reader.ReadBitsInt(1) != 0;
        if (!present)
        {
            return new OggVorbisFloorPacketInfo(false, 0, 0, 0);
        }

        if (floor.Type != 1)
        {
            throw new InvalidDataException($"Vorbis floor type {floor.Type} packet decode is not implemented.");
        }

        var range = floor.Multiplier switch
        {
            1 => 256,
            2 => 128,
            3 => 86,
            4 => 64,
            _ => throw new InvalidDataException($"Invalid Vorbis floor multiplier: {floor.Multiplier}.")
        };

        var yBits = ILog(range - 1);
        var yValueCount = Math.Max(2, floor.ValueCount);
        var yValues = yValueScratch is not null && yValueScratch.Length >= yValueCount
            ? yValueScratch
            : new int[yValueCount];
        yValues[0] = reader.ReadBitsInt(yBits);
        yValues[1] = reader.ReadBitsInt(yBits);
        var decodedValues = 2;

        for (var partition = 0; partition < floor.PartitionClasses.Length; partition++)
        {
            var classIndex = floor.PartitionClasses[partition];
            if ((uint)classIndex >= (uint)floor.ClassDimensions.Length)
            {
                throw new InvalidDataException($"Vorbis floor references invalid class: {classIndex}.");
            }

            var classDimension = floor.ClassDimensions[classIndex];
            var classSubclassBits = floor.ClassSubclasses[classIndex];
            var classValue = 0;
            if (classSubclassBits > 0)
            {
                var masterbookIndex = floor.ClassMasterbooks[classIndex];
                if ((uint)masterbookIndex >= (uint)setup.Codebooks.Length)
                {
                    throw new InvalidDataException($"Vorbis floor references invalid masterbook: {masterbookIndex}.");
                }

                classValue = ReadCodebookScalar(ref reader, setup.Codebooks[masterbookIndex], profile);
            }

            var subclassMask = (1 << classSubclassBits) - 1;
            for (var i = 0; i < classDimension; i++)
            {
                var subclassIndex = classSubclassBits == 0 ? 0 : classValue & subclassMask;
                classValue >>= classSubclassBits;

                if ((uint)subclassIndex >= (uint)floor.SubclassBooks[classIndex].Length)
                {
                    throw new InvalidDataException($"Vorbis floor references invalid subclass book slot: {subclassIndex}.");
                }

                var bookIndex = floor.SubclassBooks[classIndex][subclassIndex];
                if (bookIndex >= 0)
                {
                    if ((uint)bookIndex >= (uint)setup.Codebooks.Length)
                    {
                        throw new InvalidDataException($"Vorbis floor references invalid subclass book: {bookIndex}.");
                    }

                    yValues[decodedValues] = ReadCodebookScalar(ref reader, setup.Codebooks[bookIndex], profile);
                }
                else
                {
                    yValues[decodedValues] = 0;
                }

                decodedValues++;
            }
        }

        var floorCurve = MaterializeFloorCurve(floor, yValues, decodedValues, range, curveValues, curveOffset, curveLength, collectStats);
        return collectStats
            ? new OggVorbisFloorPacketInfo(true, decodedValues, floorCurve.ValueCount, floorCurve.Hash)
            : new OggVorbisFloorPacketInfo(true, 0, 0, 0);
    }

    private static OggVorbisResiduePacketInfo ConsumeResiduePacket(
        ref VorbisBitReader reader,
        OggVorbisResidueInfo residue,
        OggVorbisSetupSummary setup,
        ReadOnlySpan<bool> noResidue,
        ReadOnlySpan<int> channels,
        int blockSizeHalf,
        float[]? residueDestination = null,
        bool collectStats = true,
        int[]? channelBaseScratch = null,
        int[]? activeChannelBaseScratch = null)
    {
        if ((uint)residue.Classbook >= (uint)setup.Codebooks.Length)
        {
            throw new InvalidDataException($"Vorbis residue references invalid classbook: {residue.Classbook}.");
        }

        var classbook = setup.Codebooks[residue.Classbook];
        if (classbook.Dimensions <= 0)
        {
            throw new InvalidDataException("Vorbis residue classbook has no dimensions.");
        }

        if (channels.IsEmpty)
        {
            return new OggVorbisResiduePacketInfo(0, 0, 0, 0, -1, 0f);
        }

        var useFastDestinationPath = residueDestination is not null;
        var channelBaseOffsets = useFastDestinationPath
            ? (channelBaseScratch is not null && channelBaseScratch.Length >= channels.Length
                ? channelBaseScratch
                : new int[channels.Length])
            : null;
        var activeChannelBases = useFastDestinationPath && residue.Type != 2
            ? (activeChannelBaseScratch is not null && activeChannelBaseScratch.Length >= channels.Length
                ? activeChannelBaseScratch
                : new int[channels.Length])
            : null;
        var activeChannels = 0;
        for (var i = 0; i < channels.Length; i++)
        {
            var channel = channels[i];
            if ((uint)channel >= (uint)noResidue.Length)
            {
                throw new InvalidDataException($"Vorbis residue channel index is invalid: {channel}.");
            }

            if (channelBaseOffsets is not null)
            {
                channelBaseOffsets[i] = channel * blockSizeHalf;
            }

            if (!noResidue[channel])
            {
                if (activeChannelBases is not null)
                {
                    activeChannelBases[activeChannels] = channel * blockSizeHalf;
                }

                activeChannels++;
            }
        }

        if (activeChannels == 0)
        {
            return new OggVorbisResiduePacketInfo(0, 0, 0, 0, -1, 0f);
        }

        var effectiveChannels = residue.Type == 2 ? 1 : activeChannels;
        var residueLimit = blockSizeHalf * (residue.Type == 2 ? channels.Length : 1);
        var residueBegin = Math.Min(residue.Begin, residueLimit);
        var residueEnd = Math.Min(residue.End, residueLimit);
        var samplesToRead = residueEnd - residueBegin;
        if (samplesToRead <= 0)
        {
            return new OggVorbisResiduePacketInfo(0, 0, 0, 0, -1, 0f);
        }

        var partitionCount = samplesToRead / residue.PartitionSize;
        if (partitionCount <= 0)
        {
            return new OggVorbisResiduePacketInfo(0, 0, 0, 0, -1, 0f);
        }

        var classificationsPerWord = classbook.Dimensions;
        var decodedClasswords = 0;
        var decodedVectors = 0;
        var materializedValues = 0;
        var valueHash = 0L;
        var maxBookIndexUsed = -1;
        var lookupVectorMaxAbsUsed = 0f;
        var residueStageInfos = residue.StageInfos;
        var codebooks = setup.Codebooks;

        var totalClassifications = checked(effectiveChannels * partitionCount);
        var classifications = totalClassifications <= 4096
            ? ArrayPool<int>.Shared.Rent(totalClassifications)
            : new int[totalClassifications];
        var residueValueCount = Math.Max(1, blockSizeHalf * (residueDestination is null ? channels.Length : noResidue.Length));
        var residueValues = residueDestination
            ?? (residueValueCount <= 8192
                ? ArrayPool<float>.Shared.Rent(residueValueCount)
                : new float[residueValueCount]);
        try
        {
            if (residueDestination is null)
            {
                Array.Clear(residueValues, 0, residueValueCount);
            }

            if (residueDestination is not null && channelBaseOffsets is not null)
            {
                if (residue.Type == 2)
                {
                    for (var pass = 0; pass < 8; pass++)
                    {
                        var partition = 0;
                        while (partition < partitionCount)
                        {
                            if (pass == 0)
                            {
                                var classword = ReadCodebookScalar(ref reader, classbook);
                                DecodeResidueClassword(
                                    classword,
                                    residue.Classifications,
                                    classificationsPerWord,
                                    classifications,
                                    partition,
                                    partitionCount - partition);
                                if (collectStats)
                                {
                                    decodedClasswords++;
                                }
                            }

                            var classwordPartitions = Math.Min(classificationsPerWord, partitionCount - partition);
                            for (var classwordOffset = 0; classwordOffset < classwordPartitions; classwordOffset++)
                            {
                                var classification = classifications[partition + classwordOffset];
                                if ((uint)classification >= (uint)residue.Classifications)
                                {
                                    throw new InvalidDataException($"Vorbis residue classification is invalid: {classification}.");
                                }

                                var stageInfo = residueStageInfos[(classification << 3) + pass];
                                if (stageInfo.IsUsed)
                                {
                                    var book = codebooks[stageInfo.BookIndex];

                                    if (collectStats)
                                    {
                                        maxBookIndexUsed = Math.Max(maxBookIndexUsed, stageInfo.BookIndex);
                                        lookupVectorMaxAbsUsed = Math.Max(lookupVectorMaxAbsUsed, book.LookupVectorMaxAbs);
                                    }

                                    var vectorCount = stageInfo.VectorCount;
                                    for (var i = 0; i < vectorCount; i++)
                                    {
                                        var entry = ReadCodebookScalar(ref reader, book);
                                        var entryOffset = GetResidueEntryOffset(book, entry);
                                        AccumulateResidueType2Fast(
                                            residueValues,
                                            channelBaseOffsets,
                                            residueBegin,
                                            partition + classwordOffset,
                                            i,
                                            residue.PartitionSize,
                                            book,
                                            entryOffset);
                                        if (collectStats)
                                        {
                                            materializedValues += book.Dimensions;
                                            decodedVectors++;
                                        }
                                    }
                                }
                            }

                            partition += classwordPartitions;
                        }
                    }
                }
                else
                {
                    var activeChannelCount = activeChannels;
                    var residueType = residue.Type;
                    for (var pass = 0; pass < 8; pass++)
                    {
                        var partition = 0;
                        while (partition < partitionCount)
                        {
                            if (pass == 0)
                            {
                                for (var channelOrdinal = 0; channelOrdinal < activeChannelCount; channelOrdinal++)
                                {
                                    var classword = ReadCodebookScalar(ref reader, classbook);
                                    DecodeResidueClassword(
                                        classword,
                                        residue.Classifications,
                                        classificationsPerWord,
                                        classifications,
                                        channelOrdinal * partitionCount + partition,
                                        partitionCount - partition);
                                    if (collectStats)
                                    {
                                        decodedClasswords++;
                                    }
                                }
                            }

                            var classwordPartitions = Math.Min(classificationsPerWord, partitionCount - partition);
                            for (var classwordOffset = 0; classwordOffset < classwordPartitions; classwordOffset++)
                            {
                                var partitionClassificationOffset = partition + classwordOffset;
                                for (var channelOrdinal = 0; channelOrdinal < activeChannelCount; channelOrdinal++)
                                {
                                    var classification = classifications[channelOrdinal * partitionCount + partitionClassificationOffset];
                                    if ((uint)classification >= (uint)residue.Classifications)
                                    {
                                        throw new InvalidDataException($"Vorbis residue classification is invalid: {classification}.");
                                    }

                                    var stageInfo = residueStageInfos[(classification << 3) + pass];
                                    if (stageInfo.IsUsed)
                                    {
                                        var book = codebooks[stageInfo.BookIndex];

                                        if (collectStats)
                                        {
                                            maxBookIndexUsed = Math.Max(maxBookIndexUsed, stageInfo.BookIndex);
                                            lookupVectorMaxAbsUsed = Math.Max(lookupVectorMaxAbsUsed, book.LookupVectorMaxAbs);
                                        }

                                        var vectorCount = stageInfo.VectorCount;
                                        var channelBaseOffset = activeChannelBases![channelOrdinal];
                                        for (var i = 0; i < vectorCount; i++)
                                        {
                                            var entry = ReadCodebookScalar(ref reader, book);
                                            var entryOffset = GetResidueEntryOffset(book, entry);
                                            if (residueType == 0)
                                            {
                                                AccumulateResidueType0Fast(
                                                    residueValues,
                                                    channelBaseOffset,
                                                    residueBegin,
                                                    partition + classwordOffset,
                                                    i,
                                                    residue.PartitionSize,
                                                    book,
                                                    entryOffset);
                                            }
                                            else
                                            {
                                                AccumulateResidueType1Fast(
                                                    residueValues,
                                                    channelBaseOffset,
                                                    residueBegin,
                                                    partition + classwordOffset,
                                                    i,
                                                    residue.PartitionSize,
                                                    book,
                                                    entryOffset);
                                            }

                                            if (collectStats)
                                            {
                                                materializedValues += book.Dimensions;
                                                decodedVectors++;
                                            }
                                        }
                                    }
                                }
                            }

                            partition += classwordPartitions;
                        }
                    }
                }
            }
            else
            {
                for (var pass = 0; pass < 8; pass++)
                {
                    var partition = 0;
                    while (partition < partitionCount)
                    {
                        if (pass == 0)
                        {
                            var channelOrdinal = 0;
                            for (var channelIndex = 0; channelIndex < channels.Length; channelIndex++)
                            {
                                var channel = channels[channelIndex];
                                if (residue.Type != 2 && noResidue[channel])
                                {
                                    continue;
                                }

                                if (residue.Type == 2 && channelIndex > 0)
                                {
                                    continue;
                                }

                                var classword = ReadCodebookScalar(ref reader, classbook);
                                DecodeResidueClassword(
                                    classword,
                                    residue.Classifications,
                                    classificationsPerWord,
                                    classifications,
                                    channelOrdinal * partitionCount + partition,
                                    partitionCount - partition);
                                if (collectStats)
                                {
                                    decodedClasswords++;
                                }
                                channelOrdinal++;
                            }
                        }

                        var classwordPartitions = Math.Min(classificationsPerWord, partitionCount - partition);
                        for (var classwordOffset = 0; classwordOffset < classwordPartitions; classwordOffset++)
                        {
                            var channelOrdinal = 0;
                            for (var channelIndex = 0; channelIndex < channels.Length; channelIndex++)
                            {
                                var channel = channels[channelIndex];
                                if (residue.Type != 2 && noResidue[channel])
                                {
                                    continue;
                                }

                                if (residue.Type == 2 && channelIndex > 0)
                                {
                                    continue;
                                }

                                var classification = classifications[channelOrdinal * partitionCount + partition + classwordOffset];
                                if ((uint)classification >= (uint)residue.Classifications)
                                {
                                    throw new InvalidDataException($"Vorbis residue classification is invalid: {classification}.");
                                }

                                var stageInfo = residueStageInfos[(classification << 3) + pass];
                                if (stageInfo.IsUsed)
                                {
                                    var book = codebooks[stageInfo.BookIndex];

                                    if (collectStats)
                                    {
                                        maxBookIndexUsed = Math.Max(maxBookIndexUsed, stageInfo.BookIndex);
                                        lookupVectorMaxAbsUsed = Math.Max(lookupVectorMaxAbsUsed, book.LookupVectorMaxAbs);
                                    }
                                    var vectorCount = stageInfo.VectorCount;
                                    for (var i = 0; i < vectorCount; i++)
                                    {
                                        var entry = ReadCodebookScalar(ref reader, book);
                                        var written = AccumulateResidueVector(
                                            residueValues,
                                            residueValueCount,
                                            residue.Type,
                                            residueBegin,
                                            partition + classwordOffset,
                                            i,
                                            residue.PartitionSize,
                                            blockSizeHalf,
                                            channels,
                                            channelIndex,
                                            book,
                                            entry);
                                        if (collectStats)
                                        {
                                            materializedValues += written;
                                            decodedVectors++;
                                        }
                                    }
                                }

                                channelOrdinal++;
                            }
                        }

                        partition += classwordPartitions;
                    }
                }
            }
            if (collectStats)
            {
                valueHash = residueDestination is null
                    ? HashFloatValues(residueValues.AsSpan(0, residueValueCount))
                    : HashSelectedChannelValues(residueValues, blockSizeHalf, channels);
            }
        }
        catch (EndOfStreamException)
        {
            if (collectStats)
            {
                valueHash = residueDestination is null
                    ? HashFloatValues(residueValues.AsSpan(0, residueValueCount))
                    : HashSelectedChannelValues(residueValues, blockSizeHalf, channels);
            }

            return new OggVorbisResiduePacketInfo(decodedClasswords, decodedVectors, materializedValues, valueHash, maxBookIndexUsed, lookupVectorMaxAbsUsed);
        }
        finally
        {
            if (totalClassifications <= 4096)
            {
                ArrayPool<int>.Shared.Return(classifications);
            }

            if (residueDestination is null && residueValueCount <= 8192)
            {
                ArrayPool<float>.Shared.Return(residueValues);
            }
        }

        return new OggVorbisResiduePacketInfo(decodedClasswords, decodedVectors, materializedValues, valueHash, maxBookIndexUsed, lookupVectorMaxAbsUsed);
    }

    private static void ConsumeResiduePacketFast(
        ref VorbisBitReader reader,
        OggVorbisResidueInfo residue,
        OggVorbisSetupSummary setup,
        ReadOnlySpan<bool> noResidue,
        ReadOnlySpan<int> channels,
        int blockSizeHalf,
        float[] residueDestination,
        int[] channelBaseScratch,
        int[] activeChannelBaseScratch,
        OggVorbisProfileTotals? profile = null)
    {
        if ((uint)residue.Classbook >= (uint)setup.Codebooks.Length)
        {
            throw new InvalidDataException($"Vorbis residue references invalid classbook: {residue.Classbook}.");
        }

        var classbook = setup.Codebooks[residue.Classbook];
        if (classbook.Dimensions <= 0 || channels.IsEmpty)
        {
            return;
        }

        var isType2 = residue.Type == 2;
        var activeChannels = 0;
        for (var i = 0; i < channels.Length; i++)
        {
            var channel = channels[i];
            if ((uint)channel >= (uint)noResidue.Length)
            {
                throw new InvalidDataException($"Vorbis residue channel index is invalid: {channel}.");
            }

            channelBaseScratch[i] = channel * blockSizeHalf;
            if (!noResidue[channel])
            {
                if (!isType2)
                {
                    activeChannelBaseScratch[activeChannels] = channelBaseScratch[i];
                }

                activeChannels++;
            }
        }

        if (activeChannels == 0)
        {
            return;
        }

        var effectiveChannels = isType2 ? 1 : activeChannels;
        var residueLimit = blockSizeHalf * (isType2 ? channels.Length : 1);
        var residueBegin = Math.Min(residue.Begin, residueLimit);
        var residueEnd = Math.Min(residue.End, residueLimit);
        var samplesToRead = residueEnd - residueBegin;
        if (samplesToRead <= 0)
        {
            return;
        }

        var partitionCount = samplesToRead / residue.PartitionSize;
        if (partitionCount <= 0)
        {
            return;
        }

        var classificationsPerWord = classbook.Dimensions;
        var totalClassifications = checked(effectiveChannels * partitionCount);
        var classifications = totalClassifications <= 4096
            ? ArrayPool<int>.Shared.Rent(totalClassifications)
            : new int[totalClassifications];
        var residueStageInfos = residue.StageInfos;
        var codebooks = setup.Codebooks;

        try
        {
            if (isType2)
            {
                var type2Start = profile is null ? 0 : Stopwatch.GetTimestamp();
                var channelBaseOffsets = channelBaseScratch.AsSpan(0, channels.Length);
                for (var pass = 0; pass < 8; pass++)
                {
                    var partition = 0;
                    while (partition < partitionCount)
                    {
                        if (pass == 0)
                        {
                            var classword = ReadCodebookScalar(ref reader, classbook, profile);
                            DecodeResidueClassword(
                                classword,
                                residue.Classifications,
                                classificationsPerWord,
                                classifications,
                                partition,
                                partitionCount - partition);
                        }

                        var classwordPartitions = Math.Min(classificationsPerWord, partitionCount - partition);
                        for (var classwordOffset = 0; classwordOffset < classwordPartitions; classwordOffset++)
                        {
                            var classification = classifications[partition + classwordOffset];
                            if ((uint)classification >= (uint)residue.Classifications)
                            {
                                throw new InvalidDataException($"Vorbis residue classification is invalid: {classification}.");
                            }

                            var stageInfo = residueStageInfos[(classification << 3) + pass];
                            if (!stageInfo.IsUsed)
                            {
                                continue;
                            }

                            var book = codebooks[stageInfo.BookIndex];
                            var vectorCount = stageInfo.VectorCount;
                            var partitionIndex = partition + classwordOffset;
                            for (var i = 0; i < vectorCount; i++)
                            {
                                var entry = ReadCodebookScalar(ref reader, book, profile);
                                var entryOffset = GetResidueEntryOffset(book, entry);
                                var writeStart = profile is null ? 0 : Stopwatch.GetTimestamp();
                                AccumulateResidueType2Fast(
                                    residueDestination,
                                    channelBaseOffsets,
                                    residueBegin,
                                    partitionIndex,
                                    i,
                                    residue.PartitionSize,
                                    book,
                                    entryOffset);
                                if (profile is not null)
                                {
                                    profile.ResidueType2WriteTicks += Stopwatch.GetTimestamp() - writeStart;
                                    profile.ResidueType2Vectors++;
                                }
                            }
                        }

                        partition += classwordPartitions;
                    }
                }

                if (profile is not null)
                {
                    profile.ResidueType2Ticks += Stopwatch.GetTimestamp() - type2Start;
                }

                return;
            }

            var otherStart = profile is null ? 0 : Stopwatch.GetTimestamp();
            var residueType = residue.Type;
            for (var pass = 0; pass < 8; pass++)
            {
                var partition = 0;
                while (partition < partitionCount)
                {
                    if (pass == 0)
                    {
                        for (var channelOrdinal = 0; channelOrdinal < activeChannels; channelOrdinal++)
                        {
                            var classword = ReadCodebookScalar(ref reader, classbook, profile);
                            DecodeResidueClassword(
                                classword,
                                residue.Classifications,
                                classificationsPerWord,
                                classifications,
                                channelOrdinal * partitionCount + partition,
                                partitionCount - partition);
                        }
                    }

                    var classwordPartitions = Math.Min(classificationsPerWord, partitionCount - partition);
                    for (var classwordOffset = 0; classwordOffset < classwordPartitions; classwordOffset++)
                    {
                        var partitionClassificationOffset = partition + classwordOffset;
                        for (var channelOrdinal = 0; channelOrdinal < activeChannels; channelOrdinal++)
                        {
                            var classification = classifications[channelOrdinal * partitionCount + partitionClassificationOffset];
                            if ((uint)classification >= (uint)residue.Classifications)
                            {
                                throw new InvalidDataException($"Vorbis residue classification is invalid: {classification}.");
                            }

                            var stageInfo = residueStageInfos[(classification << 3) + pass];
                            if (!stageInfo.IsUsed)
                            {
                                continue;
                            }

                            var book = codebooks[stageInfo.BookIndex];
                            var vectorCount = stageInfo.VectorCount;
                            var channelBaseOffset = activeChannelBaseScratch[channelOrdinal];
                            for (var i = 0; i < vectorCount; i++)
                            {
                                var entry = ReadCodebookScalar(ref reader, book, profile);
                                var entryOffset = GetResidueEntryOffset(book, entry);
                                var writeStart = profile is null ? 0 : Stopwatch.GetTimestamp();
                                if (residueType == 0)
                                {
                                    AccumulateResidueType0Fast(
                                        residueDestination,
                                        channelBaseOffset,
                                        residueBegin,
                                        partition + classwordOffset,
                                        i,
                                        residue.PartitionSize,
                                        book,
                                        entryOffset);
                                }
                                else
                                {
                                    AccumulateResidueType1Fast(
                                        residueDestination,
                                        channelBaseOffset,
                                        residueBegin,
                                        partition + classwordOffset,
                                        i,
                                        residue.PartitionSize,
                                        book,
                                        entryOffset);
                                }

                                if (profile is not null)
                                {
                                    profile.ResidueOtherWriteTicks += Stopwatch.GetTimestamp() - writeStart;
                                    profile.ResidueOtherVectors++;
                                }
                            }
                        }
                    }

                    partition += classwordPartitions;
                }
            }

            if (profile is not null)
            {
                profile.ResidueOtherTicks += Stopwatch.GetTimestamp() - otherStart;
            }
        }
        catch (EndOfStreamException)
        {
        }
        finally
        {
            if (totalClassifications <= 4096)
            {
                ArrayPool<int>.Shared.Return(classifications);
            }
        }
    }

    private static long HashSelectedChannelValues(float[] values, int blockSizeHalf, ReadOnlySpan<int> channels)
    {
        unchecked
        {
            var hash = 1469598103934665603L;
            for (var channelIndex = 0; channelIndex < channels.Length; channelIndex++)
            {
                var channel = channels[channelIndex];
                var offset = channel * blockSizeHalf;
                var span = values.AsSpan(offset, blockSizeHalf);
                for (var i = 0; i < span.Length; i++)
                {
                    hash ^= BitConverter.SingleToInt32Bits(span[i]);
                    hash *= 1099511628211L;
                }
            }

            return hash;
        }
    }

    private static void ApplyFloorCurves(float[] residue, float[] floorCurves, bool[] noResidue, int channels, int blockSizeHalf)
    {
        for (var channel = 0; channel < channels; channel++)
        {
            var offset = channel * blockSizeHalf;
            if (noResidue[channel])
            {
                Array.Clear(residue, offset, blockSizeHalf);
                continue;
            }

            MultiplyFloatSpanInPlace(
                residue.AsSpan(offset, blockSizeHalf),
                floorCurves.AsSpan(offset, blockSizeHalf));
        }
    }

    private static void ApplyInverseCoupling(float[] residue, OggVorbisMappingInfo mapping, int blockSizeHalf)
    {
        for (var step = mapping.CouplingSteps - 1; step >= 0; step--)
        {
            var magnitudeOffset = mapping.Magnitudes[step] * blockSizeHalf;
            var angleOffset = mapping.Angles[step] * blockSizeHalf;
            for (var i = 0; i < blockSizeHalf; i++)
            {
                var magnitude = residue[magnitudeOffset + i];
                var angle = residue[angleOffset + i];
                float newMagnitude;
                float newAngle;
                if (magnitude > 0.0f)
                {
                    if (angle > 0.0f)
                    {
                        newMagnitude = magnitude;
                        newAngle = magnitude - angle;
                    }
                    else
                    {
                        newMagnitude = magnitude + angle;
                        newAngle = magnitude;
                    }
                }
                else if (angle > 0.0f)
                {
                    newMagnitude = magnitude;
                    newAngle = magnitude + angle;
                }
                else
                {
                    newMagnitude = magnitude - angle;
                    newAngle = magnitude;
                }

                residue[magnitudeOffset + i] = newMagnitude;
                residue[angleOffset + i] = newAngle;
            }
        }
    }

    private static float[] BuildVorbisWindow(int blockSize)
    {
        var window = new float[blockSize];
        var scale = MathF.PI / blockSize;
        for (var i = 0; i < blockSize; i++)
        {
            var inner = MathF.Sin((i + 0.5f) * scale);
            window[i] = MathF.Sin(0.5f * MathF.PI * inner * inner);
        }

        return window;
    }

    private static float GetVorbisLeftWindowedSample(
        ReadOnlySpan<float> block,
        int localIndex,
        int blockSize,
        int blockSizeSmall,
        bool previousWindowFlag,
        ReadOnlySpan<float> smallWindow,
        ReadOnlySpan<float> largeWindow)
    {
        if ((uint)localIndex >= (uint)blockSize)
        {
            return 0f;
        }

        var half = blockSize >> 1;
        if (localIndex >= half)
        {
            return 0f;
        }

        var sample = block[localIndex];
        if (blockSize == blockSizeSmall)
        {
            return sample * smallWindow[localIndex];
        }

        if (previousWindowFlag)
        {
            return sample * largeWindow[localIndex];
        }

        var leftWindowStart = (blockSize >> 2) - (blockSizeSmall >> 2);
        var leftWindowEnd = leftWindowStart + (blockSizeSmall >> 1);
        if (localIndex < leftWindowStart)
        {
            return 0f;
        }

        if (localIndex < leftWindowEnd)
        {
            return sample * smallWindow[localIndex - leftWindowStart];
        }

        return sample;
    }

    private static float GetVorbisRightWindowedSample(
        ReadOnlySpan<float> block,
        int localIndex,
        int blockSize,
        int blockSizeSmall,
        bool nextWindowFlag,
        ReadOnlySpan<float> smallWindow,
        ReadOnlySpan<float> largeWindow)
    {
        if ((uint)localIndex >= (uint)blockSize)
        {
            return 0f;
        }

        var half = blockSize >> 1;
        if (localIndex < half)
        {
            return 0f;
        }

        var sample = block[localIndex];
        if (blockSize == blockSizeSmall)
        {
            return sample * smallWindow[localIndex];
        }

        if (nextWindowFlag)
        {
            return sample * largeWindow[localIndex];
        }

        var rightWindowStart = ((blockSize * 3) >> 2) - (blockSizeSmall >> 2);
        var rightWindowEnd = rightWindowStart + (blockSizeSmall >> 1);
        if (localIndex < rightWindowStart)
        {
            return sample;
        }

        if (localIndex < rightWindowEnd)
        {
            return sample * smallWindow[(localIndex - rightWindowStart) + (blockSizeSmall >> 1)];
        }

        return 0f;
    }

    private static float ComputeVorbisWindowedMaxAbs(
        ReadOnlySpan<float> block,
        int blockSize,
        int blockSizeSmall,
        bool previousWindowFlag,
        bool nextWindowFlag,
        ReadOnlySpan<float> smallWindow,
        ReadOnlySpan<float> largeWindow)
    {
        var maxAbs = 0f;
        for (var i = 0; i < blockSize; i++)
        {
            var sample = i < (blockSize >> 1)
                ? GetVorbisLeftWindowedSample(block, i, blockSize, blockSizeSmall, previousWindowFlag, smallWindow, largeWindow)
                : GetVorbisRightWindowedSample(block, i, blockSize, blockSizeSmall, nextWindowFlag, smallWindow, largeWindow);
            maxAbs = Math.Max(maxAbs, Math.Abs(sample));
        }

        return maxAbs;
    }

    private static float ApplyVorbisWindow(
        ReadOnlySpan<float> block,
        Span<float> destination,
        int blockSize,
        int blockSizeSmall,
        bool previousWindowFlag,
        bool nextWindowFlag,
        ReadOnlySpan<float> smallWindow,
        ReadOnlySpan<float> largeWindow,
        bool collectDebug)
    {
        if (block.Length < blockSize || destination.Length < blockSize)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Vorbis window spans do not match the block size.");
        }

        var maxAbs = 0f;
        if (!collectDebug)
        {
            if (blockSize == blockSizeSmall)
            {
                MultiplyFloatSpans(
                    block.Slice(0, blockSize),
                    smallWindow.Slice(0, blockSize),
                    destination.Slice(0, blockSize));
                return 0f;
            }

            var halfNoDebug = blockSize >> 1;
            var smallHalfNoDebug = blockSizeSmall >> 1;
            var leftStartNoDebug = WindowLeft(blockSize, blockSizeSmall, previousWindowFlag);
            var leftEndNoDebug = leftStartNoDebug + smallHalfNoDebug;
            var rightEndNoDebug = WindowRight(blockSize, blockSizeSmall, nextWindowFlag);
            var rightStartNoDebug = rightEndNoDebug - smallHalfNoDebug;

            if (previousWindowFlag && nextWindowFlag)
            {
                MultiplyFloatSpans(
                    block.Slice(0, blockSize),
                    largeWindow.Slice(0, blockSize),
                    destination.Slice(0, blockSize));
                return 0f;
            }

            if (!previousWindowFlag)
            {
                destination[..leftStartNoDebug].Clear();
                MultiplyFloatSpans(
                    block.Slice(leftStartNoDebug, smallHalfNoDebug),
                    smallWindow.Slice(0, smallHalfNoDebug),
                    destination.Slice(leftStartNoDebug, smallHalfNoDebug));
            }
            else
            {
                MultiplyFloatSpans(
                    block.Slice(0, halfNoDebug),
                    largeWindow.Slice(0, halfNoDebug),
                    destination.Slice(0, halfNoDebug));
            }

            var middleStartNoDebug = previousWindowFlag ? halfNoDebug : leftEndNoDebug;
            var middleEndNoDebug = nextWindowFlag ? halfNoDebug : rightStartNoDebug;
            if (middleEndNoDebug > middleStartNoDebug)
            {
                block.Slice(middleStartNoDebug, middleEndNoDebug - middleStartNoDebug)
                    .CopyTo(destination[middleStartNoDebug..middleEndNoDebug]);
            }

            if (!nextWindowFlag)
            {
                MultiplyFloatSpans(
                    block.Slice(rightStartNoDebug, smallHalfNoDebug),
                    smallWindow.Slice(smallHalfNoDebug, smallHalfNoDebug),
                    destination.Slice(rightStartNoDebug, smallHalfNoDebug));
                destination[rightEndNoDebug..blockSize].Clear();
            }
            else
            {
                MultiplyFloatSpans(
                    block.Slice(halfNoDebug, halfNoDebug),
                    largeWindow.Slice(halfNoDebug, halfNoDebug),
                    destination.Slice(halfNoDebug, halfNoDebug));
            }

            return 0f;
        }

        if (blockSize == blockSizeSmall)
        {
            for (var i = 0; i < blockSize; i++)
            {
                var sample = block[i] * smallWindow[i];
                destination[i] = sample;
                if (collectDebug)
                {
                    maxAbs = Math.Max(maxAbs, Math.Abs(sample));
                }
            }

            return maxAbs;
        }

        var half = blockSize >> 1;
        var smallHalf = blockSizeSmall >> 1;
        var leftStart = WindowLeft(blockSize, blockSizeSmall, previousWindowFlag);
        var leftEnd = leftStart + smallHalf;
        var rightEnd = WindowRight(blockSize, blockSizeSmall, nextWindowFlag);
        var rightStart = rightEnd - smallHalf;

        if (previousWindowFlag && nextWindowFlag)
        {
            for (var i = 0; i < blockSize; i++)
            {
                var sample = block[i] * largeWindow[i];
                destination[i] = sample;
                if (collectDebug)
                {
                    maxAbs = Math.Max(maxAbs, Math.Abs(sample));
                }
            }

            return maxAbs;
        }

        if (!previousWindowFlag)
        {
            destination[..leftStart].Clear();
            for (var i = leftStart; i < leftEnd; i++)
            {
                var sample = block[i] * smallWindow[i - leftStart];
                destination[i] = sample;
                if (collectDebug)
                {
                    maxAbs = Math.Max(maxAbs, Math.Abs(sample));
                }
            }
        }
        else
        {
            for (var i = 0; i < half; i++)
            {
                var sample = block[i] * largeWindow[i];
                destination[i] = sample;
                if (collectDebug)
                {
                    maxAbs = Math.Max(maxAbs, Math.Abs(sample));
                }
            }
        }

        var middleStart = previousWindowFlag ? half : leftEnd;
        var middleEnd = nextWindowFlag ? half : rightStart;
        if (middleEnd > middleStart)
        {
            block.Slice(middleStart, middleEnd - middleStart).CopyTo(destination[middleStart..middleEnd]);
            if (collectDebug)
            {
                maxAbs = Math.Max(maxAbs, MaxAbs(destination[middleStart..middleEnd]));
            }
        }

        if (!nextWindowFlag)
        {
            for (var i = rightStart; i < rightEnd; i++)
            {
                var sample = block[i] * smallWindow[(i - rightStart) + smallHalf];
                destination[i] = sample;
                if (collectDebug)
                {
                    maxAbs = Math.Max(maxAbs, Math.Abs(sample));
                }
            }

            destination[rightEnd..blockSize].Clear();
        }
        else
        {
            for (var i = half; i < blockSize; i++)
            {
                var sample = block[i] * largeWindow[i];
                destination[i] = sample;
                if (collectDebug)
                {
                    maxAbs = Math.Max(maxAbs, Math.Abs(sample));
                }
            }
        }

        return maxAbs;
    }

    private static unsafe void ComputeVorbisImdct(ReadOnlySpan<float> coefficients, Span<float> output, OggImdctPlan plan, OggVorbisProfileTotals? profile = null)
    {
        var outputLength = plan.OutputLength;
        var n2 = outputLength >> 1;
        var n4 = outputLength >> 2;
        if (coefficients.Length < n2 || output.Length < outputLength)
        {
            throw new ArgumentOutOfRangeException(nameof(output), "Vorbis IMDCT spans do not match the plan.");
        }

        var trig = plan.Trig;

        fixed (float* coefficientsPtr = coefficients)
        fixed (float* outputPtr = output)
        fixed (float* trigPtr = trig)
        {
            var inputIndex = n2 - 7;
            var outputIndex = n2 + n4;
            var trigIndex = n4;
            do
            {
                outputIndex -= 4;
                outputPtr[outputIndex + 0] = (-coefficientsPtr[inputIndex + 2] * trigPtr[trigIndex + 3]) - (coefficientsPtr[inputIndex + 0] * trigPtr[trigIndex + 2]);
                outputPtr[outputIndex + 1] = (coefficientsPtr[inputIndex + 0] * trigPtr[trigIndex + 3]) - (coefficientsPtr[inputIndex + 2] * trigPtr[trigIndex + 2]);
                outputPtr[outputIndex + 2] = (-coefficientsPtr[inputIndex + 6] * trigPtr[trigIndex + 1]) - (coefficientsPtr[inputIndex + 4] * trigPtr[trigIndex + 0]);
                outputPtr[outputIndex + 3] = (coefficientsPtr[inputIndex + 4] * trigPtr[trigIndex + 1]) - (coefficientsPtr[inputIndex + 6] * trigPtr[trigIndex + 0]);
                inputIndex -= 8;
                trigIndex += 4;
            }
            while (inputIndex >= 0);

            inputIndex = n2 - 8;
            outputIndex = n2 + n4;
            trigIndex = n4;
            do
            {
                trigIndex -= 4;
                outputPtr[outputIndex + 0] = (coefficientsPtr[inputIndex + 4] * trigPtr[trigIndex + 3]) + (coefficientsPtr[inputIndex + 6] * trigPtr[trigIndex + 2]);
                outputPtr[outputIndex + 1] = (coefficientsPtr[inputIndex + 4] * trigPtr[trigIndex + 2]) - (coefficientsPtr[inputIndex + 6] * trigPtr[trigIndex + 3]);
                outputPtr[outputIndex + 2] = (coefficientsPtr[inputIndex + 0] * trigPtr[trigIndex + 1]) + (coefficientsPtr[inputIndex + 2] * trigPtr[trigIndex + 0]);
                outputPtr[outputIndex + 3] = (coefficientsPtr[inputIndex + 0] * trigPtr[trigIndex + 0]) - (coefficientsPtr[inputIndex + 2] * trigPtr[trigIndex + 1]);
                inputIndex -= 8;
                outputIndex += 4;
            }
            while (inputIndex >= 0);
        }

        if (profile is null)
        {
            ApplyVorbisMdctButterflies(plan, output, n2, n2);
            ApplyVorbisMdctBitReverse(plan, output);
        }
        else
        {
            var imdctStageStart = Stopwatch.GetTimestamp();
            ApplyVorbisMdctButterflies(plan, output, n2, n2);
            profile.ImdctButterfliesTicks += Stopwatch.GetTimestamp() - imdctStageStart;
            imdctStageStart = Stopwatch.GetTimestamp();
            ApplyVorbisMdctBitReverse(plan, output);
            profile.ImdctBitReverseTicks += Stopwatch.GetTimestamp() - imdctStageStart;
        }

        fixed (float* outputPtr = output)
        fixed (float* trigPtr = trig)
        {
            var upperWrite = n2 + n4;
            var lowerWrite = n2 + n4;
            var inputIndex = 0;
            var trigIndex = n2;
            do
            {
                upperWrite -= 4;

                outputPtr[upperWrite + 3] = (outputPtr[inputIndex + 0] * trigPtr[trigIndex + 1]) - (outputPtr[inputIndex + 1] * trigPtr[trigIndex + 0]);
                outputPtr[lowerWrite + 0] = -((outputPtr[inputIndex + 0] * trigPtr[trigIndex + 0]) + (outputPtr[inputIndex + 1] * trigPtr[trigIndex + 1]));

                outputPtr[upperWrite + 2] = (outputPtr[inputIndex + 2] * trigPtr[trigIndex + 3]) - (outputPtr[inputIndex + 3] * trigPtr[trigIndex + 2]);
                outputPtr[lowerWrite + 1] = -((outputPtr[inputIndex + 2] * trigPtr[trigIndex + 2]) + (outputPtr[inputIndex + 3] * trigPtr[trigIndex + 3]));

                outputPtr[upperWrite + 1] = (outputPtr[inputIndex + 4] * trigPtr[trigIndex + 5]) - (outputPtr[inputIndex + 5] * trigPtr[trigIndex + 4]);
                outputPtr[lowerWrite + 2] = -((outputPtr[inputIndex + 4] * trigPtr[trigIndex + 4]) + (outputPtr[inputIndex + 5] * trigPtr[trigIndex + 5]));

                outputPtr[upperWrite + 0] = (outputPtr[inputIndex + 6] * trigPtr[trigIndex + 7]) - (outputPtr[inputIndex + 7] * trigPtr[trigIndex + 6]);
                outputPtr[lowerWrite + 3] = -((outputPtr[inputIndex + 6] * trigPtr[trigIndex + 6]) + (outputPtr[inputIndex + 7] * trigPtr[trigIndex + 7]));

                lowerWrite += 4;
                inputIndex += 8;
                trigIndex += 8;
            }
            while (inputIndex < upperWrite);

            inputIndex = n2 + n4;
            upperWrite = n4;
            lowerWrite = upperWrite;
            do
            {
                upperWrite -= 4;
                inputIndex -= 4;

                outputPtr[lowerWrite + 0] = -(outputPtr[upperWrite + 3] = outputPtr[inputIndex + 3]);
                outputPtr[lowerWrite + 1] = -(outputPtr[upperWrite + 2] = outputPtr[inputIndex + 2]);
                outputPtr[lowerWrite + 2] = -(outputPtr[upperWrite + 1] = outputPtr[inputIndex + 1]);
                outputPtr[lowerWrite + 3] = -(outputPtr[upperWrite + 0] = outputPtr[inputIndex + 0]);

                lowerWrite += 4;
            }
            while (lowerWrite < inputIndex);

            inputIndex = n2 + n4;
            upperWrite = n2 + n4;
            var lowerLimit = n2;
            do
            {
                upperWrite -= 4;
                outputPtr[upperWrite + 0] = outputPtr[inputIndex + 3];
                outputPtr[upperWrite + 1] = outputPtr[inputIndex + 2];
                outputPtr[upperWrite + 2] = outputPtr[inputIndex + 1];
                outputPtr[upperWrite + 3] = outputPtr[inputIndex + 0];
                inputIndex += 4;
            }
            while (upperWrite > lowerLimit);
        }

        ScaleFloatSpanInPlace(output[..outputLength], plan.Scale);
    }

    private static void ApplyVorbisMdctButterflies(OggImdctPlan plan, Span<float> data, int offset, int points)
    {
        var stages = plan.Log2N - 5;
        if (--stages > 0)
        {
            ApplyVorbisMdctButterflyFirst(plan.Trig, data, offset, points);
        }

        for (var i = 1; --stages > 0; i++)
        {
            for (var j = 0; j < (1 << i); j++)
            {
                ApplyVorbisMdctButterflyGeneric(plan.Trig, data, offset + ((points >> i) * j), points >> i, 4 << i);
            }
        }

        for (var j = 0; j < points; j += 32)
        {
            ApplyVorbisMdctButterfly32(data, offset + j);
        }
    }

    private static unsafe void ApplyVorbisMdctBitReverse(OggImdctPlan plan, Span<float> data)
    {
        var n = plan.OutputLength;
        var writeLow = 0;
        var writeHigh = n >> 1;
        var dataOffset = writeHigh;
        var trigIndex = n;
        var bitIndex = 0;

        fixed (float* dataPtr = data)
        fixed (float* trigPtr = plan.Trig)
        fixed (int* bitPtr = plan.BitReverse)
        {
            do
            {
                var x0 = dataOffset + bitPtr[bitIndex + 0];
                var x1 = dataOffset + bitPtr[bitIndex + 1];

                var r0 = dataPtr[x0 + 1] - dataPtr[x1 + 1];
                var r1 = dataPtr[x0 + 0] + dataPtr[x1 + 0];
                var r2 = (r1 * trigPtr[trigIndex + 0]) + (r0 * trigPtr[trigIndex + 1]);
                var r3 = (r1 * trigPtr[trigIndex + 1]) - (r0 * trigPtr[trigIndex + 0]);

                writeHigh -= 4;

                r0 = 0.5f * (dataPtr[x0 + 1] + dataPtr[x1 + 1]);
                r1 = 0.5f * (dataPtr[x0 + 0] - dataPtr[x1 + 0]);

                dataPtr[writeLow + 0] = r0 + r2;
                dataPtr[writeHigh + 2] = r0 - r2;
                dataPtr[writeLow + 1] = r1 + r3;
                dataPtr[writeHigh + 3] = r3 - r1;

                x0 = dataOffset + bitPtr[bitIndex + 2];
                x1 = dataOffset + bitPtr[bitIndex + 3];

                r0 = dataPtr[x0 + 1] - dataPtr[x1 + 1];
                r1 = dataPtr[x0 + 0] + dataPtr[x1 + 0];
                r2 = (r1 * trigPtr[trigIndex + 2]) + (r0 * trigPtr[trigIndex + 3]);
                r3 = (r1 * trigPtr[trigIndex + 3]) - (r0 * trigPtr[trigIndex + 2]);

                r0 = 0.5f * (dataPtr[x0 + 1] + dataPtr[x1 + 1]);
                r1 = 0.5f * (dataPtr[x0 + 0] - dataPtr[x1 + 0]);

                dataPtr[writeLow + 2] = r0 + r2;
                dataPtr[writeHigh + 0] = r0 - r2;
                dataPtr[writeLow + 3] = r1 + r3;
                dataPtr[writeHigh + 1] = r3 - r1;

                trigIndex += 4;
                bitIndex += 4;
                writeLow += 4;
            }
            while (writeLow < writeHigh);
        }
    }

    private static unsafe void ApplyVorbisMdctButterflyFirst(float[] trig, Span<float> data, int offset, int points)
    {
        var x1 = offset + points - 8;
        var x2 = offset + (points >> 1) - 8;
        var trigIndex = 0;

        fixed (float* dataPtr = data)
        fixed (float* trigPtr = trig)
        {
            do
            {
                var r0 = dataPtr[x1 + 6] - dataPtr[x2 + 6];
                var r1 = dataPtr[x1 + 7] - dataPtr[x2 + 7];
                dataPtr[x1 + 6] += dataPtr[x2 + 6];
                dataPtr[x1 + 7] += dataPtr[x2 + 7];
                dataPtr[x2 + 6] = (r1 * trigPtr[trigIndex + 1]) + (r0 * trigPtr[trigIndex + 0]);
                dataPtr[x2 + 7] = (r1 * trigPtr[trigIndex + 0]) - (r0 * trigPtr[trigIndex + 1]);

                r0 = dataPtr[x1 + 4] - dataPtr[x2 + 4];
                r1 = dataPtr[x1 + 5] - dataPtr[x2 + 5];
                dataPtr[x1 + 4] += dataPtr[x2 + 4];
                dataPtr[x1 + 5] += dataPtr[x2 + 5];
                dataPtr[x2 + 4] = (r1 * trigPtr[trigIndex + 5]) + (r0 * trigPtr[trigIndex + 4]);
                dataPtr[x2 + 5] = (r1 * trigPtr[trigIndex + 4]) - (r0 * trigPtr[trigIndex + 5]);

                r0 = dataPtr[x1 + 2] - dataPtr[x2 + 2];
                r1 = dataPtr[x1 + 3] - dataPtr[x2 + 3];
                dataPtr[x1 + 2] += dataPtr[x2 + 2];
                dataPtr[x1 + 3] += dataPtr[x2 + 3];
                dataPtr[x2 + 2] = (r1 * trigPtr[trigIndex + 9]) + (r0 * trigPtr[trigIndex + 8]);
                dataPtr[x2 + 3] = (r1 * trigPtr[trigIndex + 8]) - (r0 * trigPtr[trigIndex + 9]);

                r0 = dataPtr[x1 + 0] - dataPtr[x2 + 0];
                r1 = dataPtr[x1 + 1] - dataPtr[x2 + 1];
                dataPtr[x1 + 0] += dataPtr[x2 + 0];
                dataPtr[x1 + 1] += dataPtr[x2 + 1];
                dataPtr[x2 + 0] = (r1 * trigPtr[trigIndex + 13]) + (r0 * trigPtr[trigIndex + 12]);
                dataPtr[x2 + 1] = (r1 * trigPtr[trigIndex + 12]) - (r0 * trigPtr[trigIndex + 13]);

                x1 -= 8;
                x2 -= 8;
                trigIndex += 16;
            }
            while (x2 >= offset);
        }
    }

    private static unsafe void ApplyVorbisMdctButterflyGeneric(float[] trig, Span<float> data, int offset, int points, int trigStride)
    {
        var x1 = offset + points - 8;
        var x2 = offset + (points >> 1) - 8;
        var trigIndex = 0;

        fixed (float* dataPtr = data)
        fixed (float* trigPtr = trig)
        {
            do
            {
                var r0 = dataPtr[x1 + 6] - dataPtr[x2 + 6];
                var r1 = dataPtr[x1 + 7] - dataPtr[x2 + 7];
                dataPtr[x1 + 6] += dataPtr[x2 + 6];
                dataPtr[x1 + 7] += dataPtr[x2 + 7];
                dataPtr[x2 + 6] = (r1 * trigPtr[trigIndex + 1]) + (r0 * trigPtr[trigIndex + 0]);
                dataPtr[x2 + 7] = (r1 * trigPtr[trigIndex + 0]) - (r0 * trigPtr[trigIndex + 1]);

                trigIndex += trigStride;

                r0 = dataPtr[x1 + 4] - dataPtr[x2 + 4];
                r1 = dataPtr[x1 + 5] - dataPtr[x2 + 5];
                dataPtr[x1 + 4] += dataPtr[x2 + 4];
                dataPtr[x1 + 5] += dataPtr[x2 + 5];
                dataPtr[x2 + 4] = (r1 * trigPtr[trigIndex + 1]) + (r0 * trigPtr[trigIndex + 0]);
                dataPtr[x2 + 5] = (r1 * trigPtr[trigIndex + 0]) - (r0 * trigPtr[trigIndex + 1]);

                trigIndex += trigStride;

                r0 = dataPtr[x1 + 2] - dataPtr[x2 + 2];
                r1 = dataPtr[x1 + 3] - dataPtr[x2 + 3];
                dataPtr[x1 + 2] += dataPtr[x2 + 2];
                dataPtr[x1 + 3] += dataPtr[x2 + 3];
                dataPtr[x2 + 2] = (r1 * trigPtr[trigIndex + 1]) + (r0 * trigPtr[trigIndex + 0]);
                dataPtr[x2 + 3] = (r1 * trigPtr[trigIndex + 0]) - (r0 * trigPtr[trigIndex + 1]);

                trigIndex += trigStride;

                r0 = dataPtr[x1 + 0] - dataPtr[x2 + 0];
                r1 = dataPtr[x1 + 1] - dataPtr[x2 + 1];
                dataPtr[x1 + 0] += dataPtr[x2 + 0];
                dataPtr[x1 + 1] += dataPtr[x2 + 1];
                dataPtr[x2 + 0] = (r1 * trigPtr[trigIndex + 1]) + (r0 * trigPtr[trigIndex + 0]);
                dataPtr[x2 + 1] = (r1 * trigPtr[trigIndex + 0]) - (r0 * trigPtr[trigIndex + 1]);

                trigIndex += trigStride;
                x1 -= 8;
                x2 -= 8;
            }
            while (x2 >= offset);
        }
    }

    private static unsafe void ApplyVorbisMdctButterfly32(Span<float> data, int offset)
    {
        const float cPi3Over8 = 0.38268343236508977f;
        const float cPi2Over8 = 0.7071067811865475f;
        const float cPi1Over8 = 0.9238795325112867f;

        fixed (float* dataPtr = data)
        {
            var values = dataPtr + offset;

            var r0 = values[30] - values[14];
            var r1 = values[31] - values[15];
            values[30] += values[14];
            values[31] += values[15];
            values[14] = r0;
            values[15] = r1;

            r0 = values[28] - values[12];
            r1 = values[29] - values[13];
            values[28] += values[12];
            values[29] += values[13];
            values[12] = (r0 * cPi1Over8) - (r1 * cPi3Over8);
            values[13] = (r0 * cPi3Over8) + (r1 * cPi1Over8);

            r0 = values[26] - values[10];
            r1 = values[27] - values[11];
            values[26] += values[10];
            values[27] += values[11];
            values[10] = (r0 - r1) * cPi2Over8;
            values[11] = (r0 + r1) * cPi2Over8;

            r0 = values[24] - values[8];
            r1 = values[25] - values[9];
            values[24] += values[8];
            values[25] += values[9];
            values[8] = (r0 * cPi3Over8) - (r1 * cPi1Over8);
            values[9] = (r1 * cPi3Over8) + (r0 * cPi1Over8);

            r0 = values[22] - values[6];
            r1 = values[7] - values[23];
            values[22] += values[6];
            values[23] += values[7];
            values[6] = r1;
            values[7] = r0;

            r0 = values[4] - values[20];
            r1 = values[5] - values[21];
            values[20] += values[4];
            values[21] += values[5];
            values[4] = (r1 * cPi1Over8) + (r0 * cPi3Over8);
            values[5] = (r1 * cPi3Over8) - (r0 * cPi1Over8);

            r0 = values[2] - values[18];
            r1 = values[3] - values[19];
            values[18] += values[2];
            values[19] += values[3];
            values[2] = (r1 + r0) * cPi2Over8;
            values[3] = (r1 - r0) * cPi2Over8;

            r0 = values[0] - values[16];
            r1 = values[1] - values[17];
            values[16] += values[0];
            values[17] += values[1];
            values[0] = (r1 * cPi3Over8) + (r0 * cPi1Over8);
            values[1] = (r1 * cPi1Over8) - (r0 * cPi3Over8);
        }

        ApplyVorbisMdctButterfly16(data, offset);
        ApplyVorbisMdctButterfly16(data, offset + 16);
    }

    private static unsafe void ApplyVorbisMdctButterfly16(Span<float> data, int offset)
    {
        const float cPi2Over8 = 0.7071067811865475f;

        fixed (float* dataPtr = data)
        {
            var values = dataPtr + offset;

            var r0 = values[1] - values[9];
            var r1 = values[0] - values[8];
            values[8] += values[0];
            values[9] += values[1];
            values[0] = (r0 + r1) * cPi2Over8;
            values[1] = (r0 - r1) * cPi2Over8;

            r0 = values[3] - values[11];
            r1 = values[10] - values[2];
            values[10] += values[2];
            values[11] += values[3];
            values[2] = r0;
            values[3] = r1;

            r0 = values[12] - values[4];
            r1 = values[13] - values[5];
            values[12] += values[4];
            values[13] += values[5];
            values[4] = (r0 - r1) * cPi2Over8;
            values[5] = (r0 + r1) * cPi2Over8;

            r0 = values[14] - values[6];
            r1 = values[15] - values[7];
            values[14] += values[6];
            values[15] += values[7];
            values[6] = r0;
            values[7] = r1;
        }

        ApplyVorbisMdctButterfly8(data, offset);
        ApplyVorbisMdctButterfly8(data, offset + 8);
    }

    private static unsafe void ApplyVorbisMdctButterfly8(Span<float> data, int offset)
    {
        fixed (float* dataPtr = data)
        {
            var values = dataPtr + offset;
            var r0 = values[6] + values[2];
            var r1 = values[6] - values[2];
            var r2 = values[4] + values[0];
            var r3 = values[4] - values[0];

            values[6] = r0 + r2;
            values[4] = r0 - r2;

            r0 = values[5] - values[1];
            r2 = values[7] - values[3];
            values[0] = r1 + r0;
            values[2] = r1 - r0;

            r0 = values[5] + values[1];
            r1 = values[7] + values[3];
            values[3] = r2 + r3;
            values[1] = r2 - r3;
            values[7] = r1 + r0;
            values[5] = r1 - r0;
        }
    }

    private static OggVorbisFloorCurveInfo MaterializeFloorCurve(
        OggVorbisFloorInfo floor,
        int[] packetYValues,
        int decodedValues,
        int range,
        float[]? destination = null,
        int destinationOffset = 0,
        int destinationLength = 0,
        bool includeHash = true)
    {
        var valueCount = Math.Min(decodedValues, floor.XList.Length);
        if (valueCount < 2)
        {
            return new OggVorbisFloorCurveInfo(0, 0);
        }

        var points = valueCount <= 128 ? ArrayPool<OggVorbisFloorPoint>.Shared.Rent(valueCount) : new OggVorbisFloorPoint[valueCount];
        var ownsCurve = destination is null;
        var curve = ownsCurve ? ArrayPool<float>.Shared.Rent(Math.Max(1, floor.XList[1])) : destination!;
        try
        {
            for (var i = 0; i < valueCount; i++)
            {
                points[i] = new OggVorbisFloorPoint(floor.XList[i], packetYValues[i] & FloorValueMask, i);
            }

            for (var i = 2; i < valueCount; i++)
            {
                var lowNeighbor = LowNeighbor(floor.XList, i);
                var highNeighbor = HighNeighbor(floor.XList, i);
                var predicted = RenderPoint(
                    floor.XList[lowNeighbor],
                    floor.XList[highNeighbor],
                    packetYValues[lowNeighbor] & FloorValueMask,
                    packetYValues[highNeighbor] & FloorValueMask,
                    floor.XList[i]);
                var val = packetYValues[i] & FloorValueMask;
                var highRoom = range - predicted;
                var lowRoom = predicted;
                var room = Math.Min(highRoom, lowRoom) << 1;
                if (val != 0)
                {
                    packetYValues[lowNeighbor] |= FloorStepFlag;
                    packetYValues[highNeighbor] |= FloorStepFlag;
                    packetYValues[i] = (val >= room)
                        ? (highRoom > lowRoom ? val - lowRoom + predicted : predicted - val + highRoom - 1)
                        : ((val & 1) != 0 ? predicted - ((val + 1) >> 1) : predicted + (val >> 1));
                    packetYValues[i] |= FloorStepFlag;
                }
                else
                {
                    packetYValues[i] = predicted;
                }

                points[i] = new OggVorbisFloorPoint(floor.XList[i], packetYValues[i] & FloorValueMask, i);
            }

            packetYValues[0] |= FloorStepFlag;
            packetYValues[1] |= FloorStepFlag;

            SortFloorPoints(points, valueCount);
            var curveLength = ownsCurve
                ? Math.Max(1, points[valueCount - 1].X)
                : Math.Min(destinationLength, Math.Max(1, points[valueCount - 1].X));
            if (curveLength <= 0)
            {
                return new OggVorbisFloorCurveInfo(0, 0);
            }

            var curveSpan = curve.AsSpan(destinationOffset, curveLength);
            curveSpan.Clear();
            var hx = 0;
            var hy = points[0].Y * floor.Multiplier;
            var materialized = 0;
            for (var i = 1; i < valueCount; i++)
            {
                var point = points[i];
                if ((packetYValues[point.Index] & FloorStepFlag) == 0)
                {
                    continue;
                }

                var clampedX = Math.Min(point.X, curveLength);
                var pointY = point.Y * floor.Multiplier;
                RenderFloorLine(hx, hy, clampedX, pointY, curveSpan);
                materialized += Math.Max(0, clampedX - hx);
                hx = clampedX;
                hy = pointY;
            }

            if (hx < curveLength)
            {
                var gain = FloorAmplitude(hy);
                for (var i = hx; i < curveLength; i++)
                {
                    curveSpan[i] = gain;
                }

                materialized += curveLength - hx;
            }

            return new OggVorbisFloorCurveInfo(materialized, includeHash ? HashFloatValues(curveSpan) : 0);
        }
        finally
        {
            if (valueCount <= 128)
            {
                ArrayPool<OggVorbisFloorPoint>.Shared.Return(points);
            }

            if (ownsCurve)
            {
                ArrayPool<float>.Shared.Return(curve);
            }
        }
    }

    private static int LowNeighbor(int[] xList, int index)
    {
        var neighbor = 0;
        for (var i = 1; i < index; i++)
        {
            if (xList[i] < xList[index] && xList[i] > xList[neighbor])
            {
                neighbor = i;
            }
        }

        return neighbor;
    }

    private static int HighNeighbor(int[] xList, int index)
    {
        var neighbor = 1;
        for (var i = 0; i < index; i++)
        {
            if (xList[i] > xList[index] && xList[i] < xList[neighbor])
            {
                neighbor = i;
            }
        }

        return neighbor;
    }

    private static int RenderPoint(int x0, int x1, int y0, int y1, int x)
    {
        var dy = y1 - y0;
        var adx = x1 - x0;
        var ady = Math.Abs(dy);
        var err = ady * (x - x0);
        var off = err / adx;
        return dy < 0 ? y0 - off : y0 + off;
    }

    private static void SortFloorPoints(OggVorbisFloorPoint[] points, int count)
    {
        for (var i = 1; i < count; i++)
        {
            var point = points[i];
            var j = i - 1;
            while (j >= 0 && points[j].X > point.X)
            {
                points[j + 1] = points[j];
                j--;
            }

            points[j + 1] = point;
        }
    }

    private static void RenderFloorLine(int x0, int y0, int x1, int y1, Span<float> curve)
    {
        if (x1 <= x0)
        {
            return;
        }

        var dy = y1 - y0;
        var adx = x1 - x0;
        var ady = Math.Abs(dy);
        var baseValue = dy / adx;
        var sy = dy < 0 ? baseValue - 1 : baseValue + 1;
        var x = x0;
        var y = y0;
        var err = 0;
        ady -= Math.Abs(baseValue) * adx;

        while (x < x1 && x < curve.Length)
        {
            curve[x] = FloorAmplitude(y);
            err += ady;
            if (err >= adx)
            {
                err -= adx;
                y += sy;
            }
            else
            {
                y += baseValue;
            }

            x++;
        }
    }

    private static float FloorAmplitude(int y)
    {
        var clamped = Math.Clamp(y, 0, 255);
        return FloorInverseDbTable[clamped];
    }

    private static void DecodeResidueClassword(
        int classword,
        int classifications,
        int dimensions,
        int[] destination,
        int offset,
        int available)
    {
        var count = Math.Min(dimensions, available);
        for (var i = dimensions - 1; i >= 0; i--)
        {
            if (i < count)
            {
                destination[offset + i] = classword % classifications;
            }

            classword /= classifications;
        }
    }

    private static int GetResidueEntryOffset(OggVorbisCodebookInfo book, int entry)
    {
        var entryOffsets = book.LookupEntryOffsets;
        if (entryOffsets.Length == 0)
        {
            return -1;
        }

        if ((uint)entry >= (uint)entryOffsets.Length)
        {
            throw new InvalidDataException($"Vorbis codebook entry is outside lookup vectors: {entry}.");
        }

        return entryOffsets[entry];
    }

    private static void AccumulateResidueType0Fast(
        float[] residueValues,
        int channelBaseOffset,
        int residueBegin,
        int partition,
        int vectorIndex,
        int partitionSize,
        OggVorbisCodebookInfo book,
        int entryOffset)
    {
        var lookupVectors = book.LookupVectors;
        var dimensions = book.Dimensions;
        var stride = partitionSize / dimensions;
        var baseOffset = channelBaseOffset + residueBegin + partition * partitionSize + vectorIndex;
        for (var dimension = 0; dimension < dimensions; dimension++)
        {
            residueValues[baseOffset + dimension * stride] += lookupVectors[entryOffset + dimension];
        }
    }

    private static void AccumulateResidueType1Fast(
        float[] residueValues,
        int channelBaseOffset,
        int residueBegin,
        int partition,
        int vectorIndex,
        int partitionSize,
        OggVorbisCodebookInfo book,
        int entryOffset)
    {
        var lookupVectors = book.LookupVectors;
        var dimensions = book.Dimensions;
        var baseOffset = channelBaseOffset + residueBegin + partition * partitionSize + vectorIndex * dimensions;
        AddFloatSpanInPlace(
            residueValues.AsSpan(baseOffset, dimensions),
            lookupVectors.AsSpan(entryOffset, dimensions));
    }

    private static void AccumulateResidueType2Fast(
        float[] residueValues,
        ReadOnlySpan<int> channelBaseOffsets,
        int residueBegin,
        int partition,
        int vectorIndex,
        int partitionSize,
        OggVorbisCodebookInfo book,
        int entryOffset)
    {
        var lookupVectors = book.LookupVectors;
        var dimensions = book.Dimensions;
        var channelCount = channelBaseOffsets.Length;
        var scalarOffset = residueBegin + partition * partitionSize + vectorIndex * dimensions;

        if (channelCount == 1)
        {
            AddFloatSpanInPlace(
                residueValues.AsSpan(channelBaseOffsets[0] + scalarOffset, dimensions),
                lookupVectors.AsSpan(entryOffset, dimensions));
            return;
        }

        if (channelCount == 2)
        {
            var leftBase = channelBaseOffsets[0];
            var rightBase = channelBaseOffsets[1];
            var sample = scalarOffset >> 1;

            if (dimensions == 2)
            {
                if ((scalarOffset & 1) == 0)
                {
                    residueValues[leftBase + sample] += lookupVectors[entryOffset];
                    residueValues[rightBase + sample] += lookupVectors[entryOffset + 1];
                }
                else
                {
                    residueValues[rightBase + sample] += lookupVectors[entryOffset];
                    residueValues[leftBase + sample + 1] += lookupVectors[entryOffset + 1];
                }

                return;
            }

            if (dimensions == 4)
            {
                if ((scalarOffset & 1) == 0)
                {
                    residueValues[leftBase + sample] += lookupVectors[entryOffset];
                    residueValues[rightBase + sample] += lookupVectors[entryOffset + 1];
                    residueValues[leftBase + sample + 1] += lookupVectors[entryOffset + 2];
                    residueValues[rightBase + sample + 1] += lookupVectors[entryOffset + 3];
                }
                else
                {
                    residueValues[rightBase + sample] += lookupVectors[entryOffset];
                    residueValues[leftBase + sample + 1] += lookupVectors[entryOffset + 1];
                    residueValues[rightBase + sample + 1] += lookupVectors[entryOffset + 2];
                    residueValues[leftBase + sample + 2] += lookupVectors[entryOffset + 3];
                }

                return;
            }

            var lookupIndex = entryOffset;
            var entryEnd = entryOffset + dimensions;

            if ((scalarOffset & 1) != 0)
            {
                residueValues[rightBase + sample] += lookupVectors[lookupIndex];
                lookupIndex++;
                sample++;
            }

            while (lookupIndex + 1 < entryEnd)
            {
                residueValues[leftBase + sample] += lookupVectors[lookupIndex];
                residueValues[rightBase + sample] += lookupVectors[lookupIndex + 1];
                lookupIndex += 2;
                sample++;
            }

            if (lookupIndex < entryEnd)
            {
                residueValues[leftBase + sample] += lookupVectors[lookupIndex];
            }

            return;
        }

        var channelSlot = scalarOffset % channelCount;
        var channelSample = scalarOffset / channelCount;
        for (var dimension = 0; dimension < dimensions; dimension++)
        {
            residueValues[channelBaseOffsets[channelSlot] + channelSample] += lookupVectors[entryOffset + dimension];
            channelSlot++;
            if (channelSlot == channelCount)
            {
                channelSlot = 0;
                channelSample++;
            }
        }
    }

    private static int AccumulateResidueVector(
        float[] residueValues,
        int residueValueCount,
        int residueType,
        int residueBegin,
        int partition,
        int vectorIndex,
        int partitionSize,
        int blockSizeHalf,
        ReadOnlySpan<int> channels,
        int channelIndex,
        OggVorbisCodebookInfo book,
        int entry)
    {
        var entryOffsets = book.LookupEntryOffsets;
        if (entryOffsets.Length == 0)
        {
            return 0;
        }

        if ((uint)entry >= (uint)entryOffsets.Length)
        {
            throw new InvalidDataException($"Vorbis codebook entry is outside lookup vectors: {entry}.");
        }
        var entryOffset = entryOffsets[entry];

        var written = 0;
        if (residueType == 0)
        {
            var channel = channels[channelIndex];
            var baseOffset = channel * blockSizeHalf + residueBegin + partition * partitionSize + vectorIndex;
            for (var dimension = 0; dimension < book.Dimensions; dimension++)
            {
                var offset = baseOffset + dimension * (partitionSize / book.Dimensions);
                if ((uint)offset < (uint)residueValueCount)
                {
                    residueValues[offset] += book.LookupVectors[entryOffset + dimension];
                    written++;
                }
            }

            return written;
        }

        if (residueType == 1)
        {
            var channel = channels[channelIndex];
            var baseOffset = channel * blockSizeHalf + residueBegin + partition * partitionSize + vectorIndex * book.Dimensions;
            for (var dimension = 0; dimension < book.Dimensions; dimension++)
            {
                var offset = baseOffset + dimension;
                if ((uint)offset < (uint)residueValueCount)
                {
                    residueValues[offset] += book.LookupVectors[entryOffset + dimension];
                    written++;
                }
            }

            return written;
        }

        if (residueType == 2)
        {
            var scalarOffset = residueBegin + partition * partitionSize + vectorIndex * book.Dimensions;
            for (var dimension = 0; dimension < book.Dimensions; dimension++)
            {
                var scalar = scalarOffset + dimension;
                var channelSlot = scalar % channels.Length;
                var sample = scalar / channels.Length;
                var channel = channels[channelSlot];
                var offset = channel * blockSizeHalf + sample;
                if ((uint)offset < (uint)residueValueCount)
                {
                    residueValues[offset] += book.LookupVectors[entryOffset + dimension];
                    written++;
                }
            }

            return written;
        }

        throw new InvalidDataException($"Unsupported Vorbis residue type: {residueType}.");
    }

    private static int[] BuildCodebookLookupEntryOffsets(int entries, int dimensions, int lookupVectorLength)
    {
        if (entries <= 0 || dimensions <= 0 || lookupVectorLength <= 0)
        {
            return Array.Empty<int>();
        }

        var entryOffsets = new int[entries];
        for (var entry = 0; entry < entries; entry++)
        {
            var entryOffset = checked(entry * dimensions);
            if (entryOffset < 0 || entryOffset + dimensions > lookupVectorLength)
            {
                throw new InvalidDataException($"Vorbis codebook lookup entry is outside lookup vectors: {entry}.");
            }

            entryOffsets[entry] = entryOffset;
        }

        return entryOffsets;
    }

    private static int ReadCodebookScalar(ref VorbisBitReader reader, OggVorbisCodebookInfo codebook, OggVorbisProfileTotals? profile = null)
    {
        var fastLookupBitCount = codebook.FastLookupBitCount;
        if (fastLookupBitCount > 0)
        {
            if (reader.RemainingBits >= fastLookupBitCount)
            {
                var prefix = (int)reader.PeekBits(fastLookupBitCount);
                var fastLength = codebook.FastLookupLengths[prefix];
                if (fastLength > 0)
                {
                    if (profile is not null)
                    {
                        profile.CodebookFastPathHits++;
                    }

                    reader.AdvanceBits(fastLength);
                    return codebook.FastLookupEntries[prefix];
                }

                if (profile is not null)
                {
                    profile.CodebookFastPathMisses++;
                }
            }
            else if (profile is not null)
            {
                profile.CodebookFastPathShortReads++;
            }
        }

        if (profile is not null)
        {
            profile.CodebookTreeDecodes++;
        }

        var node = 0;
        var decodeZero = codebook.DecodeZero;
        var decodeOne = codebook.DecodeOne;
        var decodeEntries = codebook.DecodeEntries;
        for (var bits = 0; bits < codebook.MaxCodewordLength; bits++)
        {
            node = reader.ReadBit() == 0 ? decodeZero[node] : decodeOne[node];
            if (node < 0)
            {
                throw new InvalidDataException("Vorbis codebook scalar reached an invalid Huffman node.");
            }

            var entry = decodeEntries[node];
            if (entry >= 0)
            {
                if (profile is not null)
                {
                    profile.CodebookTreeBits += bits + 1L;
                }

                return entry;
            }
        }

        throw new InvalidDataException("Vorbis codebook scalar did not match any codeword.");
    }

    private static OggVorbisCodebookFastLookup BuildCodebookFastLookup(
        int[] decodeZero,
        int[] decodeOne,
        int[] decodeEntries,
        int maxCodewordLength)
    {
        if (maxCodewordLength <= 0)
        {
            return new OggVorbisCodebookFastLookup(0, Array.Empty<byte>(), Array.Empty<int>());
        }

        var bitCount = Math.Min(CodebookFastLookupBits, maxCodewordLength);
        var tableLength = 1 << bitCount;
        var lengths = new byte[tableLength];
        var entries = new int[tableLength];

        FillCodebookFastLookup(0, 0, 0, bitCount, decodeZero, decodeOne, decodeEntries, lengths, entries);
        return new OggVorbisCodebookFastLookup(bitCount, lengths, entries);
    }

    private static void FillCodebookFastLookup(
        int node,
        int depth,
        int prefix,
        int bitCount,
        int[] decodeZero,
        int[] decodeOne,
        int[] decodeEntries,
        byte[] lengths,
        int[] entries)
    {
        var entry = decodeEntries[node];
        if (entry >= 0)
        {
            var suffixCount = 1 << (bitCount - depth);
            for (var suffix = 0; suffix < suffixCount; suffix++)
            {
                var index = prefix | (suffix << depth);
                lengths[index] = checked((byte)depth);
                entries[index] = entry;
            }

            return;
        }

        if (depth >= bitCount)
        {
            return;
        }

        var zeroNode = decodeZero[node];
        if (zeroNode >= 0)
        {
            FillCodebookFastLookup(zeroNode, depth + 1, prefix, bitCount, decodeZero, decodeOne, decodeEntries, lengths, entries);
        }

        var oneNode = decodeOne[node];
        if (oneNode >= 0)
        {
            FillCodebookFastLookup(oneNode, depth + 1, prefix | (1 << depth), bitCount, decodeZero, decodeOne, decodeEntries, lengths, entries);
        }
    }

    private static OggVorbisCodebookTree BuildCodebookHuffmanTree(byte[] lengths)
    {
        var maxLength = 0;
        var activeEntries = 0;
        var singleEntry = -1;
        for (var i = 0; i < lengths.Length; i++)
        {
            if (lengths[i] == 0)
            {
                continue;
            }

            maxLength = Math.Max(maxLength, lengths[i]);
            activeEntries++;
            singleEntry = i;
        }

        if (activeEntries == 0)
        {
            throw new InvalidDataException("Vorbis codebook has no active entries.");
        }

        if (activeEntries == 1)
        {
            var zero = new[] { 1, -1 };
            var one = new[] { 1, -1 };
            var entries = new[] { -1, singleEntry };
            return new OggVorbisCodebookTree(zero, one, entries, Math.Max(1, maxLength));
        }

        var zeroChildren = new List<int>(checked(activeEntries * maxLength + 1)) { -1 };
        var oneChildren = new List<int>(checked(activeEntries * maxLength + 1)) { -1 };
        var decodeEntries = new List<int>(checked(activeEntries * maxLength + 1)) { -1 };
        for (var entry = 0; entry < lengths.Length; entry++)
        {
            var length = lengths[entry];
            if (length == 0)
            {
                continue;
            }

            if (!TryAssignCodeword(zeroChildren, oneChildren, decodeEntries, 0, length, entry))
            {
                throw new InvalidDataException("Vorbis codebook is overspecified or cannot be represented as a prefix tree.");
            }
        }

        return new OggVorbisCodebookTree(zeroChildren.ToArray(), oneChildren.ToArray(), decodeEntries.ToArray(), maxLength);
    }

    private static float[] BuildCodebookLookupVectors(
        int lookupType,
        int dimensions,
        int entries,
        int lookupValues,
        float minimumValue,
        float deltaValue,
        bool sequenceP,
        int[] multiplicands)
    {
        var vectors = new float[checked(entries * dimensions)];
        if (lookupType == 1)
        {
            for (var entry = 0; entry < entries; entry++)
            {
                var last = 0.0f;
                long indexDivisor = 1;
                for (var dimension = 0; dimension < dimensions; dimension++)
                {
                    var multiplicandOffset = (int)(((long)entry / indexDivisor) % lookupValues);
                    var value = multiplicands[multiplicandOffset] * deltaValue + minimumValue + last;
                    vectors[entry * dimensions + dimension] = value;
                    if (sequenceP)
                    {
                        last = value;
                    }

                    indexDivisor *= lookupValues;
                }
            }

            return vectors;
        }

        if (lookupType == 2)
        {
            for (var entry = 0; entry < entries; entry++)
            {
                var last = 0.0f;
                for (var dimension = 0; dimension < dimensions; dimension++)
                {
                    var multiplicandOffset = entry * dimensions + dimension;
                    var value = multiplicands[multiplicandOffset] * deltaValue + minimumValue + last;
                    vectors[entry * dimensions + dimension] = value;
                    if (sequenceP)
                    {
                        last = value;
                    }
                }
            }

            return vectors;
        }

        throw new InvalidDataException($"Unsupported Vorbis codebook lookup type: {lookupType}.");
    }

    private static float UnpackVorbisFloat32(uint value)
    {
        double mantissa = value & 0x1FFFFF;
        if ((value & 0x80000000) != 0)
        {
            mantissa = -mantissa;
        }

        var exponent = (int)((value & 0x7FE00000) >> 21) - 788;
        exponent = Math.Clamp(exponent, -63, 63);
        return (float)Math.ScaleB(mantissa, exponent);
    }

    private static bool TryAssignCodeword(List<int> zero, List<int> one, List<int> entries, int node, int remainingDepth, int entry)
    {
        if (entries[node] >= 0)
        {
            return false;
        }

        if (remainingDepth == 0)
        {
            if (zero[node] >= 0 || one[node] >= 0)
            {
                return false;
            }

            entries[node] = entry;
            return true;
        }

        return TryAssignCodewordBranch(zero, one, entries, node, remainingDepth, entry, zeroBranch: true)
            || TryAssignCodewordBranch(zero, one, entries, node, remainingDepth, entry, zeroBranch: false);
    }

    private static bool TryAssignCodewordBranch(
        List<int> zero,
        List<int> one,
        List<int> entries,
        int node,
        int remainingDepth,
        int entry,
        bool zeroBranch)
    {
        var children = zeroBranch ? zero : one;
        var next = children[node];
        if (next < 0)
        {
            next = entries.Count;
            children[node] = next;
            zero.Add(-1);
            one.Add(-1);
            entries.Add(-1);
        }

        return TryAssignCodeword(zero, one, entries, next, remainingDepth - 1, entry);
    }

    private static void ValidateVorbisHeader(ReadOnlySpan<byte> packet, byte expectedType, string name)
    {
        if (packet.Length < 7 || packet[0] != expectedType || !packet[1..7].SequenceEqual(VorbisSignature))
        {
            throw new InvalidDataException($"Invalid Vorbis {name} packet.");
        }
    }

    private static string ReadVorbisString(ReadOnlySpan<byte> packet, ref int offset)
    {
        var byteCount = ReadUInt32AsInt(packet, ref offset, "string length");
        if ((uint)byteCount > (uint)(packet.Length - offset))
        {
            throw new InvalidDataException("Vorbis string extends past the packet boundary.");
        }

        var value = Encoding.UTF8.GetString(packet.Slice(offset, byteCount));
        offset += byteCount;
        return value;
    }

    private static int ReadUInt32AsInt(ReadOnlySpan<byte> packet, ref int offset, string fieldName)
    {
        if (packet.Length - offset < sizeof(uint))
        {
            throw new InvalidDataException($"Vorbis {fieldName} is truncated.");
        }

        var value = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(offset, sizeof(uint)));
        offset += sizeof(uint);
        if (value > int.MaxValue)
        {
            throw new InvalidDataException($"Vorbis {fieldName} is too large: {value}.");
        }

        return (int)value;
    }

    private static void ReadPackets(string path, OggPageConsumer pageConsumer, OggPacketConsumer packetConsumer)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
        var segmentTable = ArrayPool<byte>.Shared.Rent(255);
        var pagePayload = ArrayPool<byte>.Shared.Rent(64 * 1024);
        using var packetBuffer = new MemoryStream(64 * 1024);

        try
        {
            Span<byte> header = stackalloc byte[27];
            var packetIndex = 0;

            while (TryReadExact(stream, header))
            {
                if (header[0] != (byte)'O' || header[1] != (byte)'g' || header[2] != (byte)'g' || header[3] != (byte)'S')
                {
                    throw new InvalidDataException($"Invalid Ogg capture pattern at stream offset {stream.Position - header.Length}.");
                }

                if (header[4] != 0)
                {
                    throw new InvalidDataException($"Unsupported Ogg bitstream version: {header[4]}.");
                }

                var headerType = header[5];
                var granulePosition = BinaryPrimitives.ReadInt64LittleEndian(header[6..14]);
                var serialNumber = BinaryPrimitives.ReadUInt32LittleEndian(header[14..18]);
                var pageSequence = BinaryPrimitives.ReadUInt32LittleEndian(header[18..22]);
                var pageSegmentCount = header[26];
                var segmentSpan = segmentTable.AsSpan(0, pageSegmentCount);
                stream.ReadExactly(segmentSpan);

                var payloadBytes = 0;
                for (var i = 0; i < segmentSpan.Length; i++)
                {
                    payloadBytes += segmentSpan[i];
                }

                if (payloadBytes > pagePayload.Length)
                {
                    ArrayPool<byte>.Shared.Return(pagePayload);
                    pagePayload = ArrayPool<byte>.Shared.Rent(payloadBytes);
                }

                var payloadSpan = pagePayload.AsSpan(0, payloadBytes);
                stream.ReadExactly(payloadSpan);
                pageConsumer(new OggPageInfo(headerType, granulePosition, serialNumber, pageSequence, payloadBytes));

                var lastCompletedPacketSegment = -1;
                for (var i = 0; i < segmentSpan.Length; i++)
                {
                    if (segmentSpan[i] < 255)
                    {
                        lastCompletedPacketSegment = i;
                    }
                }

                var payloadOffset = 0;
                for (var i = 0; i < segmentSpan.Length; i++)
                {
                    var segmentBytes = segmentSpan[i];
                    if (segmentBytes > 0)
                    {
                        packetBuffer.Write(payloadSpan.Slice(payloadOffset, segmentBytes));
                        payloadOffset += segmentBytes;
                    }

                    if (segmentBytes < 255)
                    {
                        var packet = packetBuffer.GetBuffer().AsSpan(0, checked((int)packetBuffer.Length));
                        var packetGranulePosition = i == lastCompletedPacketSegment ? granulePosition : -1;
                        if (!packetConsumer(packet, packetIndex, packetGranulePosition))
                        {
                            return;
                        }

                        packetIndex++;
                        packetBuffer.SetLength(0);
                    }
                }
            }

            if (packetBuffer.Length != 0)
            {
                throw new InvalidDataException("Ogg stream ended inside a continued packet.");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(segmentTable);
            ArrayPool<byte>.Shared.Return(pagePayload);
        }
    }

    private static long ReadLastGranulePosition(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.RandomAccess);
        var length = stream.Length;
        if (length <= 0)
        {
            return 0;
        }

        var tailBytes = (int)Math.Min(length, 256 * 1024L);
        var tailBuffer = ArrayPool<byte>.Shared.Rent(tailBytes);
        try
        {
            stream.Position = length - tailBytes;
            stream.ReadExactly(tailBuffer.AsSpan(0, tailBytes));

            long lastGranulePosition = 0;
            for (var offset = 0; offset <= tailBytes - 27; offset++)
            {
                var span = tailBuffer.AsSpan(offset, tailBytes - offset);
                if (span[0] != (byte)'O' || span[1] != (byte)'g' || span[2] != (byte)'g' || span[3] != (byte)'S')
                {
                    continue;
                }

                if (span[4] != 0)
                {
                    continue;
                }

                var pageSegmentCount = span[26];
                if (span.Length < 27 + pageSegmentCount)
                {
                    continue;
                }

                var payloadBytes = 0;
                var segmentSpan = span.Slice(27, pageSegmentCount);
                for (var i = 0; i < segmentSpan.Length; i++)
                {
                    payloadBytes += segmentSpan[i];
                }

                if (span.Length < 27 + pageSegmentCount + payloadBytes)
                {
                    continue;
                }

                var granulePosition = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(6, 8));
                if (granulePosition >= 0)
                {
                    lastGranulePosition = granulePosition;
                }
            }

            return lastGranulePosition;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(tailBuffer);
        }
    }

    private static bool TryReadExact(Stream stream, Span<byte> destination)
    {
        var offset = 0;
        while (offset < destination.Length)
        {
            var read = stream.Read(destination[offset..]);
            if (read == 0)
            {
                if (offset == 0)
                {
                    return false;
                }

                throw new EndOfStreamException();
            }

            offset += read;
        }

        return true;
    }

    private static long HashPacket(long hash, ReadOnlySpan<byte> packet)
    {
        unchecked
        {
            var value = hash == 0 ? 1469598103934665603L : hash;
            for (var i = 0; i < packet.Length; i++)
            {
                value ^= packet[i];
                value *= 1099511628211L;
            }

            value ^= packet.Length;
            value *= 1099511628211L;
            return value;
        }
    }

    private static long HashFloatValues(ReadOnlySpan<float> values)
    {
        unchecked
        {
            var hash = 1469598103934665603L;
            for (var i = 0; i < values.Length; i++)
            {
                var bits = BitConverter.SingleToInt32Bits(values[i]);
                hash ^= bits;
                hash *= 1099511628211L;
            }

            return hash;
        }
    }

    private static long HashInt32Values(ReadOnlySpan<int> values)
    {
        unchecked
        {
            var hash = 1469598103934665603L;
            for (var i = 0; i < values.Length; i++)
            {
                hash ^= values[i];
                hash *= 1099511628211L;
            }

            return hash;
        }
    }

    private static long HashCombine(long hash, long value)
    {
        unchecked
        {
            var result = hash == 0 ? 1469598103934665603L : hash;
            result ^= value;
            result *= 1099511628211L;
            return result;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long HashFloatSample(long hash, float sample)
    {
        unchecked
        {
            return HashFloatSampleSeeded(hash == 0 ? FnvOffsetBasis : hash, sample);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long HashInt32Sample(long hash, int sample)
    {
        unchecked
        {
            return HashInt32SampleSeeded(hash == 0 ? FnvOffsetBasis : hash, sample);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long HashFloatSampleSeeded(long hash, float sample)
    {
        unchecked
        {
            hash ^= BitConverter.SingleToInt32Bits(sample);
            hash *= FnvPrime;
            return hash;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long HashInt32SampleSeeded(long hash, int sample)
    {
        unchecked
        {
            hash ^= sample;
            hash *= FnvPrime;
            return hash;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<int> ConvertFloat32ToPcm32Vector256(Vector256<float> values)
    {
        var one = Vector256.Create(1f);
        var negOne = Vector256.Create(-1f);
        var scalePos = Vector256.Create(2147483647f);
        var scaleNeg = Vector256.Create(2147483648f);
        var clamped = Avx.Max(Avx.Min(values, one), negOne);
        var signMask = Avx2.ShiftRightArithmetic(clamped.AsInt32(), 31).AsSingle();
        var scale = Avx.BlendVariable(scalePos, scaleNeg, signMask);
        return Avx.ConvertToVector256Int32(Avx.Multiply(clamped, scale));
    }

    private static unsafe void MultiplyFloatSpans(ReadOnlySpan<float> left, ReadOnlySpan<float> right, Span<float> destination)
    {
        if (left.Length != right.Length || left.Length != destination.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Vorbis multiply spans must have the same length.");
        }

        if (!Avx.IsSupported || left.Length < 8)
        {
            for (var i = 0; i < left.Length; i++)
            {
                destination[i] = left[i] * right[i];
            }

            return;
        }

        var simdCount = left.Length - (left.Length % 8);
        fixed (float* leftPtr = left)
        fixed (float* rightPtr = right)
        fixed (float* destinationPtr = destination)
        {
            var i = 0;
            for (; i < simdCount; i += 8)
            {
                var product = Avx.Multiply(
                    Avx.LoadVector256(leftPtr + i),
                    Avx.LoadVector256(rightPtr + i));
                Avx.Store(destinationPtr + i, product);
            }

            for (; i < left.Length; i++)
            {
                destinationPtr[i] = leftPtr[i] * rightPtr[i];
            }
        }
    }

    private static unsafe void MultiplyFloatSpanInPlace(Span<float> values, ReadOnlySpan<float> multipliers)
    {
        if (values.Length != multipliers.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(multipliers), "Vorbis multiply spans must have the same length.");
        }

        if (!Avx.IsSupported || values.Length < 8)
        {
            for (var i = 0; i < values.Length; i++)
            {
                values[i] *= multipliers[i];
            }

            return;
        }

        var simdCount = values.Length - (values.Length % 8);
        fixed (float* valuesPtr = values)
        fixed (float* multipliersPtr = multipliers)
        {
            var i = 0;
            for (; i < simdCount; i += 8)
            {
                var product = Avx.Multiply(
                    Avx.LoadVector256(valuesPtr + i),
                    Avx.LoadVector256(multipliersPtr + i));
                Avx.Store(valuesPtr + i, product);
            }

            for (; i < values.Length; i++)
            {
                valuesPtr[i] *= multipliersPtr[i];
            }
        }
    }

    private static unsafe void AddFloatSpanInPlace(Span<float> values, ReadOnlySpan<float> addends)
    {
        if (values.Length != addends.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(addends), "Vorbis add spans must have the same length.");
        }

        if (!Avx.IsSupported || values.Length < 8)
        {
            for (var i = 0; i < values.Length; i++)
            {
                values[i] += addends[i];
            }

            return;
        }

        var simdCount = values.Length - (values.Length % 8);
        fixed (float* valuesPtr = values)
        fixed (float* addendsPtr = addends)
        {
            var i = 0;
            for (; i < simdCount; i += 8)
            {
                var sum = Avx.Add(
                    Avx.LoadVector256(valuesPtr + i),
                    Avx.LoadVector256(addendsPtr + i));
                Avx.Store(valuesPtr + i, sum);
            }

            for (; i < values.Length; i++)
            {
                valuesPtr[i] += addendsPtr[i];
            }
        }
    }

    private static unsafe void ScaleFloatSpanInPlace(Span<float> values, float scale)
    {
        if (!Avx.IsSupported || values.Length < 8)
        {
            for (var i = 0; i < values.Length; i++)
            {
                values[i] *= scale;
            }

            return;
        }

        var scaleVector = Vector256.Create(scale);
        var simdCount = values.Length - (values.Length % 8);
        fixed (float* valuesPtr = values)
        {
            var i = 0;
            for (; i < simdCount; i += 8)
            {
                var product = Avx.Multiply(
                    Avx.LoadVector256(valuesPtr + i),
                    scaleVector);
                Avx.Store(valuesPtr + i, product);
            }

            for (; i < values.Length; i++)
            {
                valuesPtr[i] *= scale;
            }
        }
    }

    private static float MaxAbs(ReadOnlySpan<float> values)
    {
        var max = 0f;
        for (var i = 0; i < values.Length; i++)
        {
            max = Math.Max(max, Math.Abs(values[i]));
        }

        return max;
    }

    private static int WindowLeft(int blockSize, int blockSizeSmall, bool previousWindowFlag)
    {
        if (blockSize == blockSizeSmall || previousWindowFlag)
        {
            return 0;
        }

        return (blockSize / 4) - (blockSizeSmall / 4);
    }

    private static int WindowRight(int blockSize, int blockSizeSmall, bool nextWindowFlag)
    {
        if (blockSize == blockSizeSmall || nextWindowFlag)
        {
            return blockSize;
        }

        return (blockSize * 3 / 4) + (blockSizeSmall / 4);
    }

    private static long HashModePacket(long hash, OggVorbisAudioPacketInfo packet)
    {
        unchecked
        {
            var value = hash == 0 ? 1469598103934665603L : hash;
            value ^= packet.ModeNumber;
            value *= 1099511628211L;
            value ^= packet.BlockFlag ? 0x101 : 0x202;
            value *= 1099511628211L;
            value ^= packet.PreviousWindowFlag ? 0x404 : 0x808;
            value *= 1099511628211L;
            value ^= packet.NextWindowFlag ? 0x1001 : 0x2002;
            value *= 1099511628211L;
            value ^= packet.Mapping;
            value *= 1099511628211L;
            value ^= packet.FloorPresentChannels;
            value *= 1099511628211L;
            value ^= packet.FloorAbsentChannels;
            value *= 1099511628211L;
            value ^= packet.DecodedFloorValues;
            value *= 1099511628211L;
            value ^= packet.MaterializedFloorValues;
            value *= 1099511628211L;
            value ^= packet.FloorCurveHash;
            value *= 1099511628211L;
            value ^= packet.DecodedResidueClasswords;
            value *= 1099511628211L;
            value ^= packet.DecodedResidueVectors;
            value *= 1099511628211L;
            value ^= packet.MaterializedResidueValues;
            value *= 1099511628211L;
            value ^= packet.ResidueValueHash;
            value *= 1099511628211L;
            return value;
        }
    }

    private static int ILog(int value)
    {
        var bits = 0;
        while (value > 0)
        {
            bits++;
            value >>= 1;
        }

        return bits;
    }

    private static long Lookup1Values(int entries, int dimensions)
    {
        var values = (long)Math.Floor(Math.Pow(entries, 1.0 / dimensions));
        while (PowLimited(values + 1, dimensions, entries) <= entries)
        {
            values++;
        }

        while (PowLimited(values, dimensions, entries) > entries)
        {
            values--;
        }

        return values;
    }

    private static long PowLimited(long value, int exponent, long limit)
    {
        long result = 1;
        for (var i = 0; i < exponent; i++)
        {
            if (value != 0 && result > limit / value)
            {
                return limit + 1;
            }

            result *= value;
        }

        return result;
    }

    private readonly record struct OggVorbisIdentificationHeader(
        int Channels,
        int SampleRate,
        int BitrateMaximum,
        int BitrateNominal,
        int BitrateMinimum,
        int BlockSizeSmall,
        int BlockSizeLarge);

    private readonly record struct OggVorbisCommentHeader(string Vendor, string[] Comments);

    private readonly record struct OggVorbisRenderDebugInfo(
        float ImdctMaxAbs,
        float WindowedMaxAbs,
        float OutputMaxAbs,
        int OutputFramesProduced,
        long OutputFrameStart,
        long OutputFrameEnd);

    private record struct OggStats
    {
        internal uint SerialNumber;
        internal int PageCount;
        internal int PacketCount;
        internal int AudioPacketCount;
        internal int MaxPacketBytes;
        internal int MaxPagePayloadBytes;
        internal int ContinuedPageCount;
        internal long LastGranulePosition;
        internal long PacketByteHash;
        internal int ParsedAudioPacketCount;
        internal int ShortBlockPackets;
        internal int LongBlockPackets;
        internal int PreviousWindowSetPackets;
        internal int NextWindowSetPackets;
        internal int[]? ModePacketCounts;
        internal long ModePacketHash;
        internal int FloorPresentChannels;
        internal int FloorAbsentChannels;
        internal int DecodedFloorValues;
        internal int MaterializedFloorValues;
        internal long FloorCurveHash;
        internal int DecodedResidueClasswords;
        internal int DecodedResidueVectors;
        internal int MaterializedResidueValues;
        internal long ResidueValueHash;
    }

    internal sealed class OggVorbisProfileTotals
    {
        internal int AudioPacketCount;
        internal long PacketHeaderTicks;
        internal long ResidueSetupTicks;
        internal long PacketTicks;
        internal long ResetTicks;
        internal long FloorTicks;
        internal long ResidueTicks;
        internal long CouplingTicks;
        internal long ApplyFloorTicks;
        internal long ImdctTicks;
        internal long ImdctButterfliesTicks;
        internal long ImdctBitReverseTicks;
        internal long WindowTicks;
        internal long OutputTicks;
        internal long OutputFastPathTicks;
        internal long OutputScalarTicks;
        internal int OutputFastPathPackets;
        internal int OutputScalarPackets;
        internal long ResidueType2Ticks;
        internal long ResidueOtherTicks;
        internal long ResidueType2WriteTicks;
        internal long ResidueOtherWriteTicks;
        internal long ResidueType2Vectors;
        internal long ResidueOtherVectors;
        internal long CodebookFastPathHits;
        internal long CodebookTreeDecodes;
        internal long CodebookFastPathMisses;
        internal long CodebookFastPathShortReads;
        internal long CodebookTreeBits;
    }

    internal enum OggVorbisOutputTarget
    {
        Float32,
        Pcm32
    }

    internal sealed class OggVorbisFloat32Context
    {
        private readonly int _channels;
        private readonly int _blockSizeSmall;
        private readonly int _blockSizeLarge;
        private readonly OggVorbisOutputTarget _outputTarget;
        private readonly OggVorbisSetupSummary _setup;
        private readonly float[] _smallWindow;
        private readonly float[] _largeWindow;
        private readonly OggImdctPlan _smallImdctPlan;
        private readonly OggImdctPlan _largeImdctPlan;
        private readonly float[] _floorCurves;
        private readonly float[] _residue;
        private readonly bool[] _noResidue;
        private readonly int[] _floorYValues;
        private readonly int[] _residueChannelBases;
        private readonly int[] _residueActiveChannelBases;
        private readonly float[] _imdct;
        private float[] _windowed;
        private float[] _previousWindowed;
        private float[]? _capturedSamples;
        private int[]? _capturedPcm32Samples;
        private OggReaderWorkspace? _captureWorkspace;
        private readonly OggVorbisProfileTotals? _profile;
        private int _capturedSampleCount;
        private long _samples;
        private long _sink;
        private bool _hasPrevious;
        private int _previousBlockSize;
        private bool _previousNextWindowFlag;

        internal OggVorbisFloat32Context(int channels, int blockSizeSmall, int blockSizeLarge, OggVorbisSetupSummary setup, OggVorbisOutputTarget outputTarget, int captureSampleLimit = 0, OggVorbisProfileTotals? profile = null)
        {
            _channels = channels;
            _blockSizeSmall = blockSizeSmall;
            _blockSizeLarge = blockSizeLarge;
            _outputTarget = outputTarget;
            _setup = setup;
            _smallWindow = BuildVorbisWindow(blockSizeSmall);
            _largeWindow = BuildVorbisWindow(blockSizeLarge);
            _smallImdctPlan = new OggImdctPlan(blockSizeSmall);
            _largeImdctPlan = new OggImdctPlan(blockSizeLarge);
            var maxFloorValueCount = 2;
            for (var i = 0; i < setup.Floors.Length; i++)
            {
                maxFloorValueCount = Math.Max(maxFloorValueCount, Math.Max(2, setup.Floors[i].ValueCount));
            }

            var channelBlockSamples = checked(channels * blockSizeLarge);
            var channelHalfSamples = checked(channels * (blockSizeLarge >> 1));
            _floorCurves = new float[channelHalfSamples];
            _residue = new float[channelHalfSamples];
            _noResidue = new bool[channels];
            _floorYValues = new int[maxFloorValueCount];
            _residueChannelBases = new int[channels];
            _residueActiveChannelBases = new int[channels];
            _imdct = new float[blockSizeLarge];
            _windowed = new float[channelBlockSamples];
            _previousWindowed = new float[channelBlockSamples];
            _capturedSamples = outputTarget == OggVorbisOutputTarget.Float32 && captureSampleLimit > 0 ? new float[captureSampleLimit] : null;
            _capturedPcm32Samples = outputTarget == OggVorbisOutputTarget.Pcm32 && captureSampleLimit > 0 ? new int[captureSampleLimit] : null;
            _profile = profile;
        }

        internal bool IsCaptureComplete =>
            (_outputTarget == OggVorbisOutputTarget.Float32
                && (_captureWorkspace is not null || _capturedSamples is not null)
                && _capturedSampleCount >= GetCapturedSamples().Length)
            || (_outputTarget == OggVorbisOutputTarget.Pcm32
                && (_captureWorkspace is not null || _capturedPcm32Samples is not null)
                && _capturedSampleCount >= GetCapturedPcm32Samples().Length);

        internal int CapturedSampleCount => _capturedSampleCount;

        internal bool IsCompatible(
            int channels,
            int blockSizeSmall,
            int blockSizeLarge,
            OggVorbisSetupSummary setup,
            OggVorbisOutputTarget outputTarget)
            => _channels == channels
                && _blockSizeSmall == blockSizeSmall
                && _blockSizeLarge == blockSizeLarge
                && _outputTarget == outputTarget
                && ReferenceEquals(_setup.Codebooks, setup.Codebooks)
                && ReferenceEquals(_setup.Floors, setup.Floors)
                && ReferenceEquals(_setup.Residues, setup.Residues)
                && ReferenceEquals(_setup.Mappings, setup.Mappings)
                && ReferenceEquals(_setup.Modes, setup.Modes);

        internal void BindCapturedSamples(float[] samples)
        {
            if (_outputTarget != OggVorbisOutputTarget.Float32)
            {
                throw new InvalidOperationException("OGG context output target is not Float32.");
            }

            _capturedSamples = samples ?? throw new ArgumentNullException(nameof(samples));
            _capturedPcm32Samples = null;
            _captureWorkspace = null;
        }

        internal void BindCapturedPcm32Samples(int[] samples)
        {
            if (_outputTarget != OggVorbisOutputTarget.Pcm32)
            {
                throw new InvalidOperationException("OGG context output target is not PCM32.");
            }

            _capturedPcm32Samples = samples ?? throw new ArgumentNullException(nameof(samples));
            _capturedSamples = null;
            _captureWorkspace = null;
        }

        internal void BindCapturedOutput(OggReaderWorkspace workspace)
        {
            _captureWorkspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _capturedSamples = null;
            _capturedPcm32Samples = null;
        }

        internal void ResetState()
        {
            _capturedSampleCount = 0;
            _samples = 0;
            _sink = 0;
            _hasPrevious = false;
            _previousBlockSize = 0;
            _previousNextWindowFlag = false;
            Array.Clear(_windowed);
            Array.Clear(_previousWindowed);
        }

        internal void ResetCapturedOutput()
        {
            _capturedSampleCount = 0;
        }

        private Span<float> GetCapturedSamples()
        {
            if (_captureWorkspace is not null)
            {
                return _outputTarget == OggVorbisOutputTarget.Float32
                    ? _captureWorkspace.FloatOutput
                    : Span<float>.Empty;
            }

            return _capturedSamples;
        }

        private Span<int> GetCapturedPcm32Samples()
        {
            if (_captureWorkspace is not null)
            {
                return _outputTarget == OggVorbisOutputTarget.Pcm32
                    ? _captureWorkspace.Pcm32SourceOutput
                    : Span<int>.Empty;
            }

            return _capturedPcm32Samples;
        }

        internal void DecodePacket(ReadOnlySpan<byte> packet, long granulePosition)
        {
            _ = DecodePacketCore(packet, granulePosition, -1, collectDebug: false);
        }

        internal OggVorbisPacketDebugInfo DecodePacketDebug(ReadOnlySpan<byte> packet, long granulePosition, int audioPacketIndex)
        {
            return DecodePacketCore(packet, granulePosition, audioPacketIndex, collectDebug: true);
        }

        private OggVorbisPacketDebugInfo DecodePacketCore(ReadOnlySpan<byte> packet, long granulePosition, int audioPacketIndex, bool collectDebug)
        {
            var profile = _profile;
            long packetStart = 0;
            if (profile is not null)
            {
                profile.AudioPacketCount++;
                packetStart = Stopwatch.GetTimestamp();
            }

            var reader = new VorbisBitReader(packet);
            if (reader.ReadBitsInt(1) != 0)
            {
                throw new InvalidDataException("Invalid Vorbis audio packet type bit.");
            }

            var modeBits = ILog(_setup.ModeCount - 1);
            var modeNumber = reader.ReadBitsInt(modeBits);
            if ((uint)modeNumber >= (uint)_setup.Modes.Length)
            {
                throw new InvalidDataException($"Vorbis audio packet references invalid mode: {modeNumber}.");
            }

            var mode = _setup.Modes[modeNumber];
            var previousWindowFlag = false;
            var nextWindowFlag = false;
            if (mode.BlockFlag)
            {
                previousWindowFlag = reader.ReadBitsInt(1) != 0;
                nextWindowFlag = reader.ReadBitsInt(1) != 0;
            }

            if (profile is not null)
            {
                profile.PacketHeaderTicks += Stopwatch.GetTimestamp() - packetStart;
            }

            var blockSize = mode.BlockFlag ? _blockSizeLarge : _blockSizeSmall;
            var blockSizeHalf = blockSize >> 1;
            var mapping = _setup.Mappings[mode.Mapping];
            var stageStart = profile is null ? 0 : Stopwatch.GetTimestamp();
            Array.Clear(_floorCurves, 0, _channels * blockSizeHalf);
            Array.Clear(_residue, 0, _channels * blockSizeHalf);
            if (profile is not null)
            {
                profile.ResetTicks += Stopwatch.GetTimestamp() - stageStart;
                stageStart = Stopwatch.GetTimestamp();
            }

            var noResidue = _noResidue;
            var floorPresentChannels = 0;
            var floorAbsentChannels = 0;
            for (var channel = 0; channel < _channels; channel++)
            {
                var submap = mapping.Mux.Length > channel ? mapping.Mux[channel] : 0;
                var floorIndex = mapping.SubmapFloors[submap];
                var floorPacket = ParseFloorPacket(
                    ref reader,
                    _setup.Floors[floorIndex],
                    _setup,
                    _floorCurves,
                    channel * blockSizeHalf,
                    blockSizeHalf,
                    collectStats: false,
                    yValueScratch: _floorYValues,
                    profile: profile);
                noResidue[channel] = !floorPacket.Present;
                if (floorPacket.Present)
                {
                    floorPresentChannels++;
                }
                else
                {
                    floorAbsentChannels++;
                }
            }

            if (profile is not null)
            {
                profile.FloorTicks += Stopwatch.GetTimestamp() - stageStart;
                stageStart = Stopwatch.GetTimestamp();
            }

            for (var i = 0; i < mapping.CouplingSteps; i++)
            {
                var magnitude = mapping.Magnitudes[i];
                var angle = mapping.Angles[i];
                if (!noResidue[magnitude] || !noResidue[angle])
                {
                    noResidue[magnitude] = false;
                    noResidue[angle] = false;
                }
            }

            if (profile is not null)
            {
                profile.ResidueSetupTicks += Stopwatch.GetTimestamp() - stageStart;
                stageStart = Stopwatch.GetTimestamp();
            }

            var residueMaxBookIndexUsed = -1;
            var residueLookupVectorMaxAbsUsed = 0f;

            for (var submap = 0; submap < mapping.Submaps; submap++)
            {
                var submapChannels = mapping.SubmapChannels[submap];
                if (submapChannels.Length == 0)
                {
                    continue;
                }

                var residueIndex = mapping.SubmapResidues[submap];
                if (collectDebug)
                {
                    var residuePacket = ConsumeResiduePacket(
                        ref reader,
                        _setup.Residues[residueIndex],
                        _setup,
                        noResidue,
                        submapChannels,
                        blockSizeHalf,
                        _residue,
                        collectStats: true,
                        _residueChannelBases,
                        _residueActiveChannelBases);
                    residueMaxBookIndexUsed = Math.Max(residueMaxBookIndexUsed, residuePacket.MaxBookIndexUsed);
                    residueLookupVectorMaxAbsUsed = Math.Max(residueLookupVectorMaxAbsUsed, residuePacket.LookupVectorMaxAbsUsed);
                }
                else
                {
                    ConsumeResiduePacketFast(
                        ref reader,
                        _setup.Residues[residueIndex],
                        _setup,
                        noResidue,
                        submapChannels,
                        blockSizeHalf,
                        _residue,
                        _residueChannelBases,
                        _residueActiveChannelBases,
                        profile);
                }
            }

            if (profile is not null)
            {
                profile.ResidueTicks += Stopwatch.GetTimestamp() - stageStart;
                stageStart = Stopwatch.GetTimestamp();
            }

            var activeSampleCount = _channels * blockSizeHalf;
            var floorMaxAbs = collectDebug ? MaxAbs(_floorCurves.AsSpan(0, activeSampleCount)) : 0f;
            var residueMaxAbsBeforeCoupling = collectDebug ? MaxAbs(_residue.AsSpan(0, activeSampleCount)) : 0f;
            ApplyInverseCoupling(_residue, mapping, blockSizeHalf);
            if (profile is not null)
            {
                profile.CouplingTicks += Stopwatch.GetTimestamp() - stageStart;
                stageStart = Stopwatch.GetTimestamp();
            }

            var residueMaxAbsAfterCoupling = collectDebug ? MaxAbs(_residue.AsSpan(0, activeSampleCount)) : 0f;
            ApplyFloorCurves(_residue, _floorCurves, noResidue, _channels, blockSizeHalf);
            if (profile is not null)
            {
                profile.ApplyFloorTicks += Stopwatch.GetTimestamp() - stageStart;
            }

            var residueMaxAbsAfterFloor = collectDebug ? MaxAbs(_residue.AsSpan(0, activeSampleCount)) : 0f;
            var renderDebug = RenderAndOverlap(blockSize, blockSizeHalf, previousWindowFlag, nextWindowFlag, granulePosition, collectDebug);
            if (profile is not null)
            {
                profile.PacketTicks += Stopwatch.GetTimestamp() - packetStart;
            }

            if (!collectDebug)
            {
                return default;
            }

            return new OggVorbisPacketDebugInfo(
                audioPacketIndex,
                granulePosition,
                modeNumber,
                mode.BlockFlag,
                previousWindowFlag,
                nextWindowFlag,
                blockSize,
                blockSizeHalf,
                floorPresentChannels,
                floorAbsentChannels,
                floorMaxAbs,
                residueMaxAbsBeforeCoupling,
                residueMaxAbsAfterCoupling,
                residueMaxAbsAfterFloor,
                residueMaxBookIndexUsed,
                residueLookupVectorMaxAbsUsed,
                renderDebug.ImdctMaxAbs,
                renderDebug.WindowedMaxAbs,
                renderDebug.OutputMaxAbs,
                renderDebug.OutputFramesProduced,
                renderDebug.OutputFrameStart,
                renderDebug.OutputFrameEnd);
        }

        internal DecodeResult ToDecodeResult() => new(_samples, _sink);

        internal float[] ToCapturedSamples()
        {
            if (_capturedSamples is null || _capturedSampleCount == 0)
            {
                return Array.Empty<float>();
            }

            if (_capturedSampleCount == _capturedSamples.Length)
            {
                return _capturedSamples;
            }

            var result = new float[_capturedSampleCount];
            Array.Copy(_capturedSamples, result, result.Length);
            return result;
        }

        internal int[] ToCapturedPcm32Samples()
        {
            if (_capturedPcm32Samples is null || _capturedSampleCount == 0)
            {
                return Array.Empty<int>();
            }

            if (_capturedSampleCount == _capturedPcm32Samples.Length)
            {
                return _capturedPcm32Samples;
            }

            var result = new int[_capturedSampleCount];
            Array.Copy(_capturedPcm32Samples, result, result.Length);
            return result;
        }

        private OggVorbisRenderDebugInfo RenderAndOverlap(int blockSize, int blockSizeHalf, bool previousWindowFlag, bool nextWindowFlag, long granulePosition, bool collectDebug)
        {
            var currentBlockOffset = (_blockSizeLarge - blockSize) >> 1;
            Array.Clear(_windowed, 0, _channels * _blockSizeLarge);
            var plan = blockSize == _blockSizeSmall ? _smallImdctPlan : _largeImdctPlan;
            var profile = _profile;
            var imdctMaxAbs = 0f;
            var windowedMaxAbs = 0f;
            for (var channel = 0; channel < _channels; channel++)
            {
                var stageStart = profile is null ? 0 : Stopwatch.GetTimestamp();
                ComputeVorbisImdct(
                    _residue.AsSpan(channel * blockSizeHalf, blockSizeHalf),
                    _imdct.AsSpan(0, blockSize),
                    plan);
                if (profile is not null)
                {
                    profile.ImdctTicks += Stopwatch.GetTimestamp() - stageStart;
                    stageStart = Stopwatch.GetTimestamp();
                }

                var currentChannel = _windowed.AsSpan(channel * _blockSizeLarge, _blockSizeLarge);
                var currentBlock = currentChannel.Slice(currentBlockOffset, blockSize);
                var windowedMaxForChannel = ApplyVorbisWindow(
                    _imdct.AsSpan(0, blockSize),
                    currentBlock,
                    blockSize,
                    _blockSizeSmall,
                    previousWindowFlag,
                    nextWindowFlag,
                    _smallWindow,
                    _largeWindow,
                    collectDebug);
                if (profile is not null)
                {
                    profile.WindowTicks += Stopwatch.GetTimestamp() - stageStart;
                }

                if (collectDebug)
                {
                    imdctMaxAbs = Math.Max(imdctMaxAbs, MaxAbs(_imdct.AsSpan(0, blockSize)));
                    windowedMaxAbs = Math.Max(windowedMaxAbs, windowedMaxForChannel);
                }
            }

            var outputFrameStart = -1L;
            var outputFrameEnd = -1L;
            var outputMaxAbs = 0f;
            var outputFramesProduced = 0;
            if (_hasPrevious)
            {
                var outputCount = (blockSize + _previousBlockSize) >> 2;
                var previousOutputStart = _blockSizeLarge >> 1;
                var currentOutputStart = previousOutputStart - outputCount;
                var previousBlockOffset = (_blockSizeLarge - _previousBlockSize) >> 1;
                var totalFramesAfterPacket = granulePosition >= 0 ? granulePosition : long.MaxValue;
                var baseFrame = _samples / _channels;
                outputFrameStart = baseFrame;
                var producedFrames = (int)Math.Min((long)outputCount, Math.Max(0L, totalFramesAfterPacket - baseFrame));
                var outputStageStart = profile is null ? 0 : Stopwatch.GetTimestamp();
                var usedFastPath = !collectDebug && TryWriteOutputFastPath(producedFrames, previousOutputStart, currentOutputStart);
                if (usedFastPath)
                {
                }
                else if (_outputTarget == OggVorbisOutputTarget.Pcm32)
                {
                    var capturedPcm32Samples = GetCapturedPcm32Samples();
                    var captureLimit = capturedPcm32Samples.Length;
                    var sink = _sink;
                    if (producedFrames > 0 && sink == 0)
                    {
                        sink = FnvOffsetBasis;
                    }
                    if (capturedPcm32Samples.IsEmpty)
                    {
                        for (var i = 0; i < producedFrames; i++)
                        {
                            for (var channel = 0; channel < _channels; channel++)
                            {
                                var channelBase = channel * _blockSizeLarge;
                                var sample = _previousWindowed[channelBase + previousOutputStart + i] + _windowed[channelBase + currentOutputStart + i];
                                if (sample == 0f)
                                {
                                    sample = 0f;
                                }

                                if (collectDebug)
                                {
                                    outputMaxAbs = Math.Max(outputMaxAbs, Math.Abs(sample));
                                }

                                sink = HashInt32SampleSeeded(sink, FloatToPcm32(sample));
                            }
                        }

                        _sink = sink;
                        _samples += producedFrames * _channels;
                    }
                    else
                    {
                        for (var i = 0; i < producedFrames; i++)
                        {
                            for (var channel = 0; channel < _channels; channel++)
                            {
                                var channelBase = channel * _blockSizeLarge;
                                var sample = _previousWindowed[channelBase + previousOutputStart + i] + _windowed[channelBase + currentOutputStart + i];
                                if (sample == 0f)
                                {
                                    sample = 0f;
                                }

                                if (collectDebug)
                                {
                                    outputMaxAbs = Math.Max(outputMaxAbs, Math.Abs(sample));
                                }

                                var pcm32Sample = FloatToPcm32(sample);
                                sink = HashInt32SampleSeeded(sink, pcm32Sample);
                                if (_capturedSampleCount < captureLimit)
                                {
                                    capturedPcm32Samples[_capturedSampleCount++] = pcm32Sample;
                                }

                                _samples++;
                            }
                        }

                        _sink = sink;
                    }
                }
                else
                {
                    var capturedSamples = GetCapturedSamples();
                    var captureLimit = capturedSamples.Length;
                    var sink = _sink;
                    if (producedFrames > 0 && sink == 0)
                    {
                        sink = FnvOffsetBasis;
                    }
                    if (capturedSamples.IsEmpty)
                    {
                        for (var i = 0; i < producedFrames; i++)
                        {
                            for (var channel = 0; channel < _channels; channel++)
                            {
                                var channelBase = channel * _blockSizeLarge;
                                var sample = _previousWindowed[channelBase + previousOutputStart + i] + _windowed[channelBase + currentOutputStart + i];
                                if (sample == 0f)
                                {
                                    sample = 0f;
                                }

                                if (collectDebug)
                                {
                                    outputMaxAbs = Math.Max(outputMaxAbs, Math.Abs(sample));
                                }

                                sink = HashFloatSampleSeeded(sink, sample);
                            }
                        }

                        _sink = sink;
                        _samples += producedFrames * _channels;
                    }
                    else
                    {
                        for (var i = 0; i < producedFrames; i++)
                        {
                            for (var channel = 0; channel < _channels; channel++)
                            {
                                var channelBase = channel * _blockSizeLarge;
                                var sample = _previousWindowed[channelBase + previousOutputStart + i] + _windowed[channelBase + currentOutputStart + i];
                                if (sample == 0f)
                                {
                                    sample = 0f;
                                }

                                if (collectDebug)
                                {
                                    outputMaxAbs = Math.Max(outputMaxAbs, Math.Abs(sample));
                                }

                                sink = HashFloatSampleSeeded(sink, sample);
                                if (_capturedSampleCount < captureLimit)
                                {
                                    capturedSamples[_capturedSampleCount++] = sample;
                                }

                                _samples++;
                            }
                        }

                        _sink = sink;
                    }
                }

                if (profile is not null)
                {
                    var outputTicks = Stopwatch.GetTimestamp() - outputStageStart;
                    profile.OutputTicks += outputTicks;
                    if (usedFastPath)
                    {
                        profile.OutputFastPathTicks += outputTicks;
                        profile.OutputFastPathPackets++;
                    }
                    else
                    {
                        profile.OutputScalarTicks += outputTicks;
                        profile.OutputScalarPackets++;
                    }
                }

                outputFramesProduced = producedFrames;
                outputFrameEnd = producedFrames > 0 ? baseFrame + producedFrames - 1 : baseFrame - 1;
            }

            (_previousWindowed, _windowed) = (_windowed, _previousWindowed);
            _previousBlockSize = blockSize;
            _previousNextWindowFlag = nextWindowFlag;
            _hasPrevious = true;
            return collectDebug
                ? new OggVorbisRenderDebugInfo(imdctMaxAbs, windowedMaxAbs, outputMaxAbs, outputFramesProduced, outputFrameStart, outputFrameEnd)
                : default;
        }

        private bool TryWriteOutputFastPath(int producedFrames, int previousOutputStart, int currentOutputStart)
        {
            if (producedFrames < 8 || !Avx.IsSupported)
            {
                return false;
            }

            if (_channels == 1)
            {
                if (_outputTarget == OggVorbisOutputTarget.Pcm32)
                {
                    if (!Avx2.IsSupported)
                    {
                        return false;
                    }

                    WriteMonoPcm32Avx2(producedFrames, previousOutputStart, currentOutputStart);
                    return true;
                }

                WriteMonoFloat32Avx(producedFrames, previousOutputStart, currentOutputStart);
                return true;
            }

            if (_channels == 2)
            {
                if (_outputTarget == OggVorbisOutputTarget.Pcm32)
                {
                    if (!Avx2.IsSupported)
                    {
                        return false;
                    }

                    WriteStereoPcm32Avx2(producedFrames, previousOutputStart, currentOutputStart);
                    return true;
                }

                WriteStereoFloat32Avx(producedFrames, previousOutputStart, currentOutputStart);
                return true;
            }

            return false;
        }

        private unsafe void WriteMonoFloat32Avx(int producedFrames, int previousOutputStart, int currentOutputStart)
        {
            var capturedSamples = GetCapturedSamples();
            var captureLimit = capturedSamples.Length;
            var previousBase = previousOutputStart;
            var currentBase = currentOutputStart;
            var mixed = stackalloc float[8];
            var simdCount = producedFrames - (producedFrames % 8);
            var sink = _sink == 0 ? FnvOffsetBasis : _sink;

            fixed (float* previousPtr = _previousWindowed)
            fixed (float* currentPtr = _windowed)
            {
                var i = 0;
                if (capturedSamples.IsEmpty)
                {
                    for (; i < simdCount; i += 8)
                    {
                        var sum = Avx.Add(
                            Avx.LoadVector256(previousPtr + previousBase + i),
                            Avx.LoadVector256(currentPtr + currentBase + i));
                        Avx.Store(mixed, sum);
                        for (var j = 0; j < 8; j++)
                        {
                            var sample = mixed[j];
                            if (sample == 0f)
                            {
                                sample = 0f;
                            }

                            sink = HashFloatSampleSeeded(sink, sample);
                        }
                    }

                    for (; i < producedFrames; i++)
                    {
                        var sample = previousPtr[previousBase + i] + currentPtr[currentBase + i];
                        if (sample == 0f)
                        {
                            sample = 0f;
                        }

                        sink = HashFloatSampleSeeded(sink, sample);
                    }

                    _sink = sink;
                    _samples += producedFrames;
                    return;
                }

                for (; i < simdCount; i += 8)
                {
                    var sum = Avx.Add(
                        Avx.LoadVector256(previousPtr + previousBase + i),
                        Avx.LoadVector256(currentPtr + currentBase + i));
                    Avx.Store(mixed, sum);
                    for (var j = 0; j < 8; j++)
                    {
                        var sample = mixed[j];
                        if (sample == 0f)
                        {
                            sample = 0f;
                        }

                        sink = HashFloatSampleSeeded(sink, sample);
                        if (_capturedSampleCount < captureLimit)
                        {
                            capturedSamples[_capturedSampleCount++] = sample;
                        }

                        _samples++;
                    }
                }

                for (; i < producedFrames; i++)
                {
                    var sample = previousPtr[previousBase + i] + currentPtr[currentBase + i];
                    if (sample == 0f)
                    {
                        sample = 0f;
                    }

                    sink = HashFloatSampleSeeded(sink, sample);
                    if (_capturedSampleCount < captureLimit)
                    {
                        capturedSamples[_capturedSampleCount++] = sample;
                    }

                    _samples++;
                }
            }

            _sink = sink;
        }

        private unsafe void WriteStereoFloat32Avx(int producedFrames, int previousOutputStart, int currentOutputStart)
        {
            var capturedSamples = GetCapturedSamples();
            var captureLimit = capturedSamples.Length;
            var leftBase = 0;
            var rightBase = _blockSizeLarge;
            var leftMixed = stackalloc float[8];
            var rightMixed = stackalloc float[8];
            var simdCount = producedFrames - (producedFrames % 8);
            var sink = _sink == 0 ? FnvOffsetBasis : _sink;

            fixed (float* previousPtr = _previousWindowed)
            fixed (float* currentPtr = _windowed)
            {
                var previousLeft = previousPtr + leftBase + previousOutputStart;
                var previousRight = previousPtr + rightBase + previousOutputStart;
                var currentLeft = currentPtr + leftBase + currentOutputStart;
                var currentRight = currentPtr + rightBase + currentOutputStart;
                var i = 0;
                if (capturedSamples.IsEmpty)
                {
                    for (; i < simdCount; i += 8)
                    {
                        var leftSum = Avx.Add(Avx.LoadVector256(previousLeft + i), Avx.LoadVector256(currentLeft + i));
                        var rightSum = Avx.Add(Avx.LoadVector256(previousRight + i), Avx.LoadVector256(currentRight + i));
                        Avx.Store(leftMixed, leftSum);
                        Avx.Store(rightMixed, rightSum);
                        for (var j = 0; j < 8; j++)
                        {
                            var leftSample = leftMixed[j];
                            if (leftSample == 0f)
                            {
                                leftSample = 0f;
                            }

                            sink = HashFloatSampleSeeded(sink, leftSample);

                            var rightSample = rightMixed[j];
                            if (rightSample == 0f)
                            {
                                rightSample = 0f;
                            }

                            sink = HashFloatSampleSeeded(sink, rightSample);
                        }
                    }

                    for (; i < producedFrames; i++)
                    {
                        var leftSample = previousLeft[i] + currentLeft[i];
                        if (leftSample == 0f)
                        {
                            leftSample = 0f;
                        }

                        sink = HashFloatSampleSeeded(sink, leftSample);

                        var rightSample = previousRight[i] + currentRight[i];
                        if (rightSample == 0f)
                        {
                            rightSample = 0f;
                        }

                        sink = HashFloatSampleSeeded(sink, rightSample);
                    }

                    _sink = sink;
                    _samples += producedFrames * 2;
                    return;
                }

                for (; i < simdCount; i += 8)
                {
                    var leftSum = Avx.Add(Avx.LoadVector256(previousLeft + i), Avx.LoadVector256(currentLeft + i));
                    var rightSum = Avx.Add(Avx.LoadVector256(previousRight + i), Avx.LoadVector256(currentRight + i));
                    Avx.Store(leftMixed, leftSum);
                    Avx.Store(rightMixed, rightSum);
                    for (var j = 0; j < 8; j++)
                    {
                        var leftSample = leftMixed[j];
                        if (leftSample == 0f)
                        {
                            leftSample = 0f;
                        }

                        sink = HashFloatSampleSeeded(sink, leftSample);
                        if (_capturedSampleCount < captureLimit)
                        {
                            capturedSamples[_capturedSampleCount++] = leftSample;
                        }

                        _samples++;

                        var rightSample = rightMixed[j];
                        if (rightSample == 0f)
                        {
                            rightSample = 0f;
                        }

                        sink = HashFloatSampleSeeded(sink, rightSample);
                        if (_capturedSampleCount < captureLimit)
                        {
                            capturedSamples[_capturedSampleCount++] = rightSample;
                        }

                        _samples++;
                    }
                }

                for (; i < producedFrames; i++)
                {
                    var leftSample = previousLeft[i] + currentLeft[i];
                    if (leftSample == 0f)
                    {
                        leftSample = 0f;
                    }

                    sink = HashFloatSampleSeeded(sink, leftSample);
                    if (_capturedSampleCount < captureLimit)
                    {
                        capturedSamples[_capturedSampleCount++] = leftSample;
                    }

                    _samples++;

                    var rightSample = previousRight[i] + currentRight[i];
                    if (rightSample == 0f)
                    {
                        rightSample = 0f;
                    }

                    sink = HashFloatSampleSeeded(sink, rightSample);
                    if (_capturedSampleCount < captureLimit)
                    {
                        capturedSamples[_capturedSampleCount++] = rightSample;
                    }

                    _samples++;
                }
            }

            _sink = sink;
        }

        private unsafe void WriteMonoPcm32Avx2(int producedFrames, int previousOutputStart, int currentOutputStart)
        {
            var capturedPcm32Samples = GetCapturedPcm32Samples();
            var captureLimit = capturedPcm32Samples.Length;
            var previousBase = previousOutputStart;
            var currentBase = currentOutputStart;
            var mixedPcm = stackalloc int[8];
            var simdCount = producedFrames - (producedFrames % 8);
            var sink = _sink == 0 ? FnvOffsetBasis : _sink;

            fixed (float* previousPtr = _previousWindowed)
            fixed (float* currentPtr = _windowed)
            {
                var i = 0;
                if (capturedPcm32Samples.IsEmpty)
                {
                    for (; i < simdCount; i += 8)
                    {
                        var sum = Avx.Add(
                            Avx.LoadVector256(previousPtr + previousBase + i),
                            Avx.LoadVector256(currentPtr + currentBase + i));
                        var pcm32 = ConvertFloat32ToPcm32Vector256(sum);
                        Avx.Store((float*)mixedPcm, pcm32.AsSingle());
                        for (var j = 0; j < 8; j++)
                        {
                            sink = HashInt32SampleSeeded(sink, mixedPcm[j]);
                        }
                    }

                    for (; i < producedFrames; i++)
                    {
                        sink = HashInt32SampleSeeded(sink, FloatToPcm32(previousPtr[previousBase + i] + currentPtr[currentBase + i]));
                    }

                    _sink = sink;
                    _samples += producedFrames;
                    return;
                }

                for (; i < simdCount; i += 8)
                {
                    var sum = Avx.Add(
                        Avx.LoadVector256(previousPtr + previousBase + i),
                        Avx.LoadVector256(currentPtr + currentBase + i));
                    var pcm32 = ConvertFloat32ToPcm32Vector256(sum);
                    Avx.Store((float*)mixedPcm, pcm32.AsSingle());
                    for (var j = 0; j < 8; j++)
                    {
                        var sample = mixedPcm[j];
                        sink = HashInt32SampleSeeded(sink, sample);
                        if (_capturedSampleCount < captureLimit)
                        {
                            capturedPcm32Samples[_capturedSampleCount++] = sample;
                        }

                        _samples++;
                    }
                }

                for (; i < producedFrames; i++)
                {
                    var sample = FloatToPcm32(previousPtr[previousBase + i] + currentPtr[currentBase + i]);
                    sink = HashInt32SampleSeeded(sink, sample);
                    if (_capturedSampleCount < captureLimit)
                    {
                        capturedPcm32Samples[_capturedSampleCount++] = sample;
                    }

                    _samples++;
                }
            }

            _sink = sink;
        }

        private unsafe void WriteStereoPcm32Avx2(int producedFrames, int previousOutputStart, int currentOutputStart)
        {
            var capturedPcm32Samples = GetCapturedPcm32Samples();
            var captureLimit = capturedPcm32Samples.Length;
            var leftBase = 0;
            var rightBase = _blockSizeLarge;
            var leftMixedPcm = stackalloc int[8];
            var rightMixedPcm = stackalloc int[8];
            var simdCount = producedFrames - (producedFrames % 8);
            var sink = _sink == 0 ? FnvOffsetBasis : _sink;

            fixed (float* previousPtr = _previousWindowed)
            fixed (float* currentPtr = _windowed)
            {
                var previousLeft = previousPtr + leftBase + previousOutputStart;
                var previousRight = previousPtr + rightBase + previousOutputStart;
                var currentLeft = currentPtr + leftBase + currentOutputStart;
                var currentRight = currentPtr + rightBase + currentOutputStart;
                var i = 0;
                if (capturedPcm32Samples.IsEmpty)
                {
                    for (; i < simdCount; i += 8)
                    {
                        var leftSum = Avx.Add(Avx.LoadVector256(previousLeft + i), Avx.LoadVector256(currentLeft + i));
                        var rightSum = Avx.Add(Avx.LoadVector256(previousRight + i), Avx.LoadVector256(currentRight + i));
                        var leftPcm32 = ConvertFloat32ToPcm32Vector256(leftSum);
                        var rightPcm32 = ConvertFloat32ToPcm32Vector256(rightSum);
                        Avx.Store((float*)leftMixedPcm, leftPcm32.AsSingle());
                        Avx.Store((float*)rightMixedPcm, rightPcm32.AsSingle());
                        for (var j = 0; j < 8; j++)
                        {
                            sink = HashInt32SampleSeeded(sink, leftMixedPcm[j]);
                            sink = HashInt32SampleSeeded(sink, rightMixedPcm[j]);
                        }
                    }

                    for (; i < producedFrames; i++)
                    {
                        sink = HashInt32SampleSeeded(sink, FloatToPcm32(previousLeft[i] + currentLeft[i]));
                        sink = HashInt32SampleSeeded(sink, FloatToPcm32(previousRight[i] + currentRight[i]));
                    }

                    _sink = sink;
                    _samples += producedFrames * 2;
                    return;
                }

                for (; i < simdCount; i += 8)
                {
                    var leftSum = Avx.Add(Avx.LoadVector256(previousLeft + i), Avx.LoadVector256(currentLeft + i));
                    var rightSum = Avx.Add(Avx.LoadVector256(previousRight + i), Avx.LoadVector256(currentRight + i));
                    var leftPcm32 = ConvertFloat32ToPcm32Vector256(leftSum);
                    var rightPcm32 = ConvertFloat32ToPcm32Vector256(rightSum);
                    Avx.Store((float*)leftMixedPcm, leftPcm32.AsSingle());
                    Avx.Store((float*)rightMixedPcm, rightPcm32.AsSingle());
                    for (var j = 0; j < 8; j++)
                    {
                        var leftSample = leftMixedPcm[j];
                        sink = HashInt32SampleSeeded(sink, leftSample);
                        if (_capturedSampleCount < captureLimit)
                        {
                            capturedPcm32Samples[_capturedSampleCount++] = leftSample;
                        }

                        _samples++;

                        var rightSample = rightMixedPcm[j];
                        sink = HashInt32SampleSeeded(sink, rightSample);
                        if (_capturedSampleCount < captureLimit)
                        {
                            capturedPcm32Samples[_capturedSampleCount++] = rightSample;
                        }

                        _samples++;
                    }
                }

                for (; i < producedFrames; i++)
                {
                    var leftSample = FloatToPcm32(previousLeft[i] + currentLeft[i]);
                    sink = HashInt32SampleSeeded(sink, leftSample);
                    if (_capturedSampleCount < captureLimit)
                    {
                        capturedPcm32Samples[_capturedSampleCount++] = leftSample;
                    }

                    _samples++;

                    var rightSample = FloatToPcm32(previousRight[i] + currentRight[i]);
                    sink = HashInt32SampleSeeded(sink, rightSample);
                    if (_capturedSampleCount < captureLimit)
                    {
                        capturedPcm32Samples[_capturedSampleCount++] = rightSample;
                    }

                    _samples++;
                }
            }

            _sink = sink;
        }
    }

    private sealed class OggImdctPlan
    {
        internal OggImdctPlan(int outputLength)
        {
            if ((outputLength & 3) != 0 || !BitOperations.IsPow2((uint)outputLength))
            {
                throw new InvalidOperationException("Vorbis IMDCT plan expects a power-of-two output length divisible by 4.");
            }

            OutputLength = outputLength;
            Log2N = BitOperations.Log2((uint)outputLength);
            Scale = 1.0f;
            Trig = new float[outputLength + (outputLength >> 2)];
            BitReverse = new int[outputLength >> 2];

            var n2 = outputLength >> 1;
            for (var i = 0; i < outputLength >> 2; i++)
            {
                Trig[i * 2] = MathF.Cos((MathF.PI / outputLength) * (4 * i));
                Trig[(i * 2) + 1] = -MathF.Sin((MathF.PI / outputLength) * (4 * i));
                Trig[n2 + (i * 2)] = MathF.Cos((MathF.PI / (2 * outputLength)) * ((2 * i) + 1));
                Trig[n2 + (i * 2) + 1] = MathF.Sin((MathF.PI / (2 * outputLength)) * ((2 * i) + 1));
            }

            for (var i = 0; i < outputLength >> 3; i++)
            {
                Trig[outputLength + (i * 2)] = MathF.Cos((MathF.PI / outputLength) * ((4 * i) + 2)) * 0.5f;
                Trig[outputLength + (i * 2) + 1] = -MathF.Sin((MathF.PI / outputLength) * ((4 * i) + 2)) * 0.5f;
            }

            var mask = (1 << (Log2N - 1)) - 1;
            var msb = 1 << (Log2N - 2);
            for (var i = 0; i < outputLength >> 3; i++)
            {
                var acc = 0;
                for (var j = 0; (msb >> j) != 0; j++)
                {
                    if (((msb >> j) & i) != 0)
                    {
                        acc |= 1 << j;
                    }
                }

                BitReverse[i * 2] = ((~acc) & mask) - 1;
                BitReverse[(i * 2) + 1] = acc;
            }
        }

        internal int OutputLength { get; }
        internal int Log2N { get; }
        internal float Scale { get; }
        internal float[] Trig { get; }
        internal int[] BitReverse { get; }
    }

    private readonly record struct OggVorbisAudioPacketInfo(
        int ModeNumber,
        bool BlockFlag,
        bool PreviousWindowFlag,
        bool NextWindowFlag,
        int Mapping,
        int FloorPresentChannels,
        int FloorAbsentChannels,
        int DecodedFloorValues,
        int MaterializedFloorValues,
        long FloorCurveHash,
        int DecodedResidueClasswords,
        int DecodedResidueVectors,
        int MaterializedResidueValues,
        long ResidueValueHash);

    private readonly record struct OggVorbisFloorPacketInfo(
        bool Present,
        int DecodedValues,
        int MaterializedValues,
        long CurveHash);

    private readonly record struct OggVorbisFloorCurveInfo(int ValueCount, long Hash);

    private readonly record struct OggVorbisFloorPoint(int X, int Y, int Index);

    private readonly record struct OggVorbisResiduePacketInfo(
        int DecodedClasswords,
        int DecodedVectors,
        int MaterializedValues,
        long ValueHash,
        int MaxBookIndexUsed,
        float LookupVectorMaxAbsUsed);

    private readonly record struct OggVorbisCodebookTree(int[] Zero, int[] One, int[] Entries, int MaxLength);
    private readonly record struct OggVorbisCodebookFastLookup(int BitCount, byte[] Lengths, int[] Entries);

    private readonly record struct OggPageInfo(byte HeaderType, long GranulePosition, uint SerialNumber, uint PageSequence, int PayloadBytes);

    private delegate void OggPageConsumer(OggPageInfo page);

    private delegate bool OggPacketConsumer(ReadOnlySpan<byte> packet, int packetIndex, long granulePosition);

    private ref struct VorbisBitReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private readonly long _bitLength;
        private long _bitOffset;

        internal long RemainingBits => _bitLength - _bitOffset;

        internal VorbisBitReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _bitLength = (long)data.Length * 8;
            _bitOffset = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal uint ReadBits(int count) => ReadBitsCore(count, advance: true);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal uint PeekBits(int count) => ReadBitsCore(count, advance: false);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AdvanceBits(int count)
        {
            _bitOffset += count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint ReadBitsCore(int count, bool advance)
        {
            if ((uint)count > 32)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            EnsureAvailable(count);
            if (count == 0)
            {
                return 0;
            }

            var byteOffset = (int)(_bitOffset >> 3);
            var bitIndex = (int)(_bitOffset & 7);
            var availableBytes = _data.Length - byteOffset;
            var source = _data[byteOffset..];
            ulong window;
            if (availableBytes >= sizeof(ulong))
            {
                window = BinaryPrimitives.ReadUInt64LittleEndian(source);
            }
            else
            {
                window = 0;
                for (var i = 0; i < availableBytes; i++)
                {
                    window |= (ulong)source[i] << (i * 8);
                }
            }

            if (advance)
            {
                _bitOffset += count;
            }

            var mask = count == 32 ? uint.MaxValue : (1u << count) - 1u;
            return (uint)((window >> bitIndex) & mask);
        }

        internal int ReadBitsInt(int count) => checked((int)ReadBits(count));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int ReadBit()
        {
            EnsureAvailable(1);
            var bitOffset = _bitOffset;
            var bit = (_data[(int)(bitOffset >> 3)] >> (int)(bitOffset & 7)) & 1;
            _bitOffset = bitOffset + 1;
            return bit;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SkipBits(long count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            EnsureAvailable(count);
            _bitOffset += count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureAvailable(long count)
        {
            if (count == 0)
            {
                return;
            }

            if (count < 0 || _bitOffset + count > _bitLength)
            {
                throw new EndOfStreamException("Vorbis setup packet ended unexpectedly.");
            }
        }
    }
}
