using Arch.Core;
using Arch.Buffer;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class ClearPresentationFlagsSystem : BaseSystem<World, float>
    {
        public static int AuditRemovedThisUpdate;
        public static long AuditPlaybackBytesThisUpdate;
        private static readonly QueryDescription _tagQuery = new QueryDescription()
            .WithAll<GameplayTagEffectiveChangedBits>();
        private static readonly QueryDescription _attributeQuery = new QueryDescription()
            .WithAll<GameplayAttributeChangedBits>();
        private readonly CommandBuffer _commandBuffer = new();

        public ClearPresentationFlagsSystem(World world) : base(world) { }

        public override void Update(in float dt)
        {
            AuditRemovedThisUpdate = 0;
            AuditPlaybackBytesThisUpdate = 0;
            var tagJob = new ClearTagJob { CommandBuffer = _commandBuffer };
            World.InlineEntityQuery<ClearTagJob, GameplayTagEffectiveChangedBits>(in _tagQuery, ref tagJob);

            var attributeJob = new ClearAttributeJob { CommandBuffer = _commandBuffer };
            World.InlineEntityQuery<ClearAttributeJob, GameplayAttributeChangedBits>(in _attributeQuery, ref attributeJob);
            if (_commandBuffer.Size > 0)
            {
                AuditRemovedThisUpdate = _commandBuffer.Size;
                long auditStart = System.GC.GetAllocatedBytesForCurrentThread();
                _commandBuffer.Playback(World);
                AuditPlaybackBytesThisUpdate = System.GC.GetAllocatedBytesForCurrentThread() - auditStart;
            }
        }

        public override void Dispose()
        {
            _commandBuffer.Dispose();
            base.Dispose();
        }

        private struct ClearTagJob : IForEachWithEntity<GameplayTagEffectiveChangedBits>
        {
            public CommandBuffer CommandBuffer;

            public void Update(Entity entity, ref GameplayTagEffectiveChangedBits bits)
            {
                bits.Clear();
                CommandBuffer.Remove<GameplayTagEffectiveChangedBits>(entity);
            }
        }

        private struct ClearAttributeJob : IForEachWithEntity<GameplayAttributeChangedBits>
        {
            public CommandBuffer CommandBuffer;

            public void Update(Entity entity, ref GameplayAttributeChangedBits bits)
            {
                bits.Clear();
                CommandBuffer.Remove<GameplayAttributeChangedBits>(entity);
            }
        }
    }
}
