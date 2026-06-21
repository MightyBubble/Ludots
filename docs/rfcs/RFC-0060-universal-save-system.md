# RFC-0060 通用存档系统

状态：Accepted by Epic #292 implementation slice

正式结论：`gitbook/architecture/save-system.md`

注意：本仓库此前已有 `RFC-0060-ai-utility-autocast-contract.md`。Epic #292 明确引用的通用存档 RFC 使用同一编号，本文件仅作为该 Epic 的回写记录；正式规范以 GitBook 页面为准，RFC 编号不作为实现依据。

## 背景

Ludots 在 Epic #292 前没有通用存档能力。项目已有 ConfigPipeline、实体模板生成、Registry 和 Arch ECS 运行时，但没有反向持久化链路，也没有非 ECS 域的统一快照合同。

存档被定为 Core 基础设施，不做成 Mod。

## 最终决议

1. 实体世界快照复用仓库内 `src/Libraries/Arch.Extended/Arch.Persistence` 的二进制 World 序列化，不自建实体重建栈。
2. 取消早期方案中的 `PersistentId`、两趟 entity-ref fixup、模板增量重建和纯 JSON world 序列化。
3. Arch entity 身份以 Id / WorldId / Version 保真；读回后由 `SaveEntityWorldIdNormalizer` 归一化已知 entity-ref buffer。
4. 固定 buffer / unmanaged 组件通过 `UnmanagedComponentFormatter<T>` 整结构 raw-bytes 保存；managed 组件必须显式 formatter。
5. 保存上下文使用 `SaveContextHeader`，包含 `schemaVersion`、`modSetHash`、`registryFingerprint`、`mapId`、`tick`、`createdUtc`、`engineVersion`。
6. 读档前必须通过 `SaveContextValidator` 完整性闸门；schema、mod set 或 registry 不匹配立即 fail-fast。
7. 非 ECS 域通过 `ISaveParticipant` 进入 `domains.json`，不允许每个系统私建存档文件或平行 restore 管线。
8. 文件容器为 `.ldsave`，包含 `header.json`、`domains.json`、`world.bin` 三段和各自 SHA-256。
9. Core 通过 `ISaveStorage` 依赖平台抽象，`src/Core` 不使用具体文件 API。
10. 槽位由 `SaveSlotStore` 管理，写入使用 temp + commit；autosave retention 只删除 autosave 槽位，不碰 manual 槽位。
11. 保存只允许在 clean tick boundary 捕获，目前是 `SystemGroup.ClearPresentationFlags` 之后。

## 被取代内容

以下早期 RFC 方向已被 Epic #292 实现取代：

- 为所有可存实体添加新的 `PersistentId`。
- 通过两趟扫描修复组件内 entity reference。
- 从模板和增量 patch 反向重建运行时实体。
- 使用纯 JSON 作为 world 状态主格式。
- 把存档能力做成 Mod 或 showcase 私有工具。
- 在读档时做 schema 迁移、mod set fallback 或 registry 自动修补。

## 复用清单

- `Arch.Persistence`：World / archetype / chunk 图序列化。
- `ConfigPipeline` 与 mod load plan：生成 `modSetHash`。
- 现有 Registry snapshot：生成 `registryFingerprint`。
- `GameEngine` core service registry：挂载 `SaveParticipantRegistry`。
- `ISaveParticipant`：承载 GameSession、clock、TimeFlow、MapSession、Narrative、Teams 等非 ECS 域。
- `ISaveStorage`：平台存储端口。

## 新增清单

- `src/Core/Persistence/*`：snapshot、restore、formatter、context、participant、container、slot store。
- `src/Platform/Ludots.Platform.Abstractions/ISaveStorage.cs`：平台存储端口。
- `src/Tests/PersistenceTests/`：SAVE-1 到 SAVE-9 的 TDD 覆盖。
- `gitbook/architecture/save-system.md`：正式架构页。
- `scripts/acceptance/run-save-system-uat.ps1`：UAT 包装脚本。

## 验收

- header fail-fast 覆盖 schema / mod set / registry fingerprint。
- world 二进制快照覆盖 fixed buffer、managed `Name`、entity-ref 和排除策略。
- restore 后非 ECS 域和 World 一起恢复。
- restore 后继续跑 tick 的 trace 与原世界连续跑一致。
- container 支持 header-only listing、完整 decode hash 校验和原子写入失败保护。
- autosave retention 保留最新 autosave 且不删除 manual slot。
- 既有 `rts_cnc_training` showcase 完成无头 UAT，证据输出到 `artifacts/acceptance/save-system/`。

## 正式入口

实现和后续开发判断以 `gitbook/architecture/save-system.md` 为准。本 RFC 只保留历史决策、取代项和 Epic #292 的回写证据。
