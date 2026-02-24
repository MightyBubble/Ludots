using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Gameplay.Cue
{
    public class GameplayCueSystem : BaseSystem<World, float>
    {
        private readonly CueEventBuffer _cueBuffer;
        private readonly ICoordinateMapper _mapper;
        
        // Query for events that should trigger cues
        private QueryDescription _eventQuery = new QueryDescription()
            .WithAll<GameplayEvent>();

        public GameplayCueSystem(World world, CueEventBuffer cueBuffer, ICoordinateMapper mapper) : base(world)
        {
            _cueBuffer = cueBuffer;
            _mapper = mapper;
        }

        public override void Update(in float dt)
        {
            // 1. Clear previous frame's cues
            _cueBuffer.Clear();

            // 2. Process GameplayEvents
            // Note: In a real system, we'd have a config mapping EventTags -> Cue Data.
            // For Primitive phase, we hardcode simple mapping or assume EventTagId == CueTagId.
            
            World.Query(in _eventQuery, (Entity e, ref GameplayEvent evt) =>
            {
                // Resolve Position
                Vector3 spawnPos = Vector3.Zero;
                
                // If Target has VisualTransform (synced), use it.
                if (World.IsAlive(evt.Target) && World.Has<VisualTransform>(evt.Target))
                {
                    spawnPos = World.Get<VisualTransform>(evt.Target).Position;
                }
                else if (World.IsAlive(evt.Source) && World.Has<VisualTransform>(evt.Source))
                {
                    spawnPos = World.Get<VisualTransform>(evt.Source).Position;
                }
                
                // Add Cue
                _cueBuffer.TryAdd(new CueDescriptor
                {
                    CueTagId = evt.TagId,
                    Position = spawnPos,
                    Rotation = Quaternion.Identity,
                    Scale = 1.0f,
                    Source = evt.Source,
                    Target = evt.Target,
                    AssetId = 0 // Default primitive
                });
            });
            
            // 3. (Optional) Process Active Effects for Loop Cues
            // We can query ActiveEffectContainer and add persistent cues if needed.
        }
    }
}
