using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using UiTestMod.Maps;
using System.Threading.Tasks;

namespace UiTestMod.Triggers
{
    public class UiStartTrigger : Trigger
    {
        public UiStartTrigger()
        {
            EventKey = GameEvents.MapLoaded;
            AddCondition(ctx => ctx.IsMap<UiTestMap>());
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            UIRoot? uiRoot = context.Get(CoreServiceKeys.UIRoot) as UIRoot;
            if (uiRoot == null)
            {
                return Task.CompletedTask;
            }

            UiElementBuilder root = Ui.Column(
                    Ui.Row(
                        Ui.Text("LUDOTS UI TEST").FontSize(30).Bold().Color("#00FF88"),
                        Ui.Text("Home / Game").FontSize(20).Color("#FFFFFF"))
                        .Justify(UiJustifyContent.SpaceBetween)
                        .Align(UiAlignItems.Center)
                        .Padding(20)
                        .Background("#323232")
                        .Height(80),
                    Ui.Row(
                        Ui.Card(
                            Ui.Text("Mod Loaded").FontSize(24).Bold().Color("#FFD700"),
                            Ui.Text("UiTestMod is active").FontSize(18).Color("#FFFFFF"))
                            .Width(300)
                            .Height(200)
                            .Justify(UiJustifyContent.Center)
                            .Align(UiAlignItems.Center)
                            .Background(new SkiaSharp.SKColor(255, 255, 255, 20)))
                        .Justify(UiJustifyContent.Center)
                        .Align(UiAlignItems.Center)
                        .FlexGrow(1)
                        .Padding(50))
                .Width(1280)
                .Height(720)
                .Background(new SkiaSharp.SKColor(0, 0, 0, 100));

            UiScene scene = UiSceneComposer.Compose(root);
            uiRoot.MountScene(scene);
            uiRoot.IsDirty = true;
            return Task.CompletedTask;
        }
    }
}
