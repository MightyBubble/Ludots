using System.Threading.Tasks;
using Ludots.Core.Diagnostics;
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
            Log.Info(in LogChannels.Presentation, "Executing ShowUiCommand...");
            var engine = context.Get(CoreServiceKeys.Engine);
            var ui = context.Get(CoreServiceKeys.UISystem);
            if (ui != null)
            {
                Log.Info(in LogChannels.Presentation, "Setting HTML...");
                ui.SetHtml(Html, Css);
            }
            else
            {
                Log.Warn(in LogChannels.Presentation, "UISystem not found!");
            }
            
            return Task.CompletedTask;
        }
    }
}
