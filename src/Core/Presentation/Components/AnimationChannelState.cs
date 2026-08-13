using System;

namespace Ludots.Core.Presentation.Components
{
    public struct AnimationChannelState
    {
        public int ChannelId;
        public float NormalizedTime01;
        public float Weight01;
        public float Scalar0;
        public float Scalar1;

        public readonly bool IsActive => ChannelId > 0 && Weight01 > 0.001f;

        public static AnimationChannelState Create(
            int channelId,
            float normalizedTime01,
            float weight01,
            float scalar0 = 0f,
            float scalar1 = 0f)
        {
            return new AnimationChannelState
            {
                ChannelId = channelId,
                NormalizedTime01 = Clamp01(normalizedTime01),
                Weight01 = Clamp01(weight01),
                Scalar0 = scalar0,
                Scalar1 = scalar1,
            };
        }

        private static float Clamp01(float value)
        {
            return Math.Clamp(value, 0f, 1f);
        }
    }
}
