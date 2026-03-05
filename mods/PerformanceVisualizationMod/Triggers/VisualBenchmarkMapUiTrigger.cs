using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Reactive.Core;
using Ludots.UI.Reactive.Widgets;
using Ludots.UI.Widgets;
using Arch.Core;
using SkiaSharp;
using FlexLayoutSharp;
using Ludots.Core.Gameplay; // For Time/Engine if needed
using Ludots.Core.Components; // For VisualTransform if needed
using Ludots.Core.Gameplay.GAS; // For AttributeRegistry
using Ludots.Core.Gameplay.GAS.Components; // For AttributeBuffer
using Ludots.Core.Gameplay.GAS.Registry; // For AttributeRegistry
using System.Numerics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Physics2D.Components;
using Ludots.Platform.Abstractions;

namespace PerformanceVisualizationMod.Triggers
{
    /// <summary>
    /// A system to force UI refresh every frame for immediate mode rendering elements.
    /// </summary>
    public class UiRefreshSystem : Arch.System.BaseSystem<World, float>
    {
        private readonly UIRoot _uiRoot;

        public UiRefreshSystem(World world, UIRoot uiRoot) : base(world)
        {
            _uiRoot = uiRoot;
        }

        public override void Update(in float t)
        {
            if (_uiRoot != null)
            {
                _uiRoot.IsDirty = true;
            }
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
            var engine = context.GetEngine();
            var uiRoot = context.Get<UIRoot>(ContextKeys.UIRoot);
            if (engine == null || uiRoot == null) return Task.CompletedTask;

            // Register UI Refresh System to force redraws
            var refreshSystem = new UiRefreshSystem(engine.World, uiRoot);
            engine.RegisterPresentationSystem(refreshSystem);

            // Retrieve ScreenProjector service
            var screenProjector = context.Get<IScreenProjector>(ContextKeys.ScreenProjector);

            var rootWidget = new FlexNodeWidget();
            Reconciler.Render(
                new Element(typeof(VisualBenchmarkHud), new VisualBenchmarkHudProps
                {
                    World = engine.World,
                    ScreenProjector = screenProjector,
                    RunSimulation = () => engine.TriggerManager.FireEvent(VisualBenchmarkEvents.RunVisualBenchmark, engine.CreateContext()),
                    BackToEntry = () => engine.LoadMap(new MapId(engine.MergedConfig.StartupMapId))
                }),
                rootWidget
            );

            uiRoot.Content = rootWidget;
            uiRoot.IsDirty = true;
            return Task.CompletedTask;
        }
    }

    internal class VisualBenchmarkHudProps
    {
        public World World { get; set; }
        public IScreenProjector ScreenProjector { get; set; }
        public Action RunSimulation { get; set; }
        public Action BackToEntry { get; set; }
    }

    internal class VisualBenchmarkHud : Ludots.UI.Reactive.Core.Component
    {
        public override Element Render()
        {
            var props = Props as VisualBenchmarkHudProps;

            return new Element(typeof(FlexNodeWidget), new
            {
                FlexDirection = FlexDirection.Column,
                JustifyContent = Justify.FlexStart,
                AlignItems = Align.FlexStart,
                WidthPercent = 100f,
                HeightPercent = 100f,
                Padding = 20f
            }, null,
                new Element(typeof(FlexNodeWidget), new
                {
                    Text = "Visual Benchmark Mode",
                    FontSize = 32f,
                    TextColor = SKColors.White,
                    MarginBottom = 20f
                }),
                new Element(typeof(FlexNodeWidget), new
                {
                    FlexDirection = FlexDirection.Row
                }, null, 
                    new Element(typeof(FlexNodeWidget), new
                    {
                        Text = "Run Simulation (100k Entities)",
                        FontSize = 20f,
                        TextColor = SKColors.Black,
                        BackgroundColor = SKColors.Green,
                        Padding = 12f,
                        BorderRadius = 4f,
                        MarginRight = 10f,
                        OnClick = (Action)(() => props?.RunSimulation?.Invoke())
                    }),
                    new Element(typeof(FlexNodeWidget), new
                    {
                        Text = "Back",
                        FontSize = 20f,
                        TextColor = SKColors.White,
                        BackgroundColor = SKColors.Red,
                        Padding = 12f,
                        BorderRadius = 4f,
                        OnClick = (Action)(() => props?.BackToEntry?.Invoke())
                    })
                ),
                // Stats Display
                new Element(typeof(StatsDisplay), new StatsDisplayProps { World = props?.World }),
                
                // Health Bar Overlay (Immediate Mode Rendering)
                // Fix: Must provide Layout properties because HealthBarOverlay is a Widget, not a Component.
                // It needs explicit size/position to be rendered by the layout engine.
                new Element(typeof(HealthBarOverlay), new  
                { 
                    // Layout: Full Screen Overlay
                    Position = PositionType.Absolute,
                    Top = 0f,
                    Left = 0f,
                    WidthPercent = 100f,
                    HeightPercent = 100f,
                    
                    // Props (Mapped to Properties on HealthBarOverlay class)
                    World = props?.World,
                    ScreenProjector = props?.ScreenProjector 
                })
            );
        }
    }

    internal class HealthBarOverlayProps
    {
        public World World { get; set; }
        public IScreenProjector ScreenProjector { get; set; }
    }

    /// <summary>
    /// A high-performance widget that draws health bars for thousands of entities directly to the canvas.
    /// It bypasses the Flex layout engine and React reconciliation for individual bars.
    /// </summary>
    internal class HealthBarOverlay : Widget
    {
        private HealthBarOverlayProps _props = new HealthBarOverlayProps();
        private QueryDescription _query = new QueryDescription().WithAll<VisualTransform, AttributeBuffer, CullState>();
        private int _healthId = -1;
        private SKPaint _bgPaint;
        private SKPaint _fgPaint;

        public HealthBarOverlay()
        {
            // Cache paints
            _bgPaint = new SKPaint { Color = SKColors.Red, IsAntialias = false };
            _fgPaint = new SKPaint { Color = SKColors.Green, IsAntialias = false };
        }

        // Direct Properties for Reflection/Dynamic Assignment by Reconciler
        public World World 
        { 
            set 
            { 
                _props.World = value;
                if (_healthId == -1) 
                {
                     var id = AttributeRegistry.GetId("Health");
                     if (id != AttributeRegistry.InvalidId) _healthId = id;
                }
            } 
        }
        
        public IScreenProjector ScreenProjector
        {
            set { _props.ScreenProjector = value; }
        }

        // Standard Widget Props setter if used manually
        public void SetProps(HealthBarOverlayProps props)
        {
            _props = props;
            if (_healthId == -1)
            {
                var id = AttributeRegistry.GetId("Health");
                if (id != AttributeRegistry.InvalidId) _healthId = id;
            }
        }
        
        protected override void OnRender(SKCanvas canvas)
        {
            // Retry fetching ID if missing
            if (_healthId == -1)
            {
                 var id = AttributeRegistry.GetId("Health");
                 if (id != AttributeRegistry.InvalidId) _healthId = id;
            }

            if (_props?.World == null || _props.ScreenProjector == null) 
            {
                // Console.WriteLine("[HealthBarOverlay] Missing World or Projector");
                return;
            }
            
            // Draw a full screen cross to verify Canvas (Debug)
            // canvas.DrawLine(0, 0, Width, Height, _fgPaint);
            // canvas.DrawLine(0, Height, Width, 0, _fgPaint);

            // Direct Chunk Iteration
            var query = _props.World.Query(in _query);
            foreach (var chunk in query)
            {
                var transforms = chunk.GetArray<VisualTransform>();
                var attributes = chunk.GetArray<AttributeBuffer>();
                var culls = chunk.GetArray<CullState>();

                for (int i = 0; i < chunk.Count; i++)
                {
                    // 1. Culling Check
                    if (!culls[i].IsVisible) continue;
                    
                    // 2. Get Data
                    ref var transform = ref transforms[i];
                    ref var attrBuffer = ref attributes[i];

                    // 3. Project to Screen
                    Vector2 screenPos = _props.ScreenProjector.WorldToScreen(transform.Position);

                    // 4. Screen Space Culling (using Widget Width/Height)
                    // Be generous with bounds
                    if (screenPos.X < -50 || screenPos.Y < -50 || screenPos.X > Width + 50 || screenPos.Y > Height + 50) continue;

                    // 5. Calculate Health %
                    float pct = 1.0f;
                    if (_healthId != -1)
                    {
                        float current = attrBuffer.GetCurrent(_healthId);
                        float max = attrBuffer.GetBase(_healthId);
                        if (max <= 0) max = 100;
                        pct = current / max;
                    }
                    else
                    {
                        // Fallback if attribute missing
                        pct = 0.75f;
                    }

                    // 6. Draw Bar
                    float barWidth = 40f;
                    float barHeight = 6f;
                    float x = screenPos.X - barWidth / 2;
                    float y = screenPos.Y - 40f; // Offset above head

                    // Draw Background (Red)
                    canvas.DrawRect(x, y, barWidth, barHeight, _bgPaint);
                    
                    // Draw Foreground (Green)
                    canvas.DrawRect(x, y, barWidth * pct, barHeight, _fgPaint);
                }
            }
        }
    }

    internal class StatsDisplayProps
    {
        public World World { get; set; }
    }

    internal class StatsDisplay : Ludots.UI.Reactive.Core.Component
    {
        private System.Timers.Timer _timer;
        private int _fps;
        private int _totalEntities;
        private int _visibleEntities;
        private int _fixedHz;
        private int _physicsHz;
        private int _physicsSteps;
        private double _physicsMs;
        private int _potentialPairs;
        private int _contactPairs;
        private QueryDescription _visualQuery = new QueryDescription().WithAll<VisualTransform>();
        private QueryDescription _visibleQuery = new QueryDescription().WithAll<VisualTransform, CullState>();
        private QueryDescription _physicsStatsQuery = new QueryDescription().WithAll<Physics2DPerfStats>();

        public StatsDisplay()
        {
            _timer = new System.Timers.Timer(500); // Update every 500ms
            _timer.Elapsed += (s, e) => UpdateStats();
            _timer.Start();
        }

        private void UpdateStats()
        {
            var props = Props as StatsDisplayProps;
            if (props?.World == null) return;

            // Calculate FPS
            if (Ludots.Core.Engine.Time.DeltaTime > 0)
            {
                _fps = (int)(1.0f / Ludots.Core.Engine.Time.DeltaTime);
            }

            // Count Entities
            _totalEntities = props.World.CountEntities(in _visualQuery);
            
            // Count Visible Entities
            _visibleEntities = 0;
            var query = props.World.Query(in _visibleQuery);
            foreach(var chunk in query)
            {
                var culls = chunk.GetArray<CullState>();
                for(int i=0; i<chunk.Count; i++)
                {
                    if (culls[i].IsVisible) _visibleEntities++;
                }
            }

            _fixedHz = 0;
            _physicsHz = 0;
            _physicsSteps = 0;
            _physicsMs = 0;
            _potentialPairs = 0;
            _contactPairs = 0;

            var statsQuery = props.World.Query(in _physicsStatsQuery);
            foreach (var chunk in statsQuery)
            {
                var stats = chunk.GetArray<Physics2DPerfStats>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    _fixedHz = stats[i].FixedHz;
                    _physicsHz = stats[i].PhysicsHz;
                    _physicsSteps = stats[i].PhysicsStepsLastFixedTick;
                    _physicsMs = stats[i].PhysicsUpdateMs;
                    _potentialPairs = stats[i].PotentialPairs;
                    _contactPairs = stats[i].ContactPairs;
                }
            }
            
            // Force update
            SetState(() => { }); 
        }

        public override Element Render()
        {
            return new Element(typeof(FlexNodeWidget), new
            {
                Position = PositionType.Absolute,
                Bottom = 20f,
                Left = 20f,
                Padding = 10f,
                BackgroundColor = new SKColor(0, 0, 0, 128),
                BorderRadius = 8f
            }, null,
                new Element(typeof(FlexNodeWidget), new
                {
                    Text = $"Entities: {_totalEntities}",
                    FontSize = 20f,
                    TextColor = SKColors.White,
                    MarginBottom = 5f
                }),
                new Element(typeof(FlexNodeWidget), new
                {
                    Text = $"Visible: {_visibleEntities}",
                    FontSize = 20f,
                    TextColor = SKColors.Green,
                    MarginBottom = 5f
                }),
                new Element(typeof(FlexNodeWidget), new
                {
                    Text = $"FPS: {_fps}", 
                    FontSize = 20f,
                    TextColor = SKColors.Yellow
                }),
                new Element(typeof(FlexNodeWidget), new
                {
                    Text = $"FixedHz: {_fixedHz}  PhysicsHz: {_physicsHz}  Steps: {_physicsSteps}",
                    FontSize = 16f,
                    TextColor = SKColors.White,
                    MarginTop = 6f
                }),
                new Element(typeof(FlexNodeWidget), new
                {
                    Text = $"Pairs: {_contactPairs}/{_potentialPairs}  PhysicsMs: {_physicsMs:0.00}",
                    FontSize = 16f,
                    TextColor = SKColors.White
                })
            );
        }
    }
}
