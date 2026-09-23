using System;
using System.IO;
using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Calendar;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryCalendarAcceptanceTests
{
    [Test]
    public void ReadCalendarEnabled_ShowsTheCalendarIsOn()
    {
        using GraphOpsNodeGalleryRuntime runtime = BindAndTick("ReadCalendarEnabled");
        CalendarRuntime calendar = LiveCalendar();
        GraphOpsNodeVignetteLoader.RejectBannedCaption(runtime.Metrics.Detail, runtime.Op, "detail");
        Assert.That(calendar.IsEnabled, Is.True);
        Assert.That(runtime.Metrics.Detail, Does.Contain("历法已经启用"));
    }

    [Test]
    public void ReadCalendarDayIndex_ShowsTheLiveDay()
    {
        using GraphOpsNodeGalleryRuntime runtime = BindAndTick("ReadCalendarDayIndex");
        CalendarRuntime calendar = LiveCalendar();
        Assert.That(runtime.Metrics.Detail, Does.Contain(calendar.DayIndex.ToString()));
        Assert.That(runtime.Metrics.Detail, Does.Contain("日子走到"));
    }

    [Test]
    public void ReadCalendarTicksIntoDay_ShowsTheLiveTicks()
    {
        using GraphOpsNodeGalleryRuntime runtime = BindAndTick("ReadCalendarTicksIntoDay");
        CalendarRuntime calendar = LiveCalendar();
        Assert.That(runtime.Metrics.Detail, Does.Contain(calendar.TicksIntoDay.ToString()));
        Assert.That(runtime.Metrics.Detail, Does.Contain("已经走了"));
    }

    [Test]
    public void ReadCalendarDayPermille_ShowsTheLivePermille()
    {
        using GraphOpsNodeGalleryRuntime runtime = BindAndTick("ReadCalendarDayPermille");
        CalendarRuntime calendar = LiveCalendar();
        Assert.That(runtime.Metrics.Detail, Does.Contain(calendar.ReadDayPermille().ToString()));
        Assert.That(runtime.Metrics.Detail, Does.Contain("千分之"));
    }

    [Test]
    public void ReadCalendarDayPhase_ShowsTheLivePhase()
    {
        using GraphOpsNodeGalleryRuntime runtime = BindAndTick("ReadCalendarDayPhase");
        CalendarRuntime calendar = LiveCalendar();
        Assert.That(runtime.Metrics.Detail, Does.Contain(calendar.ReadDayPhaseKeyId().ToString()));
        Assert.That(runtime.Metrics.Detail, Does.Contain("昼夜相位"));
    }

    [Test]
    public void ReadCalendarYear_ShowsTheActiveYear()
    {
        using GraphOpsNodeGalleryRuntime runtime = BindAndTick("ReadCalendarYear");
        CalendarRuntime calendar = LiveCalendar();
        Assert.That(runtime.Metrics.Detail, Does.Contain(calendar.ReadYear(calendar.ActiveCalendarId).ToString()));
        Assert.That(runtime.Metrics.Detail, Does.Contain("第"));
    }

    [Test]
    public void ReadCalendarCyclePhase_ShowsTheSeasonPhase()
    {
        using GraphOpsNodeGalleryRuntime runtime = BindAndTick("ReadCalendarCyclePhase");
        CalendarRuntime calendar = LiveCalendar();
        int phase = calendar.ReadCyclePhaseKeyId(calendar.ActiveCalendarId, "season");
        Assert.That(runtime.Metrics.Detail, Does.Contain(phase.ToString()));
        Assert.That(runtime.Metrics.Detail, Does.Contain("季节相位"));
    }

    [Test]
    public void ReadCalendarCycleDay_ShowsTheDayInsideTheSeason()
    {
        using GraphOpsNodeGalleryRuntime runtime = BindAndTick("ReadCalendarCycleDay");
        CalendarRuntime calendar = LiveCalendar();
        int day = calendar.ReadCycleDay(calendar.ActiveCalendarId, "season");
        Assert.That(runtime.Metrics.Detail, Does.Contain(day.ToString()));
        Assert.That(runtime.Metrics.Detail, Does.Contain("这一季"));
    }

    [Test]
    public void ApplyCalendarStart_RewritesTheOpeningItJustRead()
    {
        using GraphOpsNodeGalleryRuntime runtime = new();
        runtime.BindOp("ApplyCalendarStart");
        runtime.EnsureWorld();
        CalendarRuntime calendar = LiveCalendar();
        int day = calendar.DayIndex;
        int ticks = calendar.TicksIntoDay;
        runtime.Tick(0.35f);
        Assert.That(calendar.DayIndex, Is.EqualTo(day));
        Assert.That(calendar.TicksIntoDay, Is.EqualTo(ticks));
        Assert.That(runtime.Metrics.Detail, Does.Contain(day.ToString()));
        Assert.That(runtime.Metrics.Detail, Does.Contain("开局落在"));
    }

    [Test]
    public void SetCalendarDayIndex_MovesTheDayForwardByOne()
    {
        CalendarRuntime calendar = LiveCalendar();
        int before = calendar.DayIndex;
        using GraphOpsNodeGalleryRuntime runtime = BindAndTick("SetCalendarDayIndex");
        Assert.That(calendar.DayIndex, Is.EqualTo(before + 1));
        Assert.That(runtime.Metrics.Detail, Does.Contain(calendar.DayIndex.ToString()));
        Assert.That(runtime.Metrics.Detail, Does.Contain("日子拨到"));
    }

    [Test]
    public void SetCalendarTicksIntoDay_WritesTheTicksItJustRead()
    {
        using GraphOpsNodeGalleryRuntime runtime = new();
        runtime.BindOp("SetCalendarTicksIntoDay");
        runtime.EnsureWorld();
        CalendarRuntime calendar = LiveCalendar();
        int ticks = calendar.TicksIntoDay;
        int day = calendar.DayIndex;
        runtime.Tick(0.35f);
        Assert.That(calendar.TicksIntoDay, Is.EqualTo(ticks));
        Assert.That(calendar.DayIndex, Is.EqualTo(day));
        Assert.That(runtime.Metrics.Detail, Does.Contain(ticks.ToString()));
        Assert.That(runtime.Metrics.Detail, Does.Contain("第"));
    }

    private static GraphOpsNodeGalleryRuntime BindAndTick(string op)
    {
        var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp(op);
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        return runtime;
    }

    private static CalendarRuntime LiveCalendar()
    {
        GameEngine engine = GraphOpsHeadlessGameEngine.SharedGallery(FindRepoRoot());
        return engine.GetService(CoreServiceKeys.CalendarRuntime)
            ?? throw new InvalidOperationException("CalendarRuntime missing from the shared gallery engine.");
    }

    private static string FindRepoRoot()
    {
        string? dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "showcase.registry.json")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
