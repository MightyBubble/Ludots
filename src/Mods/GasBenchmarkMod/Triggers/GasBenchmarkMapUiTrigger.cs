using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Reactive.Core;
using Ludots.UI.Reactive.Widgets;
using SkiaSharp;
using FlexLayoutSharp;

namespace GasBenchmarkMod.Triggers
{
    public class GasBenchmarkMapUiTrigger : Trigger
    {
        public GasBenchmarkMapUiTrigger()
        {
            EventKey = GameEvents.MapLoaded;
            AddCondition(ctx => ctx.IsMap(GasBenchmarkMapIds.GasBenchmark));
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            Console.WriteLine("[GasBenchmarkMod] MapLoaded: gas_benchmark (mounting UI)...");

            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            var uiRoot = context.Get<UIRoot>(ContextKeys.UIRoot);
            if (uiRoot == null)
            {
                Console.WriteLine("[GasBenchmarkMod] UIRoot missing in ScriptContext.");
                return Task.CompletedTask;
            }

            var rootWidget = new FlexNodeWidget();
            Reconciler.Render(
                new Element(
                    typeof(GasBenchmarkMenu),
                    new GasBenchmarkMenuProps
                    {
                        RunBenchmark = () =>
                        {
                            Console.WriteLine("[GasBenchmarkMod] UI click: Run GAS Benchmark");
                            engine.TriggerManager.FireEvent(GasBenchmarkEvents.RunGasBenchmark, engine.CreateContext());
                        },
                        BackToEntry = () => engine.LoadMap(new Ludots.Core.Map.MapId(engine.MergedConfig.StartupMapId))
                    }
                ),
                rootWidget
            );

            uiRoot.Content = rootWidget;
            uiRoot.IsDirty = true;
            Console.WriteLine("[GasBenchmarkMod] UI mounted for gas_benchmark.");
            return Task.CompletedTask;
        }
    }

    internal sealed class GasBenchmarkMenuProps
    {
        public Action RunBenchmark { get; set; }
        public Action BackToEntry { get; set; }
    }

    public class GasBenchmarkMenu : Component
    {
        public override Element Render()
        {
            var props = Props as GasBenchmarkMenuProps;

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
                    Text = "Click to spawn 100000 entities and run GAS benchmark.",
                    FontSize = 20f,
                    TextColor = SKColors.LightGray,
                    MarginBottom = 40f
                }),
                new Element(typeof(FlexNodeWidget), new
                {
                    Text = "Run GAS Benchmark",
                    FontSize = 28f,
                    TextColor = SKColors.Black,
                    BackgroundColor = SKColors.Gold,
                    Padding = 18f,
                    BorderRadius = 10f,
                    MarginBottom = 16f,
                    OnClick = (Action)(() => props?.RunBenchmark?.Invoke())
                }),
                new Element(typeof(FlexNodeWidget), new
                {
                    Text = "Back to Entry",
                    FontSize = 22f,
                    TextColor = SKColors.White,
                    BackgroundColor = SKColors.DimGray,
                    Padding = 14f,
                    BorderRadius = 10f,
                    OnClick = (Action)(() => props?.BackToEntry?.Invoke())
                })
            );
        }
    }
}
