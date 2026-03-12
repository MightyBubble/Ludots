using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class PresentationStartupPerformerSystem : BaseSystem<World, float>
    {
        private readonly PresentationCommandBuffer _commands;

        private readonly QueryDescription _query = new QueryDescription()
            .WithAll<PresentationStartupPerformers, PresentationStartupState, PresentationStableId>();

        public PresentationStartupPerformerSystem(World world, PresentationCommandBuffer commands)
            : base(world)
        {
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        }

        public override void Update(in float dt)
        {
            var query = World.Query(in _query);
            foreach (var chunk in query)
            {
                var startupPerformers = chunk.GetArray<PresentationStartupPerformers>();
                var startupStates = chunk.GetArray<PresentationStartupState>();
                var stableIds = chunk.GetArray<PresentationStableId>();

                for (int i = 0; i < chunk.Count; i++)
                {
                    if (startupStates[i].Initialized)
                        continue;

                    int scopeId = stableIds[i].Value;
                    Entity entity = chunk.Entity(i);

                    for (int slot = 0; slot < startupPerformers[i].Count; slot++)
                    {
                        if (!_commands.TryAdd(new PresentationCommand
                            {
                                Kind = PresentationCommandKind.CreatePerformer,
                                AnchorKind = PresentationAnchorKind.Entity,
                                IdA = startupPerformers[i].Get(slot),
                                IdB = scopeId,
                                Source = entity,
                            }))
                        {
                            throw new InvalidOperationException("PresentationCommandBuffer is full while creating startup performers.");
                        }
                    }

                    startupStates[i].Initialized = true;
                }
            }
        }
    }
}
