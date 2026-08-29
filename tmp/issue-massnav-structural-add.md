## 来源（tech-debt fuse：上层切片揭出下层缺陷）

#1306 路线③（CommandPref 实体化，PR #1336）实施时发现：**对移动中的 massnav 代理实体做任何裸 `World.Add`（添加组件）都会打断其执行中的 move**。

## 证据链（PR #1336 实测，可复现）

- `RoadNetworkShowcase_EngineFarRoadMove_*`：本分支红 / main 绿
- 关掉 CommandPref 种子 → 绿
- 把种子换成加 `InteractionMode` 组件 → **同样红**（证明不是 CommandPref 特有，是裸 Add 的通病）
- 加组件时机（进图期 / MapLoaded 后 / 2 tick 后）→ 全部同样断（时机无关）

**波及面**：#1306 路线①（`SetInteractionMode` op 463）对移动中代理写模式组件同样会踩；未来任何「给运行中实体挂稀疏组件」的合法操作（偏好、模式、状态标记）都踩。

## 线索

GAS 效果链对代理实体做结构变更走 `_structuralCommands` 缓冲通道（不在系统执行中途裸 Add）——说明仓库存在一条**未成文的纪律**：massnav 代理的结构变更必须走缓冲路径。但这条纪律没有文档、没有守卫，上层新代码（种子/模式组件）无从知晓。

## What to build

- massnav 侧修复：移动中代理收到组件 Add 不再断 move（或给出代理结构变更的正式通道并让裸 Add fail-fast 点名——两个方向先裁决哪个）
- 把上述纪律成文（massnav 合同页或代理实体合同），防止上层再踩
- 守卫/测试：移动中代理 + 组件 Add 的回归测试钉住

## 阻塞

- **PR #1336（#1306③）被本单阻塞**：massnav 修复合入后 #1336 rebase 重跑 CI 才能绿
- #1306① 的 `SetInteractionMode` 在移动单位上的可用性依赖本单

## Acceptance criteria

- [ ] 方向裁决（容忍裸 Add vs 强制缓冲通道）并回填
- [ ] `RoadNetworkShowcase_EngineFarRoadMove_*` 在「移动中代理被加组件」场景下绿
- [ ] 纪律成文 + 回归测试
- [ ] PR #1336 CI 转绿可合入
