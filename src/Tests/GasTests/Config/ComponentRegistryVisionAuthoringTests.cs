using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Config;
using Ludots.Core.Vision;
using CoreComponentRegistry = Ludots.Core.Config.ComponentRegistry;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
[Category("ci-gate")]
public sealed class ComponentRegistryVisionAuthoringTests
{
    [Test]
    public void VisionEmitterCm_AuthorsNamedScopeLayersPolarityAndAperture()
    {
        using World world = World.Create();
        Entity entity = world.Create();
        var (context, scopeKeys, layers) = CreateAuthoringContext();
        int scopeId = scopeKeys.GetId("test.vision.scope");
        FogLayerId ground = layers.GetId("ground");
        FogLayerId air = layers.GetId("air");

        CoreComponentRegistry.Apply(entity, "VisionEmitterCm", ValidEmitter(), context, "test");

        VisionEmitterCm emitter = world.Get<VisionEmitterCm>(entity);
        Assert.Multiple(() =>
        {
            Assert.That(emitter.ScopeKeyId, Is.EqualTo(scopeId));
            Assert.That(emitter.LayerMask, Is.EqualTo(layers.ToMask(ground) | layers.ToMask(air)));
            Assert.That(emitter.Polarity, Is.EqualTo(VisionPolarity.Reveal));
            Assert.That(emitter.Aperture.Kind, Is.EqualTo(VisionApertureKind.Cone));
            Assert.That(emitter.Aperture.RangeCm, Is.EqualTo(3_200));
            Assert.That(emitter.Aperture.HalfAngleDeg, Is.EqualTo(45));
            Assert.That(emitter.Aperture.HalfWidthCm, Is.Zero);
            Assert.That(emitter.AltitudeBand, Is.EqualTo(3));
            Assert.That(emitter.Priority, Is.EqualTo(4));
            Assert.That(emitter.DetectionStrength, Is.EqualTo(8));
            Assert.That(emitter.TrueSightStrength, Is.EqualTo(9));
        });
    }

    [Test]
    public void VisionEmitterCm_RejectsMissingAndUnsupportedApertureData()
    {
        using World world = World.Create();
        var (context, _, _) = CreateAuthoringContext();
        Entity missingRangeEntity = world.Create();
        JsonNode missingRange = ValidEmitter();
        missingRange["aperture"]!.AsObject().Remove("rangeCm");

        var missingRangeError = Assert.Throws<InvalidOperationException>(
            () => CoreComponentRegistry.Apply(missingRangeEntity, "VisionEmitterCm", missingRange, context, "test"));
        Assert.That(missingRangeError!.Message, Does.Contain("rangeCm"));

        Entity unsupportedKindEntity = world.Create();
        JsonNode unsupportedKind = ValidEmitter();
        unsupportedKind["aperture"]!["kind"] = "Wedge";

        var unsupportedKindError = Assert.Throws<InvalidOperationException>(
            () => CoreComponentRegistry.Apply(unsupportedKindEntity, "VisionEmitterCm", unsupportedKind, context, "test"));
        Assert.That(unsupportedKindError!.Message, Does.Contain("Wedge"));
        Assert.That(unsupportedKindError.Message, Does.Contain("Disk, Cone, Box, or Line"));
    }

    [Test]
    public void VisionEmitterCm_RejectsUndeclaredScopeAndLayerNames()
    {
        using World world = World.Create();
        var (context, _, _) = CreateAuthoringContext();
        Entity unknownScopeEntity = world.Create();
        JsonNode unknownScope = ValidEmitter();
        unknownScope["scope"] = "test.vision.missing";

        var unknownScopeError = Assert.Throws<InvalidOperationException>(
            () => CoreComponentRegistry.Apply(unknownScopeEntity, "VisionEmitterCm", unknownScope, context, "test"));
        Assert.That(unknownScopeError!.Message, Does.Contain("test.vision.missing"));
        Assert.That(unknownScopeError.Message, Does.Contain("Progression/scopes.json"));

        Entity unknownLayerEntity = world.Create();
        JsonNode unknownLayer = ValidEmitter();
        unknownLayer["layers"] = new JsonArray("trench");

        var unknownLayerError = Assert.Throws<InvalidOperationException>(
            () => CoreComponentRegistry.Apply(unknownLayerEntity, "VisionEmitterCm", unknownLayer, context, "test"));
        Assert.That(unknownLayerError!.Message, Does.Contain("trench"));
    }

    private static (ComponentAuthoringContext Context, ScopeKeyRegistry ScopeKeys, FogLayerRegistry Layers) CreateAuthoringContext()
    {
        var scopeKeys = new ScopeKeyRegistry();
        scopeKeys.Register("test.vision.scope");
        var layers = new FogLayerRegistry();
        layers.Register("ground", cellSizeCm: 400, updateHz: 10);
        layers.Register("air", cellSizeCm: 400, updateHz: 10);
        var context = new ComponentAuthoringContext();
        context.Set(ComponentAuthoringServiceKeys.ScopeKeyRegistry, scopeKeys);
        context.Set(ComponentAuthoringServiceKeys.VisionFogLayerRegistry, layers);
        return (context, scopeKeys, layers);
    }

    private static JsonNode ValidEmitter() => JsonNode.Parse("""
        {
          "scope": "test.vision.scope",
          "layers": ["ground", "air"],
          "polarity": "Reveal",
          "aperture": {
            "kind": "Cone",
            "rangeCm": 3200,
            "halfAngleDeg": 45,
            "halfWidthCm": 0
          },
          "altitudeBand": 3,
          "priority": 4,
          "detectionStrength": 8,
          "trueSightStrength": 9
        }
        """)!;
}
