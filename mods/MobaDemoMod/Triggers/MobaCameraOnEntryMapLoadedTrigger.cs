using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Camera.FollowTargets;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace MobaDemoMod.Triggers
{
    public sealed class MobaCameraOnEntryMapLoadedTrigger : Trigger
    {
        private readonly IModContext _ctx;

        public MobaCameraOnEntryMapLoadedTrigger(IModContext ctx)
        {
            _ctx = ctx;
            EventKey = GameEvents.MapLoaded;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            var mapTags = context.Get(CoreServiceKeys.MapTags) ?? new List<string>();
            if (!HasTag(mapTags, "moba_demo")) return Task.CompletedTask;

            var session = context.Get(CoreServiceKeys.GameSession);
            if (session == null) return Task.CompletedTask;

            // Wire camera follow target: SelectedEntity → LocalPlayer fallback chain
            session.Camera.FollowTarget = new SelectedEntityFollowTarget(engine.World, engine.GlobalContext);

            var world = engine.World;
            Entity localPlayer = default;
            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) &&
                localObj is Entity localEntity &&
                world.IsAlive(localEntity))
            {
                localPlayer = localEntity;
            }

            if (localPlayer == default)
            {
                var q = new QueryDescription().WithAll<PlayerOwner, WorldPositionCm>();
                var chunks = world.Query(in q);
                foreach (var chunk in chunks)
                {
                    var owners = chunk.GetArray<PlayerOwner>();
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        if (owners[i].PlayerId != 1) continue;
                        localPlayer = chunk.Entity(i);
                        break;
                    }

                    if (localPlayer != default) break;
                }
            }

            if (localPlayer != default && world.TryGet(localPlayer, out WorldPositionCm pos))
            {
                var pos2d = pos.Value.ToVector2();
                session.Camera.FollowTargetPositionCm = pos2d;
                session.Camera.State.TargetCm = pos2d;
                _ctx.Log("[MobaDemoMod] Camera follow target set to SelectedEntity → LocalPlayer.");
                return Task.CompletedTask;
            }

            session.Camera.State.TargetCm = Vector2.Zero;
            _ctx.Log("[MobaDemoMod] Camera centered on origin.");
            return Task.CompletedTask;
        }

        private static bool HasTag(List<string> tags, string tag)
        {
            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], tag, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
    }
}

