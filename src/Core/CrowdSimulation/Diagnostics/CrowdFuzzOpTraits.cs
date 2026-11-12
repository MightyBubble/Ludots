using System;
using System.Collections.Generic;

namespace Ludots.Core.CrowdSimulation.Diagnostics;

/// <summary>模糊操作 id 的固有特性；配置只列启用哪些 id。</summary>
public static class CrowdFuzzOpTraits
{
    private static readonly Dictionary<string, CrowdFuzzOp> Catalog = new(StringComparer.Ordinal)
    {
        ["spawn"] = new CrowdFuzzOp { Id = "spawn" },
        ["order"] = new CrowdFuzzOp { Id = "order" },
        ["place"] = new CrowdFuzzOp { Id = "place", Structure = true },
        ["remove"] = new CrowdFuzzOp { Id = "remove", Structure = true },
        ["placePath"] = new CrowdFuzzOp { Id = "placePath", Structure = true },
        ["removePath"] = new CrowdFuzzOp { Id = "removePath", Structure = true },
        ["kill"] = new CrowdFuzzOp { Id = "kill", ProbeHandles = true },
    };

    public static CrowdFuzzOp Resolve(string id, string pathPrefix)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException($"{pathPrefix}: 操作 id 不能为空。");
        if (!Catalog.TryGetValue(id, out CrowdFuzzOp? op))
            throw new InvalidOperationException($"{pathPrefix}: 未知操作 \"{id}\"。");
        return op;
    }

    public static CrowdFuzzOp[] ResolveAll(IReadOnlyList<string> ids, string pathPrefix)
    {
        if (ids == null || ids.Count == 0)
            throw new InvalidOperationException($"{pathPrefix}: 至少一条操作。");
        var ops = new CrowdFuzzOp[ids.Count];
        for (int i = 0; i < ids.Count; i++)
            ops[i] = Resolve(ids[i], $"{pathPrefix}[{i}]");
        return ops;
    }
}
