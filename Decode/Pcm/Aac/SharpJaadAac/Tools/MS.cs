using SharpJaad.AAC.Huffman;
using SharpJaad.AAC.Syntax;

namespace SharpJaad.AAC.Tools
{
    public class MS
    {
        public static void Process(CPE cpe, float[] specL, float[] specR)
        {
            ICStream ics = cpe.GetLeftChannel();
            ICSInfo info = ics.GetInfo();
            int[] offsets = info.GetSWBOffsets();
            int windowGroups = info.GetWindowGroupCount();
            int maxSFB = info.GetMaxSFB();
            int[] sfbCBl = ics.getSfbCB();
            int[] sfbCBr = cpe.GetRightChannel().getSfbCB();
            int groupOff = 0;
            int idx = 0;

            for (int g = 0; g < windowGroups; g++)
            {
                int groupLength = info.GetWindowGroupLength(g);
                for (int i = 0; i < maxSFB; i++, idx++)
                {
                    if (cpe.IsMSUsed(idx) && sfbCBl[idx] < HCB.NOISE_HCB && sfbCBr[idx] < HCB.NOISE_HCB)
                    {
                        int bandOffset = offsets[i];
                        int bandWidth = offsets[i + 1] - bandOffset;
                        for (int w = 0; w < groupLength; w++)
                        {
                            int off = groupOff + (w << 7) + bandOffset;
                            for (int j = 0; j < bandWidth; j++)
                            {
                                float t = specL[off + j] - specR[off + j];
                                specL[off + j] += specR[off + j];
                                specR[off + j] = t;
                            }
                        }
                    }
                }
                groupOff += groupLength << 7;
            }
        }
    }
}
