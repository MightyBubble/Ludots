# 组件手册

组件是场景里真正干活的角色。这页讲三种"可自由摆布"的组件——地形基座、静态网格阵、动画角色——每种给完整字段表和抄得动的片段。另外 22 个现成的能力场景（水面、粒子、阴影、刀光……）也可以直接当节点挂进场景，见文末。

## island_terrain — 岛屿地形基座

程序化生成一座岛：山体、沙滩、海面、天光全包，作为场景的地板和背景。它自己画天空和环境，**不碰相机**——开场视角完全由场景的 `camera` 决定。

```json
{ "type": "island_terrain", "config": {
    "chunksPerSide": 16, "samplesPerChunk": 33,
    "worldSizeMeters": 480, "seed": 47, "dayPhase": 0.46 } }
```

| 字段 | 人话 |
|---|---|
| `worldSizeMeters` | 岛多大（边长，米）。480 是标准岛；想小场面可以缩，但山形频率不变，太小的岛会变成一块高原 |
| `chunksPerSide` / `samplesPerChunk` | 地形网格精细度，一般不动 |
| `seed` | 换一个数换一座岛 |
| `dayPhase` | 一天里的时辰，0 到 1：0.3 清晨感、0.46 正午、0.7 黄昏 |

## static_mesh — 静态网格阵

摆不会动的东西：石头、箱子、柱子。一个组件管理一批**同一形状**的实例，一次合批画完，几百块也不卡。每块实例可以有自己的位置、大小、朝向、颜色，甚至换材质。

```json
{ "type": "static_mesh",
  "assets": ["composition.rock", "composition.rock_mossy"],
  "config": {
    "primitive": "cube",
    "material": "composition.rock",
    "instances": [
      { "position": [30, 3, 40], "scale": 6, "yawDeg": 45 },
      { "position": [45, 3, 25], "scale": 4, "yawDeg": 120,
        "color": [0.5, 0.8, 0.5, 1], "material": "composition.rock_mossy" }
    ] } }
```

| 字段 | 人话 |
|---|---|
| `config.primitive` | 形状：`cube` 方块 / `sphere` 圆球 |
| `config.material` | 全批默认材质（assets 里申报过的材质 id） |
| `instances[].position` | 这一块放哪（xyz，米） |
| `instances[].scale` | 大小：一个数等比，或 `[x,y,z]` 分轴拉 |
| `instances[].yawDeg` | 绕竖轴转多少度——石头摆自然全靠它 |
| `instances[].color` | 染色 RGBA（1 满 0 无） |
| `instances[].material` | 单给这一块换材质（比如三分之一长青苔） |

## animator — 动画角色

一个会动的人/物：装载 GLB 模型并循环播放其中一段动画。走路的兵、巡逻的守卫都是它。美术动画独立播放，不需要任何游戏逻辑接线。

```json
{ "type": "animator",
  "assets": ["composition.guard"],
  "config": {
    "clip": "Walking", "speed": 1.0, "phaseOffset": 0.5,
    "position": [185, 3, 192], "scale": 10, "facingDeg": 225 } }
```

| 字段 | 人话 |
|---|---|
| `clip` | 播模型里哪段动画（按名字模糊匹配，写 `Walking` 就能命中带这个词的片段） |
| `speed` | 播放速度，2 就是两倍速 |
| `phaseOffset` | 错相：两个兵同动画但想一前一后，给第二个 0.5 |
| `position` / `scale` / `facingDeg` | 站哪、多大、脸朝哪边 |
| `castShadows` | 默认投影；不要影子写 `false` |

## 现成的能力场景当素材库

画廊工程 `scenes/` 里还有 22 个能力场景（`skybox` 天空盒、`water` 反射水面、`particles` 粒子、`lighting` 全效光照、`crowd_anim` 四千人军团……）。它们每个是"单节点整帧"组件，在[引擎画廊 Wiki](../engine-gallery-wiki/README.md) 逐个有页：一场实拍录像加"怎么跑"。需要往自己的工程搬时，把对应场景文件抄过去、组件库包含它用的 kind 即可。

