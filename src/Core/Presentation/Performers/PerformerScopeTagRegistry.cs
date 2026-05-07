using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// Stable string-to-int registry for config-authored performer scope tags.
    /// Runtime commands and instances keep the compact int handle; JSON may author
    /// readable string scope names such as "working" or "structure".
    /// </summary>
    public static class PerformerScopeTagRegistry
    {
        private static StringIntRegistry _ids = CreateRegistry();

        public static int Register(string name) => _ids.Register(name);

        public static int GetId(string name) => _ids.GetId(name);

        public static string GetName(int id) => _ids.GetName(id);

        public static void Clear()
        {
            _ids = CreateRegistry();
        }

        private static StringIntRegistry CreateRegistry()
        {
            return new StringIntRegistry(
                capacity: 128,
                startId: 1,
                invalidId: 0,
                comparer: StringComparer.OrdinalIgnoreCase);
        }
    }
}
