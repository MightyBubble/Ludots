using System;
using System.Numerics;
using Ludots.Core.Navigation.GraphWorld;

namespace Ludots.Core.Navigation.GraphQuery
{
    public static class LoadedChunkSolvePrimer
    {
        public static void PrimeForBounds(
            WorldGridLoadedChunkContributor contributor,
            ReadOnlySpan<Vector2> pointsCm,
            int paddingChunks)
        {
            if (contributor == null) throw new ArgumentNullException(nameof(contributor));
            if (pointsCm.Length == 0)
            {
                return;
            }

            int minXcm = Round(pointsCm[0].X);
            int maxXcm = minXcm;
            int minYcm = Round(pointsCm[0].Y);
            int maxYcm = minYcm;

            for (int i = 1; i < pointsCm.Length; i++)
            {
                int xcm = Round(pointsCm[i].X);
                int ycm = Round(pointsCm[i].Y);
                minXcm = Math.Min(minXcm, xcm);
                maxXcm = Math.Max(maxXcm, xcm);
                minYcm = Math.Min(minYcm, ycm);
                maxYcm = Math.Max(maxYcm, ycm);
            }

            int paddingCm = Math.Max(0, paddingChunks) * contributor.ChunkSizeCm;
            int centerXcm = (int)(((long)minXcm + maxXcm) / 2L);
            int centerYcm = (int)(((long)minYcm + maxYcm) / 2L);
            int radiusCm = Math.Max(maxXcm - minXcm, maxYcm - minYcm) / 2;
            contributor.UpdateWindow(centerXcm, centerYcm, radiusCm + paddingCm);
        }

        private static int Round(float value)
        {
            return (int)MathF.Round(value, MidpointRounding.AwayFromZero);
        }
    }
}
