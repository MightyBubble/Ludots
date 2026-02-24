using System;
using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI.Widgets;
using Ludots.UI.Reactive.Core;
using Ludots.UI.Reactive.Widgets;
using SkiaSharp;
using FlexLayoutSharp;
using Ludots.UI;

namespace ReactiveTestMod
{
    public class Counter : Component
    {
        private int _count = 0;

        public override Element Render()
        {
            return new Element(typeof(FlexNodeWidget), new {
                // Container Style
                FlexDirection = FlexDirection.Column,
                JustifyContent = Justify.Center,
                AlignItems = Align.Center,
                BackgroundColor = SKColors.DarkSlateGray,
                WidthPercent = 100f,
                HeightPercent = 100f
            }, null, 
                // Children
                new Element(typeof(FlexNodeWidget), new {
                    Text = $"Count: {_count}",
                    FontSize = 60f,
                    TextColor = SKColors.White,
                    MarginBottom = 40f
                }),
                new Element(typeof(FlexNodeWidget), new {
                    Text = "Increment",
                    FontSize = 30f,
                    TextColor = SKColors.Black,
                    BackgroundColor = SKColors.Cyan,
                    Padding = 20f,
                    BorderRadius = 10f,
                    OnClick = (Action)(() => {
                        SetState(() => _count++);
                    })
                })
            );
        }
    }

    public class ReactiveTestModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("ReactiveTestMod Loaded!");
            context.TriggerManager.RegisterTrigger(new ReactiveStartTrigger(context));
        }

        public void OnUnload() { }
    }

    public class ReactiveStartTrigger : Trigger
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

            var uiRoot = context.Get<UIRoot>(ContextKeys.UIRoot);
            if (uiRoot == null)
            {
                _modContext.Log("[ReactiveTestMod] ERROR: UIRoot not found in context.");
                return Task.CompletedTask;
            }

            var rootWidget = new FlexNodeWidget();
            Reconciler.Render(new Element(typeof(Counter)), rootWidget);

            uiRoot.Content = rootWidget;
            uiRoot.IsDirty = true;
            return Task.CompletedTask;
        }
    }
}
