namespace SharpJaad.AAC.Tools
{
    internal class Arrays
    {
        public static void Fill<T>(T[] array, T value)
        {
            System.Array.Fill(array, value);
        }

        public static void Fill<T>(T[] array, int fromIndex, int toIndex, T value)
        {
            System.Array.Fill(array, value, fromIndex, toIndex - fromIndex);
        }
    }
}
