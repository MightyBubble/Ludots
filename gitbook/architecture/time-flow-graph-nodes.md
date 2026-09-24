# 时间流图节点

本页是图里操作时间的合同。五个域节点，以及一个人的时间从属性落到本地时钟，都已经进运行时。现行时间语义仍以 [时间体系](time-system.md) 为准。

## 1. 概述

时停、变速、冻住一个人，这三层时间引擎里已经有了。图里缺的是前两层的令牌，以及第三层从属性落到时钟的那一跳。

整局模拟 `simulation` 和玩法步进 `simulation.gas` 用暂停令牌、变速令牌。令牌不是属性，不进 AttributeSink。

一个人的快慢已经是属性 `time.scale_permille`。图里用现成的改属性节点写它。AttributeSink 把这个数落到这个人的本地时钟。本地时钟系统只读落地后的整数，不再自己翻属性缓冲。

## 2. 结构

```text
整局 / 玩法
  图节点拿令牌、放回令牌、读停没停、读当前倍率
  → TimeFlowService（已有）
  → 整局暂停时，这一帧模拟步长为 0；玩法暂停时，玩法步进为 0

一个人
  ModifyAttributeSet / 效果覆盖 写入 time.scale_permille
  → AttributeCalculation 里的 sink「Time.EntityScalePermille」
  → EntityLocalClock.ScalePermille
  → EntityLocalClockSystem 用「本拍玩法步数 × 这个整数」推进 LocalStep
```

组合判断：新变体是图节点，加上一条已有 AttributeSink 管线里的绑定。不新开时间服务，不新开属性写入面，不把「半速 / 时停 / 只停技能」做成配置枚举。下一份玩法改的是连线和挂载点。

复用：`TimeFlowService` 的拿令牌、放回、读有效倍率、读是否暂停；`AttributeMutationOps`；`ModifyAttributeSet`；`LoadAttribute` / `LoadSelfAttribute`；`AttributeBindingSystem`；`WriteBlackboardInt` / `WriteMapVarInt` 存令牌编号。

新增：五个图节点；一个 sink 和一条绑定。本地时钟组件增加倍率字段，只由这个 sink 写。

## 3. 详情

### 3.1 域节点

域名必填，只能点已经注册的时间域。内建的是 `simulation`、`simulation.gas`。图不能新建时间域。没注册就失败，并点名这个域名。

接在历法节点后面，序号 520–524。不回收 484–499。

| 节点 | 用在 | 结果 |
|---|---|---|
| `ReadTimeFlowPaused` | 脚本、触发图、查询 | 这个域现在停没停 |
| `ReadTimeFlowScalePermille` | 脚本、触发图、查询 | 这个域的有效倍率。1000 是原速，0 是停着。含父域 |
| `AcquireTimeFlowPause` | 脚本、触发图 | 拿一张暂停令牌，交出令牌编号 |
| `AcquireTimeFlowScale` | 脚本、触发图 | 拿一张变速令牌。输入是倍率整数，交出令牌编号 |
| `ReleaseTimeFlowToken` | 脚本、触发图 | 放回这张令牌。输入是编号 |

查询里不能拿令牌、不能放回。变速倍率必须大于 0，且不超过 8000。写成 0 会失败，停住用暂停节点。同一域上多张变速令牌按现有规则相乘：基础倍率乘每张令牌，再除以 1000。任一张暂停令牌都会让该域停住；父域停了，子域一起停。

令牌编号由作者用已有的黑板整数或地图变量存着。放回时把这个数接回 `ReleaseTimeFlowToken`。拥有者用正在跑的这张图的文档编号，原因用节点编号。文档编号是空的，拿令牌失败并点名。作者不另填一套拥有者名字。

放回一张已经不在的令牌，失败并点名编号。同一张令牌放回两次，第二次同样失败。

`ReadTimeFlowScalePermille` 读的是有效倍率。整局两倍、玩法再拿一张两倍时，玩法读出来是四倍。引擎写玩法步进策略时仍用相对父域的倍率，避免在步进上再乘一次。图不另开一个「相对父域」读节点。

### 3.2 一个人的时间，走属性汇入

不新增「设置这个人的时间」节点。

写：触发图里的 `ModifyAttributeSet`，或效果上的属性覆盖，目标属性 `time.scale_permille`。0 是冻住这个人，1000 跟玩法同步，2000 是两倍。脚本图按属性写入合同，仍然不能直接改属性。

读：`LoadAttribute` / `LoadSelfAttribute`，读的是属性当前值。

落地：`assets/GAS/attribute_bindings.json` 增加一条：

| 字段 | 值 |
|---|---|
| id | `Bind.Time.EntityScalePermille` |
| attribute | `time.scale_permille` |
| sink | `Time.EntityScalePermille` |
| channel | 0 |
| mode | Override |
| scale | 1 |
| resetPolicy | None |

`resetPolicy` 用 `None`。这个数是状态，不是每拍清零的脉冲。清零再写会把「还没写到」和「故意冻住」混成同一个 0。这条绑定只接受通道 0、覆盖、不重置。加成或每拍清零，装载失败并点名这条绑定。

Sink 在 AttributeCalculation 扫描带本地时钟的人。有时钟、没有这个属性，失败并点名，和现在缺属性的失败同一句。有属性、没有时钟的人不动。校验仍是：有限、整数千分比、0 到 8000。通过后把整数写进 `EntityLocalClock.ScalePermille`。

本地时钟系统排在这次落地之后，同在 AttributeCalculation。它只读 `ScalePermille` 和本拍玩法步数，推进累加器和 `LocalStep`。这一拍还没落地就推进，失败并点名这个人。倍率还没落地时字段是 0，和故意冻住是同一个数，所以时钟用单独的落地标记区分，没落地不能当成冻住。

限时标签到期仍留在 InputCollection。同一拍里它看到的是上一拍的 `LocalStep`。这一拍把倍率写成 0，步数要到 AttributeCalculation 落地之后才停。

一个人被冻住，只停他自己的玩法逻辑时间。整局模拟、别的人、世界日子照走。

### 3.3 时停技能怎么接

瞄准时停住世界：输入或面板上的触发图调用 `AcquireTimeFlowPause`，域填 `simulation`，编号写入黑板或地图变量。确认或取消时，同一条仍在跑的触发图 `ReleaseTimeFlowToken`。

只停技能、世界还在走：域填 `simulation.gas`。

冻住一个敌人、自己还在打：对那个敌人的 `time.scale_permille` 写成 0。效果结束、属性回到 1000 之后，下一拍 sink 把时钟倍率写回去。

## 4. 场景

玩家按下技能瞄准。单位、抛射物、技能冷却一起停。玩家确认落点，世界从停住的地方继续走。

玩家打开战斗菜单，菜单自己拿一张暂停。这时再按瞄准，瞄准再拿一张。关掉瞄准，菜单还在，世界仍然停着。关掉菜单，世界继续走。

玩家把玩法调成半速。人还在按原速赶路，技能冷却变成一半。再把整局调成两倍，走路变两倍，玩法冷却变成原速（两倍乘半速）。

玩家对一名敌人施放冻结。这个人的冷却停住，旁边的人继续跳，天上的日子继续走。冻结结束，这个人的冷却继续走。

## 5. 边界

整局暂停之后，跑在模拟步进里的脚本自己也停。它拿了 `simulation` 的暂停令牌，就没有下一拍给它放回。放回必须挂在输入、面板这类帧上仍会跑的触发图。只暂停 `simulation.gas` 时，整局模拟上的图还能放回。

读档会按存档重拿令牌，编号是新的。图里记着的旧编号再放回，失败并点名。要跨读档保持暂停，读档之后的图按域重新拿，不复用旧编号。

图不能切换主历，也不能新建时间域。`Clock.Speed` 不是属性。面板上的倍速如果要做，读 `ReadTimeFlowScalePermille`，写 `AcquireTimeFlowScale` / `ReleaseTimeFlowToken`。

本地时钟的倍率字段只由 sink 写。效果阶段不直接改这个字段，也不直接改 `LocalStep`。

## 6. UAT

```gherkin
Feature: 瞄准时世界停住

  Scenario: 按下瞄准，单位停住
    Given 战斗正在进行，一名单位正在移动
    When 玩家按下技能瞄准
    Then 这名单位停在原地
    And 技能冷却不再减少
    When 玩家确认落点
    Then 这名单位继续移动
    And 技能冷却继续减少

Feature: 两张暂停可以叠在一起

  Scenario: 菜单还开着时，关掉瞄准世界仍然停
    Given 玩家打开了战斗菜单，世界已经停住
    And 玩家又按下了技能瞄准
    When 玩家取消瞄准
    Then 世界仍然停住
    When 玩家关掉战斗菜单
    Then 世界继续走

Feature: 只把玩法变慢

  Scenario: 半速只作用在技能冷却上
    Given 整局是原速，一名单位正在赶路，技能冷却正在走
    When 玩法步进被调成半速
    Then 这名单位仍按原速赶路
    And 技能冷却变成一半速度

Feature: 冻住一个人

  Scenario: 只冻住被点名的那个人
    Given 两名角色的技能冷却都在走
    When 其中一名角色的时间倍率被写成 0
    Then 这名角色的冷却停住
    And 另一名角色的冷却继续走
    And 世界的日子继续走
    When 这名角色的时间倍率回到 1000
    Then 这名角色的冷却继续走

Feature: 点了不存在的时间域

  Scenario: 域名没注册就失败
    Given 图里把时间域写成一个没有登记的名字
    When 这张图运行
    Then 运行失败
    And 失败说明里有这个名字

Feature: 令牌不能放回两次

  Scenario: 第二次放回同一张令牌会失败
    Given 玩家已经取消瞄准，暂停已经解开
    When 同一张暂停令牌再次被放回
    Then 放回失败
    And 失败说明里有这张令牌的编号

Feature: 变速不能拿来表示停住

  Scenario: 倍率写成 0 会失败
    Given 图里要给整局一张变速令牌
    When 倍率填成 0
    Then 拿令牌失败
    And 失败说明要求停住时改用暂停
```
