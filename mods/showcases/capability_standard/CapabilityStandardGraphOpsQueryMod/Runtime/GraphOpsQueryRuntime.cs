using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Config;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace CapabilityStandardGraphOpsQueryMod.Runtime;

/// <summary>
/// 画廊分镜：全图搜人 → 按阵营/标签/属性过滤 → 排序 → 聚合出最强/最弱。
/// EnsureWorld 用 FrontDoor 编译样例图，证明作者面可用；失败关闭。
/// </summary>
public sealed class GraphOpsQueryRuntime
{
    public GraphShowcaseMetrics Metrics { get; } = new()
    {
        ShowcaseId = "capability_standard_graph_ops_query"
    };

    public int CompiledGraphs { get; private set; }

    public void EnsureWorld()
    {
        if (CompiledGraphs > 0) return;

        CompileOrThrow(
            "gallery.query.filterPipeline",
            """
            {
              "kind": "Query",
              "entry": "all",
              "nodes": [
                { "id": "all", "op": "QueryAllMapEntities" },
                { "id": "team", "op": "QueryFilterTeam", "teamId": 1 },
                { "id": "tagAny", "op": "QueryFilterTagAny", "tag": "Enemy" },
                { "id": "tagNone", "op": "QueryFilterTagNone", "tag": "Dead" },
                { "id": "minHp", "op": "ConstFloat", "floatValue": 1 },
                { "id": "maxHp", "op": "ConstFloat", "floatValue": 100 },
                { "id": "attr", "op": "QueryFilterAttributeRange", "attribute": "Health" },
                { "id": "sort", "op": "QuerySortByAttribute", "attribute": "Health" },
                { "id": "sum", "op": "AggSumAttribute", "attribute": "Health" },
                { "id": "avg", "op": "AggAverageAttribute", "attribute": "Health" },
                { "id": "maxA", "op": "AggMaxAttribute", "attribute": "Health" },
                { "id": "minA", "op": "AggMinAttribute", "attribute": "Health" },
                { "id": "maxE", "op": "AggMaxEntityByAttribute", "attribute": "Health" },
                { "id": "minE", "op": "AggMinEntityByAttribute", "attribute": "Health" }
              ],
              "controlEdges": [
                { "from": "all", "fromPort": "next", "to": "team" },
                { "from": "team", "fromPort": "next", "to": "tagAny" },
                { "from": "tagAny", "fromPort": "next", "to": "tagNone" },
                { "from": "tagNone", "fromPort": "next", "to": "minHp" },
                { "from": "minHp", "fromPort": "next", "to": "maxHp" },
                { "from": "maxHp", "fromPort": "next", "to": "attr" },
                { "from": "attr", "fromPort": "next", "to": "sort" },
                { "from": "sort", "fromPort": "next", "to": "sum" },
                { "from": "sum", "fromPort": "next", "to": "avg" },
                { "from": "avg", "fromPort": "next", "to": "maxA" },
                { "from": "maxA", "fromPort": "next", "to": "minA" },
                { "from": "minA", "fromPort": "next", "to": "maxE" },
                { "from": "maxE", "fromPort": "next", "to": "minE" }
              ],
              "valueEdges": [
                { "from": "all", "fromPort": "list", "to": "team", "toPort": "list" },
                { "from": "team", "fromPort": "list", "to": "tagAny", "toPort": "list" },
                { "from": "tagAny", "fromPort": "list", "to": "tagNone", "toPort": "list" },
                { "from": "tagNone", "fromPort": "list", "to": "attr", "toPort": "list" },
                { "from": "minHp", "fromPort": "value", "to": "attr", "toPort": "min" },
                { "from": "maxHp", "fromPort": "value", "to": "attr", "toPort": "max" },
                { "from": "attr", "fromPort": "list", "to": "sort", "toPort": "list" },
                { "from": "sort", "fromPort": "list", "to": "sum", "toPort": "list" },
                { "from": "sort", "fromPort": "list", "to": "avg", "toPort": "list" },
                { "from": "sort", "fromPort": "list", "to": "maxA", "toPort": "list" },
                { "from": "sort", "fromPort": "list", "to": "minA", "toPort": "list" },
                { "from": "sort", "fromPort": "list", "to": "maxE", "toPort": "list" },
                { "from": "sort", "fromPort": "list", "to": "minE", "toPort": "list" }
              ],
              "outputs": [
                { "id": "sumHp", "destination": "Summary", "type": "Float", "source": "sum", "key": "gallery.sumHp" },
                { "id": "strongest", "destination": "Summary", "type": "Entity", "source": "maxE", "key": "gallery.strongest" }
              ]
            }
            """);

        CompileOrThrow(
            "gallery.query.fromCollection",
            """
            {
              "kind": "Query",
              "entry": "caster",
              "nodes": [
                { "id": "caster", "op": "LoadCaster" },
                { "id": "from", "op": "QueryFromCollection", "collectionKey": "squad.members" },
                { "id": "tmpl", "op": "QueryFilterTemplate", "template": "Unit.Soldier" },
                { "id": "count", "op": "AggCount" }
              ],
              "controlEdges": [
                { "from": "caster", "fromPort": "next", "to": "from" },
                { "from": "from", "fromPort": "next", "to": "tmpl" },
                { "from": "tmpl", "fromPort": "next", "to": "count" }
              ],
              "valueEdges": [
                { "from": "caster", "fromPort": "value", "to": "from", "toPort": "source" },
                { "from": "from", "fromPort": "list", "to": "tmpl", "toPort": "list" },
                { "from": "tmpl", "fromPort": "list", "to": "count", "toPort": "list" }
              ],
              "outputs": [
                { "id": "n", "destination": "Summary", "type": "Int", "source": "count", "key": "gallery.squadCount" }
              ]
            }
            """);

        Metrics.AgentCount = 12;
        Metrics.ThinkWaves = 1;
        Metrics.Detail =
            "全图搜人后按阵营与标签筛出活着的敌人，再按生命值排序，算出总和/均值，并指出最强与最弱；也能从小队花名册按模板筛人。";
        CompiledGraphs = 2;
    }

    private static void CompileOrThrow(string graphId, string json)
    {
        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
        JsonObject obj = JsonNode.Parse(json)!.AsObject();
        GraphControlFlowCompileResult compiled =
            GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, graphId, options);
        if (!compiled.Succeeded || !compiled.Package.HasValue)
        {
            var sb = new StringBuilder();
            foreach (GraphDiagnostic d in compiled.Diagnostics)
            {
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(d.Code).Append(':').Append(d.Message);
            }

            throw new InvalidOperationException(
                $"Gallery graph '{graphId}' FrontDoor failed: {(sb.Length == 0 ? "no diagnostics" : sb.ToString())}");
        }
    }
}
