using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.Relationships
{
    public sealed class RelationshipProcessingSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly RelationshipChangeBuffer _changeBuffer;
        private readonly RelationshipCallbackProcessor _callbackProcessor;
        private readonly RelationshipSynergyProcessor _synergyProcessor;

        public RelationshipProcessingSystem(
            GameEngine engine,
            RelationshipChangeBuffer changeBuffer,
            TagOps tagOps,
            TeamEntityLookup teamLookup)
        {
            _engine = engine;
            _changeBuffer = changeBuffer;
            _callbackProcessor = new RelationshipCallbackProcessor(engine.World, tagOps, teamLookup);
            _synergyProcessor = new RelationshipSynergyProcessor(engine.World, tagOps, teamLookup);
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            RelationshipCatalogRuntime? catalogRuntime = _engine.GetService(CoreServiceKeys.RelationshipCatalogRuntime);
            if (catalogRuntime == null)
            {
                _changeBuffer.Clear();
                return;
            }

            if (_changeBuffer.Count > 0)
            {
                _callbackProcessor.Process(_engine, catalogRuntime, _changeBuffer.GetSpan());
                _changeBuffer.Clear();
            }

            if (catalogRuntime.Synergies.Count > 0)
            {
                _synergyProcessor.Evaluate(_engine, catalogRuntime);
            }
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }
    }
}
