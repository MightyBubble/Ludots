# 框选 marker 不跟随：根因定位

**结论**：这是 **PR #1486 引入的回归**（提交 `c202aa3978`，作者 Codex，**不在 main 里**），
与 cube 替换、mannequin 替换**都无关**。换成人也一样失效，证实了这一点。

## 症状

框选单位后，蓝色选择环（`presenter.case_e.selection_marker`）创建在单位脚下，
但**单位移动时环不动**，停在创建时那个世界坐标上。

## 根因：一行改错的分支

`src/Core/Presentation/Systems/PresenterBehaviorSystem.cs`
`ResolveDefaultTransformSourceBatch`：

```csharp
Entity parentEntity = parents[index].Parent;
source.Value = parentEntity != Entity.Null && World.IsAlive(parentEntity)
-     ? TransformSource.InheritParent   // #1486 之前：随父实体走
+     ? TransformSource.WorldFixed      // #1486 之后：世界坐标冻结一次
      : states[index].AnchorKind == PresentationAnchorKind.Entity
          ? TransformSource.EntityTransform
          : TransformSource.WorldFixed;
```

选择环是**挂载型 presenter**（有 owner payload root 作为父），因此恒定落入第一个分支。
改之前它继承父变换（跟随单位），改之后变成 `WorldFixed`（冻结在世界坐标）。

`TransformSource.WorldFixed` 的语义就是"世界固定"——对这个用途是错的。

## 运行期证据

框选后（1600x900，`ludots.presenters.query`）：

| defId | 用途 | `logicCm`（owner 真实位置） | `presenterPlaneCm` | `transformSource` |
|---:|---|---|---|---|
| 31 | selection_marker | 4641→4636→**4630**（在动） | 337,459（**跟随**） | WorldFixed |
| 32 | selection_preview_marker | 4630（未变） | 283,298（**冻结**） | WorldFixed |

两者 owner 相同（entity 34 `MassNavigation.Agent.Azure.Heavy`）、`anchorKind` 都是 Entity，
但 defId 32 的 `presenterPlaneCm` 自创建后不再更新。

> 注：defId 31 表现"看起来在跟随"，是因为它在被查询窗口内刚被重建过。
> 两个 marker 都标 `WorldFixed`，行为都是错的；差别只是采样时机。

## 为什么换 cube / 换 mannequin 都复现

marker 是**独立的 presenter**，与 agent 身体的渲染路径无关。
`WorldFixed` 由 presenter 自身的父挂载状态决定，与 body 是 SkinnedMesh 还是
InstancedStaticMesh/cube 完全无关。

## 影响面

**不止选择环。** 任何"挂在实体上、且自身有父级"的 presenter 都会中招——
即所有通过 `CreatePresenter` + owner payload 挂到单位上的装饰（选择环、指示器、
光环、地面标记等）。这是一个**通用呈现正确性缺陷**，不是选择系统的局部问题。

## 修复进展：一行不够

把 `WorldFixed` 改回 `InheritParent` 后重新实测（框选同一批单位，跟踪 owner 34）：

| 采样 | `logicCm`（owner 真实位置） | `presenterPlaneCm` | `transformSource` |
|---|---|---|---|
| t0 | -1014, -1023 | -1688, -1685 | InheritParent |
| t1 | -413, -362（在动） | -1688, -1685（**仍冻结**） | InheritParent |
| t2 | 238, 237（在动） | -1688, -1685（**仍冻结**） | InheritParent |

`transformSource` 已正确变回 `InheritParent`，但位置**依然不跟随**。

### 第二个缺陷

`ResolveTransformBatch`（同文件 :2821-2856）为 `InheritParent` 分支读取
父 presenter 的 `PresenterWorldPosition`：

```csharp
parentSnapshot.WorldPosition = World.Has<PresenterWorldPosition>(parentEntity)
    ? World.Get<PresenterWorldPosition>(parentEntity).Value
    : Vector3.Zero;
```

`hasParent` 还额外要求父实体带 `PresenterState`。若父 presenter 自身的
`PresenterWorldPosition` 不在这一批里被刷新（典型情况：父是 owner payload root，
它属于另一条解析路径/另一个 chunk），marker 就会读到过期的父位置并一直冻结。

此外 `PresenterGroundingUtility.ResolveTransform` 的 `InheritParent` 分支
（`PresenterGroundingUtility.cs:33`）是**全仓唯一**处理该枚举值的地方，
且它直接采信 `parent.WorldPosition` —— 父位置陈旧则子必然陈旧。

### 结论

这不是"改一个枚举值"就能修的。**`InheritParent` 链路（赋值 → 父位置刷新 →
子解析）存在多处缺口，而它恰好是挂载型 presenter 的正确路径。**
`WorldFixed` 是把这个缺口掩盖成了"明显冻结"，改回 `InheritParent` 只是让缺口
变成"看起来设置了正确来源但仍然不跟随"。

**建议**：不要在本分支上继续盲修。这是 presenter 变换链路的架构问题，
应回到 #1486 的上下文里，连同它的 attachment/parameter 依赖模型一起审。
本分支的正确动作是**暂不合并 #1486 的该提交**，并开 issue 记录。

## 处置建议

1. **修这一行**：回到 `InheritParent`（若 #1486 有别的意图，需要它给出证据说明为何放宽）。
2. **确认 #1486 该改动的动机**：它在同一个 commit 里改了 attachment/parameter 依赖模型，
   可能 `InheritParent` 在别处有新语义；需要看它的测试是否覆盖了"挂载 presenter 跟随 owner"。
3. **补回归测试**：一个挂在移动实体上的 scoped presenter，跑 N 帧后断言其
   `presenterPlaneCm` 与 owner 的 `logicCm` 同步位移。这是当前缺口。
4. **#1486 合并前必须解决**：该提交是 Draft，且**不在 main**，本回归是合并门槛问题。

## 为什么 case_e 一直没暴露这个问题

**因为 case_e 的单位从不移动。**

`CaseESelectionShowcaseAcceptanceTests` 对 marker 的全部断言只有三类：

| 断言 | 检查内容 |
|---|---|
| `AssertMarkerVisible` | 环**存在**且 `case_e.marker.visible != 0` |
| `AssertMarkerHidden` | 环**不存在**或不可见 |
| `AssertPreviewOn/Off` | 同上（黄色预览环） |

底层 `CountVisibleMarkersOwnedBy`（:909）只读 `PresenterState.DefId` 与
`case_e.marker.visible` **两个字段**，**完全不读位置**。

再查该文件是否有"让单位移动"的用例：

```
$ grep -nE "VisualTransform|SetPosition|Move|Translate"       src/Tests/GasTests/Production/CaseESelectionShowcaseAcceptanceTests.cs
301:  // ── 05③：PointerMoved 输入边沿命中 → case_e.selection_preview ──
```

**零命中**（唯一命中是注释里的鼠标移动）。单位全程静止，
于是"冻结在世界坐标"与"跟随单位"**在测试里不可区分**，
这个回归对 case_e 的验收完全隐形。

`MassNavSelectionChainTests` 同样只覆盖"选中的是谁/命中判定"，不覆盖
"marker 是否随 owner 移动"。

### 这暴露的是验收缺口

**没有任何一条测试断言"挂载型 presenter 会跟随其 owner 移动"。**
所以：

1. #1486 把 `InheritParent` 改成 `WorldFixed`，CI 全绿。
2. case_e 验收全绿（单位静止）。
3. 直到 10K 场景里单位真的在跑，人才肉眼发现环不动。

## 备注

- 本次定位使用的立方体替换仅为隔离渲染成本，**未提交**。
- mannequin 版本（当前工作区）同样复现，验证完毕。
