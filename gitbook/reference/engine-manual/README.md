# 纯 Raylib 引擎用户手册

这一套手册讲 Ludots 里的"纯 raylib 引擎"：一个不依赖任何游戏框架、能打开"工程"文件的迷你引擎播放器，像用 Unity Player 打开一个 Unity 工程那样用它。

**先认门：这套文档分两个视角。** 你现在在读的是**产品用户手册**——用引擎摆场景、跑验收、打包发布的人看这里，通篇不需要写代码。如果你是要**开发引擎本身**（写组件、改装载器、动渲染器），你的入口在开发者侧：[引擎工程分层与关卡容器格式](../../architecture/raylib-engine-project-scene-format.md)（架构合同）与[引擎画廊开发指南](../../architecture/raylib-engine-gallery-dev-guide.md)（登记环），能力实拍见[引擎画廊 Wiki](../engine-gallery-wiki/README.md)。

## 上手

- [快速上手](start.md) — 三条命令跑起来，鼠标键盘怎么操作。
- [工程是什么](project.md) — 一个文件夹就是一个工程，里面每样东西干什么。
- [场景怎么写](scene.md) — 从零写一个自己的场景，每个字段的人话解释。

## 进阶

- [组件手册](components.md) — 地形、静态网格、动画角色三类可摆组件的完整字段表。
- [材质手册](materials.md) — 父材质/子材质怎么写，贴图放哪里。
- [播放器与命令行](player.md) — 全部命令行参数、菜单、常见报错。
- [验收与证据](acceptance.md) — 怎么跑一条验收、截图和帧统计在哪看。
- [打包分发](packaging.md) — 把播放器和工程拷到没装开发环境的电脑上跑。

深入引擎内部实现（接口、装载器、分层合同）看[引擎工程分层与关卡容器格式](../../architecture/raylib-engine-project-scene-format.md)；场景实拍画廊看[引擎画廊 Wiki](../engine-gallery-wiki/README.md)。
