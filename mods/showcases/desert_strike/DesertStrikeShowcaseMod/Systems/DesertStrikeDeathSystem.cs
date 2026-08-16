using System;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.Components;
using DesertStrikeShowcaseMod.Runtime;

namespace DesertStrikeShowcaseMod.Systems
{
    public sealed class DesertStrikeDeathSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription UnitQuery = new QueryDescription()
            .WithAll<AttributeBuffer, DesertStrikeUnit>();

        private static readonly QueryDescription BaseQuery = new QueryDescription()
            .WithAll<AttributeBuffer, DesertStrikeBase>();

        private readonly DesertStrikeState _state;
        private readonly int _healthAttributeId;
        private readonly CommandBuffer _commands = new();
        private int _destroyedBaseTeam;

        public DesertStrikeDeathSystem(GameEngine engine, DesertStrikeState state)
            : base(engine.World)
        {
            _state = state;
            _healthAttributeId = EnsureAttributeId("Health");
        }

        public override void Update(in float dt)
        {
            _destroyedBaseTeam = 0;

            foreach (ref var chunk in World.Query(in UnitQuery))
            {
                var attributes = chunk.GetSpan<AttributeBuffer>();
                ref var entityFirst = ref chunk.Entity(0);
                foreach (var index in chunk)
                {
                    Arch.Core.Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                    if (attributes[index].GetCurrent(_healthAttributeId) > 0f)
                    {
                        continue;
                    }

                    if (TryQueueDestroy(entity))
                    {
                        _state.UnitsDestroyed++;
                    }
                }
            }

            foreach (ref var chunk in World.Query(in BaseQuery))
            {
                var attributes = chunk.GetSpan<AttributeBuffer>();
                var teams = chunk.GetSpan<Team>();
                ref var entityFirst = ref chunk.Entity(0);
                foreach (var index in chunk)
                {
                    Arch.Core.Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                    if (attributes[index].GetCurrent(_healthAttributeId) > 0f)
                    {
                        continue;
                    }

                    _destroyedBaseTeam = teams[index].Id;
                    TryQueueDestroy(entity);
                }
            }

            if (_commands.Size > 0)
            {
                _commands.Playback(World);
            }

            if (_destroyedBaseTeam > 0 && !_state.GameOver)
            {
                _state.GameOver = true;
                _state.DestroyedBaseTeam = _destroyedBaseTeam;
                _state.WinnerPlayerId = _destroyedBaseTeam == _state.PlayerTeam ? 2 : 1;
            }
        }

        private bool TryQueueDestroy(Entity entity)
        {
            if (World.Has<PresentationDestroyPending>(entity))
            {
                return false;
            }

            if (!World.Has<PresentationStableId>(entity))
            {
                _commands.Destroy(entity);
                return true;
            }

            _commands.Add(entity, new PresentationDestroyPending());
            return true;
        }

        private static int EnsureAttributeId(string attributeName)
        {
            int id = AttributeRegistry.GetId(attributeName);
            return id > 0 ? id : AttributeRegistry.Register(attributeName);
        }
    }
}
