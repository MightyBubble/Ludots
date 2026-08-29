using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.Scripting;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

/// <summary>
/// Headless acceptance for the activity dispatch showcase: all three dispatch paths
/// (forced / pooled / automatic) driven through the config-only rail
/// (custom map event → TriggerGraph → OfferActivity), plus cue drain and pooled
/// determinism. Evidence written to artifacts/acceptance/activity_dispatch/.
/// </summary>
[TestFixture]
public sealed class ActivityDispatchShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "activity_dispatch";

    private static readonly string[] Mods =
    {
        "LudotsCoreMod",
        "ActivityDispatchShowcaseMod",
    };

    [Test]
    public void ThreeDispatchPaths_EndToEnd_FromConfigRail()
    {
        using GameEngine engine = CreateEngine();
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 2);

        var activities = engine.GetService(CoreServiceKeys.ActivityRuntimeService) as ActivityRuntimeService
            ?? throw new InvalidOperationException("ActivityRuntimeService missing after engine start.");

        // Forced: custom event → graph → OfferActivity → active instance with four option shapes.
        Fire(engine, "ActivityShowcase.Forced");
        TickUntil(engine, () => activities.CaptureViews().Any(v => v.ActivityId == "showcase.forced_supply" && v.State == ActivityInstanceState.Active), 5,
            "forced activity must present after the Forced rail event");
        ActivityView forced = activities.CaptureViews().Single(v => v.ActivityId == "showcase.forced_supply");
        var options = new List<ActivityOptionView>();
        Assert.That(activities.TryGetActiveOptions(forced.Entity, null, options), Is.True);
        Assert.That(options.Select(o => o.OptionId), Is.EqualTo(new[] { "hold", "withdraw", "forward_camp" }),
            "Gate-hidden 'request_aid' must not appear; blocked 'forward_camp' must stay visible with a reason.");
        Assert.That(options.First(o => o.OptionId == "hold").IsBaseline, Is.True);
        ActivityOptionView camp = options.Single(o => o.OptionId == "forward_camp");
        Assert.That(camp.Executable, Is.True,
            "council Health=60 satisfies the >=50 execution condition, so the camp option starts executable");

        // Confirm the baseline option: single-layer settle, no second instance opened.
        activities.ResolveOption(forced.Entity, "hold");
        Assert.That(activities.CaptureViews().Count(v => v.State != ActivityInstanceState.Resolved), Is.EqualTo(0));
        Assert.That(activities.CaptureViews().Single(v => v.InstanceId == forced.InstanceId).SelectedOptionId, Is.EqualTo("hold"));

        // The settle effect (task.create) must have created the follow-up task.
        var tasks = engine.GetService(CoreServiceKeys.TaskRuntimeService) as TaskRuntimeService
            ?? throw new InvalidOperationException("TaskRuntimeService missing.");
        Assert.That(tasks.CaptureViews().Any(t => t.TaskId == "showcase.task.hold"), Is.True,
            "option effect task.create must settle into a tracked task.");

        // Pooled: same stream state → same candidate; distribution weights are honored deterministically.
        Fire(engine, "ActivityShowcase.Pooled");
        TickUntil(engine, () => activities.CaptureViews().Any(v => v.State != ActivityInstanceState.Resolved), 5,
            "pooled draw must produce an instance");
        ActivityView pooled = activities.CaptureViews().First(v => v.State != ActivityInstanceState.Resolved);
        Assert.That(pooled.ActivityId, Is.AnyOf("showcase.pool_caravan", "showcase.pool_omen"),
            $"unexpected pool candidate '{pooled.ActivityId}'.");

        // Automatic: no options, settles silently into history with the automatic flag.
        Fire(engine, "ActivityShowcase.Automatic");
        TickUntil(engine, () => activities.CaptureViews().Any(v => v.ActivityId == "showcase.auto_report" && v.State == ActivityInstanceState.Resolved), 5,
            "automatic activity must settle without player input");
        ActivityView reported = activities.CaptureViews().Single(v => v.ActivityId == "showcase.auto_report");
        Assert.That(reported.SelectedOptionId, Is.Empty);
        Assert.That(tasks.CaptureViews().Any(t => t.TaskId == "showcase.task.report"), Is.True,
            "automatic effect must create the report task.");

        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        WriteEvidence(activities, tasks, pooled.ActivityId);
    }

    [Test]
    public void PooledDraw_IsDeterministic_AcrossEngineRestarts()
    {
        string firstDraw = DrawPooledCandidate();
        string secondDraw = DrawPooledCandidate();
        Assert.That(firstDraw, Is.EqualTo(secondDraw),
            "same fixed stream seed must draw the same candidate across cold engine starts.");
    }

    [Test]
    public void CueWindow_Drains_EveryFixedStep()
    {
        using GameEngine engine = CreateEngine();
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 2);

        var activities = engine.GetService(CoreServiceKeys.ActivityRuntimeService) as ActivityRuntimeService;
        Fire(engine, "ActivityShowcase.Automatic");
        TickUntil(engine, () => activities!.Presentation.Cues.Count > 0, 5,
            "automatic settle must land cues in the presentation window");
        Tick(engine, 5);
        Assert.That(activities!.Presentation.Cues.Count, Is.EqualTo(0),
            "the ClearPresentationFlags drain must clear the cue window within a few fixed steps (graph slices may resume after the drain of their issuing tick).");
        Assert.That(activities.Lifecycle.Events.Count, Is.EqualTo(0),
            "lifecycle buffer must drain with the same step.");
    }

    private static string DrawPooledCandidate()
    {
        using GameEngine engine = CreateEngine();
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 2);
        var activities = engine.GetService(CoreServiceKeys.ActivityRuntimeService) as ActivityRuntimeService
            ?? throw new InvalidOperationException("ActivityRuntimeService missing.");
        Fire(engine, "ActivityShowcase.Pooled");
        TickUntil(engine, () => activities.CaptureViews().Any(v => v.State != ActivityInstanceState.Resolved), 5,
            "pooled draw must produce an instance");
        return activities.CaptureViews().First(v => v.State != ActivityInstanceState.Resolved).ActivityId;
    }

    private static void Fire(GameEngine engine, string eventKey)
    {
        var registry = engine.GetService(CoreServiceKeys.CustomEventNameRegistry)
            ?? throw new InvalidOperationException("CustomEventNameRegistry missing.");
        var context = engine.CreateContext();
        context.Set(CoreServiceKeys.MapId, engine.CurrentMapSession!.MapId);
        context.Set(CoreServiceKeys.MapSession, engine.CurrentMapSession);
        engine.TriggerManager.FireMapCustomEvent(engine.CurrentMapSession.MapId, eventKey, context, registry);
    }

    private static GameEngine CreateEngine()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, Mods),
            Path.Combine(repoRoot, "assets"));
        engine.SetService(CoreServiceKeys.UiCaptured, false);
        return engine;
    }

    private static void Tick(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
        }
    }

    private static void TickUntil(GameEngine engine, System.Func<bool> condition, int maxFrames, string describeFailure)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            Tick(engine, 1);
            if (condition())
            {
                return;
            }
        }

        Assert.Fail(describeFailure);
    }

    private static void WriteEvidence(ActivityRuntimeService activities, TaskRuntimeService tasks, string pooledCandidate)
    {
        string artifactDir = Path.Combine(FindRepoRoot(), "artifacts", "acceptance", "activity_dispatch");
        Directory.CreateDirectory(artifactDir);
        var lines = new List<string>
        {
            "# activity dispatch acceptance",
            "",
            $"pooled_candidate: {pooledCandidate}",
        };
        foreach (ActivityView view in activities.CaptureViews())
        {
            lines.Add($"activity | {view.ActivityId} | #{view.InstanceId} | {view.State} | selected={view.SelectedOptionId}");
        }
        foreach (TaskView task in tasks.CaptureViews())
        {
            lines.Add($"task | {task.TaskId} | {task.State}");
        }
        File.WriteAllLines(Path.Combine(artifactDir, "acceptance.md"), lines);
    }

    private static string FindRepoRoot()
    {
        string? dir = Path.GetDirectoryName(typeof(ActivityDispatchShowcaseAcceptanceTests).Assembly.Location);
        while (dir != null && !File.Exists(Path.Combine(dir, "showcase.registry.json")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
