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

namespace GasBenchmarkMod.Triggers
{
    public sealed class GasBenchmarkEntryMenuTrigger : Trigger
    {
        public GasBenchmarkEntryMenuTrigger()
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
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (context.Get(CoreServiceKeys.UIRoot) is not UIRoot uiRoot)
            {
                Console.WriteLine("[GasBenchmarkMod] UIRoot missing in ScriptContext (entry).");
                return Task.CompletedTask;
            }

            var textMeasurer = (IUiTextMeasurer)context.Get(CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)context.Get(CoreServiceKeys.UiImageSizeProvider);
            uiRoot.MountScene(CreateScene(textMeasurer, imageSizeProvider, () => engine.LoadMap(GasBenchmarkMapIds.GasBenchmark)));
            uiRoot.IsDirty = true;
            Console.WriteLine("[GasBenchmarkMod] Entry menu mounted.");
            return Task.CompletedTask;
        }

        private static UiScene CreateScene(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider, Action openGasBenchmark)
        {
            var scene = new UiScene(textMeasurer, imageSizeProvider);
            int nextId = 1;
            scene.Mount(
                Ui.Column(
                        Ui.Text("GAS BENCHMARK")
                            .FontSize(54f)
                            .Bold()
                            .Color(UiColor.White),
                        Ui.Text("Entry menu: open GAS benchmark map from here.")
                            .FontSize(20f)
                            .Color(UiColor.LightGray),
                        BuildButton("Open GAS Benchmark Map", UiColor.Gold, UiColor.Black, _ => openGasBenchmark()))
                    .WidthPercent(100f)
                    .HeightPercent(100f)
                    .Justify(UiJustifyContent.Center)
                    .Align(UiAlignItems.Center)
                    .Background(new UiColor(0, 0, 0, 200))
                    .Gap(18f)
                    .Build(scene.Dispatcher, ref nextId));
            return scene;
        }

        private static UiElementBuilder BuildButton(string text, UiColor background, UiColor foreground, Action<UiActionContext> onClick)
        {
            return Ui.Button(text, onClick)
                .FontSize(28f)
                .Padding(18f, 14f)
                .Radius(10f)
                .Background(background)
                .Color(foreground);
        }
    }
}
