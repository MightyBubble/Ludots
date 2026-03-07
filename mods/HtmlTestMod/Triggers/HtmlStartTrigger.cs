using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.HtmlEngine.Markup;
using System.Threading.Tasks;

namespace HtmlTestMod.Triggers
{
    public class HtmlStartTrigger : Trigger
    {
        private static readonly UiMarkupLoader Loader = new();

        public HtmlStartTrigger()
        {
            EventKey = GameEvents.MapLoaded;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            UIRoot? uiRoot = context.Get(CoreServiceKeys.UIRoot) as UIRoot;
            if (uiRoot == null)
            {
                return Task.CompletedTask;
            }

            string html = @"
                <div id='root' class='root'>
                  <h1 class='title'>HTML TEST MOD</h1>
                  <p class='subtitle'>Markup + C# runtime is active.</p>
                  <div class='row'>
                    <div class='pill'>Mod: HtmlTestMod</div>
                    <div class='pill'>Engine: Native DOM</div>
                    <button id='ok' class='pill primary'>OK</button>
                  </div>
                </div>
            ";

            string css = @"
                #root {
                  width: 1280px;
                  height: 720px;
                  display: flex;
                  flex-direction: column;
                  justify-content: center;
                  align-items: center;
                  gap: 14px;
                  background-color: rgba(12, 18, 30, 220);
                }
                .title { font-size: 54px; color: #ffffff; }
                .subtitle { font-size: 22px; color: #c8d3e6; }
                .row { display: flex; flex-direction: row; gap: 12px; }
                .pill {
                  padding: 12px 16px;
                  border-radius: 16px;
                  background-color: rgba(255,255,255,28);
                  color: #7fffd4;
                  font-size: 16px;
                }
                .primary {
                  background-color: #3a79dc;
                  color: #ffffff;
                }
            ";

            uiRoot.MountScene(Loader.LoadScene(html, css));
            uiRoot.IsDirty = true;
            return Task.CompletedTask;
        }
    }
}
