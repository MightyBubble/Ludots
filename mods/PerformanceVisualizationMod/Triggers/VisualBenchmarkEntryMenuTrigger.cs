using System;
using System.Threading.Tasks;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using SkiaSharp;

namespace PerformanceVisualizationMod.Triggers
{
    public sealed class VisualBenchmarkEntryMenuTrigger : Trigger
    {
        public VisualBenchmarkEntryMenuTrigger()
        {
            EventKey = GameEvents.MapLoaded;
            AddCondition(ctx =>
            {
                var engine = ctx.GetEngine();
                return engine?.MergedConfig != null && ctx.IsMap(new MapId(engine.MergedConfig.StartupMapId));
            });
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null || context.Get(CoreServiceKeys.UIRoot) is not UIRoot uiRoot)
            {
                return Task.CompletedTask;
            }

            uiRoot.MountScene(CreateScene(() => engine.LoadMap(VisualBenchmarkMapIds.VisualBenchmark)));
            uiRoot.IsDirty = true;
            return Task.CompletedTask;
        }

        private static UiScene CreateScene(Action openVisualBenchmark)
        {
            var scene = new UiScene();
            int nextId = 1;
            scene.Mount(
                Ui.Column(
                        Ui.Text("Ludots Visual Benchmark")
                            .FontSize(48f)
                            .Bold()
                            .Color(SKColors.White),
                        BuildButton("Start Visual Benchmark", SKColors.White, SKColors.Black, _ => openVisualBenchmark()))
                    .WidthPercent(100f)
                    .HeightPercent(100f)
                    .Justify(UiJustifyContent.Center)
                    .Align(UiAlignItems.Center)
                    .Background(SKColors.Black)
                    .Gap(24f)
                    .Build(scene.Dispatcher, ref nextId));
            return scene;
        }

        private static UiElementBuilder BuildButton(string text, SKColor background, SKColor foreground, Action<UiActionContext> onClick)
        {
            return Ui.Button(text, onClick)
                .FontSize(24f)
                .Padding(20f, 16f)
                .Radius(8f)
                .Background(background)
                .Color(foreground);
        }
    }
}
