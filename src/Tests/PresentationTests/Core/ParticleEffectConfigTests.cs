using System;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Particles;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class ParticleEffectConfigTests
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    [Test]
    public void ParticleEffectConfigLoader_LoadsCatalogEntryAndMeshAssetReferencesParticleEffectId()
    {
        string core = CreateCoreRoot("Ludots_ParticleEffectConfig_LoadsReference");
        WriteCatalog(
            core,
            "Presentation/particle_effects.json", "ArrayById", "id",
            "Presentation/mesh_assets.json", "ArrayById", "id");
        WriteParticleEffects(core, ValidParticleEffectsJson());
        WriteMeshAssets(
            core,
            """
            [
              {
                "id": "effect.quarks.spark",
                "type": "Primitive",
                "primitiveKind": "Sphere",
                "vfx": {
                  "spawnMode": "Loop",
                  "particleEffectId": "quarks.spark.trail"
                }
              }
            ]
            """);

        var pipeline = CreatePipeline(core);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        var particleEffects = new ParticleEffectRegistry();
        new ParticleEffectConfigLoader(pipeline, particleEffects).Load(catalog);

        var meshes = new MeshAssetRegistry();
        new MeshAssetConfigLoader(pipeline, meshes, particleEffects).Load(catalog);

        int meshAssetId = meshes.GetId("effect.quarks.spark");
        Assert.That(meshAssetId, Is.GreaterThan(0));
        Assert.That(meshes.TryGetDescriptor(meshAssetId, out MeshAssetDescriptor descriptor), Is.True);
        Assert.That(descriptor.VfxEffectData.IsValid, Is.True);
        Assert.That(descriptor.VfxEffectData.ParticleSystem, Is.Not.Null);
        Assert.That(descriptor.VfxEffectData.ParticleEffectAssetId, Is.EqualTo(particleEffects.GetId("quarks.spark.trail")));
        Assert.That(descriptor.VfxEffectData.ParticleSystem!.RenderMode, Is.EqualTo(ParticleRenderMode.Mesh));
        Assert.That(descriptor.VfxEffectData.ParticleSystem!.BlendMode, Is.EqualTo(ParticleBlendMode.Alpha));
    }

    [Test]
    public void MeshAssetConfigLoader_VfxParticleEffectId_RequiresParticleRegistry()
    {
        string core = CreateCoreRoot("Ludots_ParticleEffectConfig_RequiresRegistry");
        WriteCatalog(core, "Presentation/mesh_assets.json", "ArrayById", "id");
        WriteMeshAssets(core, MeshAssetReferencingParticleEffectJson());

        var pipeline = CreatePipeline(core);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        var meshes = new MeshAssetRegistry();

        Exception ex = Assert.Throws<InvalidOperationException>(
            () => new MeshAssetConfigLoader(pipeline, meshes).Load(catalog))!;
        Assert.That(ex.Message, Does.Contain("requires the Presentation particle effect registry"));
    }

    [Test]
    public void MeshAssetConfigLoader_VfxParticleSystem_RejectsEmbeddedParticlePayload()
    {
        string core = CreateCoreRoot("Ludots_ParticleEffectConfig_RejectsEmbedded");
        WriteCatalog(core, "Presentation/mesh_assets.json", "ArrayById", "id");
        WriteMeshAssets(
            core,
            """
            [
              {
                "id": "effect.bad.embedded",
                "type": "Primitive",
                "primitiveKind": "Sphere",
                "vfx": {
                  "spawnMode": "Loop",
                  "particleSystem": {
                    "shape": "Cone"
                  }
                }
              }
            ]
            """);

        var pipeline = CreatePipeline(core);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        var meshes = new MeshAssetRegistry();

        Exception ex = Assert.Throws<InvalidOperationException>(
            () => new MeshAssetConfigLoader(pipeline, meshes, new ParticleEffectRegistry()).Load(catalog))!;
        Assert.That(ex.Message, Does.Contain("must reference particleEffectId"));
        Assert.That(ex.Message, Does.Contain("Presentation/particle_effects.json"));
    }

    [Test]
    public void MeshAssetConfigLoader_VfxParticleSystem_RejectsEmbeddedParticlePayloadEvenWhenNull()
    {
        string core = CreateCoreRoot("Ludots_ParticleEffectConfig_RejectsEmbeddedNull");
        WriteCatalog(core, "Presentation/mesh_assets.json", "ArrayById", "id");
        WriteMeshAssets(
            core,
            """
            [
              {
                "id": "effect.bad.embedded_null",
                "type": "Primitive",
                "primitiveKind": "Sphere",
                "vfx": {
                  "spawnMode": "Loop",
                  "particleEffectId": "quarks.spark.trail",
                  "particleSystem": null
                }
              }
            ]
            """);

        var pipeline = CreatePipeline(core);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        var meshes = new MeshAssetRegistry();

        Exception ex = Assert.Throws<InvalidOperationException>(
            () => new MeshAssetConfigLoader(pipeline, meshes, new ParticleEffectRegistry()).Load(catalog))!;
        Assert.That(ex.Message, Does.Contain("must reference particleEffectId"));
        Assert.That(ex.Message, Does.Contain("Presentation/particle_effects.json"));
    }

    [Test]
    public void MeshAssetConfigLoader_VfxParticleEffectId_RejectsLegacyEmitterFields()
    {
        string core = CreateCoreRoot("Ludots_ParticleEffectConfig_RejectsMixedSources");
        WriteCatalog(core, "Presentation/mesh_assets.json", "ArrayById", "id");
        WriteMeshAssets(
            core,
            """
            [
              {
                "id": "effect.bad.mixed",
                "type": "Primitive",
                "primitiveKind": "Sphere",
                "vfx": {
                  "spawnMode": "Loop",
                  "particleEffectId": "quarks.spark.trail",
                  "emitter": {
                    "shape": "PrimitiveSphere",
                    "particleCount": 4,
                    "ringSegments": 12,
                    "radiusScale": 1,
                    "coreRadiusScale": 0.2,
                    "particleRadiusScale": 0.1,
                    "lifetimeSeconds": 1,
                    "pulseSpeedRadPerSecond": 1,
                    "orbitSpeedRadPerSecond": 1,
                    "shellRingCount": 1,
                    "beamCount": 0
                  }
                }
              }
            ]
            """);

        var pipeline = CreatePipeline(core);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        var meshes = new MeshAssetRegistry();

        Exception ex = Assert.Throws<InvalidOperationException>(
            () => new MeshAssetConfigLoader(pipeline, meshes, new ParticleEffectRegistry()).Load(catalog))!;
        Assert.That(ex.Message, Does.Contain("legacy emitter/color"));
    }

    [Test]
    public void MeshAssetConfigLoader_VfxParticleEffectId_RejectsLegacyEmitterFieldsEvenWhenNull()
    {
        string core = CreateCoreRoot("Ludots_ParticleEffectConfig_RejectsNullLegacyEmitter");
        WriteCatalog(core, "Presentation/mesh_assets.json", "ArrayById", "id");
        WriteMeshAssets(
            core,
            """
            [
              {
                "id": "effect.bad.null_legacy",
                "type": "Primitive",
                "primitiveKind": "Sphere",
                "vfx": {
                  "spawnMode": "Loop",
                  "particleEffectId": "quarks.spark.trail",
                  "emitter": null
                }
              }
            ]
            """);

        var pipeline = CreatePipeline(core);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        var meshes = new MeshAssetRegistry();

        Exception ex = Assert.Throws<InvalidOperationException>(
            () => new MeshAssetConfigLoader(pipeline, meshes, new ParticleEffectRegistry()).Load(catalog))!;
        Assert.That(ex.Message, Does.Contain("legacy emitter/color"));
    }

    [Test]
    public void MeshAssetConfigLoader_VfxParticleEffectId_RejectsLegacyColorFields()
    {
        string core = CreateCoreRoot("Ludots_ParticleEffectConfig_RejectsLegacyColors");
        WriteCatalog(core, "Presentation/mesh_assets.json", "ArrayById", "id");
        WriteMeshAssets(
            core,
            """
            [
              {
                "id": "effect.bad.colors",
                "type": "Primitive",
                "primitiveKind": "Sphere",
                "vfx": {
                  "spawnMode": "Loop",
                  "particleEffectId": "quarks.spark.trail",
                  "coreColor": [1, 1, 1, 1]
                }
              }
            ]
            """);

        var pipeline = CreatePipeline(core);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        var meshes = new MeshAssetRegistry();

        Exception ex = Assert.Throws<InvalidOperationException>(
            () => new MeshAssetConfigLoader(pipeline, meshes, new ParticleEffectRegistry()).Load(catalog))!;
        Assert.That(ex.Message, Does.Contain("legacy emitter/color"));
    }

    [Test]
    public void MeshAssetConfigLoader_VfxParticleEffectId_RejectsUnknownParticleAsset()
    {
        string core = CreateCoreRoot("Ludots_ParticleEffectConfig_RejectsUnknownParticle");
        WriteCatalog(core, "Presentation/mesh_assets.json", "ArrayById", "id");
        WriteMeshAssets(core, MeshAssetReferencingParticleEffectJson());

        var pipeline = CreatePipeline(core);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        var meshes = new MeshAssetRegistry();

        Exception ex = Assert.Throws<InvalidOperationException>(
            () => new MeshAssetConfigLoader(pipeline, meshes, new ParticleEffectRegistry()).Load(catalog))!;
        Assert.That(ex.Message, Does.Contain("unknown particle effect asset"));
    }

    [Test]
    public void MeshAssetConfigLoader_VfxParticleEffectId_RequiresMatchingSpawnMode()
    {
        string core = CreateCoreRoot("Ludots_ParticleEffectConfig_RequiresMatchingSpawnMode");
        WriteCatalog(
            core,
            "Presentation/particle_effects.json", "ArrayById", "id",
            "Presentation/mesh_assets.json", "ArrayById", "id");
        JsonObject effect = ValidParticleEffectObject();
        effect["spawnMode"] = "Once";
        WriteParticleEffects(core, JsonArrayString(effect));
        WriteMeshAssets(core, MeshAssetReferencingParticleEffectJson());

        var pipeline = CreatePipeline(core);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        var particleEffects = new ParticleEffectRegistry();
        new ParticleEffectConfigLoader(pipeline, particleEffects).Load(catalog);
        var meshes = new MeshAssetRegistry();

        Exception ex = Assert.Throws<InvalidOperationException>(
            () => new MeshAssetConfigLoader(pipeline, meshes, particleEffects).Load(catalog))!;
        Assert.That(ex.Message, Does.Contain("must match particle effect"));
    }

    [Test]
    public void ParticleEffectConfigLoader_RejectsZeroSeed()
    {
        JsonObject effect = ValidParticleEffectObject();
        effect["seed"] = 0;

        Exception ex = AssertParticleEffectLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("seed"));
        Assert.That(ex.Message, Does.Contain("non-zero"));
    }

    [Test]
    public void ParticleEffectConfigLoader_RejectsWrongCaseEnum()
    {
        JsonObject effect = ValidParticleEffectObject();
        effect["renderMode"] = "mesh";

        Exception ex = AssertParticleEffectLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("renderMode"));
        Assert.That(ex.Message, Does.Contain("invalid value"));
    }

    [Test]
    public void ParticleEffectConfigLoader_RejectsMissingBlendMode()
    {
        JsonObject effect = ValidParticleEffectObject();
        effect.Remove("blendMode");

        Exception ex = AssertParticleEffectLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("blendMode"));
    }

    [Test]
    public void ParticleEffectConfigLoader_LoadsBillboardTextureSheetAndBlendMode()
    {
        JsonObject effect = ValidParticleEffectObject();
        effect["renderMode"] = "Billboard";
        effect["blendMode"] = "Additive";
        effect["textureSheet"] = ValidTextureSheetObject();

        string core = CreateCoreRoot("Ludots_ParticleEffectConfig_LoadsTextureSheet");
        WriteCatalog(core, "Presentation/particle_effects.json", "ArrayById", "id");
        WriteParticleEffects(core, JsonArrayString(effect));

        var pipeline = CreatePipeline(core);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        var particleEffects = new ParticleEffectRegistry();
        new ParticleEffectConfigLoader(pipeline, particleEffects).Load(catalog);

        int effectId = particleEffects.GetId("quarks.spark.trail");
        Assert.That(particleEffects.TryGet(effectId, out ParticleEffectAssetData loaded), Is.True);
        Assert.That(loaded.RenderMode, Is.EqualTo(ParticleRenderMode.Billboard));
        Assert.That(loaded.BlendMode, Is.EqualTo(ParticleBlendMode.Additive));
        Assert.That(loaded.TextureSheet, Is.Not.Null);
        Assert.That(loaded.TextureSheet!.TextureAssetId, Is.EqualTo("quarks.texture.flame"));
        Assert.That(loaded.TextureSheet.Columns, Is.EqualTo(4));
        Assert.That(loaded.TextureSheet.Rows, Is.EqualTo(2));
        Assert.That(loaded.TextureSheet.FrameCount, Is.EqualTo(8));
    }

    [Test]
    public void ParticleEffectConfigLoader_RejectsBillboardWithoutTextureSheet()
    {
        JsonObject effect = ValidParticleEffectObject();
        effect["renderMode"] = "Billboard";

        Exception ex = AssertParticleEffectLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("textureSheet"));
        Assert.That(ex.Message, Does.Contain("required"));
    }

    [Test]
    public void ParticleEffectConfigLoader_RejectsTextureSheetOnMeshParticles()
    {
        JsonObject effect = ValidParticleEffectObject();
        effect["textureSheet"] = ValidTextureSheetObject();

        Exception ex = AssertParticleEffectLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("textureSheet"));
        Assert.That(ex.Message, Does.Contain("only valid"));
    }

    [Test]
    public void ParticleEffectConfigLoader_RejectsTextureSheetStartFrameOutsideFrameCount()
    {
        JsonObject effect = ValidParticleEffectObject();
        effect["renderMode"] = "Billboard";
        JsonObject sheet = ValidTextureSheetObject();
        sheet["startFrame"] = new JsonArray(0, 8);
        effect["textureSheet"] = sheet;

        Exception ex = AssertParticleEffectLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("startFrame"));
    }

    [Test]
    public void ParticleEffectConfigLoader_RejectsStretchedBillboardWithoutLengthScale()
    {
        JsonObject effect = ValidParticleEffectObject();
        effect["renderMode"] = "StretchedBillboard";
        effect["textureSheet"] = ValidTextureSheetObject();

        Exception ex = AssertParticleEffectLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("stretchedLengthScale"));
    }

    [Test]
    public void ParticleEffectConfigLoader_RejectsLengthScaleOnNonStretchedParticles()
    {
        JsonObject effect = ValidParticleEffectObject();
        effect["stretchedLengthScale"] = 1.2;

        Exception ex = AssertParticleEffectLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("stretchedLengthScale"));
        Assert.That(ex.Message, Does.Contain("only valid"));
    }

    [Test]
    public void ParticleEffectConfigLoader_RejectsNumericEnumString()
    {
        JsonObject effect = ValidParticleEffectObject();
        effect["renderMode"] = "2";

        Exception ex = AssertParticleEffectLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("renderMode"));
        Assert.That(ex.Message, Does.Contain("enum name"));
    }

    [Test]
    public void ParticleEffectConfigLoader_RejectsInvalidRange()
    {
        JsonObject effect = ValidParticleEffectObject();
        effect["startSpeed"] = new JsonArray(2.0, 1.0);

        Exception ex = AssertParticleEffectLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("Particle value ranges"));
    }

    [Test]
    public void ParticleEffectConfigLoader_RejectsNonFiniteNumber()
    {
        string json = ValidParticleEffectsJson().Replace("\"durationSeconds\": 1.5", "\"durationSeconds\": 1e39", StringComparison.Ordinal);

        Exception ex = AssertParticleEffectLoadFails(json);
        Assert.That(ex.Message, Does.Contain("durationSeconds"));
    }

    [Test]
    public void ParticleEffectConfigLoader_RejectsUnsortedCurveKeys()
    {
        JsonObject effect = ValidParticleEffectObject();
        effect["sizeOverLife"] = new JsonArray(
            new JsonObject { ["position"] = 1.0, ["value"] = 1.0 },
            new JsonObject { ["position"] = 0.0, ["value"] = 0.2 });

        Exception ex = AssertParticleEffectLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("sorted"));
    }

    [Test]
    public void ParticleEffectConfigLoader_RejectsUnsortedGradientKeys()
    {
        JsonObject effect = ValidParticleEffectObject();
        effect["colorOverLife"] = new JsonArray(
            new JsonObject { ["position"] = 1.0, ["color"] = new JsonArray(1.0, 1.0, 1.0, 0.0) },
            new JsonObject { ["position"] = 0.0, ["color"] = new JsonArray(1.0, 0.4, 0.1, 1.0) });

        Exception ex = AssertParticleEffectLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("sorted"));
    }

    [Test]
    public void ParticleEffectConfigLoader_RejectsUnknownCurveKeyField()
    {
        JsonObject effect = ValidParticleEffectObject();
        JsonObject firstKey = effect["sizeOverLife"]!.AsArray()[0]!.AsObject();
        firstKey["tangent"] = 0.5;

        Exception ex = AssertParticleEffectLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("unsupported field 'tangent'"));
    }

    [Test]
    public void ParticleEffectConfigLoader_RejectsUnknownGradientKeyField()
    {
        JsonObject effect = ValidParticleEffectObject();
        JsonObject firstKey = effect["colorOverLife"]!.AsArray()[0]!.AsObject();
        firstKey["blendMode"] = "Soft";

        Exception ex = AssertParticleEffectLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("unsupported field 'blendMode'"));
    }

    private static Exception AssertParticleEffectLoadFails(string particleEffectsJson)
    {
        string core = CreateCoreRoot("Ludots_ParticleEffectConfig_Invalid");
        WriteCatalog(core, "Presentation/particle_effects.json", "ArrayById", "id");
        WriteParticleEffects(core, particleEffectsJson);

        var pipeline = CreatePipeline(core);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        var particleEffects = new ParticleEffectRegistry();
        return Assert.Catch<Exception>(
            () => new ParticleEffectConfigLoader(pipeline, particleEffects).Load(catalog))!;
    }

    private static ConfigPipeline CreatePipeline(string core)
    {
        var vfs = new VirtualFileSystem();
        vfs.Mount("Core", core);
        var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
        return new ConfigPipeline(vfs, modLoader);
    }

    private static string CreateCoreRoot(string label)
    {
        string core = Path.Combine(Path.GetTempPath(), label, Guid.NewGuid().ToString("N"), "Core");
        Directory.CreateDirectory(Path.Combine(core, "Configs", "Presentation"));
        return core;
    }

    private static void WriteCatalog(string coreRoot, params string[] triples)
    {
        if (triples.Length % 3 != 0)
        {
            throw new ArgumentException("Catalog entries must be path/policy/idField triples.", nameof(triples));
        }

        Directory.CreateDirectory(Path.Combine(coreRoot, "Configs"));
        using var writer = new StringWriter();
        writer.WriteLine("[");
        for (int i = 0; i < triples.Length; i += 3)
        {
            if (i > 0)
            {
                writer.WriteLine(",");
            }

            writer.Write($"  {{ \"Path\": \"{triples[i]}\", \"Policy\": \"{triples[i + 1]}\"");
            if (!string.IsNullOrWhiteSpace(triples[i + 2]))
            {
                writer.Write($", \"IdField\": \"{triples[i + 2]}\"");
            }

            writer.Write(" }");
        }

        writer.WriteLine();
        writer.WriteLine("]");
        File.WriteAllText(Path.Combine(coreRoot, "Configs", "config_catalog.json"), writer.ToString(), Utf8NoBom);
    }

    private static void WriteParticleEffects(string coreRoot, string json)
    {
        File.WriteAllText(Path.Combine(coreRoot, "Configs", "Presentation", "particle_effects.json"), json, Utf8NoBom);
    }

    private static void WriteMeshAssets(string coreRoot, string json)
    {
        File.WriteAllText(Path.Combine(coreRoot, "Configs", "Presentation", "mesh_assets.json"), json, Utf8NoBom);
    }

    private static string MeshAssetReferencingParticleEffectJson()
    {
        return """
        [
          {
            "id": "effect.quarks.spark",
            "type": "Primitive",
            "primitiveKind": "Sphere",
            "vfx": {
              "spawnMode": "Loop",
              "particleEffectId": "quarks.spark.trail"
            }
          }
        ]
        """;
    }

    private static JsonObject ValidParticleEffectObject()
    {
        return JsonNode.Parse(ValidParticleEffectsJson())!.AsArray()[0]!.AsObject();
    }

    private static string JsonArrayString(JsonObject obj)
    {
        var array = new JsonArray(obj.DeepClone());
        return array.ToJsonString();
    }

    private static string ValidParticleEffectsJson()
    {
        return """
        [
          {
            "id": "quarks.spark.trail",
            "version": "quarks.ludots.v1",
            "spawnMode": "Loop",
            "shape": "Cone",
            "renderMode": "Mesh",
            "blendMode": "Alpha",
            "primitive": "Sphere",
            "overflowPolicy": "DropNewest",
            "maxParticles": 96,
            "seed": 12345,
            "durationSeconds": 1.5,
            "emissionRatePerSecond": 36,
            "burstCount": 12,
            "shapeRadius": 0.35,
            "shapeAngleRadians": 0.45,
            "shapeThickness": 0.8,
            "startLife": [0.65, 1.2],
            "startSpeed": [0.7, 2.2],
            "startSize": [0.08, 0.18],
            "startColor": [1.0, 0.72, 0.22, 1.0],
            "sizeOverLife": [
              { "position": 0.0, "value": 0.25 },
              { "position": 0.25, "value": 1.0 },
              { "position": 1.0, "value": 0.0 }
            ],
            "colorOverLife": [
              { "position": 0.0, "color": [1.0, 0.72, 0.22, 1.0] },
              { "position": 0.55, "color": [0.3, 0.8, 1.0, 0.55] },
              { "position": 1.0, "color": [0.1, 0.15, 0.25, 0.0] }
            ],
            "gravity": [0.0, 0.35, 0.0],
            "drag": 0.15,
            "worldSpace": true
          }
        ]
        """;
    }

    private static JsonObject ValidTextureSheetObject()
    {
        return new JsonObject
        {
            ["textureAssetId"] = "quarks.texture.flame",
            ["columns"] = 4,
            ["rows"] = 2,
            ["frameCount"] = 8,
            ["framesPerSecond"] = 16.0,
            ["startFrame"] = new JsonArray(0, 0),
            ["playbackMode"] = "Loop",
        };
    }
}
