using System.Diagnostics;

namespace SharpJaad.AAC
{
    public static class DecoderProfiling
    {
        [ThreadStatic]
        private static bool _enabled;
        [ThreadStatic]
        private static ProfileSnapshot _snapshot;

        internal static bool Enabled => _enabled;

        public static void Begin()
        {
            _enabled = true;
            _snapshot = default;
        }

        public static ProfileSnapshot End()
        {
            var result = _snapshot;
            _enabled = false;
            _snapshot = default;
            return result;
        }

        internal static long Timestamp()
        {
            return Stopwatch.GetTimestamp();
        }

        internal static void AddDecodeTicks(long ticks)
        {
            _snapshot.DecodeTicks += ticks;
            _snapshot.Frames++;
        }

        internal static void AddIcsInfoTicks(long ticks)
        {
            _snapshot.IcsInfoTicks += ticks;
        }

        internal static void AddSectionTicks(long ticks)
        {
            _snapshot.SectionTicks += ticks;
        }

        internal static void AddScaleFactorTicks(long ticks)
        {
            _snapshot.ScaleFactorTicks += ticks;
        }

        internal static void AddSpectralTicks(long ticks)
        {
            _snapshot.SpectralTicks += ticks;
        }

        internal static void AddTnsTicks(long ticks)
        {
            _snapshot.TnsTicks += ticks;
        }

        internal static void AddGainControlTicks(long ticks)
        {
            _snapshot.GainControlTicks += ticks;
        }

        internal static void AddProcessTicks(long ticks)
        {
            _snapshot.ProcessTicks += ticks;
        }

        internal static void AddProcessStereoTicks(long ticks)
        {
            _snapshot.ProcessStereoTicks += ticks;
        }

        internal static void AddProcessPredictionTicks(long ticks)
        {
            _snapshot.ProcessPredictionTicks += ticks;
        }

        internal static void AddProcessCouplingTicks(long ticks)
        {
            _snapshot.ProcessCouplingTicks += ticks;
        }

        internal static void AddProcessTnsTicks(long ticks)
        {
            _snapshot.ProcessTnsTicks += ticks;
        }

        internal static void AddProcessFilterbankTicks(long ticks)
        {
            _snapshot.ProcessFilterbankTicks += ticks;
        }

        internal static void AddProcessFilterbankMdctTicks(long ticks)
        {
            _snapshot.ProcessFilterbankMdctTicks += ticks;
        }

        internal static void AddProcessFilterbankWindowTicks(long ticks)
        {
            _snapshot.ProcessFilterbankWindowTicks += ticks;
        }

        internal static void AddProcessGainTicks(long ticks)
        {
            _snapshot.ProcessGainTicks += ticks;
        }

        internal static void AddProcessSbrTicks(long ticks)
        {
            _snapshot.ProcessSbrTicks += ticks;
        }

        internal static void AddOutputTicks(long ticks)
        {
            _snapshot.OutputTicks += ticks;
        }

        public struct ProfileSnapshot
        {
            public long Frames;
            public long DecodeTicks;
            public long ProcessTicks;
            public long OutputTicks;
            public long IcsInfoTicks;
            public long SectionTicks;
            public long ScaleFactorTicks;
            public long SpectralTicks;
            public long TnsTicks;
            public long GainControlTicks;
            public long ProcessStereoTicks;
            public long ProcessPredictionTicks;
            public long ProcessCouplingTicks;
            public long ProcessTnsTicks;
            public long ProcessFilterbankTicks;
            public long ProcessGainTicks;
            public long ProcessSbrTicks;
            public long ProcessFilterbankMdctTicks;
            public long ProcessFilterbankWindowTicks;
        }
    }
}
