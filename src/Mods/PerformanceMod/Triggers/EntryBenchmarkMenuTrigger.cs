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

namespace PerformanceMod.Triggers
{
    public class EntryBenchmarkMenuTrigger : Trigger
    {
        public EntryBenchmarkMenuTrigger()
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
            if (engine == null) return Task.CompletedTask;

            var uiRoot = context.Get<UIRoot>(ContextKeys.UIRoot);
            if (uiRoot == null) return Task.CompletedTask;

            var rootWidget = new FlexNodeWidget();
            Reconciler.Render(
                new Element(
                    typeof(EntryBenchmarkMenu),
                    new EntryBenchmarkMenuProps
                    {
                        GoBenchmark = () => engine.LoadMap(PerformanceMapIds.Benchmark),
                        GoEntry = () => engine.LoadMap(new MapId(engine.MergedConfig.StartupMapId))
                    }
                ),
                rootWidget
            );

            uiRoot.Content = rootWidget;
            uiRoot.IsDirty = true;
            return Task.CompletedTask;
        }
    }

    internal sealed class EntryBenchmarkMenuProps
    {
        public Action GoBenchmark { get; set; }
        public Action GoEntry { get; set; }
    }

    public class EntryBenchmarkMenu : Component
    {
        public override Element Render()
        {
            var props = Props as EntryBenchmarkMenuProps;

            return new Element(typeof(FlexNodeWidget), new
            {
                FlexDirection = FlexDirection.Column,
                JustifyContent = Justify.Center,
                AlignItems = Align.Center,
                BackgroundColor = new SKColor(0, 0, 0, 200),
                WidthPercent = 100f,
                HeightPercent = 100f
            }, null,
                new Element(typeof(FlexNodeWidget), new
                {
                    Text = "PERFORMANCE",
                    FontSize = 54f,
                    TextColor = SKColors.White,
                    MarginBottom = 20f
                }),
                new Element(typeof(FlexNodeWidget), new
                {
                    Text = "Entry menu: open benchmark map from here.",
                    FontSize = 20f,
                    TextColor = SKColors.LightGray,
                    MarginBottom = 40f
                }),
                new Element(typeof(FlexNodeWidget), new
                {
                    Text = "Open Benchmark Map",
                    FontSize = 28f,
                    TextColor = SKColors.Black,
                    BackgroundColor = SKColors.Gold,
                    Padding = 18f,
                    BorderRadius = 10f,
                    MarginBottom = 16f,
                    OnClick = (Action)(() => props?.GoBenchmark?.Invoke())
                }),
                new Element(typeof(FlexNodeWidget), new
                {
                    Text = "Back to Entry",
                    FontSize = 22f,
                    TextColor = SKColors.White,
                    BackgroundColor = SKColors.DimGray,
                    Padding = 14f,
                    BorderRadius = 10f,
                    OnClick = (Action)(() => props?.GoEntry?.Invoke())
                })
            );
        }
    }
}
