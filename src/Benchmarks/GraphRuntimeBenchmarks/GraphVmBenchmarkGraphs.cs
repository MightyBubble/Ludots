using Ludots.Core.GraphRuntime;

namespace Ludots.Benchmarks.GraphRuntime;

internal static class GraphVmBenchmarkGraphs
{
    public static GraphVmDocument CreateCountedLoopGraph(int limit)
    {
        return new GraphVmDocument
        {
            Id = "bench.graphvm.counted-loop",
            Entry = "zeroI",
            Nodes = new List<GraphVmNode>
            {
                new() { Id = "zeroI", Op = nameof(GraphVmOpcode.ConstInt), IntValue = 0 },
                new() { Id = "storeI", Op = nameof(GraphVmOpcode.StoreInt), Slot = 0 },
                new() { Id = "zeroSum", Op = nameof(GraphVmOpcode.ConstInt), IntValue = 0 },
                new() { Id = "storeSum", Op = nameof(GraphVmOpcode.StoreInt), Slot = 1 },
                new() { Id = "limit", Op = nameof(GraphVmOpcode.ConstInt), IntValue = limit },
                new() { Id = "storeLimit", Op = nameof(GraphVmOpcode.StoreInt), Slot = 2 },
                new() { Id = "one", Op = nameof(GraphVmOpcode.ConstInt), IntValue = 1 },
                new() { Id = "storeOne", Op = nameof(GraphVmOpcode.StoreInt), Slot = 3 },
                new() { Id = "loadIForCheck", Op = nameof(GraphVmOpcode.LoadInt), Slot = 0 },
                new() { Id = "loadLimit", Op = nameof(GraphVmOpcode.LoadInt), Slot = 2 },
                new() { Id = "iBelowLimit", Op = nameof(GraphVmOpcode.LessThanInt) },
                new() { Id = "branchLoop", Op = nameof(GraphVmOpcode.BranchBool) },
                new() { Id = "loadSum", Op = nameof(GraphVmOpcode.LoadInt), Slot = 1 },
                new() { Id = "loadIForAdd", Op = nameof(GraphVmOpcode.LoadInt), Slot = 0 },
                new() { Id = "addIToSum", Op = nameof(GraphVmOpcode.AddInt) },
                new() { Id = "storeNewSum", Op = nameof(GraphVmOpcode.StoreInt), Slot = 1 },
                new() { Id = "loadIForInc", Op = nameof(GraphVmOpcode.LoadInt), Slot = 0 },
                new() { Id = "loadOne", Op = nameof(GraphVmOpcode.LoadInt), Slot = 3 },
                new() { Id = "incI", Op = nameof(GraphVmOpcode.AddInt) },
                new() { Id = "storeNewI", Op = nameof(GraphVmOpcode.StoreInt), Slot = 0 },
                new() { Id = "jumpCheck", Op = nameof(GraphVmOpcode.Jump) },
                new() { Id = "loadSumForReturn", Op = nameof(GraphVmOpcode.LoadInt), Slot = 1 },
                new() { Id = "done", Op = nameof(GraphVmOpcode.ReturnInt) }
            },
            ControlEdges = new List<GraphVmControlEdge>
            {
                new("zeroI", GraphVmControlPorts.Next, "storeI"),
                new("storeI", GraphVmControlPorts.Next, "zeroSum"),
                new("zeroSum", GraphVmControlPorts.Next, "storeSum"),
                new("storeSum", GraphVmControlPorts.Next, "limit"),
                new("limit", GraphVmControlPorts.Next, "storeLimit"),
                new("storeLimit", GraphVmControlPorts.Next, "one"),
                new("one", GraphVmControlPorts.Next, "storeOne"),
                new("storeOne", GraphVmControlPorts.Next, "loadIForCheck"),
                new("loadIForCheck", GraphVmControlPorts.Next, "loadLimit"),
                new("loadLimit", GraphVmControlPorts.Next, "iBelowLimit"),
                new("iBelowLimit", GraphVmControlPorts.Next, "branchLoop"),
                new("branchLoop", GraphVmControlPorts.True, "loadSum"),
                new("branchLoop", GraphVmControlPorts.False, "loadSumForReturn"),
                new("loadSum", GraphVmControlPorts.Next, "loadIForAdd"),
                new("loadIForAdd", GraphVmControlPorts.Next, "addIToSum"),
                new("addIToSum", GraphVmControlPorts.Next, "storeNewSum"),
                new("storeNewSum", GraphVmControlPorts.Next, "loadIForInc"),
                new("loadIForInc", GraphVmControlPorts.Next, "loadOne"),
                new("loadOne", GraphVmControlPorts.Next, "incI"),
                new("incI", GraphVmControlPorts.Next, "storeNewI"),
                new("storeNewI", GraphVmControlPorts.Next, "jumpCheck"),
                new("jumpCheck", GraphVmControlPorts.Target, "loadIForCheck"),
                new("loadSumForReturn", GraphVmControlPorts.Next, "done")
            },
            ValueEdges = new List<GraphVmValueEdge>
            {
                new("zeroI", GraphVmValuePorts.Value, "storeI", GraphVmValuePorts.Value),
                new("zeroSum", GraphVmValuePorts.Value, "storeSum", GraphVmValuePorts.Value),
                new("limit", GraphVmValuePorts.Value, "storeLimit", GraphVmValuePorts.Value),
                new("one", GraphVmValuePorts.Value, "storeOne", GraphVmValuePorts.Value),
                new("loadIForCheck", GraphVmValuePorts.Value, "iBelowLimit", GraphVmValuePorts.A),
                new("loadLimit", GraphVmValuePorts.Value, "iBelowLimit", GraphVmValuePorts.B),
                new("iBelowLimit", GraphVmValuePorts.Value, "branchLoop", GraphVmValuePorts.Condition),
                new("loadSum", GraphVmValuePorts.Value, "addIToSum", GraphVmValuePorts.A),
                new("loadIForAdd", GraphVmValuePorts.Value, "addIToSum", GraphVmValuePorts.B),
                new("addIToSum", GraphVmValuePorts.Value, "storeNewSum", GraphVmValuePorts.Value),
                new("loadIForInc", GraphVmValuePorts.Value, "incI", GraphVmValuePorts.A),
                new("loadOne", GraphVmValuePorts.Value, "incI", GraphVmValuePorts.B),
                new("incI", GraphVmValuePorts.Value, "storeNewI", GraphVmValuePorts.Value),
                new("loadSumForReturn", GraphVmValuePorts.Value, "done", GraphVmValuePorts.Value)
            }
        };
    }

    public static GraphVmDocument CreateDrinkUntilFullGraph(int limit)
    {
        return new GraphVmDocument
        {
            Id = "bench.graphvm.drink-until-full",
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
}
