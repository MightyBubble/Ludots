using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Diagnostics;

namespace Ludots.Core.Config
{
    public class DataRegistry<T> where T : class, IIdentifiable
    {
        private readonly Dictionary<string, T> _data = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        private readonly ConfigPipeline _pipeline;

        public DataRegistry(ConfigPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        public void Load(string relativePath, ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            Log.Info(in LogChannels.Config, $"Loading DataRegistry<{typeof(T).Name}> from {relativePath}...");

            var entry = ConfigPipeline.GetEntryOrDefault(catalog, relativePath, ConfigMergePolicy.ArrayById, "Id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);

            int count = 0;
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };

            for (int i = 0; i < merged.Count; i++)
            {
                try
                {
                    var item = merged[i].Node.Deserialize<T>(options);
                    if (item != null)
                    {
                        _data[item.Id] = item;
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(in LogChannels.Config, $"Error deserializing item {merged[i].Id}: {ex.Message}");
                }
            }

            Log.Info(in LogChannels.Config, $"Loaded {count} DataRegistry<{typeof(T).Name}> items.");
        }

        public T Get(string id)
        {
            return _data.TryGetValue(id, out var item) ? item : null;
        }

        public IEnumerable<T> GetAll()
        {
            return _data.Values;
        }
        
        public bool Contains(string id)
        {
            return _data.ContainsKey(id);
        }
    }
}
