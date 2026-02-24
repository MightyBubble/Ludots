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

namespace GasBenchmarkMod.Triggers
{
    public class GasBenchmarkEntryMenuTrigger : Trigger
    {
        public GasBenchmarkEntryMenuTrigger()
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
            if (uiRoot == null)
            {
                Console.WriteLine("[GasBenchmarkMod] UIRoot missing in ScriptContext (entry).");
                return Task.CompletedTask;
            }

            if (uiRoot.Content == null)
            {
                var rootWidget = new FlexNodeWidget();
                Reconciler.Render(
                    new Element(
                        typeof(GasBenchmarkEntryMenu),
                        new GasBenchmarkEntryMenuProps
                        {
                            OpenGasBenchmark = () => engine.LoadMap(GasBenchmarkMapIds.GasBenchmark)
                        }
                    ),
                    rootWidget
                );

                uiRoot.Content = rootWidget;
                uiRoot.IsDirty = true;
                Console.WriteLine("[GasBenchmarkMod] Entry menu mounted.");
                return Task.CompletedTask;
            }

            if (uiRoot.Content is not FlexNodeWidget existingRoot) return Task.CompletedTask;
            if (ContainsGasBenchmarkButton(existingRoot)) return Task.CompletedTask;

            var wrapper = new FlexNodeWidget();
            wrapper.SetWidthPercent(100f);
            wrapper.SetHeightPercent(100f);

            existingRoot.SetWidthPercent(100f);
            existingRoot.SetHeightPercent(100f);
            existingRoot.FlexGrow = 1f;

            var openButton = new FlexNodeWidget
            {
                Text = "GAS Benchmark",
                FontSize = 18f,
                TextColor = SKColors.Black,
                BackgroundColor = SKColors.Gold,
                Padding = 12f,
                BorderRadius = 10f,
                OnClick = () => engine.LoadMap(GasBenchmarkMapIds.GasBenchmark)
            };
            openButton.PositionType = PositionType.Absolute;
            openButton.FlexNode.StyleSetPosition(Edge.Right, 18f);
            openButton.FlexNode.StyleSetPosition(Edge.Bottom, 18f);

            wrapper.AddChild(existingRoot);
            wrapper.AddChild(openButton);

            uiRoot.Content = wrapper;
            uiRoot.IsDirty = true;
            Console.WriteLine("[GasBenchmarkMod] Added GAS Benchmark floating button on entry.");
            return Task.CompletedTask;
        }

        private static bool ContainsGasBenchmarkButton(FlexNodeWidget root)
        {
            if (string.Equals(root.Text, "GAS Benchmark", StringComparison.Ordinal))
            {
                return true;
            }

            foreach (var child in root.Children)
            {
                if (ContainsGasBenchmarkButton(child)) return true;
            }

            return false;
        }
    }

    internal sealed class GasBenchmarkEntryMenuProps
    {
        public Action OpenGasBenchmark { get; set; }
    }

    public class GasBenchmarkEntryMenu : Component
    {
        public override Element Render()
        {
            var props = Props as GasBenchmarkEntryMenuProps;

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
                    Text = "GAS BENCHMARK",
                    FontSize = 54f,
                    TextColor = SKColors.White,
                    MarginBottom = 20f
                }),
                new Element(typeof(FlexNodeWidget), new
                {
                    Text = "Entry menu: open GAS benchmark map from here.",
                    FontSize = 20f,
                    TextColor = SKColors.LightGray,
                    MarginBottom = 40f
                }),
                new Element(typeof(FlexNodeWidget), new
                {
                    Text = "Open GAS Benchmark Map",
                    FontSize = 28f,
                    TextColor = SKColors.Black,
                    BackgroundColor = SKColors.Gold,
                    Padding = 18f,
                    BorderRadius = 10f,
                    MarginBottom = 16f,
                    OnClick = (Action)(() => props?.OpenGasBenchmark?.Invoke())
                })
            );
        }
    }
}
