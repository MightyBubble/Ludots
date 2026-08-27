# Showcase 画廊导览

Ludots 的 showcase 分三层看：先认词条，再练专项，最后看词条串成剧情。每一层的可启动条目、验收测试与证据目录都登记在 `showcase.registry.json`，门户画廊在线浏览：<https://mightybubble.github.io/Ludots/gallery.html>（证据查看器：<https://mightybubble.github.io/Ludots/tests.html>）。

## 第一层：词条（一个合同一条）

图节点和图技能合同都是单独一间房，不把一大家子塞进同一场。

- 图节点：registry 里 `capability_standard_graph_op_*`，词典 [图节点词典](../reference/graph-node-op-wiki/README.md)
- 技能合同：registry 里 `capability_standard_ability_feature_*`，词典 [技能词条画廊](../reference/ability-feature-wiki/README.md)

词条先把「这个动词能干什么」说清楚，是后面两层的词汇表。英雄技能沙盒是把多招串成一栏的组合戏，不是技能词条入口。

## 第二层：专项（一个能力一场）

capability_standard 专项把一组词条组织成一场可玩的能力演示：行为树竞技场、HFSM 哨塔、脚本流沙盒、能力图沙盒、伤员评分、实时技能热改工作台等，清单见 [能力标准 Showcase](../architecture/capability-standard-showcases.md)。每场专项都有 headless 验收测试兜底，进度以 registry 的 `acceptanceTest` 回链为准。

## 第三层：剧情（词条串成戏）

剧情层是总装舞台：旗舰 [夜袭三波](../architecture/graph-layering-flow-and-behavior.md)（英雄进圈→两波敌人→击杀阈值→Boss→胜利面板，全数据驱动的 MapTriggerGraph），叠加 [夜袭 override](../architecture/graph-layering-flow-and-behavior.md) 看跨 mod 触发图如何改写基底规则。引擎侧渲染能力另有 20 场 [引擎画廊 Wiki](../reference/engine-gallery-wiki/README.md)，每场一页可播验收录像。

## 作者之旅：推荐的逛展顺序

用 launcher 逐站启动（`.\scripts\run-mod-launcher.cmd cli launch preset:<id> --adapter raylib`），从读到写、从词到戏：

1. **词条浏览**——先在词典里认词，再启动词条画廊看词「动起来」：
   `capability_standard_graph_op_ConstInt_raylib` → `capability_standard_graph_op_CompareEqInt_raylib`（整数/枚举比较词条）→ `capability_standard_graph_op_SendEvent_raylib` → `capability_standard_graph_op_QueryRadius_raylib` → `capability_standard_ability_feature_EffectSignal_raylib`（点一下就打中）→ `capability_standard_ability_feature_BlockTagsBlocked_raylib`（身上有禁招印就放不出）
2. **专项进阶**——把词条组织成控制流与状态：
   `capability_standard_script_flow_sandbox_raylib`（脚本控制流 / 跨拍 Yield）→ `capability_standard_behavior_tree_arena_raylib`（行为树）→ `capability_standard_hfsm_sentry_arena_raylib`（状态机）→ `capability_standard_graph_score_raylib`（组合短剧）→ `capability_standard_graph_formal_text_raylib`（拼句上字幕）→ `capability_standard_ability_graph_sandbox_raylib`（GAS 能力图）→ `capability_standard_live_skill_workbench_raylib`（技能热改）
3. **夜袭总装**——词条与专项全部汇进一场戏：
   `map_trigger_night_raid_raylib` → `map_trigger_night_raid_override_raylib`（叠加 override 看跨 mod 改写）

启动器菜单里的 preset 是平铺列表；上面这条推荐顺序是作者视角的导读，launcher 的结构化分组是后续独立工作。
