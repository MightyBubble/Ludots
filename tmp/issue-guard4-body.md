## 来源

PR #1120 落地 #1117 的文档项与禁则①②守卫时，禁则④（本机 I/O 层概念不得进入存档与网络载荷——#902 §3.5）的守卫测试注明「另行走单」，本单认领该剩余项。

## What to build

架构守卫测试（照 `TerminologyGovernanceTests` 先例，可并入该文件或并列新文件）：

- **存档侧**：扫描存档 schema / 持久化写入路径（锚点：`src/Core/Persistence/CoreSaveParticipants.cs` 一带的 launchContext 写入形态），拒绝新增 seatId、controlSchemeId、物理设备标识字段
- **网络载荷侧**：#711 合入 main 前网络形态未定，本单先落存档侧；#711 合入时补网络载荷扫描并回本单勾选

已知例外（依 #1118 裁决，显式豁免名单管理，只减不增）：

- 存档 `launchContext.localSeats[].controlSchemeId`——裁决为接受现状（进图快照、跨机读档 fail-fast 属预期），首个面向玩家发布前复审

## 时机

#1058 多座位落地时，设备→Seat 绑定、多 scheme 声明都会扩展存档接触面——守卫需先行在位，防止本机 I/O 概念随多座位流入存档。

## Acceptance criteria

- [ ] 存档侧守卫合入且全绿（豁免名单显式、只减不增）
- [ ] #1117 剩余项注明由本单承接
- [ ] #711 合入 main 后补网络载荷扫描

## 边界

- 不改 #1118 裁决结果，不改现有存档行为——本单只加守卫，不动数据
