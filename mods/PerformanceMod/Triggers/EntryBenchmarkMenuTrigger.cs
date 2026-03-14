using System.Threading.Tasks;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Skia;
using SkiaSharp;

namespace PerformanceMod.Triggers
{
    public sealed class EntryBenchmarkMenuTrigger : Trigger
    {
        public EntryBenchmarkMenuTrigger()
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

            UiScene scene = CreateScene(
                () => engine.LoadMap(PerformanceMapIds.Benchmark),
                () => engine.LoadMap(new MapId(engine.MergedConfig.StartupMapId)));
            uiRoot.MountScene(scene);
            uiRoot.IsDirty = true;
            return Task.CompletedTask;
        }

        private static UiScene CreateScene(System.Action goBenchmark, System.Action goEntry)
        {
            var scene = new UiScene(new SkiaTextMeasurer(), new SkiaImageSizeProvider());
            int nextId = 1;
            scene.Mount(
                Ui.Column(
                        Ui.Text("PERFORMANCE")
                            .FontSize(54f)
                            .Bold()
                            .Color(SKColors.White.ToUiColor()),
                        Ui.Text("Entry menu: open benchmark map from here.")
                            .FontSize(20f)
                            .Color(SKColors.LightGray.ToUiColor())
                            .Margin(0f, 12f),
                        BuildButton("Open Benchmark Map", SKColors.Gold.ToUiColor(), SKColors.Black.ToUiColor(), _ => goBenchmark()),
                        BuildButton("Back to Entry", SKColors.DimGray.ToUiColor(), SKColors.White.ToUiColor(), _ => goEntry()))
                    .WidthPercent(100f)
                    .HeightPercent(100f)
                    .Justify(UiJustifyContent.Center)
                    .Align(UiAlignItems.Center)
                    .Background(new SKColor(0, 0, 0, 200).ToUiColor())
                    .Gap(16f)
                    .Build(scene.Dispatcher, ref nextId));
            return scene;
        }

        private static UiElementBuilder BuildButton(string text, UiColor background, UiColor foreground, System.Action<UiActionContext> onClick)
        {
            return Ui.Button(text, onClick)
                .FontSize(24f)
                .Padding(18f, 14f)
                .Radius(10f)
                .Background(background)
                .Color(foreground)
                .Width(260f);
        }
    }
}
