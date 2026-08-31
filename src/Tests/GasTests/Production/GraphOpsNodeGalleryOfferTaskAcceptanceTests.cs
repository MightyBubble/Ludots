using System;
using System.Linq;
using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryOfferTaskAcceptanceTests
{
    [Test]
    public void OfferTask_RollCallCreatesNamedScopedTask()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("OfferTask");
        runtime.EnsureWorld();

        Assert.That(runtime.Vignette.GraphKind, Is.EqualTo("Script"));

        GameEngine engine = GraphOpsHeadlessGameEngine.SharedGallery(FindRepoRoot());
        var tasks = engine.GetService(CoreServiceKeys.TaskRuntimeService) as TaskRuntimeService
            ?? throw new InvalidOperationException("TaskRuntimeService missing from the shared gallery engine.");
        int before = tasks.CaptureViews().Count(v => v.TaskId == "gallery.op.offer_task");

        runtime.Tick(0.35f);

        TaskView offered = tasks.CaptureViews()
            .Where(v => v.TaskId == "gallery.op.offer_task")
            .OrderBy(v => v.InstanceId)
            .Last();
        Assert.That(tasks.CaptureViews().Count(v => v.TaskId == "gallery.op.offer_task") - before, Is.EqualTo(1));
        Assert.That(offered.State, Is.EqualTo(TaskInstanceState.Active));
        Assert.That(engine.World.IsAlive(offered.ScopeHost), Is.True);
        Assert.That(engine.World.Get<Name>(offered.Entity).Value, Is.EqualTo("点名派发任务"));
        Assert.That(runtime.Metrics.Detail, Does.Contain("任务已派发"));
        Assert.That(runtime.Metrics.Detail, Does.Not.Contains("{"));

        runtime.Tick(0.35f);

        Assert.That(tasks.CaptureViews().Count(v => v.TaskId == "gallery.op.offer_task") - before, Is.EqualTo(1));
    }

    private static string FindRepoRoot()
    {
        string? dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "showcase.registry.json")))
        {
            dir = System.IO.Path.GetDirectoryName(dir);
        }

        return dir ?? throw new System.IO.DirectoryNotFoundException("Repository root not found.");
    }
}
