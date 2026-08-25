using System;
using Ludots.Core.Engine;

namespace Ludots.Core.Persistence
{
    public static class SaveContextValidator
    {
        public static void Validate(SaveContextHeader header, GameEngine engine)
        {
            if (header == null) throw new ArgumentNullException(nameof(header));
            if (engine == null) throw new ArgumentNullException(nameof(engine));

            if (header.SchemaVersion != SaveContextHeader.CurrentSchemaVersion)
            {
                throw new SaveContextException(
                    $"Save schemaVersion mismatch: expected {SaveContextHeader.CurrentSchemaVersion}, actual {header.SchemaVersion}.");
            }

            string currentMapId = engine.CurrentMapSession?.MapId.Value
                ?? engine.MergedConfig?.StartupMapId
                ?? string.Empty;
            if (string.IsNullOrWhiteSpace(header.MapId) ||
                !string.Equals(header.MapId, currentMapId, StringComparison.Ordinal))
            {
                throw new SaveContextException(
                    $"Save mapId mismatch: expected {currentMapId}, actual {header.MapId}. Load the corresponding map before loading this save.");
            }

            string currentModSetHash = SaveContextHashes.ComputeModSetHash(engine);
            if (!string.Equals(header.ModSetHash, currentModSetHash, StringComparison.Ordinal))
            {
                throw new SaveContextException(
                    $"Save modSetHash mismatch: expected {currentModSetHash}, actual {header.ModSetHash}. Load the corresponding mod set and map before loading this save.");
            }

            string currentRegistryFingerprint = SaveContextHashes.ComputeRegistryFingerprint(engine);
            if (!string.Equals(header.RegistryFingerprint, currentRegistryFingerprint, StringComparison.Ordinal))
            {
                throw new SaveContextException(
                    $"Save registryFingerprint mismatch: expected {currentRegistryFingerprint}, actual {header.RegistryFingerprint}.");
            }
        }
    }
}
