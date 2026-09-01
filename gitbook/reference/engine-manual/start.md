# 快速上手

纯 raylib 引擎是一个迷你引擎加一个播放器：引擎负责画（地形、网格、动画、材质、光影），播放器负责打开工程、跑场景、出截图。你在仓库里就能跑，不需要装 Unity 或别的什么。

## 先跑起来

从仓库根目录执行（三条按需选一）：

```text
dotnet run --project src/Apps/Raylib/Ludots.App.RaylibPlayer -- --project projects/engine_gallery
```

打开一个菜单窗口，数字键选场景、回车进入——这是浏览整个画廊工程的方式。

```text
dotnet run --project src/Apps/Raylib/Ludots.App.RaylibPlayer -- --project projects/engine_gallery --scene composition
```

直接进"组合场景"那一关：一座岛、一圈石头、两个巡逻的兵。

```text
dotnet run --project src/Apps/Raylib/Ludots.App.RaylibPlayer -- --project projects/engine_gallery --scene composition --frames 120 --screenshot shot.png --json stats.json
```

无窗口跑 120 帧存一张截图加一份帧统计——这是验收和取证据的标准跑法。

`--project` 永远要给：播放器只认工程目录，不认识散落的文件。

## 进了场景怎么操作

- 按住鼠标左键拖：转视角；
- 滚轮：拉近拉远；
- W A S D 或方向键：平移镜头；
- R：一键回到这一关开头的默认视角；
- ESC：退回菜单。

## 心法

这个引擎的规矩是**场景写在数据文件里，不写在代码里**。想挪一块石头、换一个相机角度、加一排树，改的都是 `projects/engine_gallery/scenes/` 里的 JSON 文件，改完重新跑命令立刻生效，全程不用碰 C#。下一页[工程是什么](project.md)带你认这些文件。
