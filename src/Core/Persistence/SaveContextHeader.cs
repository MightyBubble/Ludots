using System;

namespace Ludots.Core.Persistence
{
    public sealed record SaveContextHeader(
        int SchemaVersion,
        string ModSetHash,
        string RegistryFingerprint,
        string MapId,
        int Tick,
        DateTimeOffset CreatedUtc,
        string EngineVersion)
    {
        public const int CurrentSchemaVersion = 1;
    }
}
