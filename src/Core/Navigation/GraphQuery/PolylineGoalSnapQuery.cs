using System;

namespace Ludots.Core.Navigation.GraphQuery
{
    public static class PolylineGoalSnapQuery
    {
        public static int SnapGoalOntoPolyline(
            float goalXcm,
            float goalYcm,
            Span<int> pathXcm,
            Span<int> pathYcm,
            int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            if (count == 1)
            {
                return 1;
            }

            float bestDistanceSq = float.MaxValue;
            int bestSegmentIndex = count - 2;
            float bestT = 1f;
            int bestXcm = pathXcm[count - 1];
            int bestYcm = pathYcm[count - 1];

            for (int i = 0; i < count - 1; i++)
            {
                float startXcm = pathXcm[i];
                float startYcm = pathYcm[i];
                float endXcm = pathXcm[i + 1];
                float endYcm = pathYcm[i + 1];
                float deltaXcm = endXcm - startXcm;
                float deltaYcm = endYcm - startYcm;
                float lengthSq = (deltaXcm * deltaXcm) + (deltaYcm * deltaYcm);
                float t = lengthSq <= 0.0001f
                    ? 0f
                    : Math.Clamp(((goalXcm - startXcm) * deltaXcm + (goalYcm - startYcm) * deltaYcm) / lengthSq, 0f, 1f);

                int projectedXcm = (int)MathF.Round(startXcm + (deltaXcm * t), MidpointRounding.AwayFromZero);
                int projectedYcm = (int)MathF.Round(startYcm + (deltaYcm * t), MidpointRounding.AwayFromZero);
                float dx = goalXcm - projectedXcm;
                float dy = goalYcm - projectedYcm;
                float distanceSq = (dx * dx) + (dy * dy);
                if (distanceSq >= bestDistanceSq)
                {
                    continue;
                }

                bestDistanceSq = distanceSq;
                bestSegmentIndex = i;
                bestT = t;
                bestXcm = projectedXcm;
                bestYcm = projectedYcm;
            }

            if (bestT <= 0.001f)
            {
                return Math.Max(1, bestSegmentIndex + 1);
            }

            int writeIndex = bestSegmentIndex + 1;
            pathXcm[writeIndex] = bestXcm;
            pathYcm[writeIndex] = bestYcm;
            return writeIndex + 1;
        }
    }
}
