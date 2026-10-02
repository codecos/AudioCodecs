using System;
using Concentus.Structs;

namespace Concentus;

/// <summary>
/// multi-layer perceptron processor
/// </summary>
internal static class MultiLayerPerceptron
{
	private const int MAX_NEURONS = 100;

	internal static float tansig_approx(float x)
	{
		float num = 1f;
		if (!(x < 8f))
		{
			return 1f;
		}
		if (!(x > -8f))
		{
			return -1f;
		}
		if (x < 0f)
		{
			x = 0f - x;
			num = -1f;
		}
		int num2 = (int)Math.Floor(0.5f + 25f * x);
		x -= 0.04f * (float)num2;
		float num3 = Tables.tansig_table[num2];
		float num4 = 1f - num3 * num3;
		num3 += x * num4 * (1f - num3 * x);
		return num * num3;
	}

	internal static void mlp_process(MLP m, float[] input, float[] output)
	{
		float[] array = new float[100];
		float[] weights = m.weights;
		int num = 0;
		for (int i = 0; i < m.topo[1]; i++)
		{
			float num2 = weights[num];
			num++;
			for (int j = 0; j < m.topo[0]; j++)
			{
				num2 += input[j] * weights[num];
				num++;
			}
			array[i] = tansig_approx(num2);
		}
		for (int i = 0; i < m.topo[2]; i++)
		{
			float num3 = weights[num];
			num++;
			for (int k = 0; k < m.topo[1]; k++)
			{
				num3 += array[k] * weights[num];
				num++;
			}
			output[i] = tansig_approx(num3);
		}
	}
}
