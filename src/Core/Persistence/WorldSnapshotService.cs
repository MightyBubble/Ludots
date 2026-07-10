using System;
using System.Text.Json.Nodes;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace Ludots.Core.Persistence
{
    public sealed class WorldSnapshotService
    {
        private readonly LudotsBinaryWorldSerializer? _worldSerializer;

        public WorldSnapshotService()
        {
        }

        public WorldSnapshotService(LudotsBinaryWorldSerializer worldSerializer)
        {
            _worldSerializer = worldSerializer ?? throw new ArgumentNullException(nameof(worldSerializer));
        }

        public WorldSaveSnapshot Capture(GameEngine engine, SaveSnapshotBoundary boundary)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));

            boundary.EnsureClean();
            SaveContextHeader header = SaveContextFactory.Capture(engine);
            JsonObject domains = RequireSaveParticipants(engine).CaptureDomains();
            LudotsBinaryWorldSerializer serializer = _worldSerializer
                ?? LudotsPersistenceSerializerFactory.Create(engine);
            byte[] worldBytes = serializer.Serialize(engine.World);

            return new WorldSaveSnapshot(header, domains, worldBytes);
        }

        private static SaveParticipantRegistry RequireSaveParticipants(GameEngine engine)
        {
            SaveParticipantRegistry registry = engine.GetService(CoreServiceKeys.SaveParticipants);
            if (registry == null)
            {
                throw new SaveContextException("Save participant registry is not available.");
            }

            return registry;
        }
    }
}
