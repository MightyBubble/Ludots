using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Fields;
using Ludots.Core.Fields.Influence;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial.Eqs.Generators;
using Ludots.Core.Spatial.Eqs.Tests;

namespace Ludots.Core.Spatial.Eqs.Config
{
    public static class EqsInfluenceConfigLoader
    {
        public static EqsInfluenceConfigDocument LoadFromDirectory(string configRoot)
        {
            if (string.IsNullOrWhiteSpace(configRoot))
            {
                throw new ArgumentException("Config root is required.", nameof(configRoot));
            }

            string spatial = Path.Combine(configRoot, "Spatial");
            return LoadFromJson(
                ReadRequired(Path.Combine(spatial, "influence_fields.json")),
                ReadRequired(Path.Combine(spatial, "eqs_queries.json")),
                ReadRequired(Path.Combine(spatial, "eqs_scenarios.json")));
        }

        public static EqsInfluenceConfigDocument LoadFromJson(string fieldsJson, string queriesJson, string scenariosJson)
        {
            InfluenceFieldConfig[] fields = ParseFields(ParseArray(fieldsJson, "Spatial/influence_fields.json"));
            EqsQueryConfig[] queries = ParseQueries(ParseArray(queriesJson, "Spatial/eqs_queries.json"));
            EqsScenarioConfig[] scenarios = ParseScenarios(ParseArray(scenariosJson, "Spatial/eqs_scenarios.json"), queries, fields);
            return new EqsInfluenceConfigDocument(fields, queries, scenarios);
        }

        public static InfluenceFieldRegistry MaterializeFields(
            EqsInfluenceConfigDocument document,
            IEnumerable<string>? fieldIds = null)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            HashSet<string>? filter = null;
            if (fieldIds != null)
            {
                filter = new HashSet<string>(fieldIds, StringComparer.Ordinal);
            }

            var registry = new InfluenceFieldRegistry();
            for (int i = 0; i < document.Fields.Length; i++)
            {
                InfluenceFieldConfig cfg = document.Fields[i];
                if (filter != null && !filter.Contains(cfg.Id))
                {
                    continue;
                }

                var grid = new FieldGridSpec2D(cfg.CellSizeCm, cfg.ChunkSizeCells);
                InfluenceField field = registry.GetOrCreate(cfg.Id, grid, cfg.DefaultValue);
                for (int s = 0; s < cfg.Sources.Length; s++)
                {
                    InfluenceSourceConfig source = cfg.Sources[s];
                    field.Stamp(source.Position, source.RadiusCm, source.Peak, source.Falloff);
                }
            }

            return registry;
        }

        public static EqsQuery CreateQuery(EqsQueryConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            IEqsGenerator generator = CreateGenerator(config.Generator, $"eqs_queries[{config.Id}].generator");
            var tests = new IEqsTest[config.Tests.Length];
            for (int i = 0; i < config.Tests.Length; i++)
            {
                tests[i] = CreateTest(config.Tests[i], $"eqs_queries[{config.Id}].tests[{i}]");
            }

            return new EqsQuery(generator, tests);
        }

        public static EqsQueryConfig RequireQuery(EqsInfluenceConfigDocument document, string queryId)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            for (int i = 0; i < document.Queries.Length; i++)
            {
                if (string.Equals(document.Queries[i].Id, queryId, StringComparison.Ordinal))
                {
                    return document.Queries[i];
                }
            }

            throw new InvalidOperationException($"Unknown EQS query id '{queryId}'.");
        }

        public static EqsScenarioConfig RequireScenario(EqsInfluenceConfigDocument document, string scenarioId)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            for (int i = 0; i < document.Scenarios.Length; i++)
            {
                if (string.Equals(document.Scenarios[i].Id, scenarioId, StringComparison.Ordinal))
                {
                    return document.Scenarios[i];
                }
            }

            throw new InvalidOperationException($"Unknown EQS scenario id '{scenarioId}'.");
        }

        private static InfluenceFieldConfig[] ParseFields(JsonArray arr)
        {
            var list = new List<InfluenceFieldConfig>(arr.Count);
            for (int i = 0; i < arr.Count; i++)
            {
                string path = $"Spatial/influence_fields.json[{i}]";
                JsonObject obj = RequireObject(arr[i], path);
                string id = RequireString(obj, "id", path);
                int cellSizeCm = RequireInt(obj, "cellSizeCm", path);
                int chunkSizeCells = RequireInt(obj, "chunkSizeCells", path);
                if (cellSizeCm <= 0 || chunkSizeCells <= 0)
                {
                    throw Fail(path, "cellSizeCm and chunkSizeCells must be > 0.");
                }

                float defaultValue = TryFloat(obj, "defaultValue", out float dv) ? dv : 0f;
                JsonArray sourcesArr = RequireArray(obj, "sources", path);
                var sources = new InfluenceSourceConfig[sourcesArr.Count];
                for (int s = 0; s < sourcesArr.Count; s++)
                {
                    string sp = $"{path}.sources[{s}]";
                    JsonObject sourceObj = RequireObject(sourcesArr[s], sp);
                    string falloffText = RequireString(sourceObj, "falloff", sp);
                    if (!Enum.TryParse(falloffText, ignoreCase: true, out FalloffKind falloff) ||
                        !Enum.IsDefined(falloff))
                    {
                        throw Fail($"{sp}.falloff", $"Unknown FalloffKind '{falloffText}'.");
                    }

                    sources[s] = new InfluenceSourceConfig(
                        RequireInt(sourceObj, "xCm", sp),
                        RequireInt(sourceObj, "yCm", sp),
                        RequireInt(sourceObj, "radiusCm", sp),
                        RequireFloat(sourceObj, "peak", sp),
                        falloff);
                }

                list.Add(new InfluenceFieldConfig(id, cellSizeCm, chunkSizeCells, defaultValue, sources));
            }

            return list.ToArray();
        }

        private static EqsQueryConfig[] ParseQueries(JsonArray arr)
        {
            var list = new List<EqsQueryConfig>(arr.Count);
            for (int i = 0; i < arr.Count; i++)
            {
                string path = $"Spatial/eqs_queries.json[{i}]";
                JsonObject obj = RequireObject(arr[i], path);
                string id = RequireString(obj, "id", path);
                EqsGeneratorConfig generator = ParseGenerator(RequireObject(obj["generator"], $"{path}.generator"), $"{path}.generator");
                JsonArray testsArr = RequireArray(obj, "tests", path);
                var tests = new EqsTestConfig[testsArr.Count];
                for (int t = 0; t < testsArr.Count; t++)
                {
                    tests[t] = ParseTest(RequireObject(testsArr[t], $"{path}.tests[{t}]"), $"{path}.tests[{t}]");
                }

                EqsSelectionConfig selection = ParseSelection(
                    RequireObject(obj["selection"], $"{path}.selection"),
                    $"{path}.selection");
                list.Add(new EqsQueryConfig(id, generator, tests, selection));
            }

            return list.ToArray();
        }

        private static EqsScenarioConfig[] ParseScenarios(
            JsonArray arr,
            EqsQueryConfig[] queries,
            InfluenceFieldConfig[] fields)
        {
            var queryIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < queries.Length; i++)
            {
                queryIds.Add(queries[i].Id);
            }

            var fieldIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < fields.Length; i++)
            {
                fieldIds.Add(fields[i].Id);
            }

            var list = new List<EqsScenarioConfig>(arr.Count);
            for (int i = 0; i < arr.Count; i++)
            {
                string path = $"Spatial/eqs_scenarios.json[{i}]";
                JsonObject obj = RequireObject(arr[i], path);
                string id = RequireString(obj, "id", path);
                WorldCmInt2 origin = RequireWorld(RequireObject(obj["origin"], $"{path}.origin"), $"{path}.origin");
                string queryId = RequireString(obj, "queryId", path);
                if (!queryIds.Contains(queryId))
                {
                    throw Fail($"{path}.queryId", $"Unknown query id '{queryId}'.");
                }

                JsonArray fieldArr = RequireArray(obj, "influenceFieldIds", path);
                var influenceFieldIds = new string[fieldArr.Count];
                for (int f = 0; f < fieldArr.Count; f++)
                {
                    string fieldId = fieldArr[f]?.GetValue<string>()
                        ?? throw Fail($"{path}.influenceFieldIds[{f}]", "Field id must be a string.");
                    if (!fieldIds.Contains(fieldId))
                    {
                        throw Fail($"{path}.influenceFieldIds[{f}]", $"Unknown influence field id '{fieldId}'.");
                    }

                    influenceFieldIds[f] = fieldId;
                }

                JsonObject presentationObj = RequireObject(obj["presentation"], $"{path}.presentation");
                string influenceFieldId = RequireString(presentationObj, "influenceFieldId", $"{path}.presentation");
                if (!fieldIds.Contains(influenceFieldId))
                {
                    throw Fail($"{path}.presentation.influenceFieldId", $"Unknown influence field id '{influenceFieldId}'.");
                }

                var presentation = new EqsPresentationConfig(
                    influenceFieldId,
                    TryBool(presentationObj, "drawCandidates", out bool drawCandidates) ? drawCandidates : true,
                    TryBool(presentationObj, "drawBest", out bool drawBest) ? drawBest : true,
                    TryFloat(presentationObj, "normalizePeak", out float peak) ? peak : 10f);

                if (presentation.NormalizePeak <= 0f)
                {
                    throw Fail($"{path}.presentation.normalizePeak", "normalizePeak must be > 0.");
                }

                list.Add(new EqsScenarioConfig(id, origin, queryId, influenceFieldIds, presentation));
            }

            return list.ToArray();
        }

        private static EqsGeneratorConfig ParseGenerator(JsonObject obj, string path)
        {
            string kind = RequireString(obj, "kind", path);
            return new EqsGeneratorConfig(
                kind,
                TryInt(obj, "extentCm", out int extent) ? extent : 0,
                TryInt(obj, "cellSizeCm", out int cell) ? cell : 0,
                TryInt(obj, "radiusCm", out int radius) ? radius : 0,
                TryInt(obj, "innerCm", out int inner) ? inner : 0,
                TryInt(obj, "outerCm", out int outer) ? outer : 0,
                TryInt(obj, "count", out int count) ? count : 0);
        }

        private static EqsTestConfig ParseTest(JsonObject obj, string path)
        {
            string kind = RequireString(obj, "kind", path);
            WorldCmInt2? reference = null;
            if (obj.TryGetPropertyValue("reference", out JsonNode? refNode) && refNode != null)
            {
                reference = RequireWorld(RequireObject(refNode, $"{path}.reference"), $"{path}.reference");
            }

            OverlapShape shape = OverlapShape.Radius;
            if (obj.TryGetPropertyValue("shape", out JsonNode? shapeNode) && shapeNode != null)
            {
                string shapeText = shapeNode.GetValue<string>();
                if (!Enum.TryParse(shapeText, ignoreCase: true, out shape) || !Enum.IsDefined(shape))
                {
                    throw Fail($"{path}.shape", $"Unknown OverlapShape '{shapeText}'.");
                }
            }

            string? fieldKey = TryString(obj, "fieldKey", out string fk) ? fk : null;
            return new EqsTestConfig(
                kind,
                TryBool(obj, "preferNear", out bool preferNear) && preferNear,
                TryBool(obj, "preferLow", out bool preferLow) && preferLow,
                TryBool(obj, "preferMore", out bool preferMore) && preferMore,
                TryFloat(obj, "weight", out float weight) ? weight : 1f,
                TryFloat(obj, "normalizeScale", out float normalizeScale) ? normalizeScale : 1f,
                TryInt(obj, "normalizeCount", out int normalizeCount) ? normalizeCount : 8,
                fieldKey,
                shape,
                TryInt(obj, "extentCm", out int extentCm) ? extentCm : 0,
                reference);
        }

        private static EqsSelectionConfig ParseSelection(JsonObject obj, string path)
        {
            return new EqsSelectionConfig(
                RequireString(obj, "kind", path),
                TryInt(obj, "topN", out int topN) ? topN : 0,
                TryFloat(obj, "threshold", out float threshold) ? threshold : 0f);
        }

        private static IEqsGenerator CreateGenerator(EqsGeneratorConfig cfg, string path)
        {
            if (string.Equals(cfg.Kind, "Ring", StringComparison.OrdinalIgnoreCase))
            {
                return new RingGenerator(cfg.RadiusCm, cfg.Count);
            }

            if (string.Equals(cfg.Kind, "Grid", StringComparison.OrdinalIgnoreCase))
            {
                return new GridGenerator(cfg.ExtentCm, cfg.CellSizeCm);
            }

            if (string.Equals(cfg.Kind, "Donut", StringComparison.OrdinalIgnoreCase))
            {
                return new DonutGenerator(cfg.InnerCm, cfg.OuterCm, cfg.CellSizeCm);
            }

            if (string.Equals(cfg.Kind, "Circle", StringComparison.OrdinalIgnoreCase))
            {
                return new CircleGenerator(cfg.RadiusCm, cfg.CellSizeCm);
            }

            throw Fail(path, $"Unknown EQS generator kind '{cfg.Kind}'.");
        }

        private static IEqsTest CreateTest(EqsTestConfig cfg, string path)
        {
            if (string.Equals(cfg.Kind, "Distance", StringComparison.OrdinalIgnoreCase))
            {
                return new DistanceTest(cfg.PreferNear, weight: cfg.Weight, reference: cfg.Reference);
            }

            if (string.Equals(cfg.Kind, "Influence", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(cfg.FieldKey))
                {
                    throw Fail(path, "Influence test requires fieldKey.");
                }

                return new InfluenceTest(cfg.FieldKey, cfg.PreferLow, cfg.Weight, cfg.NormalizeScale);
            }

            if (string.Equals(cfg.Kind, "Overlap", StringComparison.OrdinalIgnoreCase))
            {
                return new OverlapTest(cfg.OverlapShape, cfg.ExtentCm, cfg.PreferMore, cfg.Weight, cfg.NormalizeCount);
            }

            throw Fail(path, $"Unknown EQS test kind '{cfg.Kind}'.");
        }

        private static JsonArray ParseArray(string json, string path)
        {
            JsonNode? node = JsonNode.Parse(json);
            if (node is not JsonArray arr)
            {
                throw Fail(path, "Root value must be a JSON array.");
            }

            return arr;
        }

        private static string ReadRequired(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Missing EQS/Influence config file '{path}'.", path);
            }

            return File.ReadAllText(path);
        }

        private static JsonObject RequireObject(JsonNode? node, string path)
        {
            if (node is JsonObject obj)
            {
                return obj;
            }

            throw Fail(path, "Expected JSON object.");
        }

        private static JsonArray RequireArray(JsonObject obj, string key, string path)
        {
            if (!obj.TryGetPropertyValue(key, out JsonNode? node) || node is not JsonArray arr)
            {
                throw Fail($"{path}.{key}", "Expected JSON array.");
            }

            return arr;
        }

        private static string RequireString(JsonObject obj, string key, string path)
        {
            if (!TryString(obj, key, out string value) || string.IsNullOrWhiteSpace(value))
            {
                throw Fail($"{path}.{key}", "Expected non-empty string.");
            }

            return value;
        }

        private static bool TryString(JsonObject obj, string key, out string value)
        {
            value = string.Empty;
            if (!obj.TryGetPropertyValue(key, out JsonNode? node) || node == null)
            {
                return false;
            }

            value = node.GetValue<string>();
            return true;
        }

        private static int RequireInt(JsonObject obj, string key, string path)
        {
            if (!TryInt(obj, key, out int value))
            {
                throw Fail($"{path}.{key}", "Expected integer.");
            }

            return value;
        }

        private static bool TryInt(JsonObject obj, string key, out int value)
        {
            value = 0;
            if (!obj.TryGetPropertyValue(key, out JsonNode? node) || node == null)
            {
                return false;
            }

            value = node.GetValue<int>();
            return true;
        }

        private static float RequireFloat(JsonObject obj, string key, string path)
        {
            if (!TryFloat(obj, key, out float value))
            {
                throw Fail($"{path}.{key}", "Expected number.");
            }

            return value;
        }

        private static bool TryFloat(JsonObject obj, string key, out float value)
        {
            value = 0f;
            if (!obj.TryGetPropertyValue(key, out JsonNode? node) || node == null)
            {
                return false;
            }

            value = node.GetValue<float>();
            return true;
        }

        private static bool TryBool(JsonObject obj, string key, out bool value)
        {
            value = false;
            if (!obj.TryGetPropertyValue(key, out JsonNode? node) || node == null)
            {
                return false;
            }

            value = node.GetValue<bool>();
            return true;
        }

        private static WorldCmInt2 RequireWorld(JsonObject obj, string path)
        {
            return new WorldCmInt2(RequireInt(obj, "xCm", path), RequireInt(obj, "yCm", path));
        }

        private static Exception Fail(string path, string message)
            => new InvalidOperationException($"{path}: {message}");
    }
}
