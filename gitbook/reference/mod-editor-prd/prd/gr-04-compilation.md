# gr-04 · 编译与校验

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/gr-04-compilation.md)；编辑器需求见 [UXD](../uxd/gr-04-compilation.md)；引擎实现见 [runtime spec](../spec-runtime/gr-04-compilation.md)；editor spec 见 [editor spec](../spec-editor/gr-04-compilation.md)；现状见 [reference](../reference/gr-04-compilation.md)。

## 1. 定位

文档到指令的确定性翻译，附一次性全量校验：结构、白名单、可达性、数据流、预算、输出 schema 全在装载期判完。

## 2. 产品承诺

- **一次报全**：一份文档的全部问题在同一次装载里报出，不挤牙膏。
- **死代码即错误**：不可达节点、从未定义的读都是装载错误，不进运行期。
- **糖是别名**：While/Until/Wait 只是写法糖，语义等价于基础控制流；糖只开放给 Script。
- **符号装载期解析**：tag、属性、图名等字符串引用在装载期换成整数 id 并回写指令，运行期零字符串查找；解析幂等。
- **预算前置**：编译期即按指令预算封顶，超限文档不产出程序。

## 3. 运行行为

检查依固定顺序执行（头、唯一性、入口、边、寄存器、必需边、端口、可达性、数据流、预算、输出 schema）；编译产物注册后进入终态，装载链末尾冻结。

## 4. 异常承诺

一切编译问题以封闭诊断码集合报出并指明图、节点或边；编译失败不留下半装载状态。

**相关文档**：[配置说明](../config/gr-04-compilation.md) · [gr-02](gr-02-document.md) · [gr-05](gr-05-execution.md)
