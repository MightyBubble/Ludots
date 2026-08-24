# Presenter 能力逐条目录

presentation 域的全部作者面能力，逐条可看。每条回答四件事：**是什么 / 怎么写 / 跑哪个 showcase / 看什么验收证据**。本目录引用的每个 preset id、每条证据路径都随 main 提交，可逐条点击求证。

| 分组 | 条目数 | 页面 |
|---|---|---|
| 资产类型 AssetKind | 10 条 | [asset-kinds.md](asset-kinds.md) |
| 行为 BehaviorKind | 13 条（含标准生产配置） | [behaviors.md](behaviors.md) |
| 渲染车道 VisualRenderPath | 6 条 + LOD/裁剪 | [render-lanes.md](render-lanes.md) |
| 环境与光照 | 11 条 | [environment.md](environment.md) |
| 指令 PresenterCommandKind | 11 条 + Extension | [commands.md](commands.md) |
| 参数 sink 机制 | 声明→编译→写入→重发全链 | [param-sink.md](param-sink.md) |
| 验收与学习路线 | 5 条路线 + 6 项性能基线 | [acceptance-map.md](acceptance-map.md) |

统一跑法（preset 见各条目）：

```powershell
.\scripts\run-mod-launcher.cmd cli launch preset:<preset> --adapter raylib
```

架构全景（为什么是"资产+行为+车道"三层）见 [Presenter-as-Actor 架构设计](../../architecture/presenter-as-actor-architecture.md)；全部配置文件的字段表与 fail-loud 边界见 [Raylib 渲染配置结构](../raylib-render-config-structure.md)；五类配置文件的字段表与校验边界统一见 [Raylib 渲染配置结构](../raylib-render-config-structure.md)。

诚实标注：**Sound（音频）目前契约就绪、raylib 侧无消费者**——见 [asset-kinds.md](asset-kinds.md) 对应条目，这是 presentation 域唯一"合同在、执行缺"的能力面。
