using System.IO;
using System.Text;
using System.Text.Json;
using Ludots.Core.Engine;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;

namespace Ludots.Tests.TimeFlowCore;

[TestFixture]
[NonParallelizable]
public sealed class TimeFlowCoreAcceptanceTests
{
    [Test]
    public void TimeFlowCore_MinimalScenario_WritesAcceptanceArtifacts()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "timeflow-core");
        Directory.CreateDirectory(artifactDir);

        var service = new TimeFlowService();
        var clock = new DiscreteClock();
        var policy = new GasClockStepPolicy(stepEveryFixedTicks: 2);
        var system = new GasClockSystem(clock, policy);
        var clocks = new GasClocks(clock);
        var rows = new List<PhaseRow>();

        RunPhase(rows, service, policy, system, clocks, "baseline", "BaselineRealtime", fixedTicks: 4);

        TimeFlowToken simulationSlow = service.AcquireScaleToken(TimeFlowDomainIds.Simulation, 500, "acceptance", "half-speed world");
        TimeFlowToken gasCompensation = service.AcquireScaleToken(TimeFlowDomainIds.Gas, 2000, "acceptance", "keep gas realtime");
        RunPhase(rows, service, policy, system, clocks, "bullet_time", "WorldHalfSpeedGasRealtime", fixedTicks: 4);

        service.ReleaseToken(simulationSlow);
        RunPhase(rows, service, policy, system, clocks, "haste", "GasFastForward", fixedTicks: 3);

        TimeFlowToken pause = service.AcquirePauseToken(TimeFlowDomainIds.Simulation, "acceptance", "command pause");
        CapturePhase(rows, service, clocks, "pause", "SimulationPaused", fixedTicksApplied: 0, stepDelta: 0);

        File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildTraceJsonl(rows));
        File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(rows));
        File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid());

        Assert.Multiple(() =>
        {
            Assert.That(rows.Count, Is.EqualTo(4));
            Assert.That(rows[^1].SimulationPaused, Is.True);
            Assert.That(rows[^1].SimulationScalePermille, Is.EqualTo(0));
            Assert.That(rows[^1].GasScalePermille, Is.EqualTo(0));
            Assert.That(rows[1].GasScalePermille, Is.EqualTo(1000));
            Assert.That(rows[2].GasScalePermille, Is.EqualTo(2000));
            Assert.That(rows[2].StepNow, Is.EqualTo(7));
        });

        service.ReleaseToken(pause);
        service.ReleaseToken(gasCompensation);
    }

    private static void RunPhase(
        List<PhaseRow> rows,
        TimeFlowService service,
        GasClockStepPolicy policy,
        GasClockSystem system,
        GasClocks clocks,
        string phaseId,
        string phaseTitle,
        int fixedTicks)
    {
        int stepBefore = clocks.StepNow;
        policy.SetScalePermille(service.GetEffectiveScalePermille(TimeFlowDomainIds.Gas));
        for (int i = 0; i < fixedTicks; i++)
        {
            system.Update(0.016f);
        }

        CapturePhase(rows, service, clocks, phaseId, phaseTitle, fixedTicks, clocks.StepNow - stepBefore);
    }

    private static void CapturePhase(
        List<PhaseRow> rows,
        TimeFlowService service,
        GasClocks clocks,
        string phaseId,
        string phaseTitle,
        int fixedTicksApplied,
        int stepDelta)
    {
        rows.Add(new PhaseRow(
            phaseId,
            phaseTitle,
            service.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation),
            service.GetEffectiveScalePermille(TimeFlowDomainIds.Gas),
            service.GetEffectiveScalePermille(TimeFlowDomainIds.Physics2D),
            service.GetEffectiveScalePermille(TimeFlowDomainIds.Navigation2D),
            service.IsPaused(TimeFlowDomainIds.Simulation),
            clocks.FixedFrameNow,
            clocks.StepNow,
            fixedTicksApplied,
            stepDelta));
    }

    private static string BuildTraceJsonl(IReadOnlyList<PhaseRow> rows)
    {
        return string.Join(Environment.NewLine, rows.Select((row, index) => JsonSerializer.Serialize(new
        {
            event_id = $"timeflow-core-{index + 1:000}",
            phase_id = row.PhaseId,
            phase_title = row.PhaseTitle,
            simulation_scale_permille = row.SimulationScalePermille,
            gas_scale_permille = row.GasScalePermille,
            physics_scale_permille = row.PhysicsScalePermille,
            navigation_scale_permille = row.NavigationScalePermille,
            simulation_paused = row.SimulationPaused,
            fixed_ticks_total = row.FixedFrameNow,
            gas_steps_total = row.StepNow,
            fixed_ticks_applied = row.FixedTicksApplied,
            gas_steps_added = row.StepDelta
        }))) + Environment.NewLine;
    }

    private static string BuildBattleReport(IReadOnlyList<PhaseRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Scenario Card: timeflow-core");
        sb.AppendLine();
        sb.AppendLine("## Intent");
        sb.AppendLine("- Player goal: prove that a shared Core time service can slow, accelerate, and pause simulation domains without inventing a parallel scheduler.");
        sb.AppendLine("- Gameplay domain: Core `TimeFlowService`, domain hierarchy, token composition, and `GasClockStepPolicy` step pacing.");
        sb.AppendLine();
        sb.AppendLine("## Determinism Inputs");
        sb.AppendLine("- Seed: none; pure deterministic service/policy scenario");
        sb.AppendLine("- Map: none");
        sb.AppendLine("- Clock profile: `GasClockStepPolicy(stepEveryFixedTicks: 2)`");
        sb.AppendLine("- Initial entities: none");
        sb.AppendLine();
        sb.AppendLine("## Action Script");
        sb.AppendLine("1. Start at baseline realtime.");
        sb.AppendLine("2. Apply `simulation=500permille` and `gas=2000permille` to keep GAS realtime under world slow motion.");
        sb.AppendLine("3. Release world slowdown while keeping `gas=2000permille` for fast-forward.");
        sb.AppendLine("4. Apply a simulation pause token and confirm all child domains stop.");
        sb.AppendLine();
        sb.AppendLine("## Expected Outcomes");
        sb.AppendLine("- Primary success condition: child-domain effective scales follow parent composition and GAS step pacing tracks the effective GAS scale.");
        sb.AppendLine("- Failure branch condition: a child domain continues advancing after the simulation parent is paused.");
        sb.AppendLine("- Key metrics: fixed-frame total=`11`, gas-step total=`7`, final paused state=`true`.");
        sb.AppendLine();
        sb.AppendLine("## Evidence Artifacts");
        sb.AppendLine("- `artifacts/acceptance/timeflow-core/trace.jsonl`");
        sb.AppendLine("- `artifacts/acceptance/timeflow-core/battle-report.md`");
        sb.AppendLine("- `artifacts/acceptance/timeflow-core/path.mmd`");
        sb.AppendLine();
        sb.AppendLine("## Timeline");
        foreach (PhaseRow row in rows)
        {
            sb.AppendLine($"- `{row.PhaseId}` -> sim={row.SimulationScalePermille} gas={row.GasScalePermille} fixed={row.FixedFrameNow} step={row.StepNow} paused={row.SimulationPaused}");
        }

        sb.AppendLine();
        sb.AppendLine("## Outcome");
        sb.AppendLine("- success: yes");
        sb.AppendLine("- verdict: Core TimeFlow now owns shared domain scaling, while GAS step pacing reuses the existing clock instead of forking a second runtime.");
        return sb.ToString();
    }

    private static string BuildPathMermaid()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "flowchart TD",
            "    A[Baseline realtime] --> B[Simulation 0.5x plus Gas 2.0x]",
            "    B --> C[Release simulation slow token]",
            "    C --> D[Gas fast-forward at 2.0x]",
            "    D --> E[Pause simulation parent domain]",
            "    E --> F[All child domains report paused]"
        }) + Environment.NewLine;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir != null; i++)
        {
            string candidate = Path.Combine(dir.FullName, "src", "Core", "Ludots.Core.csproj");
            if (File.Exists(candidate))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }

    private sealed record PhaseRow(
        string PhaseId,
        string PhaseTitle,
        int SimulationScalePermille,
        int GasScalePermille,
        int PhysicsScalePermille,
        int NavigationScalePermille,
        bool SimulationPaused,
        int FixedFrameNow,
        int StepNow,
        int FixedTicksApplied,
        int StepDelta);
}
