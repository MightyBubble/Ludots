using System;
using Ludots.Core.Registry;

using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Assets
{
    public static class AnimationChannelRegistry
    {
        public const string Locomotion = WellKnownAnimationChannelNames.Locomotion;
        public const string AimYaw = WellKnownAnimationChannelNames.AimYaw;
        public const string Recoil = WellKnownAnimationChannelNames.Recoil;

        private static StringIntRegistry _ids = CreateRegistry();

        public static int Register(string name) => _ids.Register(Canonicalize(name));

        public static int GetId(string name) => string.IsNullOrWhiteSpace(name) ? 0 : _ids.GetId(Canonicalize(name));

        public static string GetName(int id) => _ids.GetName(id);

        public static void Clear()
        {
            _ids = CreateRegistry();
        }

        private static StringIntRegistry CreateRegistry()
        {
            return new StringIntRegistry(
                capacity: 64,
                startId: 1,
                invalidId: 0,
                comparer: StringComparer.Ordinal);
        }

        private static string Canonicalize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Animation channel must not be null or whitespace.", nameof(name));
            }

            string trimmed = name.Trim();
            if (!string.Equals(name, trimmed, StringComparison.Ordinal))
            {
                throw new ArgumentException("Animation channel must not include leading or trailing whitespace.", nameof(name));
            }

            return name;
        }
    }
}
