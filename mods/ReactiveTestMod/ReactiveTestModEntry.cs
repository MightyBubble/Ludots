using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;

namespace ReactiveTestMod
{
    public sealed class ReactiveTestModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("ReactiveTestMod Loaded!");
            context.OnEvent(GameEvents.MapLoaded, new ReactiveStartTrigger(context).ExecuteAsync);
        }

        public void OnUnload()
        {
        }
    }

    public sealed class ReactiveStartTrigger : Trigger
    {
        private readonly IModContext _modContext;

        public ReactiveStartTrigger(IModContext modContext)
        {
            _modContext = modContext;
            EventKey = GameEvents.MapLoaded;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            _modContext.Log("[ReactiveTestMod] Setting up Reactive UI...");

            if (context.Get(CoreServiceKeys.UIRoot) is not UIRoot uiRoot)
            {
                _modContext.Log("[ReactiveTestMod] UIRoot not found in ScriptContext.");
                return Task.CompletedTask;
            }

            var textMeasurer = (IUiTextMeasurer)context.Get(CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)context.Get(CoreServiceKeys.UiImageSizeProvider);
            var page = new ReactivePage<CounterState>(
                textMeasurer,
                imageSizeProvider,
                new CounterState(0),
                BuildCounterScene);
            uiRoot.MountScene(page.Scene);
            uiRoot.IsDirty = true;
            return Task.CompletedTask;
        }

        private static UiElementBuilder BuildCounterScene(ReactiveContext<CounterState> context)
        {
            return Ui.Column(
                    Ui.Text($"Count: {context.State.Count}")
                        .FontSize(60f)
                        .Bold()
                        .Color(UiColor.White)
                        .Margin(0f, 24f),
                    Ui.Button("Increment", _ => context.SetState(state => state with
                    {
                        Count = state.Count + 1
                    }))
                        .FontSize(30f)
                        .Padding(24f, 18f)
                        .Radius(12f)
                        .Background(UiColor.Cyan)
                        .Color(UiColor.Black))
                .WidthPercent(100f)
                .HeightPercent(100f)
                .Justify(UiJustifyContent.Center)
                .Align(UiAlignItems.Center)
                .Background(UiColor.DarkSlateGray)
                .Gap(12f);
        }

        private sealed record CounterState(int Count);
    }
}
