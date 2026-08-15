## Graph Infrastructure Closeout — Self Review

- **Task / Issue**: Graph 基建收口：FuncLib / ActionLib 宿主约束、查询容量输出与显式 Halt 合同
- **Date**: 2026-08-15
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 本轮复用既有 graph opcode、VM、registry 和 catalog，补齐已有查询参数的编译发射、ActionLib/FuncLib 合同校验、显式 `HaltReturnInt` 登记期拒绝，以及行为树/脚本压力门；没有新增 profile enum、preset 开关或第二套管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 查询 `AllowTruncated` 的 dropped 输出 | 1 | 既有 Query/Linear graph 编译器 + `GraphInstruction` flags / scratch register |
| FuncLib 纯度和 Invoke 目标闭合 | 0 | `GraphYieldPurityValidator` + `GraphProgramRegistry.ValidateInvokeTargets` |
| ActionLib 宿主与 Yield 约束 | 2 | `GAS/action_lib.json` + `GraphActionCatalogLoader` + `GraphActionCatalog` |
| Graph 程序显式结束合同 | 0 | `GraphKindOperationPolicy` / `GraphProgramRegistry` |
| BT / Script 压力门收口 | 1 | `BehaviorTreeWorld` / `GasGraphOpHandlerTable` |
| 图能力进度入口 | 3 | `gitbook/architecture/graph-capability-status.md` |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable` 的既有空间查询、关系查询、寄存器移动和显式结束 op
- Queues / Systems: 无新增 system、queue 或 effect transaction
- Resolvers / Registries: `GraphProgramRegistry`、`GraphFunctionCatalog`、`GraphActionCatalog`、`GraphIdRegistry`、`GraphYieldPurityValidator`
- Existing graphs / tests: 既有 `GAS/graphs.json`、`GAS/func_lib.json`、`GAS/action_lib.json`、FourX graph JSON 与 GasTests graph/AI 覆盖

### 4. New Layer 0 ops

N/A。没有新增 opcode；`HaltReturnInt` 仍是既有显式结束 op，本轮只是让所有已要求显式结束的 graph kind 都能从正式作者面写出这个终点。

### 5. Transaction boundary

无。本轮只改图编译、目录装载、调用宿主校验和无头压力门，不改变 effect transaction 的提交或回滚边界。

### 6. Config SSOT

行为配置落在：

- `GAS/graphs.json` 的 `queryCapacityPolicy` / `droppedOutput` 字段
- `GAS/func_lib.json`
- `GAS/action_lib.json`
- 既有 showcase graph JSON
- `gitbook/architecture/graph-capability-status.md`

是否新增 JSON schema: NO。使用已有 graph/catalog schema 表达组合与宿主约束，不引入新的 profile DSL。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加说不清的默认 fallback
- [x] 未新增第二套 graph VM 或作者前门
- [x] 未用生产捷径伪造压力门数据

### 8. Next variant test

下一个 Mod 变体修改 graph 连线、查询容量策略或 catalog 条目，不修改 Core enum，不复制 loader/registry。

### 9. Final closeout validation

- `dotnet test src\Tests\GasTests\GasTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~GraphBehaviorPressureMatrixTests.WritePressureMatrices_M1_M2_M3_M4_M5_M6"`: PASS；M1 全部采样拓扑与 M6 最大 cast wave 都守住 15ms 硬门。
- `dotnet test src\Tests\GasTests\GasTests.csproj -c Debug --no-build --filter "TestCategory=ci-gate"`: PASS，422/422。
- `dotnet test src\Tests\GasTests\GasTests.csproj -c Debug --no-build --filter "FullyQualifiedName~Graph"`: PASS，499/499。
- `python scripts\generate-graph-op-node-galleries.py --strict`: PASS。
- `python scripts\validate-registry.py`: PASS，错误 0；23 个既有 screenshot warning。
- `pwsh .\scripts\validate-docs.ps1`: PASS。
- `git diff --check`: PASS。

最终压力样本：M1 `A=10000,N_topo=64` 为 7.010ms；M6 `targets=10000,I=128` 为 12.566ms。
