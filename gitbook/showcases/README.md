# Showcase 画廊导览

Ludots 的 showcase 分三层看：先认词条，再练专项，最后看词条串成剧情。每一层的可启动条目、验收测试与证据目录都登记在 `showcase.registry.json`，门户画廊在线浏览：<https://mightybubble.github.io/Ludots/gallery.html>（证据查看器：<https://mightybubble.github.io/Ludots/tests.html>）。

## 第一层：词条（一个图 op 一条）

每个可执行图节点都是一个可单独启动的词条画廊（registry 里 `capability_standard_graph_op_*` 条目），配套 140 页人话词典 [图节点词典](../reference/graph-node-op-wiki/README.md)——每页一场给玩家看的短剧，加一节给 mod 作者的写法。词条先把「这个动词能干什么」说清楚，是后面两层的词汇表。

## 第二层：专项（一个能力一场）

capability_standard 专项把一组词条组织成一场可玩的能力演示：行为树竞技场、HFSM 哨塔、脚本流沙盒、能力图沙盒、伤员评分、实时技能热改工作台等，清单见 [能力标准 Showcase](../architecture/capability-standard-showcases.md)。每场专项都有 headless 验收测试兜底，进度以 registry 的 `acceptanceTest` 回链为准。

## 第三层：剧情（词条串成戏）

剧情层是总装舞台：旗舰 [夜袭三波](../architecture/graph-layering-flow-and-behavior.md)（英雄进圈→两波敌人→击杀阈值→Boss→胜利面板，全数据驱动的 MapTriggerGraph），叠加 [夜袭 override](../architecture/graph-layering-flow-and-behavior.md) 看跨 mod 触发图如何改写基底规则。引擎侧渲染能力另有 20 场 [引擎画廊 Wiki](../reference/engine-gallery-wiki/README.md)，每场一页可播验收录像。

## 作者之旅：推荐的逛展顺序

用 launcher 逐站启动（`.\scripts\run-mod-launcher.cmd cli launch preset:<id> --adapter raylib`），从读到写、从词到戏：

1. **词条浏览**——先在词典里认词，再启动词条画廊看词「动起来」：
   `capability_standard_graph_op_ConstInt_raylib` → `capability_standard_graph_op_CompareEqInt_raylib`（整数/枚举比较词条）→ `capability_standard_graph_op_SendEvent_raylib` → `capability_standard_graph_op_QueryRadius_raylib`
2. **专项进阶**——把词条组织成控制流与状态：
   `capability_standard_script_flow_sandbox_raylib`（脚本控制流 / 跨拍 Yield）→ `capability_standard_behavior_tree_arena_raylib`（行为树）→ `capability_standard_hfsm_sentry_arena_raylib`（状态机）→ `capability_standard_graph_score_raylib`（组合短剧）→ `capability_standard_ability_graph_sandbox_raylib`（GAS 能力图）→ `capability_standard_live_skill_workbench_raylib`（技能热改）
3. **夜袭总装**——词条与专项全部汇进一场戏：
   `map_trigger_night_raid_raylib` → `map_trigger_night_raid_override_raylib`（叠加 override 看跨 mod 改写）

启动器菜单里的 preset 是平铺列表；上面这条推荐顺序是作者视角的导读，launcher 的结构化分组是后续独立工作。
