using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using WasdSandboxMod.Systems;

namespace WasdSandboxMod
{
    /// <summary>
    /// Minimal WASD perspective sandbox: the seat activates scheme.wasd_move at startup, both heroes
    /// declare their own camera profile via CameraProfileBinding, and T cycles possession between
    /// them — TPS follow for one, top-down twin-stick for the other. The camera profile switch is
    /// core adoption; this mod only owns the possession cycle.
    /// </summary>
    public sealed class WasdSandboxModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[WasdSandboxMod] Loaded - minimal WASD perspective sandbox");
            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine != null)
                {
                    engine.RegisterSystem(
                        new WasdSandboxPerspectiveToggleSystem(engine.World, engine.GlobalContext),
                        SystemGroup.InputCollection);
                }

                return Task.CompletedTask;
            });
        }

        public void OnUnload()
        {
        }
    }
}
