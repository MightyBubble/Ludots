# 审计交接：控制流作者糖 + #860 codegen 整合线

**主审 PR：** https://github.com/MightyBubble/Ludots/pull/869  
**分支：** `cursor/graph-cf-sugar-integrate-b361`  
**当前 base：** `cursor/ui-panel-graph-mvp-28e6`（#859）  
**日期：** 2026-08-11  
**关联：** #858 / #859 / #848 / #860 / #862 / #865 / #866 / #867  

---

## 1. 给审计代理的结论（先看这段）

| 维度 | 结论 |
|------|------|
| 总体 | **建议按序并入 main**：先合 **#859**，再合 **#869**（相对 #859 的增量） |
| 合同 | 作者糖仅为编译期降级；**Effect 不能 Wait/Yield**；codegen 仍是 Tests spike，未接线正式局 |
| 风险 | A/B 单独 PR（#866/#867）**不要再合**（`AuthoredOpKind` 曾冲突，已在 #869 消除） |
| 验证 | 整合线相关 GasTests **30/30** 本地复跑通过 |
| iOS/发布 | 商店包不走运行时 Roslyn；预生成或解释执行（见 #860 R3） |

**一句话：** #869 = 在 #859 图基建上，把「好写的分支/等待/循环糖」和「#860 科研 codegen」收成一条可审可合的线。

---

## 2. 地图：谁干什么

```text
main
  └─ #859  资源条 MVP + Script/Query CF 真引脚 + L2 行为图（产品+基建）
        └─ #869  Track A/B 作者糖整合 + cherry-pick #860 R0/C codegen + CallStack 适配
```

| 编号 | 角色 | 并入策略 |
|------|------|----------|
| [#859](https://github.com/MightyBubble/Ludots/pull/859) | 图分层 + Query 真引脚 + UI 资源条 | **先合 main** |
| [#869](https://github.com/MightyBubble/Ludots/pull/869) | 糖整合 + codegen spike | **#859 之后合**（base=#859） |
| [#866](https://github.com/MightyBubble/Ludots/pull/866) / [#867](https://github.com/MightyBubble/Ludots/pull/867) | A/B 分轨 | **废弃，勿合** |
| [#862](https://github.com/MightyBubble/Ludots/pull/862) / [#865](https://github.com/MightyBubble/Ludots/pull/865) | R0 / Track C（相对 main） | 能力已在 #869 cherry-pick；合 #869 后可关或仅作对照 |
| [#860](https://github.com/MightyBubble/Ludots/issues/860) | Roslyn/ALC Epic | R0+C 科研进度；R1+ 未做 |
| [#858](https://github.com/MightyBubble/Ludots/issues/858) | UI 面板 Epic | UIP-1 在 #859；与 codegen 无关 |

---

## 3. #869 交付清单（审什么）

### 3.1 作者糖（Script ControlFlow）

| 作者节点 | 降级 | Kind |
|----------|------|------|
| `BranchBool` | `JumpIfFalse` + `Jump` | Script only |
| `SwitchInt` | `ConstInt` + `CompareEqInt` + `JumpIfFalse` + `Jump` + default | Script only |
| `Wait` | 别名 → `Yield` | Script only |
| `While` / `Until` | 比较 + 回边 `Jump`（无 While opcode） | Script only |

`AuthoredOpKind`：**BranchBool=1, SwitchInt=2, While=3, Until=4**（已消 A/B 冲突）。

### 3.2 Codegen spike（GasTests only）

- 白名单：`ConstInt` / `AddInt` / `CompareLtInt` / `CompareEqInt` / `Jump` / `JumpIfFalse`
- Roslyn → Collectible ALC → `Execute(ref state)` + tight locals
- 失败关闭：保留上一份入口，**不静默回退解释器**
- 适配 #859：解释对照路径提供 caller-owned `CallStack`

### 3.3 明确非目标

- 未接线正式 `GraphProgramRegistry` 热重载宿主
- 未做 Query 聚合热重载 UAT（#860 场景）
- Effect / Score / Query / Validation / Derived **禁止** Wait/Yield
- 不新增 While/Switch L0 opcode、不平行第二套 VM

---

## 4. 验证命令与结果

```bash
dotnet test src/Tests/GasTests/GasTests.csproj \
  --filter "FullyQualifiedName~GraphBranchSwitchSugarTests|FullyQualifiedName~GraphScriptWaitLoopSugarTests|FullyQualifiedName~GraphRoslynAlc" \
  -o /tmp/integrate-full-out
```

**结果：30/30 Passed**（2026-08-11，整合分支 `5ba9beb84`）。

覆盖要点：

- Branch/Switch 路径与 fail-closed（缺 default / 无 case / 非法端口 / 重复 case / 缺 selector / Query 拒糖）
- Wait→Yield、切片恢复、While/Until、失控步数、Query 拒 Wait/While/Until、Effect 拒 Wait
- Codegen ≡ interpret、热换、编译失败保留旧入口、微基准门禁

Composition gate：`artifacts/gas-composition-gate.md`（A / B / integrate / R0 / C 段）。

---

## 5. 审计检查表（请勾）

- [ ] #859 本身 UI + Query CF 合同已审过或与本线一并审
- [ ] #869 相对 #859 的 diff：仅糖 + Tests codegen + gate（无意外 Core 大改）
- [ ] `AuthoredOpKind` 编号与 ParseOps/CompileNode 一致，无双轨枚举
- [ ] Effect+Wait / Effect+Yield 失败关闭仍在
- [ ] Codegen 不引用未白名单 API；失败不假装成功
- [ ] 确认 **不要合** #866 / #867
- [ ] 并入顺序：`main ← #859 ← #869`
- [ ] 发布/iOS：不把 Roslyn 热编译当商店路径

---

## 6. 并入 main 操作建议

1. 审并合 **#859** → `main`  
2. 将 #869 的 base 若仍指向旧 #859 头，在 #859 合入后 **retarget base=`main`**（或 rebase 再推）  
3. 再合 **#869**  
4. 关闭 #866、#867（已取代）；按需关闭或标注 #862/#865（内容已在 #869）  
5. 在 #860 评论记录：R0+C 已随 #869 落地 Tests spike；R1 Query/Chunk、R2 宿主合同、R3 预生成/AOT 未做  

---

## 7. 已知残留（不阻断本轮，记债）

| 项 | 说明 |
|----|------|
| Query 热重载 UAT | #860 场景仍缺（R1） |
| Codegen 未接正式加载器 | 仅 GasTests |
| Slice 上无限 While | 单 slice 返回 Running，靠宿主/步数；RunToHalt 会硬抛 MaxInstructions |
| #848 | 能力多已进 #859；是否关 PR 由 #859 业主决定 |
