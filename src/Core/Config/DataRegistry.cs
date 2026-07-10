using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Diagnostics;

namespace Ludots.Core.Config
{
    public class DataRegistry<T> where T : class, IIdentifiable
    {
        private readonly Dictionary<string, T> _data = new Dictionary<string, T>(StringComparer.Ordinal);
        private readonly ConfigPipeline _pipeline;

        public DataRegistry(ConfigPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        public void Load(string relativePath, ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            Log.Info(in LogChannels.Config, $"Loading DataRegistry<{typeof(T).Name}> from {relativePath}...");

            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);

            int count = 0;
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = false, IncludeFields = true };

            for (int i = 0; i < merged.Count; i++)
            {
                try
                {
                    var item = merged[i].Node.Deserialize<T>(options);
                    if (item != null)
                    {
                        if (string.IsNullOrWhiteSpace(item.Id))
                        {
                            throw new InvalidOperationException(
                                $"DataRegistry<{typeof(T).Name}> entry '{merged[i].Id}' from {relativePath} deserialized without an exact Id property.");
                        }

                        if (!string.Equals(item.Id, merged[i].Id, StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"DataRegistry<{typeof(T).Name}> id mismatch in {relativePath}: catalog entry '{merged[i].Id}' vs item Id '{item.Id}'.");
                        }

                        Register(item);
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Error deserializing DataRegistry<{typeof(T).Name}> item '{merged[i].Id}' from {relativePath}: {ex.Message}",
                        ex);
                }
            }

            Log.Info(in LogChannels.Config, $"Loaded {count} DataRegistry<{typeof(T).Name}> items.");
        }

        public void Register(T item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if (string.IsNullOrWhiteSpace(item.Id))
            {
                throw new InvalidOperationException(
                    $"DataRegistry<{typeof(T).Name}> cannot register an item without an Id.");
            }

            if (_data.ContainsKey(item.Id))
            {
                throw new InvalidOperationException(
                    $"DataRegistry<{typeof(T).Name}> duplicate id '{item.Id}'.");
            }

            _data[item.Id] = item;
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
