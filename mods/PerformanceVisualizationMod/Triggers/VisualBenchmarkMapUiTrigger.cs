using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;

namespace PerformanceVisualizationMod.Triggers
{
    public sealed class VisualBenchmarkStatsOverlaySystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly QueryDescription _visualQuery = new QueryDescription().WithAll<VisualTransform>();
        private readonly QueryDescription _visibleQuery = new QueryDescription().WithAll<VisualTransform, CullState>();
        private readonly QueryDescription _physicsStatsQuery = new QueryDescription().WithAll<Physics2DPerfStats>();

        public VisualBenchmarkStatsOverlaySystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float t)
        {
            if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.ScreenOverlayBuffer.Name, out var overlayObj) || overlayObj is not ScreenOverlayBuffer overlay)
            {
                return;
            }

            int totalEntities = _engine.World.CountEntities(in _visualQuery);
            int visibleEntities = 0;
            var visibleQuery = _engine.World.Query(in _visibleQuery);
            foreach (var chunk in visibleQuery)
            {
                var culls = chunk.GetArray<CullState>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (culls[i].IsVisible)
                    {
                        visibleEntities++;
                    }
                }
            }

            int fixedHz = 0;
            int physicsHz = 0;
            int physicsSteps = 0;
            double physicsMs = 0;
            int potentialPairs = 0;
            int contactPairs = 0;
            var statsQuery = _engine.World.Query(in _physicsStatsQuery);
            foreach (var chunk in statsQuery)
            {
                var stats = chunk.GetArray<Physics2DPerfStats>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    fixedHz = stats[i].FixedHz;
                    physicsHz = stats[i].PhysicsHz;
                    physicsSteps = stats[i].PhysicsStepsLastFixedTick;
                    physicsMs = stats[i].PhysicsUpdateMs;
                    potentialPairs = stats[i].PotentialPairs;
                    contactPairs = stats[i].ContactPairs;
                }
            }

            int fps = Ludots.Core.Engine.Time.DeltaTime > 0 ? (int)(1f / Ludots.Core.Engine.Time.DeltaTime) : 0;
            overlay.AddRect(16, 520, 420, 122, new Vector4(0f, 0f, 0f, 0.70f), new Vector4(0.3f, 0.7f, 1f, 0.8f));
            overlay.AddText(28, 532, "Visual Benchmark Stats", 16, new Vector4(1f, 0.92f, 0.35f, 1f));
            overlay.AddText(28, 556, $"Entities: {totalEntities}  Visible: {visibleEntities}  FPS: {fps}", 14, new Vector4(1f, 1f, 1f, 0.95f));
            overlay.AddText(28, 578, $"FixedHz: {fixedHz}  PhysicsHz: {physicsHz}  Steps: {physicsSteps}", 14, new Vector4(1f, 1f, 1f, 0.95f));
            overlay.AddText(28, 600, $"Pairs: {contactPairs}/{potentialPairs}  PhysicsMs: {physicsMs:0.00}", 14, new Vector4(1f, 1f, 1f, 0.95f));
            overlay.AddText(28, 622, "Health bars migrated off legacy widget path; benchmark HUD now uses runtime UI + overlay metrics.", 12, new Vector4(0.78f, 0.84f, 0.94f, 0.9f));
        }
    }

    public class VisualBenchmarkMapUiTrigger : Trigger
    {
        public VisualBenchmarkMapUiTrigger()
        {
            EventKey = GameEvents.MapLoaded;
            AddCondition(ctx => ctx.IsMap(VisualBenchmarkMapIds.VisualBenchmark));
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            GameEngine? engine = context.GetEngine();
            UIRoot? uiRoot = context.Get(CoreServiceKeys.UIRoot) as UIRoot;
            if (engine == null || uiRoot == null)
            {
                return Task.CompletedTask;
            }

            engine.RegisterPresentationSystem(new VisualBenchmarkStatsOverlaySystem(engine));

            UiScene scene = UiSceneComposer.Compose(
                Ui.Column(
                        Ui.Text("Visual Benchmark Mode").FontSize(32).Bold(),
                        Ui.Row(
                            Ui.Button("Run Simulation (100k Entities)", _ => engine.TriggerManager.FireEvent(VisualBenchmarkEvents.RunVisualBenchmark, engine.CreateContext())),
                            Ui.Button("Back", _ => engine.LoadMap(new MapId(engine.MergedConfig.StartupMapId))).Background("#444444"))
                            .Gap(10),
                        Ui.Text("Runtime UI owns the menu; world metrics render via ScreenOverlayBuffer.").FontSize(18).Color("#E0E0E0"))
                    .Width(1280)
                    .Height(720)
                    .Gap(20)
                    .Padding(20)
                    .Justify(UiJustifyContent.Start)
                    .Align(UiAlignItems.Start));

            uiRoot.MountScene(scene);
            uiRoot.IsDirty = true;
            return Task.CompletedTask;
        }
    }
}
