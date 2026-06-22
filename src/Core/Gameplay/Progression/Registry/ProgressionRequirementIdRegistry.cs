using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.Progression.Registry
{
    public static class ProgressionRequirementIdRegistry
    {
        private static StringIntRegistry _ids = CreateRegistry();

        public const int InvalidId = 0;
        public const int MaxRequirements = 4095;

        public static bool IsFrozen => _ids.IsFrozen;

        public static void Clear()
        {
            _ids = CreateRegistry();
        }

        public static int Register(string name)
        {
            if (_ids.IsFrozen)
            {
                throw new InvalidOperationException("ProgressionRequirementIdRegistry is frozen.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Progression requirement id name cannot be null or whitespace.", nameof(name));
            }

            int id = _ids.GetId(name);
            if (id != InvalidId)
            {
                return id;
            }

            if (_ids.Count >= MaxRequirements)
            {
                throw new InvalidOperationException($"ProgressionRequirementIdRegistry supports up to {MaxRequirements} requirements.");
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
                capacity: MaxRequirements + 1,
                startId: 1,
                invalidId: InvalidId,
                comparer: StringComparer.OrdinalIgnoreCase);
        }
    }
}
