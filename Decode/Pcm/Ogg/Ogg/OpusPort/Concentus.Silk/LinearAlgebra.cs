using Concentus.Common;

namespace Concentus.Silk;

internal static class LinearAlgebra
{
	internal static void silk_solve_LDL(int[] A, int A_ptr, int M, int[] b, int[] x_Q16)
	{
		int[] l_Q = new int[M * M];
		int[] array = new int[16];
		int[] inv_D = new int[32];
		silk_LDL_factorize(A, A_ptr, M, l_Q, inv_D);
		silk_LS_SolveFirst(l_Q, M, b, array);
		silk_LS_divide_Q16(array, inv_D, M);
		silk_LS_SolveLast(l_Q, M, array, x_Q16);
	}

	private static void silk_LDL_factorize(int[] A, int A_ptr, int M, int[] L_Q16, int[] inv_D)
	{
		int[] array = new int[M];
		int[] array2 = new int[M];
		int num = 1;
		int num2 = Inlines.silk_max_32(Inlines.silk_SMMUL(Inlines.silk_ADD_SAT32(A[A_ptr], A[A_ptr + Inlines.silk_SMULBB(M, M) - 1]), 21475), 512);
		for (int i = 0; i < M; i++)
		{
			if (num != 1)
			{
				break;
			}
			num = 0;
			for (int j = 0; j < M; j++)
			{
				int[] array3 = L_Q16;
				int num3 = Inlines.MatrixGetPointer(j, 0, M);
				int num4 = 0;
				for (int k = 0; k < j; k++)
				{
					array[k] = Inlines.silk_SMULWW(array2[k], array3[num3 + k]);
					num4 = Inlines.silk_SMLAWW(num4, array[k], array3[num3 + k]);
				}
				num4 = Inlines.silk_SUB32(Inlines.MatrixGet(A, A_ptr, j, j, M), num4);
				if (num4 < num2)
				{
					num4 = Inlines.silk_SUB32(Inlines.silk_SMULBB(i + 1, num2), num4);
					for (int k = 0; k < M; k++)
					{
						Inlines.MatrixSet(A, A_ptr, k, k, M, Inlines.silk_ADD32(Inlines.MatrixGet(A, A_ptr, k, k, M), num4));
					}
					num = 1;
					break;
				}
				array2[j] = num4;
				int num5 = Inlines.silk_INVERSE32_varQ(num4, 36);
				int b = Inlines.silk_LSHIFT(num5, 4);
				int num6 = Inlines.silk_SMULWW(Inlines.silk_SUB32(16777216, Inlines.silk_SMULWW(num4, b)), b);
				inv_D[j * 2] = num5;
				inv_D[j * 2 + 1] = num6;
				Inlines.MatrixSet(L_Q16, j, j, M, 65536);
				array3 = A;
				num3 = Inlines.MatrixGetPointer(j, 0, M) + A_ptr;
				int num7 = Inlines.MatrixGetPointer(j + 1, 0, M);
				for (int k = j + 1; k < M; k++)
				{
					num4 = 0;
					for (int l = 0; l < j; l++)
					{
						num4 = Inlines.silk_SMLAWW(num4, array[l], L_Q16[num7 + l]);
					}
					num4 = Inlines.silk_SUB32(array3[num3 + k], num4);
					Inlines.MatrixSet(L_Q16, k, j, M, Inlines.silk_ADD32(Inlines.silk_SMMUL(num4, num6), Inlines.silk_RSHIFT(Inlines.silk_SMULWW(num4, num5), 4)));
					num7 += M;
				}
			}
		}
	}

	private static void silk_LS_divide_Q16(int[] T, int[] inv_D, int M)
	{
		for (int i = 0; i < M; i++)
		{
			int b = inv_D[i * 2];
			int b2 = inv_D[i * 2 + 1];
			int a = T[i];
			T[i] = Inlines.silk_ADD32(Inlines.silk_SMMUL(a, b2), Inlines.silk_RSHIFT(Inlines.silk_SMULWW(a, b), 4));
		}
	}

	private static void silk_LS_SolveFirst(int[] L_Q16, int M, int[] b, int[] x_Q16)
	{
		for (int i = 0; i < M; i++)
		{
			int num = Inlines.MatrixGetPointer(i, 0, M);
			int num2 = 0;
			for (int j = 0; j < i; j++)
			{
				num2 = Inlines.silk_SMLAWW(num2, L_Q16[num + j], x_Q16[j]);
			}
			x_Q16[i] = Inlines.silk_SUB32(b[i], num2);
		}
	}

	private static void silk_LS_SolveLast(int[] L_Q16, int M, int[] b, int[] x_Q16)
	{
		for (int num = M - 1; num >= 0; num--)
		{
			int num2 = Inlines.MatrixGetPointer(0, num, M);
			int num3 = 0;
			for (int num4 = M - 1; num4 > num; num4--)
			{
				num3 = Inlines.silk_SMLAWW(num3, L_Q16[num2 + Inlines.silk_SMULBB(num4, M)], x_Q16[num4]);
			}
			x_Q16[num] = Inlines.silk_SUB32(b[num], num3);
		}
	}
}
