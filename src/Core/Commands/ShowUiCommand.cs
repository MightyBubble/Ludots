using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.Core.UI;

namespace Ludots.Core.Commands
{
    public class ShowUiCommand : GameCommand
    {
        public string Html { get; set; }
        public string Css { get; set; }

        public override Task ExecuteAsync(ScriptContext context)
        {
            System.Console.WriteLine("[ShowUiCommand] Executing...");
            // Get UISystem from context? Or directly assume we have UI related objects.
            // The original used context.GetService<IUiSystem>("UISystem") from GameContext.
            // ScriptContext now wraps things.
            
            // Assuming ScriptContext has a way to access services or "Engine"
            var engine = context.Get<GameEngine>(ContextKeys.Engine);
            // Or maybe a UIRoot? 
            
            // For now, let's keep the logic compatible with old context usage where possible,
            // or adapt to new ScriptContext structure.
            
            // Note: The previous implementation used GameContext which wrapped GameEngine.
            // ScriptContext contains "Engine".
            
            // We need a standard way to get UiSystem. 
            // In GameEngine.cs, we didn't explicitly expose IUiSystem in Initialize.
            // But we can check if it's in GlobalContext or if we can get it from Engine.
            
            // For this specific task, I'll assume we can't easily get IUiSystem yet 
            // without "ScriptContextExtensions", but I'll implement basic retrieval.
            
            // Wait, previous code:
            // var ui = context.GetService<IUiSystem>("UISystem");
            // context was GameContext.
            
            // New context is ScriptContext.
            // Let's see if we can get "UISystem".
            
            var ui = context.Get<IUiSystem>(ContextKeys.UISystem);
            if (ui != null)
            {
                System.Console.WriteLine("[ShowUiCommand] Setting HTML...");
                ui.SetHtml(Html, Css);
            }
            else
            {
                System.Console.WriteLine("[ShowUiCommand] UISystem not found!");
            }
            
            return Task.CompletedTask;
        }
    }
}
