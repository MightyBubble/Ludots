using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ludots.Core.CrowdSimulation.Presentation;

internal sealed class CrowdViewCatalog
{
    public string[] Ids { get; }
    public string[] Labels { get; }
    public int Length => Labels.Length;

    public CrowdViewCatalog(string[] ids, string[] labels)
    {
        if (ids == null || labels == null || ids.Length == 0 || ids.Length != labels.Length)
            throw new InvalidOperationException("CrowdSimulationDebug.json presentation.viewLabels 至少一条。");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < ids.Length; i++)
        {
            string id = ids[i];
            if (!IsName(id))
                throw new InvalidOperationException($"CrowdSimulationDebug.json presentation.viewLabels 的键 \"{id}\" 必须是名字。");
            if (!seen.Add(id))
                throw new InvalidOperationException($"CrowdSimulationDebug.json presentation.viewLabels 的键 \"{id}\" 重复。");
            if (string.IsNullOrWhiteSpace(labels[i]))
                throw new InvalidOperationException($"CrowdSimulationDebug.json presentation.viewLabels.{id} 必填。");
        }

        Ids = ids;
        Labels = labels;
    }

    public static CrowdViewCatalog Named(params (string Id, string Label)[] rows)
    {
        var ids = new string[rows.Length];
        var labels = new string[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            ids[i] = rows[i].Id;
            labels[i] = rows[i].Label;
        }

        return new CrowdViewCatalog(ids, labels);
    }

    public string LabelOf(string id)
    {
        for (int i = 0; i < Ids.Length; i++)
        {
            if (string.Equals(Ids[i], id, StringComparison.Ordinal))
                return Labels[i];
        }

        throw new InvalidOperationException($"未知视图 \"{id}\"。");
    }

    private static bool IsName(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        for (int i = 0; i < id.Length; i++)
        {
            char c = id[i];
            bool letter = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_';
            bool digit = c is >= '0' and <= '9';
            if (i == 0 && !letter) return false;
            if (!letter && !digit) return false;
        }

        return true;
    }
}

internal sealed class CrowdViewCatalogConverter : JsonConverter<CrowdViewCatalog>
{
    public override CrowdViewCatalog Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("viewLabels 必须是对象。");
        var ids = new List<string>();
        var labels = new List<string>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("viewLabels 必须是对象。");
            string id = reader.GetString() ?? "";
            if (!reader.Read() || reader.TokenType != JsonTokenType.String)
                throw new JsonException($"viewLabels.{id} 必须是字符串。");
            ids.Add(id);
            labels.Add(reader.GetString() ?? "");
        }

        try
        {
            return new CrowdViewCatalog(ids.ToArray(), labels.ToArray());
        }
        catch (InvalidOperationException ex)
        {
            throw new JsonException(ex.Message, ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, CrowdViewCatalog value, JsonSerializerOptions options)
        => throw new NotSupportedException();
}
