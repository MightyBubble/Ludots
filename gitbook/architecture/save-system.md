# 通用存档系统

本页是 Ludots Core Save/Load 的正式架构口径。存档是 Core 基础设施，不是 Mod；具体玩法和 UI 入口可以由 Mod 提供，但状态快照、完整性闸门、容器格式和槽位语义归属 Core / Platform 边界。

## 架构结论

Core 存档以 `WorldSaveSnapshot` 为内部交换对象：

| 部分 | 载荷 | 归属 |
|------|------|------|
| `header.json` | `SaveContextHeader`：`schemaVersion`、`modSetHash`、`registryFingerprint`、`mapId`、`tick`、`createdUtc`、`engineVersion` | Core |
| `domains.json` | 非 ECS 域状态，由 `ISaveParticipant` 贡献 | Core |
| `world.bin` | Arch.Persistence 二进制 World 快照 | Core |

实体世界复用仓库内已有的 `src/Libraries/Arch.Extended/Arch.Persistence`。Ludots 不再建立 `PersistentId`、两趟 entity-ref fixup 或模板反向重建栈；Arch 的整世界快照保留 `Entity` 的 Id / WorldId / Version，读档后再做 WorldId 归一化和 entity-ref 有效性校验。

## 快照与恢复流程

保存必须发生在干净 tick 边界。当前正式边界是 `SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags)`；进行中的 system phase 不允许捕获快照。

保存流程：

```text
GameEngine
  -> WorldSnapshotService.Capture
  -> SaveContextFactory.Capture
  -> SaveParticipantRegistry.CaptureDomains
  -> LudotsBinaryWorldSerializer.Serialize
  -> SaveContainerCodec.Encode / SaveSlotStore.WriteSlot
```

恢复流程：

```text
SaveSlotStore.ReadSlot / SaveContainerCodec.Decode
  -> WorldRestoreService.Restore
  -> SaveContextValidator.Validate
  -> LudotsBinaryWorldSerializer.Deserialize
  -> GameEngine.RestoreWorldSnapshot
  -> LudotsWorldStateImporter.ImportOwnedSnapshotInto
  -> SaveParticipantRegistry.RestoreDomains
```

恢复必须先通过 `schemaVersion`、`modSetHash` 和 `registryFingerprint` 闸门。任何不匹配都 fail-fast，不迁移、不 fallback、不尝试向后兼容。

## 组件序列化

Arch.Persistence 的 contractless 反射不能正确覆盖 Ludots 的 fixed buffer 组件，所以 Core 显式注册 formatter：

- unmanaged 组件使用 `UnmanagedComponentFormatter<T>` 做整结构 raw-bytes 序列化。
- managed 组件必须显式 formatter，目前 `Name` 使用 `NameFormatter`，`MapEntity` 使用 `MapEntityFormatter`。
- formatter 和组件类型集由 `LudotsCorePersistenceFormatters` 静态缓存，`ArchBinarySerializer` 以线程级缓存复用；一次进程内反射发现只应构建一次。
- 新 unmanaged 组件由 formatter 自动发现覆盖；新 managed 组件必须加入 `LudotsCorePersistenceFormatters`，并补覆盖 fixed buffer、entity-ref 和读回保真度的测试。

`LudotsBinaryWorldSerializer` 在写入前后执行：

- `SaveEntityReferenceValidator.Validate`：拒绝引用缺失实体或被排除实体。
- `SaveEntityWorldIdNormalizer.Normalize`：读回后把已知 entity-ref buffer 的 WorldId 对齐到当前 World。
- `SaveEntityInclusionPolicy.Default`：排除 `SaveExcludedTag`、`GameplayEvent`、`SimulationBudgetFuseEvent`、`PresentationDestroyPending`。

恢复导入使用消费式 world 转移：`WorldRestoreService` 反序列化得到的 world 由 `LudotsWorldStateImporter.ImportOwnedSnapshotInto` 直接导入目标 world，并在导入后从源 world 解绑已转移的 Arch 内部存储。这样 restore 不再额外执行整世界 serialize/deserialize，也不依赖源 world 后续 `Dispose()` 的内部清理细节。

## 非 ECS 域

非 ECS 运行时状态只能通过 `ISaveParticipant` 进入 `domains.json`：

```csharp
public interface ISaveParticipant
{
    string DomainKey { get; }
    JsonNode CaptureState();
    void RestoreState(JsonNode state);
}
```

`SaveParticipantRegistry` 要求 domain key 唯一，读档时拒绝未知 domain，也拒绝缺失已注册 domain。Core 引擎初始化时注册这些 domain：

- `clock`
- `gameSession`
- `inventory`
- `mapSessions`
- `narrative`
- `relationships`
- `teams`
- `timeFlow`

`inventory` 与 `relationships` 当前是空 domain 占位，用来固定存档合同；真正 runtime 状态接入时必须在同一 domain 下深化 participant，不新建平行存档管线。

## 容器、槽位与平台边界

外部文件使用 `.ldsave` 单文件容器。`SaveContainerCodec` 写入固定 frame header、三段长度和三段 SHA-256：

```text
magic/version/reserved
headerLength/domainLength/worldLength
headerHash/domainHash/worldHash
header.json
domains.json
world.bin
```

`ReadHeader()` 只校验并读取 `header.json`，用于槽位列表展示；它不触碰 `world.bin`。完整读档必须走 `Decode()`，并校验所有 section hash。

Core 不直接依赖平台文件 API。存储边界是 `ISaveStorage`，定义在 `src/Platform/Ludots.Platform.Abstractions`：

- `ListFileKeys`
- `Exists`
- `ReadAllBytes`
- `WriteAllBytes`
- `CommitTempFile`
- `Delete`

`SaveSlotStore` 只处理槽位语义与容器编解码。槽位 key 固定为 `saves/{kind}/{name}.ldsave`，其中 `kind` 当前包括 `manual` 和 `autosave`。写入先写 temp key，再调用 `CommitTempFile`，失败时保留原槽位。`AutosaveSlotPolicy` 只清理 autosave kind，绝不删除 manual 槽位。

### 落盘归属

生产 `ISaveStorage` adapter 归 Platform 实现层，不归 Core、不归 Mod：

- 桌面：`src/Platform/Ludots.Platform.Desktop` 的 `DesktopSaveStorage`——真实磁盘文件 adapter，key 是相对路径（`/` 分隔），根目录在构造时固定并做越界拒绝；`CommitTempFile` 用 `File.Move(overwrite: true)` 原子替换。Raylib host（`RaylibHostComposer`）以 `{baseDir}/Saves` 为根构造并注册到 `CoreServiceKeys.SaveStorage`，Mod 从引擎解析 `ISaveStorage` 后自建 `SaveSlotStore` 使用，不落文件 API。
- UE 等外部宿主：在宿主仓库实现同一 port（仓库外，接口即合同）。
- Web 平台 adapter（OPFS 等）待 Web 存档需求立项时在同一 Platform 层补位，不在 Core 加分支。

## 验收口径

Core Save/Load 的验收必须覆盖：

- Arch.Persistence world 二进制往返保真。
- fixed buffer / entity-ref / managed component formatter。
- header 完整性闸门 fail-fast。
- 实体纳入/排除策略与 clean tick boundary。
- Entity Id / WorldId / Version 稳定性和 entity-ref 有效性。
- Core participant capture / restore。
- restore 后确定性续跑 trace，必须比较 tick、fixed frame 和稳定 world 状态哈希。
- `.ldsave` section hash、header-only listing、原子写入、autosave retention。
- 既有 showcase UAT：`rts_cnc_training` 存档、变更、读档、续跑一致。

自动化入口：

```powershell
dotnet test src\Tests\PersistenceTests\PersistenceTests.csproj --filter SaveSystemUatTests
scripts\acceptance\run-save-system-uat.ps1
```

UAT 证据输出到 `artifacts/acceptance/save-system/`。

## 代码锚点

- `src/Core/Persistence/WorldSnapshotService.cs`
- `src/Core/Persistence/WorldRestoreService.cs`
- `src/Core/Persistence/LudotsBinaryWorldSerializer.cs`
- `src/Core/Persistence/LudotsCorePersistenceFormatters.cs`
- `src/Core/Persistence/SaveContextHeader.cs`
- `src/Core/Persistence/SaveContextHashes.cs`
- `src/Core/Persistence/SaveParticipantRegistry.cs`
- `src/Core/Persistence/CoreSaveParticipants.cs`
- `src/Core/Persistence/SaveContainerCodec.cs`
- `src/Core/Persistence/SaveSlotStore.cs`
- `src/Platform/Ludots.Platform.Abstractions/ISaveStorage.cs`
- `src/Platform/Ludots.Platform.Desktop/DesktopSaveStorage.cs`
- `src/Tests/PersistenceTests/`
