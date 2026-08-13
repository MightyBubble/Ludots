using System.Text.RegularExpressions;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryRelAcceptanceTests
{
    private static readonly string[] RelFamilyOps =
    [
        "RelationshipRemoveLink",
        "RelationshipGetMetric",
        "RelationshipSetFlag",
        "RelationshipQueryOutgoing",
        "RelationshipQueryIncoming",
        "RelationshipQueryMutual",
        "RelationshipQueryBetweenPair",
        "RelationshipFilterMetricRange",
        "RelationshipFilterFlag",
        "RelationshipSortByMetric",
        "RelationshipAggSumMetric",
        "RelationshipAggMaxMetric",
        "RelationshipAggAverageMetric",
        "RelationshipAggMinMetric",
        "RelationshipAggMaxEntityByMetric",
        "RelationshipAggMinEntityByMetric",
        "RelationshipHasLink"
    ];

    [Test]
    public void QueryOutgoing_FriendCountGreaterThanZero()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipQueryOutgoing");
        Assert.That(int.Parse(runtime.Context.CaptionValues["friendCount"]), Is.GreaterThan(0));
        Assert.That(runtime.Metrics.Detail, Does.Contain("我交的"));
        AssertBannedEnglish(runtime.Metrics.Detail);
    }

    [Test]
    public void RemoveLink_DecreasesLinks()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipRemoveLink");
        int before = int.Parse(runtime.Context.CaptionValues["linksBefore"]);
        int after = int.Parse(runtime.Context.CaptionValues["linksAfter"]);
        Assert.That(after, Is.LessThan(before));
        Assert.That(runtime.Metrics.Detail, Does.Contain("拆掉"));
        AssertBannedEnglish(runtime.Metrics.Detail);
    }

    [Test]
    public void RemoveLink_SecondThinkStillBreaksALink()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipRemoveLink");
        runtime.Tick(0.35f);
        int before = int.Parse(runtime.Context.CaptionValues["linksBefore"]);
        int after = int.Parse(runtime.Context.CaptionValues["linksAfter"]);
        Assert.That(runtime.Metrics.ThinkWaves, Is.EqualTo(2));
        Assert.That(after, Is.LessThan(before));
        Assert.That(runtime.Metrics.Detail, Does.Contain("拆掉"));
        AssertBannedEnglish(runtime.Metrics.Detail);
    }

    [Test]
    public void GetMetric_CaptionContainsNumber()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipGetMetric");
        Assert.That(runtime.Metrics.Detail, Does.Contain("好感"));
        Assert.That(Regex.IsMatch(runtime.Metrics.Detail, @"\d+"), Is.True, runtime.Metrics.Detail);
        AssertBannedEnglish(runtime.Metrics.Detail);
    }

    [Test]
    public void AggMaxEntityByMetric_NamesHighestPerson()
    {
        using GraphOpsNodeGalleryRuntime runtime = Play("RelationshipAggMaxEntityByMetric");
        Assert.That(runtime.Metrics.Detail, Does.Contain("最高的人"));
        AssertBannedEnglish(runtime.Metrics.Detail);
    }

    [TestCaseSource(nameof(RelFamilyOps))]
    public void RelFamilyOp_RendersPlayerCaption(string op)
    {
        using GraphOpsNodeGalleryRuntime runtime = Play(op);
        AssertBannedEnglish(runtime.Metrics.Detail);
        AssertActorHealthMatchesWorld(runtime);
        int caster = GraphOpsNodeActorBinding.FindRole(runtime.Vignette, "caster");
        Assert.That(runtime.Context.ActorHudLit[caster], Is.True, op);
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase));
        }
    }

    private static GraphOpsNodeGalleryRuntime Play(string op)
    {
        var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp(op);
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        return runtime;
    }

    private static void AssertActorHealthMatchesWorld(GraphOpsNodeGalleryRuntime runtime)
    {
        GraphOpsNodeDriverContext ctx = runtime.Context;
        for (int i = 0; i < ctx.SimActors.Length; i++)
        {
            float world = GraphOpsNodeActorBinding.ReadHealth(ctx.SimWorld, ctx.SimActors[i]);
            Assert.That(
                world,
                Is.EqualTo(ctx.ActorHealth[i]).Within(0.01f),
                $"{runtime.Op}:{ctx.Vignette.Actors[i].Id}");
        }
    }

    private static void AssertBannedEnglish(string detail)
    {
        Assert.That(detail, Does.Not.Contain("tally"));
        Assert.That(detail, Does.Not.Contain("Validation"));
        Assert.That(detail, Does.Not.Contain("FuncLib"));
        Assert.That(detail, Does.Not.Contain("True"));
        Assert.That(detail, Does.Not.Contain("False"));
        Assert.That(detail, Does.Not.Contain("耗时"));
        Assert.That(detail, Does.Not.Contain("GraphNodeOp"));
        Assert.That(detail, Does.Not.Contain("loyalty"));
        Assert.That(detail, Does.Not.Contain("Loyalty"));
        Assert.That(detail, Does.Not.Contain("Trusted"));
        Assert.That(detail, Does.Not.Contain("Estranged"));
        Assert.That(detail, Does.Not.Contain("SocialBond"));
        Assert.That(detail, Does.Not.Contain("Relationship"));
    }
}
