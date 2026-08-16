# misc-02 · 物品与兑换

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/misc-02-items-exchange.md)；编辑器需求见 [UXD](../uxd/misc-02-items-exchange.md)；引擎实现见 [runtime spec](../spec-runtime/misc-02-items-exchange.md)；编辑器实现见 [editor spec](../spec-editor/misc-02-items-exchange.md)；现状见 [reference](../reference/misc-02-items-exchange.md)。

## 1. 定位

物品域三张表加兑换一张表：形状表用网格掩码刻画物品占格（俄罗斯方块式），布局表声明容器网格（背包/仓库/装备栏），定义表把形状、堆叠、tag、装备效果、技能授予组装成物品物种；兑换表声明"投入什么换产出什么"的操作，可挂关系门槛。

## 2. 产品承诺

- **形状即占格**：rows 掩码声明占哪些格；rotatable 物品自动获得四个旋转；布局的 blockedRows/namedSlots 决定容器内哪些格不可用或专属。
- **定义引用即装配**：物品定义引用形状（必填）、限定槽位、挂装备效果与技能授予、声明装载容器——引用在加载期解析，解析不到即失败。
- **堆叠有下限**：maxStack ≤ 0 一律归 1——不存在 0 叠或负叠物品。
- **兑换是效果入口**：操作声明关系要求（Relationship 旗标）、投入（属性成本/物品）、产出（造物品等）；依赖 Item 与 Relationship 注册表，加载分两段式解析。
- **根表空、内容在 mod**：四张根表是空占位，底座不预设任何物品——玩法 mod 说了算。

## 3. 运行行为

InventoryRuntimeService 管容器与物品实例；装备授予经 EquipmentGrantSync 同步效果/技能；兑换操作由 ExchangeRuntime 注入效果系统执行（投入校验→扣除→产出）。

## 4. 异常承诺

形状/定义引用未注册、定义缺 shape、容器引用非法、兑换引用未注册物品或关系、操作结构非法——启动失败并指明条目与位置。

**相关文档**：[配置说明](../config/misc-02-items-exchange.md) · [misc-01](misc-01-progression.md) · [rel-01](rel-01-catalog.md)
