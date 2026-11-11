using System;
using System.Text;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using NUnit.Framework;
using PresenterTreeOverrideShowcaseMod;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    [NonParallelizable]
    public sealed class PresenterTreeOverrideShowcaseAcceptanceTests
    {
        private const string MapId = "presenter_tree_override_showcase";

        private static readonly string[] Mods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "RaylibPlatformMeshesMod",
            "PresenterTreeOverrideShowcaseMod",
        };

        private static GameEngine CreateLoadedEngine(int frames = 12)
        {
            _ = typeof(PresenterTreeOverrideShowcaseModEntry).Assembly;
            GameEngine engine = PresenterBlacksmithShowcaseTestHarness.CreateEngine(Mods);
            PresenterBlacksmithShowcaseTestHarness.LoadMap(engine, MapId, frames);
            return engine;
        }

        [Test]
        public void Diagnostic_Dump()
        {
            using GameEngine engine = CreateLoadedEngine();
            var sb = new StringBuilder();
            var q = new QueryDescription().WithAll<PlacedInstanceId>();
            engine.World.Query(in q, (Entity e, ref PlacedInstanceId id) =>
            {
                sb.Append(id.Value);
                if (engine.World.Has<WorldPositionCm>(e)) sb.Append(" pos=").Append(engine.World.Get<WorldPositionCm>(e).Value.ToString());
                if (engine.World.Has<CullState>(e)) sb.Append(" visible=").Append(engine.World.Get<CullState>(e).IsVisible);
                if (engine.World.Has<VisualTransform>(e)) sb.Append(" vt=").Append(engine.World.Get<VisualTransform>(e).Position);
                sb.AppendLine();
            });
            var defs = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)!;
            var pq = new QueryDescription().WithAll<PresenterState, PresenterWorldPosition>();
            engine.World.Query(in pq, (Entity e, ref PresenterState s, ref PresenterWorldPosition p) =>
            {
                sb.Append("  presenter def=").Append(defs.GetName(s.DefId)).Append(" pos=").Append(p.Value);
                if (engine.World.Has<PresenterCullState>(e)) sb.Append(" ownerVisible=").Append(engine.World.Get<PresenterCullState>(e).OwnerCullVisible);
                sb.AppendLine();
            });
            TestContext.Out.WriteLine(sb.ToString());
        }
    }
}
