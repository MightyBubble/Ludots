# attr-02 · 修改器

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/attr-02-modifiers.md)；编辑器需求见 [UXD](../uxd/attr-02-modifiers.md)；引擎实现见 [runtime spec](../spec-runtime/attr-02-modifiers.md)；editor spec 见 [editor spec](../spec-editor/attr-02-modifiers.md)；现状见 [reference](../reference/attr-02-modifiers.md)。

## 1. 定位

修改器是效果对属性的单条数值指令：属性、运算、数值三要素。一个效果可带多条，是属性体系的主要写入来源。

## 2. 产品承诺

- **三运算**：Add 累加、Multiply 倍乘、Override 覆盖；多条按声明顺序依次作用，后者以前者为基。
- **双轨落点**：非 Buff 效果的修改器即时改 Current；Buff 效果的修改器进聚合重算，随 Buff 生效与消退（见 attr-03）。
- **写入权威唯一**：属性写入统一走一个权威入口，负责脏标记、表现位与失败回滚；绕过权威的直写不是产品行为。
- **钳制承诺**：即时写受属性约束钳制，血条型上限等于当前聚合上限；聚合写的上限裁决权归聚合管线。
- **容量诚实**：单个效果的修改器条数超上限（见事实页）时加载失败并指明效果与位置，不静默截断。

## 3. 运行行为

即时修改器在效果提交点执行，事务活跃时先进暂存缓冲、提交时统一回写，提交前对外不可见；聚合修改器只在聚合重算时叠加。写入前后值相等不打脏、不发表现通知。

## 4. 异常承诺

引用未注册属性、条数超上限——启动失败并指明效果 id 与字段；执行期异常整体回滚该次写入，不留半程状态。

**相关文档**：[attr-01](attr-01-definition.md) · [attr-03](attr-03-aggregation.md) · [attr-06](attr-06-events.md)
