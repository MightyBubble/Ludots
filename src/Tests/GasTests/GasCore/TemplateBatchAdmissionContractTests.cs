using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.Gameplay.Spawning;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Core;

[TestFixture]
public sealed class TemplateBatchAdmissionContractTests
{
    [Test]
    public void CaseEMarineShape_StaysOffBatchLane()
    {
        using World world = World.Create();
        var template = new EntityTemplate
        {
            Id = "case_e_marine",
            Components =
            {
                ["Name"] = JsonNode.Parse("{ \"Value\": \"可框选单位\" }")!,
                ["Team"] = JsonNode.Parse("{ \"Id\": 0 }")!,
                ["WorldPositionCm"] = JsonNode.Parse("{ \"Value\": { \"X\": 0, \"Y\": 0 } }")!,
                ["FacingDirection"] = JsonNode.Parse("{ \"AngleRad\": 0 }")!,
                ["AttributeBuffer"] = JsonNode.Parse("{ \"base\": { \"Health\": 60 } }")!,
                ["SpatialBounds"] = JsonNode.Parse("{ \"kind\": \"Box3D\", \"localCenterXCm\": 0, \"localCenterYCm\": 120, \"localCenterZCm\": 0 }")!,
                ["SpatialBox3D"] = JsonNode.Parse("{ \"halfSizeXCm\": 90, \"halfSizeYCm\": 120, \"halfSizeZCm\": 90 }")!,
                ["GameplayTagContainer"] = JsonNode.Parse("{}")!,
                ["OrderBuffer"] = JsonNode.Parse("{}")!,
                ["BlackboardSpatialBuffer"] = JsonNode.Parse("{}")!,
                ["BlackboardEntityBuffer"] = JsonNode.Parse("{}")!,
                ["BlackboardIntBuffer"] = JsonNode.Parse("{}")!,
                ["PlayerOwner"] = JsonNode.Parse("{ \"PlayerId\": 1 }")!,
            },
        };
        var spawner = new TemplateEntityBatchSpawner(world, new EntityTemplateKeyRegistry(), scratchCapacity: 4);

        Assert.That(spawner.IsBatchCompatible(template.Id, template), Is.False);
        Assert.That(world.Size, Is.EqualTo(0));
    }

    [Test]
    public void SpatialComponents_AreNotAdmittedByDefault()
    {
        using World world = World.Create();
        var posed = new EntityTemplate
        {
            Id = "posed_spatial",
            Components =
            {
                ["Name"] = JsonNode.Parse("{ \"Value\": \"Posed\" }")!,
                ["WorldPositionCm"] = JsonNode.Parse("{ \"Value\": { \"X\": 0, \"Y\": 0 } }")!,
                ["FacingDirection"] = JsonNode.Parse("{ \"AngleRad\": 0 }")!,
                ["SpatialBounds"] = JsonNode.Parse("{ \"kind\": \"Box3D\", \"localCenterXCm\": 0, \"localCenterYCm\": 0, \"localCenterZCm\": 0 }")!,
                ["SpatialBox3D"] = JsonNode.Parse("{ \"halfSizeXCm\": 1, \"halfSizeYCm\": 1, \"halfSizeZCm\": 1 }")!,
            },
        };
        var footprint = new EntityTemplate
        {
            Id = "footprint_only",
            Components =
            {
                ["Name"] = JsonNode.Parse("{ \"Value\": \"Footprint\" }")!,
                ["Team"] = JsonNode.Parse("{ \"Id\": 1 }")!,
                ["SpatialFootprint2D"] = JsonNode.Parse("{}")!,
            },
        };
        var spawner = new TemplateEntityBatchSpawner(world, new EntityTemplateKeyRegistry(), scratchCapacity: 4);

        Assert.That(spawner.IsBatchCompatible(posed.Id, posed), Is.False);
        Assert.That(spawner.IsBatchCompatible(footprint.Id, footprint), Is.False);
        Assert.That(world.Size, Is.EqualTo(0));
    }

    [Test]
    public void RegisteredAdmission_ProbesADetachedWorld()
    {
        using World world = World.Create();
        var template = new EntityTemplate
        {
            Id = "admitted_agent",
            Components =
            {
                ["Name"] = JsonNode.Parse("{ \"Value\": \"Admitted\" }")!,
                ["Team"] = JsonNode.Parse("{ \"Id\": 1 }")!,
                ["CrowdSimulationAgent"] = JsonNode.Parse("""
                    {
                      "profileId": "foot_r100",
                      "agentTypeId": "foot",
                      "speedCmPerSecond": 1400,
                      "personalRadiusCm": 600,
                      "pushPriority": 2,
                      "special": false
                    }
                    """)!,
            },
        };
        var spawner = new TemplateEntityBatchSpawner(world, new EntityTemplateKeyRegistry(), scratchCapacity: 4);
        Assert.That(spawner.IsBatchCompatible(template.Id, template), Is.False);

        spawner.RegisterBatchComponent<CrowdSimulationAgent>("CrowdSimulationAgent");
        spawner.RegisterBatchComponent<CrowdSimulationAgent>("CrowdSimulationAgent");

        Assert.That(spawner.IsBatchCompatible(template.Id, template), Is.True);
        Assert.That(world.Size, Is.EqualTo(0));
    }
}
