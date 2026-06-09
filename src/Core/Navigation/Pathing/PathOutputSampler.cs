using System;

namespace Ludots.Core.Navigation.Pathing
{
    internal static class PathOutputSampler
    {
        public static void WritePreservingEndpoints(
            PathStore store,
            in PathHandle handle,
            int[] sourceXcm,
            int[] sourceYcm,
            int count)
        {
            if (sourceXcm.Length <= count)
            {
                store.TryWrite(in handle, sourceXcm, sourceYcm, count);
                return;
            }

            if (count <= 1)
            {
                Span<int> oneX = stackalloc int[1] { sourceXcm[0] };
                Span<int> oneY = stackalloc int[1] { sourceYcm[0] };
                store.TryWrite(in handle, oneX, oneY, 1);
                return;
            }

            int[] xs = new int[count];
            int[] ys = new int[count];
            int lastSource = sourceXcm.Length - 1;
            int lastOutput = count - 1;
            for (int i = 0; i < count; i++)
            {
                int sourceIndex = (int)(((long)i * lastSource + (lastOutput / 2)) / lastOutput);
                xs[i] = sourceXcm[sourceIndex];
                ys[i] = sourceYcm[sourceIndex];
            }

            xs[0] = sourceXcm[0];
            ys[0] = sourceYcm[0];
            xs[^1] = sourceXcm[^1];
            ys[^1] = sourceYcm[^1];
            store.TryWrite(in handle, xs, ys, count);
        }
    }
}
