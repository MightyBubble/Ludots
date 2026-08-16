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
public sealed class ParticleVfxConfigTests
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    [Test]
    public void ParticleVfxConfigLoader_LoadsCatalogEntryAndMeshAssetReferencesParticleVfxId()
    {
        string core = CreateCoreRoot("Ludots_ParticleVfxConfig_LoadsReference");
        WriteCatalog(
            core,
            "Presentation/particle_vfx.json", "ArrayById", "id",
            "Presentation/mesh_assets.json", "ArrayById", "id");
        WriteParticleVfx(core, ValidParticleVfxJson());
        WriteMeshAssets(
            core,
            """
            [
              {
                "id": "effect.quarks.spark",
                "type": "Primitive",
                "primitiveKind": "Sphere",
                "vfx": {
                  "particleVfxId": "quarks.spark.trail"
                }
              }
            ]
            """);

        var pipeline = CreatePipeline(core);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        var particleVfx = new ParticleVfxRegistry();
        new ParticleVfxConfigLoader(pipeline, particleVfx).Load(catalog);

        var meshes = new MeshAssetRegistry();
        new MeshAssetConfigLoader(pipeline, meshes, particleVfx).Load(catalog);

        int meshAssetId = meshes.GetId("effect.quarks.spark");
        Assert.That(meshAssetId, Is.GreaterThan(0));
        Assert.That(meshes.TryGetDescriptor(meshAssetId, out MeshAssetDescriptor descriptor), Is.True);
        Assert.That(descriptor.VfxData.IsValid, Is.True);
        Assert.That(descriptor.VfxData.ParticleSystem, Is.Not.Null);
        Assert.That(descriptor.VfxData.ParticleVfxAssetId, Is.EqualTo(particleVfx.GetId("quarks.spark.trail")));
        Assert.That(descriptor.VfxData.ParticleSystem!.RenderMode, Is.EqualTo(ParticleRenderMode.Primitive));
        Assert.That(descriptor.VfxData.ParticleSystem!.BlendMode, Is.EqualTo(ParticleBlendMode.Alpha));
    }

    [Test]
    public void MeshAssetConfigLoader_ParticleVfxId_RequiresParticleRegistry()
    {
        string core = CreateCoreRoot("Ludots_ParticleVfxConfig_RequiresRegistry");
        WriteCatalog(core, "Presentation/mesh_assets.json", "ArrayById", "id");
        WriteMeshAssets(core, MeshAssetReferencingParticleVfxJson());

        var pipeline = CreatePipeline(core);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        var meshes = new MeshAssetRegistry();

        Exception ex = Assert.Throws<InvalidOperationException>(
            () => new MeshAssetConfigLoader(pipeline, meshes).Load(catalog))!;
        Assert.That(ex.Message, Does.Contain("requires the Presentation particle VFX registry"));
    }

    [Test]
    public void MeshAssetConfigLoader_VfxParticleSystem_RejectsEmbeddedParticlePayload()
    {
        string core = CreateCoreRoot("Ludots_ParticleVfxConfig_RejectsEmbedded");
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
            () => new MeshAssetConfigLoader(pipeline, meshes, new ParticleVfxRegistry()).Load(catalog))!;
        Assert.That(ex.Message, Does.Contain("not supported"));
        Assert.That(ex.Message, Does.Contain("Presentation/particle_vfx.json"));
    }

    [Test]
    public void MeshAssetConfigLoader_VfxParticleSystem_RejectsEmbeddedParticlePayloadEvenWhenNull()
    {
        string core = CreateCoreRoot("Ludots_ParticleVfxConfig_RejectsEmbeddedNull");
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
                  "particleVfxId": "quarks.spark.trail",
                  "particleSystem": null
                }
              }
            ]
            """);

        var pipeline = CreatePipeline(core);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        var meshes = new MeshAssetRegistry();

        Exception ex = Assert.Throws<InvalidOperationException>(
            () => new MeshAssetConfigLoader(pipeline, meshes, new ParticleVfxRegistry()).Load(catalog))!;
        Assert.That(ex.Message, Does.Contain("not supported"));
        Assert.That(ex.Message, Does.Contain("Presentation/particle_vfx.json"));
    }

    [Test]
    public void MeshAssetConfigLoader_ParticleVfxId_RejectsLegacyEmitterFields()
    {
        string core = CreateCoreRoot("Ludots_ParticleVfxConfig_RejectsMixedSources");
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
                  "particleVfxId": "quarks.spark.trail",
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
            () => new MeshAssetConfigLoader(pipeline, meshes, new ParticleVfxRegistry()).Load(catalog))!;
        Assert.That(ex.Message, Does.Contain("not supported"));
    }

    [Test]
    public void MeshAssetConfigLoader_ParticleVfxId_RejectsLegacyEmitterFieldsEvenWhenNull()
    {
        string core = CreateCoreRoot("Ludots_ParticleVfxConfig_RejectsNullLegacyEmitter");
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
                  "particleVfxId": "quarks.spark.trail",
                  "emitter": null
                }
              }
            ]
            """);

        var pipeline = CreatePipeline(core);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        var meshes = new MeshAssetRegistry();

        Exception ex = Assert.Throws<InvalidOperationException>(
            () => new MeshAssetConfigLoader(pipeline, meshes, new ParticleVfxRegistry()).Load(catalog))!;
        Assert.That(ex.Message, Does.Contain("not supported"));
    }

    [Test]
    public void MeshAssetConfigLoader_ParticleVfxId_RejectsLegacyColorFields()
    {
        string core = CreateCoreRoot("Ludots_ParticleVfxConfig_RejectsLegacyColors");
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
                  "particleVfxId": "quarks.spark.trail",
                  "coreColor": [1, 1, 1, 1]
                }
              }
            ]
            """);

        var pipeline = CreatePipeline(core);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        var meshes = new MeshAssetRegistry();

        Exception ex = Assert.Throws<InvalidOperationException>(
            () => new MeshAssetConfigLoader(pipeline, meshes, new ParticleVfxRegistry()).Load(catalog))!;
        Assert.That(ex.Message, Does.Contain("not supported"));
    }

    [Test]
    public void MeshAssetConfigLoader_ParticleVfxId_RejectsUnknownParticleAsset()
    {
        string core = CreateCoreRoot("Ludots_ParticleVfxConfig_RejectsUnknownParticle");
        WriteCatalog(core, "Presentation/mesh_assets.json", "ArrayById", "id");
        WriteMeshAssets(core, MeshAssetReferencingParticleVfxJson());

        var pipeline = CreatePipeline(core);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        var meshes = new MeshAssetRegistry();

        Exception ex = Assert.Throws<InvalidOperationException>(
            () => new MeshAssetConfigLoader(pipeline, meshes, new ParticleVfxRegistry()).Load(catalog))!;
        Assert.That(ex.Message, Does.Contain("unknown particle VFX asset"));
    }

    [Test]
    public void MeshAssetConfigLoader_ParticleVfxId_RejectsMeshSpawnMode()
    {
        string core = CreateCoreRoot("Ludots_ParticleVfxConfig_RejectsMeshSpawnMode");
        WriteCatalog(
            core,
            "Presentation/particle_vfx.json", "ArrayById", "id",
            "Presentation/mesh_assets.json", "ArrayById", "id");
        WriteParticleVfx(core, ValidParticleVfxJson());
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
                  "particleVfxId": "quarks.spark.trail"
                }
              }
            ]
            """);

        var pipeline = CreatePipeline(core);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        var particleVfx = new ParticleVfxRegistry();
        new ParticleVfxConfigLoader(pipeline, particleVfx).Load(catalog);
        var meshes = new MeshAssetRegistry();

        Exception ex = Assert.Throws<InvalidOperationException>(
            () => new MeshAssetConfigLoader(pipeline, meshes, particleVfx).Load(catalog))!;
        Assert.That(ex.Message, Does.Contain("spawnMode"));
        Assert.That(ex.Message, Does.Contain("not supported"));
    }

    [Test]
    public void ParticleVfxConfigLoader_RejectsOverflowPolicyField()
    {
        JsonObject effect = ValidParticleVfxObject();
        effect["overflowPolicy"] = "DropNewest";
        Exception ex = AssertParticleVfxLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("overflowPolicy"));
        Assert.That(ex.Message, Does.Contain("unsupported field"));
    }

    [Test]
    public void ParticleVfxConfigLoader_RejectsNonPositiveStartLife()
    {
        JsonObject effect = ValidParticleVfxObject();
        effect["startLife"] = new JsonArray(0, 1);
        Exception ex = AssertParticleVfxLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("startLife"));
        Assert.That(ex.Message, Does.Contain("min > 0"));
    }

    [Test]
    public void ParticleVfxConfigLoader_RejectsZeroSeed()
    {
        JsonObject effect = ValidParticleVfxObject();
        effect["seed"] = 0;

        Exception ex = AssertParticleVfxLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("seed"));
        Assert.That(ex.Message, Does.Contain("non-zero"));
    }

    [Test]
    public void ParticleVfxConfigLoader_RejectsWrongCaseEnum()
    {
        JsonObject effect = ValidParticleVfxObject();
        effect["renderMode"] = "primitive";

        Exception ex = AssertParticleVfxLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("renderMode"));
        Assert.That(ex.Message, Does.Contain("invalid value"));
    }

    [Test]
    public void ParticleVfxConfigLoader_RejectsMissingBlendMode()
    {
        JsonObject effect = ValidParticleVfxObject();
        effect.Remove("blendMode");

        Exception ex = AssertParticleVfxLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("blendMode"));
    }

    [Test]
    public void ParticleVfxConfigLoader_LoadsBillboardTextureSheetAndBlendMode()
    {
        JsonObject effect = ValidParticleVfxObject();
        effect["renderMode"] = "Billboard";
        effect["blendMode"] = "Additive";
        effect["textureSheet"] = ValidTextureSheetObject();

        string core = CreateCoreRoot("Ludots_ParticleVfxConfig_LoadsTextureSheet");
        WriteCatalog(core, "Presentation/particle_vfx.json", "ArrayById", "id");
        WriteParticleVfx(core, JsonArrayString(effect));

        var pipeline = CreatePipeline(core);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        var particleVfx = new ParticleVfxRegistry();
        new ParticleVfxConfigLoader(pipeline, particleVfx).Load(catalog);

        int effectId = particleVfx.GetId("quarks.spark.trail");
        Assert.That(particleVfx.TryGet(effectId, out ParticleVfxAssetData loaded), Is.True);
        Assert.That(loaded.RenderMode, Is.EqualTo(ParticleRenderMode.Billboard));
        Assert.That(loaded.BlendMode, Is.EqualTo(ParticleBlendMode.Additive));
        Assert.That(loaded.TextureSheet, Is.Not.Null);
        Assert.That(loaded.TextureSheet!.TextureAssetId, Is.EqualTo("quarks.texture.flame"));
        Assert.That(loaded.TextureSheet.Columns, Is.EqualTo(4));
        Assert.That(loaded.TextureSheet.Rows, Is.EqualTo(2));
        Assert.That(loaded.TextureSheet.FrameCount, Is.EqualTo(8));
    }

    [Test]
    public void ParticleVfxConfigLoader_RejectsBillboardWithoutTextureSheet()
    {
        JsonObject effect = ValidParticleVfxObject();
        effect["renderMode"] = "Billboard";

        Exception ex = AssertParticleVfxLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("textureSheet"));
        Assert.That(ex.Message, Does.Contain("required"));
    }

    [Test]
    public void ParticleVfxConfigLoader_RejectsTextureSheetOnMeshParticles()
    {
        JsonObject effect = ValidParticleVfxObject();
        effect["textureSheet"] = ValidTextureSheetObject();

        Exception ex = AssertParticleVfxLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("textureSheet"));
        Assert.That(ex.Message, Does.Contain("only valid"));
    }

    [Test]
    public void ParticleVfxConfigLoader_RejectsTextureSheetStartFrameOutsideFrameCount()
    {
        JsonObject effect = ValidParticleVfxObject();
        effect["renderMode"] = "Billboard";
        JsonObject sheet = ValidTextureSheetObject();
        sheet["startFrame"] = new JsonArray(0, 8);
        effect["textureSheet"] = sheet;

        Exception ex = AssertParticleVfxLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("startFrame"));
    }

    [Test]
    public void ParticleVfxConfigLoader_RejectsStretchedBillboardWithoutLengthScale()
    {
        JsonObject effect = ValidParticleVfxObject();
        effect["renderMode"] = "StretchedBillboard";
        effect["textureSheet"] = ValidTextureSheetObject();

        Exception ex = AssertParticleVfxLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("stretchedLengthScale"));
    }

    [Test]
    public void ParticleVfxConfigLoader_RejectsLengthScaleOnNonStretchedParticles()
    {
        JsonObject effect = ValidParticleVfxObject();
        effect["stretchedLengthScale"] = 1.2;

        Exception ex = AssertParticleVfxLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("stretchedLengthScale"));
        Assert.That(ex.Message, Does.Contain("only valid"));
    }

    [Test]
    public void ParticleVfxConfigLoader_RejectsNumericEnumString()
    {
        JsonObject effect = ValidParticleVfxObject();
        effect["renderMode"] = "2";

        Exception ex = AssertParticleVfxLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("renderMode"));
        Assert.That(ex.Message, Does.Contain("enum name"));
    }

    [Test]
    public void ParticleVfxConfigLoader_RejectsInvalidRange()
    {
        JsonObject effect = ValidParticleVfxObject();
        effect["startSpeed"] = new JsonArray(2.0, 1.0);

        Exception ex = AssertParticleVfxLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("Particle value ranges"));
    }

    [Test]
    public void ParticleVfxConfigLoader_RejectsNonFiniteNumber()
    {
        string json = ValidParticleVfxJson().Replace("\"durationSeconds\": 1.5", "\"durationSeconds\": 1e39", StringComparison.Ordinal);

        Exception ex = AssertParticleVfxLoadFails(json);
        Assert.That(ex.Message, Does.Contain("durationSeconds"));
    }

    [Test]
    public void ParticleVfxConfigLoader_RejectsUnsortedCurveKeys()
    {
        JsonObject effect = ValidParticleVfxObject();
        effect["sizeOverLife"] = new JsonArray(
            new JsonObject { ["position"] = 1.0, ["value"] = 1.0 },
            new JsonObject { ["position"] = 0.0, ["value"] = 0.2 });

        Exception ex = AssertParticleVfxLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("sorted"));
    }

    [Test]
    public void ParticleVfxConfigLoader_RejectsUnsortedGradientKeys()
    {
        JsonObject effect = ValidParticleVfxObject();
        effect["colorOverLife"] = new JsonArray(
            new JsonObject { ["position"] = 1.0, ["color"] = new JsonArray(1.0, 1.0, 1.0, 0.0) },
            new JsonObject { ["position"] = 0.0, ["color"] = new JsonArray(1.0, 0.4, 0.1, 1.0) });

        Exception ex = AssertParticleVfxLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("sorted"));
    }

    [Test]
    public void ParticleVfxConfigLoader_RejectsUnknownCurveKeyField()
    {
        JsonObject effect = ValidParticleVfxObject();
        JsonObject firstKey = effect["sizeOverLife"]!.AsArray()[0]!.AsObject();
        firstKey["tangent"] = 0.5;

        Exception ex = AssertParticleVfxLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("unsupported field 'tangent'"));
    }

    [Test]
    public void ParticleVfxConfigLoader_RejectsUnknownGradientKeyField()
    {
        JsonObject effect = ValidParticleVfxObject();
        JsonObject firstKey = effect["colorOverLife"]!.AsArray()[0]!.AsObject();
        firstKey["blendMode"] = "Soft";

        Exception ex = AssertParticleVfxLoadFails(JsonArrayString(effect));
        Assert.That(ex.Message, Does.Contain("unsupported field 'blendMode'"));
    }

    private static Exception AssertParticleVfxLoadFails(string particleVfxJson)
    {
        string core = CreateCoreRoot("Ludots_ParticleVfxConfig_Invalid");
        WriteCatalog(core, "Presentation/particle_vfx.json", "ArrayById", "id");
        WriteParticleVfx(core, particleVfxJson);

        var pipeline = CreatePipeline(core);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        var particleVfx = new ParticleVfxRegistry();
        return Assert.Catch<Exception>(
            () => new ParticleVfxConfigLoader(pipeline, particleVfx).Load(catalog))!;
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
        Directory.CreateDirectory(Path.Combine(core, "Presentation"));
        return core;
    }

    private static void WriteCatalog(string coreRoot, params string[] triples)
    {
        if (triples.Length % 3 != 0)
        {
            throw new ArgumentException("Catalog entries must be path/policy/idField triples.", nameof(triples));
        }

        Directory.CreateDirectory(coreRoot);
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
        File.WriteAllText(Path.Combine(coreRoot, "config_catalog.json"), writer.ToString(), Utf8NoBom);
    }

    private static void WriteParticleVfx(string coreRoot, string json)
    {
        File.WriteAllText(Path.Combine(coreRoot, "Presentation", "particle_vfx.json"), json, Utf8NoBom);
    }

    private static void WriteMeshAssets(string coreRoot, string json)
    {
        File.WriteAllText(Path.Combine(coreRoot, "Presentation", "mesh_assets.json"), json, Utf8NoBom);
    }

    private static string MeshAssetReferencingParticleVfxJson()
    {
        return """
        [
          {
            "id": "effect.quarks.spark",
            "type": "Primitive",
            "primitiveKind": "Sphere",
            "vfx": {
              "particleVfxId": "quarks.spark.trail"
            }
          }
        ]
        """;
    }

    private static JsonObject ValidParticleVfxObject()
    {
        return JsonNode.Parse(ValidParticleVfxJson())!.AsArray()[0]!.AsObject();
    }

    private static string JsonArrayString(JsonObject obj)
    {
        var array = new JsonArray(obj.DeepClone());
        return array.ToJsonString();
    }

    private static string ValidParticleVfxJson()
    {
        return """
        [
          {
            "id": "quarks.spark.trail",
            "version": "quarks.ludots.v1",
            "spawnMode": "Loop",
            "shape": "Cone",
            "renderMode": "Primitive",
            "blendMode": "Alpha",
            "primitive": "Sphere",
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
