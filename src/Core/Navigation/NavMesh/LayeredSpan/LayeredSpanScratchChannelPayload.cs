using System.Runtime.CompilerServices;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Exact managed array payload byte accounting for preallocated layered-span scratch channels.
    /// Counts only element storage (Length * element size), not array object headers or references.
    /// </summary>
    internal static class LayeredSpanScratchChannelPayload
    {
        internal static long Sum(params long[] terms)
        {
            long total = 0;
            for (int i = 0; i < terms.Length; i++)
            {
                total = checked(total + terms[i]);
            }

            return total;
        }

        internal static long Of(int[] array) => OfElements(array.Length, sizeof(int));

        internal static long Of(byte[] array) => OfElements(array.Length, sizeof(byte));

        internal static long Of(short[] array) => OfElements(array.Length, sizeof(short));

        internal static long Of(long[] array) => OfElements(array.Length, sizeof(long));

        internal static long Of<T>(T[] array) where T : struct
            => OfElements(array.Length, Unsafe.SizeOf<T>());

        private static long OfElements(int length, int elementSize)
            => length == 0 ? 0L : checked((long)length * elementSize);
    }
}
