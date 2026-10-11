# ord-05 · 输入协议

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/ord-05-input-protocol.md)；编辑器需求见 [UXD](../uxd/ord-05-input-protocol.md)；引擎实现见 [runtime spec](../spec-runtime/ord-05-input-protocol.md)；editor spec 见 [editor spec](../spec-editor/ord-05-input-protocol.md)；现状见 [reference](../reference/ord-05-input-protocol.md)。

## 1. 定位

技能时间轴不在半路停下来等玩家按键或点目标。玩家要选目标、要确认，都在交互上下文里做完（图读指针、写集合），再随施法命令一起送进来。时间轴里只剩一种等待：事件门，等某个事件标记出现。

## 2. 产品承诺

- **目标在下令时定型**：施法命令带着目标和目标上下文进入时间轴，时间轴中途不改目标。
- **事件门有期限**：等待事件标记的门，标记出现就放行；配了超时的，到期没等到也放行，不带数据。
- **写错类型当场报错**：时间轴里写了引擎不认识的步骤类型，启动即失败，报出是哪个技能的第几步。

## 3. 运行行为

能力执行走到事件门时进入等待；此后每帧检查本帧事件里有没有要等的标记，有就放行；配了超时的，到点放行。

## 4. 异常承诺

未知步骤类型（包括已删除的 `InputGate`、`TargetCollectionGate`）一律启动失败，报错里写明能力 id 和步骤序号。

**相关文档**：[配置说明](../config/ord-05-input-protocol.md) · [ord-06](ord-06-input-mappings.md) · [ord-04](ord-04-blackboard.md)
