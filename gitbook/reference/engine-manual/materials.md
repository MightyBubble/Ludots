# 材质手册

材质决定一个表面长什么样：什么颜色、多粗糙、多金属、贴什么图。引擎的材质是"父材质 + 子材质"两层写法，跟 Unity 的 Material、Unreal 的 Material Instance 一个路数——**父材质定基调，子材质只写差异**，不用整份复制。

## 一个材质文件

材质是工程 `materials/` 目录里的 JSON 文件，一个文件一个材质：

```json
{
  "id": "rock",
  "domain": "Surface",
  "flags": ["Opaque"],
  "roughness": 0.88,
  "metalness": 0.04,
  "textures": { "albedo": "textures/rock_albedo.png" },
  "params": { "floats": {}, "colors": {} }
}
```

| 字段 | 人话 |
|---|---|
| `id` | 材质名，场景里用 `材质文件名`（不带 .json）引用它 |
| `flags` | 画法：`Opaque` 不透明、`Cutout` 镂空（草叶铁丝网）、`AlphaBlend` 半透明、`Additive` 加色发光、`DoubleSided` 双面 |
| `roughness` | 0 镜面到 1 哑光，石头 0.85 上下、金属 0.3 上下 |
| `metalness` | 0 非金属 1 金属 |
| `textures.albedo` | 主贴图，工程根的相对路径；不写就是纯色靠染色 |
| `params` | 自定义着色器参数（进阶，一般空着） |

## 子材质：只写不一样的地方

想让一部分石头长青苔？建一个子材质，声明 `parent`，只覆盖要变的字段——其余（包括贴图）全部继承父材质：

```json
{
  "id": "rock_mossy",
  "domain": "Surface",
  "parent": "rock",
  "roughness": 0.32
}
```

这条链可以继续往下接（子再孙子），引擎按"从根到叶、后写的赢"合并。子材质有两个规矩：不能改 `flags` 和着色器，那是父材质说了算的；引用的父材质文件必须躺在同一个目录。

## 在场景里用

1. 场景 `assets` 里申报：`{ "id": "lvl.rock", "kind": "material", "source": "materials/rock.json" }`（子材质挨着申报一条）；
2. `static_mesh` 组件的 `material` 填默认材质 id，个别实例在 `instances[].material` 换成子材质 id——一批石头里三成长青苔就是这么来的。

## 贴图放哪

贴图放工程 `textures/` 目录，材质里写相对路径（`textures/rock_albedo.png`）。普通 PNG 即可；平铺类贴图做成四方连续的，近看不穿帮。仓库自带的两张示例：`rock_albedo.png` 灰岩颗粒、供参照。
