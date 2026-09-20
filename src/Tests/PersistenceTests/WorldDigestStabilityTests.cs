using System.Linq;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Persistence;

/// <summary>
/// Determinism measurement guard: world digests are only meaningful if a world that received no
/// input yields identical digests on repeated captures. Wall-clock diagnostics fields (render
/// interpolation alpha, frame timings, stopwatch-measured physics update time) must stay out of the
/// serialized payload — this test exists so that mistake cannot land silently again (#1311).
/// </summary>
public sealed class WorldDigestStabilityTests
{
    [Test]
    public void StaticWorldYieldsIdenticalDigestsAcrossCaptures()
    {
        using GameEngine engine = CreateInitializedEngine();
        UseTurnBasedPacemaker(engine);
        RunFixedSteps(engine, 3);

        string digestA = CaptureDigest(engine);
        string digestB = CaptureDigest(engine);

        Assert.That(digestB, Is.EqualTo(digestA), "second capture of an untouched world must match the first");
    }


    // Row-sorted digest lens (same as SaveContinuationTrace): raw blob bytes are not stable across
    // serialize invocations, so per-entity/per-component sorted rows are the only meaningful basis.
    private static string CaptureDigest(GameEngine engine)
    {
        LudotsBinaryWorldSerializer serializer = LudotsPersistenceSerializerFactory.Create(engine);
        byte[] worldBytes = serializer.Serialize(engine.World);
        using var canonical = serializer.Deserialize(worldBytes);
        SaveEntityWorldIdNormalizer.Normalize(canonical, 0);
        return RowDigest(canonical);
    }

    private static string RowDigest(Arch.Core.World world)
    {
        var options = SaveContinuationTrace.CreateComponentSerializerOptions();
        var rows = new List<string>();
        world.Query(in Arch.Core.QueryDescription.Null, entity =>
        {
            Arch.Core.Signature signature = world.GetSignature(entity);
            var componentRows = new List<string>(signature.Components.Length);
            foreach (Arch.Core.ComponentType componentType in signature.Components)
            {
                Type type = componentType.Type;
                object? component = world.Get(entity, componentType);
                componentRows.Add(component == null
                    ? $"{type.FullName ?? type.Name}=<null>"
                    : $"{type.FullName ?? type.Name}={Convert.ToHexString(MessagePack.MessagePackSerializer.Serialize(type, component, options))}");
            }

            componentRows.Sort(StringComparer.Ordinal);
            rows.Add($"{entity.Id}:{entity.Version}|{string.Join("|", componentRows)}");
        });

        rows.Sort(StringComparer.Ordinal);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join("\n", rows))))[..16];
    }

    private static void RunFixedSteps(GameEngine engine, int count)
    {
        var pacemaker = (TurnBasedPacemaker)engine.Pacemaker;
        for (int i = 0; i < count; i++)
        {
            pacemaker.Step();
            engine.Tick(0.05f);
        }
    }

    private static void UseTurnBasedPacemaker(GameEngine engine)
    {
        engine.Pacemaker = new TurnBasedPacemaker();
        engine.SimulationBudgetMsPerFrame = int.MaxValue;
        engine.SimulationMaxSlicesPerLogicFrame = 1000;
        engine.Start();
    }

    private static GameEngine CreateInitializedEngine()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            string gitPath = Path.Combine(dir, ".git");
            if ((Directory.Exists(gitPath) || File.Exists(gitPath)) &&
                Directory.Exists(Path.Combine(dir, "src")) &&
                Directory.Exists(Path.Combine(dir, "mods")))
            {
                break;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(dir!, new[] { "LudotsCoreMod" }),
            Path.Combine(dir!, "assets"));
        engine.LoadStartupMap();
        return engine;
    }
}
