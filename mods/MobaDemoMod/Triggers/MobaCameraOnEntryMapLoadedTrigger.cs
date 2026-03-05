using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Map;
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
            var mapId = context.Get<MapId>(ContextKeys.MapId);
            
            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            var config = (GameConfig)engine.GlobalContext[ContextKeys.GameConfig];
            string startupMapId = config.StartupMapId;
            
            bool isEntry = mapId.Value == startupMapId;
            if (!isEntry) return Task.CompletedTask;

            var session = context.Get<GameSession>(ContextKeys.GameSession);
            if (session == null) return Task.CompletedTask;

            // MobaConfig is guaranteed to be in GlobalContext by InstallMobaDemoOnGameStartTrigger
            var mobaConfig = (MobaConfig)engine.GlobalContext[InstallMobaDemoOnGameStartTrigger.MobaConfigKey];

            session.Camera.State.Yaw = mobaConfig.Camera.InitialYawDegrees;
            session.Camera.State.Pitch = mobaConfig.Camera.InitialPitchDegrees;
            session.Camera.State.DistanceCm = mobaConfig.Camera.InitialDistanceCm;

            var world = engine.World;
            var q = new QueryDescription().WithAll<PlayerOwner, WorldPositionCm>();
            var chunks = world.Query(in q);
            foreach (var chunk in chunks)
            {
                var owners = chunk.GetArray<PlayerOwner>();
                var positions = chunk.GetArray<WorldPositionCm>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (owners[i].PlayerId != 1) continue;
                    var p = positions[i].Value;
                    session.Camera.State.TargetCm = p.ToVector2();
                    _ctx.Log("[MobaDemoMod] Camera centered on local player.");
                    return Task.CompletedTask;
                }
            }

            session.Camera.State.TargetCm = Vector2.Zero;
            _ctx.Log("[MobaDemoMod] Camera centered on origin.");
            return Task.CompletedTask;
        }
    }
}

