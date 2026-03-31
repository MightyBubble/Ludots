# PR73 合并与架构审计

本文记录 PR73 在干净工作树中的合并、收敛与架构审计结论。本文提供证据与结论，不替代 `docs/architecture/` 中的正式设计描述。

## 1 审计范围

- PR73 是否已经进入 `origin/main`
- PR73 在干净工作树中的本地合并可行性
- `chunk_streaming` / `road_network` showcase 是否符合 Ludots 六边形与 Mod 边界
- 相关架构文档是否符合中文 SSOT 与文档治理规范

## 2 结论

- `origin/main` 未包含 PR73，对应内容已在干净工作树中完成本地合并。
- 本地合并基线 commit 为 `50397908c235186884b3ded98090f690e11e2847`。
- 合并后的主要架构问题已收敛：`chunk_streaming` 重复根目录已删除，运行时入口与验收入口已统一，文档冲突与英文正文已清理。

## 3 已解决问题

### 3.1 Showcase 结构

- 删除了顶层 `mods/showcases/chunk_streaming/` 的重复实现，保留 `mods/showcases/chunk_streaming/ChunkStreamingShowcaseMod/` 作为唯一根目录。
- `launcher.config.json`、测试工程引用与实际 Mod 目录现在一致，不再存在双根漂移。

### 3.2 运行时与验收入口

- `ChunkStreamingShowcaseRuntime` 已通过 `engine.GlobalContext["ChunkStreamingShowcaseMod.Runtime"]` 暴露正式运行时入口。
- `src/Tests/GasTests/ChunkStreamingShowcaseTests.cs` 不再直接伪造旧式相机路径，而是通过 showcase runtime 的控制命令驱动地标跳转与重置。
- showcase runtime 现在持有自己的“已下达相机目标”，chunk window 以同一命令目标为准，不再出现“命令已发出但下一帧又被旧 camera state 拉回”的漂移。

### 3.3 文档治理

- `docs/architecture/interaction/README.md` 的冲突标记已清理，并收敛为单一中文 SSOT。
- `docs/architecture/README.md`
- `docs/architecture/entity_selection_architecture.md`
- `docs/architecture/order_navigation_movement.md`

以上正文已改为中文，并保留可验证的代码与测试路径。

- `docs/rfcs/RFC-0059-road-order-nav-runtime-unification.md` 中关于 `chunk_streaming` 双根目录的陈旧描述已改为当前约束。
- `artifacts/doc-governance-report.md` 已按当前真实状态重写。

## 4 验证结果

### 4.1 关键回归

- `C:\Users\123\.dotnet\dotnet.exe test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~RoadNetworkShowcaseTests|FullyQualifiedName~ChunkStreamingShowcaseTests|FullyQualifiedName~LoadedGraphRuntimeTests"`
  - 结果：31 passed

### 4.2 目录与文档检查

- `docs/architecture/interaction/README.md` 已无冲突标记
- `chunk_streaming` 只保留单一 Mod 根目录
- `artifacts/doc-governance-report.md` 与本次修复后的文档状态一致

## 5 剩余风险

- 本次没有跑完整仓库测试矩阵，只验证了 PR73 相关关键路径。
- `src/Tests/GasTests/GasTests.csproj` 仍存在既有 warning：引用 `mods/InteractionShowcaseMod/InteractionShowcaseMod.csproj`，但仓库当前实际路径是 `mods/showcases/interaction/InteractionShowcaseMod/InteractionShowcaseMod.csproj`。这不是本次改动引入的问题，但会持续污染测试输出。

## 6 相关文档

- 架构索引：见 [../architecture/README.md](../architecture/README.md)
- 文档治理：见 [../conventions/04_documentation_governance.md](../conventions/04_documentation_governance.md)
- RFC：见 [../rfcs/RFC-0059-road-order-nav-runtime-unification.md](../rfcs/RFC-0059-road-order-nav-runtime-unification.md)
