using System.Threading.Tasks;
using CoreInputMod.ViewMode;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using AoeEmpireMod.Systems;

namespace AoeEmpireMod.Triggers;

/// <summary>
/// Registers 5-nation team relations, AoE view mode, and presentation systems on game start.
/// </summary>
public sealed class InstallAoeEmpireOnGameStartTrigger : Trigger
{
    private const string InstalledKey = "AoeEmpireMod.Installed";
    private readonly IModContext _ctx;

    public InstallAoeEmpireOnGameStartTrigger(IModContext ctx)
    {
        _ctx = ctx;
        EventKey = GameEvents.GameStart;
    }

    public override Task ExecuteAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        if (engine.GlobalContext.TryGetValue(InstalledKey, out object? obj) && obj is bool installed && installed)
        {
            return Task.CompletedTask;
        }

        engine.GlobalContext[InstalledKey] = true;
        _ctx.Log("[AoeEmpireMod] Installing 5-nation AoE empire gameplay.");

        ConfigureTeamRelations();
        ViewModeRegistrar.RegisterFromVfs(_ctx, engine.GlobalContext, "Rts");
        engine.RegisterPresentationSystem(new AoeEmpireSelectionFeedbackPresentationSystem(engine));
        engine.RegisterSystem(new AoeEmpireTechTreeProjectionSystem(engine), SystemGroup.InputCollection);

        return Task.CompletedTask;
    }

    private static void ConfigureTeamRelations()
    {
        for (int a = 1; a <= 5; a++)
        {
            for (int b = a + 1; b <= 5; b++)
            {
                TeamManager.SetRelationshipSymmetric(a, b, TeamRelationship.Hostile);
            }
        }
    }
}
