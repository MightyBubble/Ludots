using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.Physics3D;

public sealed class Physics3DWorldConfigLoader
{
    private readonly ConfigPipeline _pipeline;

    public Physics3DWorldConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public Physics3DWorldConfig Load(
        ConfigCatalog catalog,
        ConfigConflictReport report,
        string relativePath = "Physics3D/world.json")
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(report);
        ConfigCatalogEntry entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
        var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
        if (mergedObject == null)
        {
            throw new InvalidOperationException($"Required Physics3D config '{relativePath}' has no fragments.");
        }

        JsonSerializerOptions options = StrictJsonOptions.CreateExact(includeFields: true);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        Physics3DWorldConfig config = mergedObject.Deserialize<Physics3DWorldConfig>(options)
            ?? throw new InvalidOperationException($"Physics3D config '{relativePath}' deserialized to null.");
        config.Validate();
        return config;
    }
}
