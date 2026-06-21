using System;
using Ludots.Core.Engine;

namespace Ludots.Core.Persistence
{
    public static class SaveContextFactory
    {
        public static SaveContextHeader Capture(GameEngine engine)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));

            return Capture(engine, DateTimeOffset.UtcNow);
        }

        public static SaveContextHeader Capture(GameEngine engine, DateTimeOffset createdUtc)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));

            return new SaveContextHeader(
                SaveContextHeader.CurrentSchemaVersion,
                SaveContextHashes.ComputeModSetHash(engine),
                SaveContextHashes.ComputeRegistryFingerprint(engine),
                ResolveMapId(engine),
                engine.GameSession?.CurrentTick ?? 0,
                createdUtc,
                typeof(GameEngine).Assembly.GetName().Version?.ToString() ?? string.Empty);
        }

        private static string ResolveMapId(GameEngine engine)
        {
            string? mapId = engine.CurrentMapSession?.MapId.Value;
            if (!string.IsNullOrWhiteSpace(mapId))
            {
                return mapId;
            }

            return engine.MergedConfig?.StartupMapId ?? string.Empty;
        }
    }
}
