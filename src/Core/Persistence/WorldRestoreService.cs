using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace Ludots.Core.Persistence
{
    public sealed class WorldRestoreService
    {
        private readonly LudotsBinaryWorldSerializer? _worldSerializer;

        public WorldRestoreService()
        {
        }

        public WorldRestoreService(LudotsBinaryWorldSerializer worldSerializer)
        {
            _worldSerializer = worldSerializer ?? throw new ArgumentNullException(nameof(worldSerializer));
        }

        public void Restore(GameEngine engine, WorldSaveSnapshot snapshot)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            SaveContextValidator.Validate(snapshot.Header, engine);
            World restoredWorld;
            try
            {
                LudotsBinaryWorldSerializer serializer = _worldSerializer
                    ?? LudotsPersistenceSerializerFactory.Create(engine);
                restoredWorld = serializer.Deserialize(snapshot.WorldBytes);
            }
            catch (Exception ex) when (ex is not SaveContextException)
            {
                throw new SaveContextException(
                    $"Save world.bin is invalid: {GetDiagnosticMessage(ex)}",
                    ex);
            }

            using (restoredWorld)
            {
                engine.RestoreWorldSnapshot(restoredWorld, snapshot.Domains);
            }
        }

        private static string GetDiagnosticMessage(Exception exception)
        {
            Exception root = exception;
            while (root.InnerException != null)
            {
                root = root.InnerException;
            }

            if (!string.Equals(root.Message, exception.Message, StringComparison.Ordinal))
            {
                return $"{exception.Message}: {root.Message}";
            }

            return exception.Message;
        }
    }
}
