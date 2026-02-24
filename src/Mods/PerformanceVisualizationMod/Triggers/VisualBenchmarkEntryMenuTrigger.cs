using System;
using System.Threading.Tasks;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Reactive.Core;
using Ludots.UI.Reactive.Widgets;
using SkiaSharp;
using FlexLayoutSharp;

namespace PerformanceVisualizationMod.Triggers
{
    public class VisualBenchmarkEntryMenuTrigger : Trigger
    {
        public VisualBenchmarkEntryMenuTrigger()
        {
            EventKey = GameEvents.MapLoaded;
            AddCondition(ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine?.MergedConfig == null) return false;
                return ctx.IsMap(new MapId(engine.MergedConfig.StartupMapId));
            });
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            var uiRoot = context.Get<UIRoot>(ContextKeys.UIRoot);
            if (engine == null || uiRoot == null) return Task.CompletedTask;

            var rootWidget = new FlexNodeWidget();
            Reconciler.Render(
                new Element(typeof(VisualBenchmarkEntryMenu), new VisualBenchmarkEntryMenuProps
                {
                    OpenVisualBenchmark = () => engine.LoadMap(VisualBenchmarkMapIds.VisualBenchmark)
                }),
                rootWidget
            );

            uiRoot.Content = rootWidget;
            uiRoot.IsDirty = true;
            return Task.CompletedTask;
        }
    }

    internal class VisualBenchmarkEntryMenuProps
    {
        public Action OpenVisualBenchmark { get; set; } = () => { };
    }

    internal class VisualBenchmarkEntryMenu : Ludots.UI.Reactive.Core.Component
    {
        public override Element Render()
        {
            var props = Props as VisualBenchmarkEntryMenuProps;
            
            return new Element(typeof(FlexNodeWidget), new
            {
                FlexDirection = FlexDirection.Column,
                JustifyContent = Justify.Center,
                AlignItems = Align.Center,
                WidthPercent = 100f,
                HeightPercent = 100f,
                BackgroundColor = SKColors.Black
            }, null,
                new Element(typeof(FlexNodeWidget), new
                {
                    Text = "Ludots Visual Benchmark",
                    FontSize = 48f,
                    TextColor = SKColors.White,
                    MarginBottom = 40f
                }),
                new Element(typeof(FlexNodeWidget), new
                {
                    Text = "Start Visual Benchmark",
                    FontSize = 24f,
                    TextColor = SKColors.Black,
                    BackgroundColor = SKColors.White,
                    Padding = 20f,
                    BorderRadius = 8f,
                    OnClick = (Action)(() => props?.OpenVisualBenchmark?.Invoke())
                })
            );
        }
    }
}
