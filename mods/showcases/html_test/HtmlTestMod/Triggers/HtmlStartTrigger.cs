using Ludots.Core.Commands;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using System.Threading.Tasks;

namespace HtmlTestMod.Triggers
{
    public class HtmlStartTrigger : Trigger
    {
        public HtmlStartTrigger()
        {
            EventKey = GameEvents.MapLoaded;
            // Should check for HtmlTestMap? But let's assume it runs on any map for now or stick to original behavior.
            // Original didn't check for specific map, just MapLoaded. 
            // We can keep it generic or strict. Let's keep it generic for now as I didn't see HtmlTestMap created.
        }

        public override async Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null) return;

            var html = @"
                <div class='root'>
                  <div class='title'>HTML TEST MOD</div>
                  <div class='subtitle'>If you can read this, HTML/CSS UI is working.</div>
                  <div class='row'>
                    <div class='pill'>Mod: HtmlTestMod</div>
                    <div class='pill'>Event: MapLoaded</div>
                  </div>
                </div>
            ";

            var css = @"
                .root {
                  width: 1280px;
                  height: 720px;
                  display: flex;
                  flex-direction: column;
                  justify-content: center;
                  align-items: center;
                  background-color: rgba(0, 0, 0, 140);
                }
                .title {
                  font-size: 54px;
                  color: #ffffff;
                  margin-bottom: 10px;
                }
                .subtitle {
                  font-size: 20px;
                  color: #cccccc;
                  margin-bottom: 24px;
                }
                .row { display: flex; flex-direction: row; gap: 12px; }
                .pill {
                  padding: 10px 14px;
                  border-radius: 16px;
                  background-color: rgba(255, 255, 255, 28);
                  color: #00ff88;
                  font-size: 16px;
                }
            ";

            var cmd = new ShowUiCommand { Html = html, Css = css };
            await cmd.ExecuteAsync(context);
        }
    }
}
