using System.IO;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial.Eqs.Config;
using Ludots.Core.Systems;

namespace EqsInfluenceShowcaseMod;

public sealed class EqsInfluenceShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        EqsInfluenceConfigDocument document = LoadDocument(context);
        var runtime = new EqsInfluenceShowcaseRuntime(document);

        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(EqsInfluenceShowcaseRuntime.InstalledKey, out object? installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[EqsInfluenceShowcaseRuntime.InstalledKey] = true;
            engine.GlobalContext[EqsInfluenceShowcaseRuntime.RuntimeKey] = runtime;
            runtime.Arm(engine);
            engine.RegisterSystem(new EqsInfluenceShowcaseSimulationSystem(engine, runtime), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new EqsInfluenceShowcasePresentationSystem(engine, runtime));
            context.Log("[EqsInfluenceShowcaseMod] Armed avoid-threat scenario with Influence field + EQS overlays.");
            return Task.CompletedTask;
        });

        context.Log("[EqsInfluenceShowcaseMod] Loaded");
    }

    public void OnUnload()
    {
    }

    private static EqsInfluenceConfigDocument LoadDocument(IModContext context)
    {
        string fields = ReadText(context, "assets/Configs/Spatial/influence_fields.json");
        string queries = ReadText(context, "assets/Configs/Spatial/eqs_queries.json");
        string scenarios = ReadText(context, "assets/Configs/Spatial/eqs_scenarios.json");
        return EqsInfluenceConfigLoader.LoadFromJson(fields, queries, scenarios);
    }

    private static string ReadText(IModContext context, string relativePath)
    {
        using Stream stream = context.GetResource($"{context.ModId}:{relativePath}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
