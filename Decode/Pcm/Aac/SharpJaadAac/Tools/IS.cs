using SharpJaad.AAC.Huffman;
using SharpJaad.AAC.Syntax;

namespace SharpJaad.AAC.Tools
{
    public class IS
    {
        public static void Process(CPE cpe, float[] specL, float[] specR)
        {
            ICStream ics = cpe.GetRightChannel();
            ICSInfo info = ics.GetInfo();
            int[] offsets = info.GetSWBOffsets();
            int windowGroups = info.GetWindowGroupCount();
            int maxSFB = info.GetMaxSFB();
            int[] sfbCB = ics.getSfbCB();
            int[] sectEnd = ics.GetSectEnd();
            float[] scaleFactors = ics.GetScaleFactors();
            bool msMaskPresent = cpe.IsMSMaskPresent();

            int idx = 0, groupOff = 0;
            for (int g = 0; g < windowGroups; g++)
            {
                int groupLength = info.GetWindowGroupLength(g);
                for (int i = 0; i < maxSFB;)
                {
                    if (sfbCB[idx] == HCB.INTENSITY_HCB || sfbCB[idx] == HCB.INTENSITY_HCB2)
                    {
                        int end = sectEnd[idx];
                        for (; i < end; i++, idx++)
                        {
                            int direction = sfbCB[idx] == HCB.INTENSITY_HCB ? 1 : -1;
                            if (msMaskPresent && cpe.IsMSUsed(idx))
                            {
                                direction = -direction;
                            }

                            float scale = direction * scaleFactors[idx];
                            int bandOffset = offsets[i];
                            int bandWidth = offsets[i + 1] - bandOffset;
                            for (int w = 0; w < groupLength; w++)
                            {
                                int off = groupOff + (w << 7) + bandOffset;
                                for (int j = 0; j < bandWidth; j++)
                                {
                                    specR[off + j] = specL[off + j] * scale;
                                }
                            }
                        }
                    }
                    else
                    {
                        int end = sectEnd[idx];
                        idx += end - i;
                        i = end;
                    }
                }
                groupOff += groupLength << 7;
            }
        }
    }
}
