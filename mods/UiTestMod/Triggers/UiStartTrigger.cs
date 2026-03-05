using Ludots.Core.Scripting;
using Ludots.Core.Commands;
using Ludots.Core.Engine;
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

        public override async Task ExecuteAsync(ScriptContext context)
        {
            System.Console.WriteLine("[UiStartTrigger] Executing...");
            var engine = context.GetEngine();
            if (engine == null) 
            {
                System.Console.WriteLine("[UiStartTrigger] Engine not found!");
                return;
            }

            string html = @"
                    <div class='container'>
                        <div class='header'>
                            <div class='logo'>LUDOTS UI TEST</div>
                            <div class='nav'>
                                <div class='nav-item'>Home</div>
                                <div class='nav-item'>Game</div>
                            </div>
                        </div>
                        <div class='content'>
                            <div class='card'>
                                <div class='card-title'>Mod Loaded</div>
                                <div class='card-body'>UiTestMod is active</div>
                            </div>
                        </div>
                    </div>
                ";

            string css = @"
                    .container {
                        display: flex;
                        flex-direction: column;
                        width: 1280px;
                        height: 720px;
                        background-color: rgba(0, 0, 0, 100);
                    }
                    .header {
                        height: 80px;
                        display: flex;
                        flex-direction: row;
                        justify-content: space-between;
                        align-items: center;
                        background-color: rgba(50, 50, 50, 200);
                        padding: 20px;
                    }
                    .logo {
                        font-size: 30px;
                        color: #00FF00;
                        margin-left: 20px;
                    }
                    .nav { display: flex; flex-direction: row; }
                    .nav-item { margin-left: 20px; color: white; font-size: 20px; }
                    .content {
                        flex-grow: 1;
                        display: flex;
                        flex-direction: row;
                        justify-content: center;
                        align-items: center;
                        padding: 50px;
                    }
                    .card {
                        width: 300px;
                        height: 200px;
                        background-color: rgba(255, 255, 255, 20);
                        margin: 20px;
                        display: flex;
                        flex-direction: column;
                        justify-content: center;
                        align-items: center;
                        padding: 10px;
                    }
                    .card-title { font-size: 24px; color: #FFD700; margin-bottom: 10px; }
                    .card-body { font-size: 18px; color: white; }
                ";

            var cmd = new ShowUiCommand 
            { 
                Html = html,
                Css = css
            };
            await cmd.ExecuteAsync(context);
        }
    }
}
