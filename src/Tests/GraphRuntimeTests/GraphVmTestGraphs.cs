using System.Collections.Generic;
using Ludots.Core.GraphRuntime;

namespace Ludots.Tests.GraphRuntime
{
    internal static class GraphVmTestGraphs
    {
        public static GraphVmDocument CreateDrinkUntilFullGraph(int limit = 3)
        {
            return new GraphVmDocument
            {
                Id = "tests.graphvm.drink-until-full",
                Entry = "zeroWater",
                Nodes = new List<GraphVmNode>
                {
                    new() { Id = "drinkLoadWater", Op = nameof(GraphVmOpcode.LoadInt), Slot = 0 },
                    new() { Id = "drinkLoadOne", Op = nameof(GraphVmOpcode.LoadInt), Slot = 2 },
                    new() { Id = "drinkAdd", Op = nameof(GraphVmOpcode.AddInt) },
                    new() { Id = "drinkStoreWater", Op = nameof(GraphVmOpcode.StoreInt), Slot = 0 },
                    new() { Id = "drinkYield", Op = nameof(GraphVmOpcode.Yield) },
                    new() { Id = "drinkReturn", Op = nameof(GraphVmOpcode.Return) },
                    new() { Id = "zeroWater", Op = nameof(GraphVmOpcode.ConstInt), IntValue = 0 },
                    new() { Id = "storeWater", Op = nameof(GraphVmOpcode.StoreInt), Slot = 0 },
                    new() { Id = "limitValue", Op = nameof(GraphVmOpcode.ConstInt), IntValue = limit },
                    new() { Id = "storeLimit", Op = nameof(GraphVmOpcode.StoreInt), Slot = 1 },
                    new() { Id = "oneValue", Op = nameof(GraphVmOpcode.ConstInt), IntValue = 1 },
                    new() { Id = "storeOne", Op = nameof(GraphVmOpcode.StoreInt), Slot = 2 },
                    new() { Id = "loadWaterForCheck", Op = nameof(GraphVmOpcode.LoadInt), Slot = 0 },
                    new() { Id = "loadLimit", Op = nameof(GraphVmOpcode.LoadInt), Slot = 1 },
                    new() { Id = "waterBelowLimit", Op = nameof(GraphVmOpcode.LessThanInt) },
                    new() { Id = "branchNeedDrink", Op = nameof(GraphVmOpcode.BranchBool) },
                    new() { Id = "callDrink", Op = nameof(GraphVmOpcode.Call) },
                    new() { Id = "loadWaterForReturn", Op = nameof(GraphVmOpcode.LoadInt), Slot = 0 },
                    new() { Id = "done", Op = nameof(GraphVmOpcode.ReturnInt) }
                },
                ControlEdges = new List<GraphVmControlEdge>
                {
                    new("zeroWater", GraphVmControlPorts.Next, "storeWater"),
                    new("storeWater", GraphVmControlPorts.Next, "limitValue"),
                    new("limitValue", GraphVmControlPorts.Next, "storeLimit"),
                    new("storeLimit", GraphVmControlPorts.Next, "oneValue"),
                    new("oneValue", GraphVmControlPorts.Next, "storeOne"),
                    new("storeOne", GraphVmControlPorts.Next, "loadWaterForCheck"),
                    new("loadWaterForCheck", GraphVmControlPorts.Next, "loadLimit"),
                    new("loadLimit", GraphVmControlPorts.Next, "waterBelowLimit"),
                    new("waterBelowLimit", GraphVmControlPorts.Next, "branchNeedDrink"),
                    new("branchNeedDrink", GraphVmControlPorts.True, "callDrink"),
                    new("branchNeedDrink", GraphVmControlPorts.False, "loadWaterForReturn"),
                    new("callDrink", GraphVmControlPorts.Call, "drinkLoadWater"),
                    new("callDrink", GraphVmControlPorts.Next, "loadWaterForCheck"),
                    new("loadWaterForReturn", GraphVmControlPorts.Next, "done"),
                    new("drinkLoadWater", GraphVmControlPorts.Next, "drinkLoadOne"),
                    new("drinkLoadOne", GraphVmControlPorts.Next, "drinkAdd"),
                    new("drinkAdd", GraphVmControlPorts.Next, "drinkStoreWater"),
                    new("drinkStoreWater", GraphVmControlPorts.Next, "drinkYield"),
                    new("drinkYield", GraphVmControlPorts.Next, "drinkReturn")
                },
                ValueEdges = new List<GraphVmValueEdge>
                {
                    new("zeroWater", GraphVmValuePorts.Value, "storeWater", GraphVmValuePorts.Value),
                    new("limitValue", GraphVmValuePorts.Value, "storeLimit", GraphVmValuePorts.Value),
                    new("oneValue", GraphVmValuePorts.Value, "storeOne", GraphVmValuePorts.Value),
                    new("loadWaterForCheck", GraphVmValuePorts.Value, "waterBelowLimit", GraphVmValuePorts.A),
                    new("loadLimit", GraphVmValuePorts.Value, "waterBelowLimit", GraphVmValuePorts.B),
                    new("waterBelowLimit", GraphVmValuePorts.Value, "branchNeedDrink", GraphVmValuePorts.Condition),
                    new("loadWaterForReturn", GraphVmValuePorts.Value, "done", GraphVmValuePorts.Value),
                    new("drinkLoadWater", GraphVmValuePorts.Value, "drinkAdd", GraphVmValuePorts.A),
                    new("drinkLoadOne", GraphVmValuePorts.Value, "drinkAdd", GraphVmValuePorts.B),
                    new("drinkAdd", GraphVmValuePorts.Value, "drinkStoreWater", GraphVmValuePorts.Value)
                }
            };
        }

        public static string FormatDiagnostics(GraphVmDiagnostic[] diagnostics)
        {
            if (diagnostics.Length == 0)
            {
                return string.Empty;
            }

            var parts = new string[diagnostics.Length];
            for (int i = 0; i < diagnostics.Length; i++)
            {
                parts[i] = $"{diagnostics[i].Code}:{diagnostics[i].NodeId}:{diagnostics[i].Message}";
            }

            return string.Join("\n", parts);
        }
    }
}
