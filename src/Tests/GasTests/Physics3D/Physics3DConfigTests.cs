using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Physics3D;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
[NonParallelizable]
public sealed class Physics3DConfigTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "Ludots_Physics3D_Config", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Configs", "Physics3D"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public void Loader_RejectsUnknownFields()
    {
        JsonObject config = CreateValidConfigJson();
        config["UnexpectedCapacity"] = 10;

        Assert.Throws<JsonException>(() => Load(config));
    }

    [Test]
    public void Loader_RejectsIntegerEnums()
    {
        JsonObject config = CreateValidConfigJson();
        config[nameof(Physics3DWorldConfig.MaterialCombineMode)] = 4;

        Assert.Throws<JsonException>(() => Load(config));
    }

    private Physics3DWorldConfig Load(JsonObject config)
    {
        string configPath = Path.Combine(_root, "Configs", "Physics3D", "world.json");
        File.WriteAllText(configPath, config.ToJsonString());
        var vfs = new VirtualFileSystem();
        vfs.Mount("Core", _root);
        var pipeline = new ConfigPipeline(vfs, modLoader: null!);
        var catalog = new ConfigCatalog();
        catalog.Add(new ConfigCatalogEntry("Physics3D/world.json", ConfigMergePolicy.DeepObject));
        return new Physics3DWorldConfigLoader(pipeline).Load(catalog, new ConfigConflictReport());
    }

    private static JsonObject CreateValidConfigJson()
    {
        JsonSerializerOptions options = StrictJsonOptions.CreateExact(includeFields: true);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return JsonSerializer.SerializeToNode(
            Physics3DWorldTests.CreateConfig(mobileCapacity: 4, staticCapacity: 2),
            options)?.AsObject()
            ?? throw new InvalidOperationException("Failed to serialize Physics3D test config.");
    }
}
