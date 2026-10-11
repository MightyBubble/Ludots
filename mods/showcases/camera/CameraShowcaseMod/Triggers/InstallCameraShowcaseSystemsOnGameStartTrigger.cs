using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using CameraShowcaseMod.Systems;

namespace CameraShowcaseMod.Triggers
{
    /// <summary>Registers the showcase per-tick input poll (F4 command-source follow) once per game start.</summary>
    public sealed class InstallCameraShowcaseSystemsOnGameStartTrigger : Trigger
    {
        private readonly IModContext _context;

        public InstallCameraShowcaseSystemsOnGameStartTrigger(IModContext context)
        {
            _context = context;
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            if (context.Get(CoreServiceKeys.Engine) is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            engine.RegisterSystem(new CameraShowcaseCommandFollowSystem(engine.GlobalContext), SystemGroup.LocalInput);
            _context.Log("[CameraShowcaseMod] Command-follow poll system registered");
            return Task.CompletedTask;
        }
    }
}
