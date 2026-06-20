using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.Progression.Registry
{
    public static class ProgressionIdRegistry
    {
        private static StringIntRegistry _ids = CreateRegistry();

        public const int InvalidId = 0;
        public const int MaxProgressions = 4095;

        public static bool IsFrozen => _ids.IsFrozen;

        public static void Clear()
        {
            _ids = CreateRegistry();
        }

        public static int Register(string name)
        {
            if (_ids.IsFrozen)
            {
                throw new InvalidOperationException("ProgressionIdRegistry is frozen.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Progression id name cannot be null or whitespace.", nameof(name));
            }

            int id = _ids.GetId(name);
            if (id != InvalidId)
            {
                return id;
            }

            if (_ids.Count >= MaxProgressions)
            {
                throw new InvalidOperationException($"ProgressionIdRegistry supports up to {MaxProgressions} progressions.");
            }

            return _ids.Register(name);
        }

        public static int GetId(string name)
        {
            return _ids.GetId(name);
        }

        public static string GetName(int id)
        {
            return _ids.GetName(id);
        }

        public static void Freeze()
        {
            _ids.Freeze();
        }

        private static StringIntRegistry CreateRegistry()
        {
            return new StringIntRegistry(
                capacity: MaxProgressions + 1,
                startId: 1,
                invalidId: InvalidId,
                comparer: StringComparer.OrdinalIgnoreCase);
        }
    }
}
