using System;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Map;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Skia;
using SkiaSharp;

namespace PerformanceVisualizationMod.Triggers
{
    public sealed class UiRefreshSystem : BaseSystem<World, float>
    {
        private readonly UIRoot _uiRoot;

        public UiRefreshSystem(World world, UIRoot uiRoot)
            : base(world)
        {
            _uiRoot = uiRoot;
        }

        public override void Update(in float t)
        {
            _uiRoot.IsDirty = true;
        }
    }

    public sealed class VisualBenchmarkMapUiTrigger : Trigger
    {
        private const string RefreshSystemRegisteredKey = "PerformanceVisualizationMod.UiRefreshSystemRegistered";

        private static readonly QueryDescription VisualQuery = new QueryDescription().WithAll<VisualTransform>();
        private static readonly QueryDescription VisibleQuery = new QueryDescription().WithAll<VisualTransform, CullState>();
        private static readonly QueryDescription HealthBarQuery = new QueryDescription().WithAll<VisualTransform, AttributeBuffer, CullState>();
        private static readonly QueryDescription PhysicsStatsQuery = new QueryDescription().WithAll<Physics2DPerfStats>();

        private static readonly SKPaint HealthBarBackgroundPaint = new SKPaint
        {
            Color = SKColors.Red,
            IsAntialias = false
        };

        private static readonly SKPaint HealthBarForegroundPaint = new SKPaint
        {
            Color = SKColors.Green,
            IsAntialias = false
        };

        private static readonly SKPaint StatsPanelPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 176),
            IsAntialias = true
        };

        private static readonly SKPaint StatsTextPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };

        private static readonly SKFont StatsFont = new SKFont(SKTypeface.Default, 18f);

        public VisualBenchmarkMapUiTrigger()
        {
            EventKey = GameEvents.MapLoaded;
            AddCondition(ctx => ctx.IsMap(VisualBenchmarkMapIds.VisualBenchmark));
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null || context.Get(CoreServiceKeys.UIRoot) is not UIRoot uiRoot)
            {
                return Task.CompletedTask;
            }

            EnsureRefreshSystem(engine, uiRoot);
            uiRoot.MountScene(CreateScene(engine, context.Get(CoreServiceKeys.ScreenProjector)));
            uiRoot.IsDirty = true;
            return Task.CompletedTask;
        }

        private static void EnsureRefreshSystem(GameEngine engine, UIRoot uiRoot)
        {
            if (engine.GlobalContext.ContainsKey(RefreshSystemRegisteredKey))
            {
                return;
            }

            engine.RegisterPresentationSystem(new UiRefreshSystem(engine.World, uiRoot));
            engine.GlobalContext[RefreshSystemRegisteredKey] = true;
        }

        private static UiScene CreateScene(GameEngine engine, IScreenProjector? screenProjector)
        {
            var scene = new UiScene(new SkiaTextMeasurer(), new SkiaImageSizeProvider());
            int nextId = 1;
            scene.Mount(BuildRoot(engine, screenProjector).Build(scene.Dispatcher, ref nextId));
            return scene;
        }

        private static UiElementBuilder BuildRoot(GameEngine engine, IScreenProjector? screenProjector)
        {
            return new UiElementBuilder(UiNodeKind.Container, "div")
                .WidthPercent(100f)
                .HeightPercent(100f)
                .Children(
                    Ui.Canvas(new UiCanvasContent((canvas, rect) => DrawOverlay(canvas, rect, engine, screenProjector)))
                        .WidthPercent(100f)
                        .HeightPercent(100f)
                        .Absolute(0f, 0f),
                    Ui.Card(
                            Ui.Text("Visual Benchmark Mode")
                                .FontSize(32f)
                                .Bold()
                                .Color(SKColors.White.ToUiColor()),
                            Ui.Text("100k entity simulation with unified UI canvas overlay.")
                                .FontSize(16f)
                                .Color(SKColors.LightGray.ToUiColor()),
                            Ui.Row(
                                    BuildButton("Run Simulation", SKColors.Green.ToUiColor(), SKColors.Black.ToUiColor(), _ =>
                                        engine.TriggerManager.FireEvent(VisualBenchmarkEvents.RunVisualBenchmark, engine.CreateContext())),
                                    BuildButton("Back", SKColors.Red.ToUiColor(), SKColors.White.ToUiColor(), _ =>
                                        engine.LoadMap(new MapId(engine.MergedConfig.StartupMapId))))
                                .Gap(10f)
                                .Wrap(),
                            Ui.Text("Canvas draws health bars and live counters without reintroducing a second widget runtime.")
                                .FontSize(12f)
                                .Color(new SKColor(210, 220, 230).ToUiColor()))
                        .Width(420f)
                        .Padding(16f)
                        .Gap(10f)
                        .Radius(14f)
                        .Background(new SKColor(12, 18, 24, 220).ToUiColor())
                        .Border(1f, new SKColor(56, 72, 88).ToUiColor())
                        .Absolute(20f, 20f)
                        .ZIndex(10));
        }

        private static UiElementBuilder BuildButton(string text, UiColor background, UiColor foreground, Action<UiActionContext> onClick)
        {
            return Ui.Button(text, onClick)
                .FontSize(20f)
                .Padding(12f, 10f)
                .Radius(8f)
                .Background(background)
                .Color(foreground);
        }

        private static void DrawOverlay(SKCanvas canvas, SKRect rect, GameEngine engine, IScreenProjector? screenProjector)
        {
            if (screenProjector == null)
            {
                return;
            }

            DrawHealthBars(canvas, rect, engine.World, screenProjector);
            DrawStats(canvas, rect, engine.World);
        }

        private static void DrawHealthBars(SKCanvas canvas, SKRect rect, World world, IScreenProjector screenProjector)
        {
            int healthId = AttributeRegistry.GetId("Health");
            var query = world.Query(in HealthBarQuery);
            foreach (var chunk in query)
            {
                var transforms = chunk.GetArray<VisualTransform>();
                var attributes = chunk.GetArray<AttributeBuffer>();
                var culls = chunk.GetArray<CullState>();

                for (int i = 0; i < chunk.Count; i++)
                {
                    if (!culls[i].IsVisible)
                    {
                        continue;
                    }

                    Vector2 screenPos = screenProjector.WorldToScreen(transforms[i].Position);
                    if (screenPos.X < -50f || screenPos.Y < -50f || screenPos.X > rect.Width + 50f || screenPos.Y > rect.Height + 50f)
                    {
                        continue;
                    }

                    float pct = 1f;
                    if (healthId != AttributeRegistry.InvalidId)
                    {
                        float current = attributes[i].GetCurrent(healthId);
                        float max = attributes[i].GetBase(healthId);
                        pct = max > 0f ? Math.Clamp(current / max, 0f, 1f) : 1f;
                    }

                    const float barWidth = 40f;
                    const float barHeight = 6f;
                    float x = screenPos.X - barWidth * 0.5f;
                    float y = screenPos.Y - 40f;
                    canvas.DrawRect(x, y, barWidth, barHeight, HealthBarBackgroundPaint);
                    canvas.DrawRect(x, y, barWidth * pct, barHeight, HealthBarForegroundPaint);
                }
            }
        }

        private static void DrawStats(SKCanvas canvas, SKRect rect, World world)
        {
            int totalEntities = world.CountEntities(in VisualQuery);
            int visibleEntities = 0;

            var visibleQuery = world.Query(in VisibleQuery);
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
            int potentialPairs = 0;
            int contactPairs = 0;
            double physicsMs = 0d;

            var statsQuery = world.Query(in PhysicsStatsQuery);
            foreach (var chunk in statsQuery)
            {
                var stats = chunk.GetArray<Physics2DPerfStats>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    fixedHz = stats[i].FixedHz;
                    physicsHz = stats[i].PhysicsHz;
                    physicsSteps = stats[i].PhysicsStepsLastFixedTick;
                    potentialPairs = stats[i].PotentialPairs;
                    contactPairs = stats[i].ContactPairs;
                    physicsMs = stats[i].PhysicsUpdateMs;
                }
            }

            int fps = Ludots.Core.Engine.Time.DeltaTime > 0f
                ? (int)(1f / Ludots.Core.Engine.Time.DeltaTime)
                : 0;

            const float panelWidth = 360f;
            const float panelHeight = 116f;
            float x = 20f;
            float y = rect.Height - panelHeight - 20f;
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + panelWidth, y + panelHeight), 12f, 12f), StatsPanelPaint);

            float lineY = y + 28f;
            canvas.DrawText($"Entities: {totalEntities}", x + 14f, lineY, SKTextAlign.Left, StatsFont, StatsTextPaint);
            lineY += 22f;
            canvas.DrawText($"Visible: {visibleEntities}    FPS: {fps}", x + 14f, lineY, SKTextAlign.Left, StatsFont, StatsTextPaint);
            lineY += 22f;
            canvas.DrawText($"FixedHz: {fixedHz}  PhysicsHz: {physicsHz}  Steps: {physicsSteps}", x + 14f, lineY, SKTextAlign.Left, StatsFont, StatsTextPaint);
            lineY += 22f;
            canvas.DrawText($"Pairs: {contactPairs}/{potentialPairs}  PhysicsMs: {physicsMs:0.00}", x + 14f, lineY, SKTextAlign.Left, StatsFont, StatsTextPaint);
        }
    }
}
