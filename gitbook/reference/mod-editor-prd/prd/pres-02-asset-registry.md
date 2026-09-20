# pres-02 · 表现资产清单

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/pres-02-asset-registry.md)；编辑器需求见 [UXD](../uxd/pres-02-asset-registry.md)；引擎实现见 [runtime spec](../spec-runtime/pres-02-asset-registry.md)；editor spec 见 [editor spec](../spec-editor/pres-02-asset-registry.md)；现状见 [reference](../reference/pres-02-asset-registry.md)。

## 1. 定位

表现资产清单是"游戏里有哪些可画的东西"的户口本：网格、材质、平台宿主、实例批次四类资产各居其表，按 id 注册供表现器与渲染器消费。

## 2. 产品承诺

- **四表各司其职**：mesh 定义形状（图元/模型/公告牌/VFX）、material 定义表面（域+旗标）、host 把逻辑资产钉到平台真实路径、instanced_batches 声明合批渲染通道。
- **逻辑与平台分离**：mesh/material 只有 id 与语义，平台真实文件路径只出现在 host_assets——换后端/换平台只动 host 表。
- **源路径封闭**：mesh 与 material 表拒绝 sourceUris 字段；越权写即失败。
- **无 prefab 物种**：mesh 的 type 白名单不含 Prefab，写了即被拒绝并指路表现器 AssetBinding（见 pres-01）。
- **合批是一等公民**：instanced_batches 支持 owner、分组、自定义数据通道、行为与渐进提交，GAS 与表现事件可作为驱动键。

## 3. 运行行为

mesh 注册进 MeshAssetRegistry 供渲染器取用；host 表按当前后端 id 过滤后供给宿主合成器；instanced 批次由批量渲染路径按 owner 与 groups 消费；lod_profiles 与 particle_vfx 为引擎侧配套表。

## 4. 异常承诺

id 缺失或重复、type 非法或为 Prefab、material 域缺失、host 的 assetId 未注册、批次 groups 为空——启动失败并指明条目与位置。

**相关文档**：[配置说明](../config/pres-02-asset-registry.md) · [pres-01](pres-01-performers.md) · [pres-03](pres-03-animation.md)
