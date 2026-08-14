# PR #660 合并收口修复 · Cursor 提示词包

> 生成日期：2026-07-26。依据：`docs/audit/pr660-merge-audit-20260726.md` + 三方互验终版清单。
> 用法：**每条提示词独立粘贴给 Cursor，一次只做一条**。不要整包一次投喂。
> 执行顺序建议：P0-1 → P0-2 → P0-3 → P1-4 → P1-5 → P1-6 → P1-7 → P2-9 → P2-10 → D-11。

---

## 通用前言（每条提示词都已内置，无需单独粘贴）

包内每条提示词均包含以下纪律，已写入正文：

1. **仓库纪律**：先读 `AGENTS.md` 与 `gitbook/contributing/ai-assisted-development.md` 的任务执行决策规范；禁止 fallback/向后兼容包袱/重复造轮子/跨越职责；测试必须断言真实行为，禁止恒真测试。
2. **分支纪律**：主工作树有未提交改动。开工前必须 `git status` 确认、从 `main` HEAD 拉新分支（命名 `codex/post-merge-closeout-<序号>`），不得触碰既有未提交改动，不得直接提交到 main。
3. **环境纪律（本机实测必须遵守）**：
   - 本机 dotnet 因缺 `ProgramFiles(x86)` 环境变量会导致 NuGet 崩溃（`Value cannot be null (Parameter 'path1')`）。**每次执行 dotnet 命令前必须补注入**：
     - PowerShell：`${env:ProgramFiles(x86)} = 'C:\Program Files (x86)'`
     - cmd：`set "ProgramFiles(x86)=C:\Program Files (x86)"`
     - Git Bash：`env 'ProgramFiles(x86)=C:\Program Files (x86)' "C:\Program Files\dotnet\dotnet.exe" ...`
   - dotnet 路径：`C:\Program Files\dotnet\dotnet.exe`（SDK 9.0.312，测试目标 net8.0）。
4. **证据纪律**：任何"警告数为 0 / 测试全绿"的结论，必须用 `-t:Rebuild`（或干净 worktree）与显式过滤器复现；**增量编译的 0 警告不算证据，局部过滤器的绿不算全绿**。结论必须附可复现命令。
5. **零分配断言口径**：统一以 `Assert.That(allocated` 为统计口径（当前合并树：硬零 41 处、容差 15 处）；不要用"同行含 alloc 字样"的 grep（会把断言消息文本误算进去）。

---
---

# P0-1 修复 Moba 回归夹具

```text
在仓库 C:\001_AI\LudotsProd 工作。先读 AGENTS.md 与 gitbook/contributing/ai-assisted-development.md 的任务执行决策规范并遵守。先执行 git status；从 main HEAD 新建分支 codex/post-merge-closeout-01，不触碰任何既有未提交改动，不提交到 main。

背景：合并提交 9a6af246cc 上，测试 MobaLocalOrderSource_ResolvesCallerSuppliedTargetCollectionKey 稳定失败，错误为：
  System.InvalidOperationException : MobaLocalOrderSourceSystem requires GameConfig.gasRuntimeCapacity.
原因：提交 948640f6aa 给 mods/showcases/moba_demo/MobaDemoMod/Systems/MobaLocalOrderSourceSystem.cs 构造函数新增了 GameConfig.gasRuntimeCapacity 硬要求（缺失或 CommandIntentScratchCapacity<=0 即抛异常），但该测试夹具未同步更新。

任务：只修改 src/Tests/GasTests/InteractionInput/InputOrderContractTests.cs 中该测试（约 340-380 行）的 globals 夹具：为 new GameConfig { Constants = ... } 增加
  GasRuntimeCapacity = new GasRuntimeCapacityConfig { CommandIntentScratchCapacity = 32 }
（32 为测试值；生产默认是 4096，见 InputOrderMappingSystem.DefaultCommandIntentScratchCapacity 与 assets/Configs/game.json。）若 GasRuntimeCapacityConfig 的其他必填校验导致构造失败，按该类 Validate() 的要求补齐最小正数值。只改夹具，不动生产代码，不新增 fallback。

验证（必须逐条执行并全部通过；每条 dotnet 命令前先补环境变量，PowerShell 写法：${env:ProgramFiles(x86)} = 'C:\Program Files (x86)'；dotnet 用 "C:\Program Files\dotnet\dotnet.exe"）：
1. dotnet test src/Tests/GasTests/GasTests.csproj -c Release --filter "Name~MobaLocalOrderSource" --nologo -v minimal  → 要求 1/1 通过。
2. dotnet test src/Tests/GasTests/GasTests.csproj -c Release --no-build --filter "FullyQualifiedName~Features.InputRouting" --nologo -v minimal → 全绿无连带红。

完成判据：两条验证全绿；diff 仅含该测试文件的夹具改动。汇报实际输出，不要只汇报"应该过了"。
```

---

# P0-2 定案 SkillMapping 分配红 + 统一零分配断言

```text
在仓库 C:\001_AI\LudotsProd 工作。先读 AGENTS.md 与 gitbook/contributing/ai-assisted-development.md 并遵守。先 git status；从 main HEAD 新建分支 codex/post-merge-closeout-02，不触碰既有未提交改动，不提交到 main。dotnet 命令前先补环境变量（${env:ProgramFiles(x86)} = 'C:\Program Files (x86)'），dotnet 用 "C:\Program Files\dotnet\dotnet.exe"。

背景：合并提交 9a6af246cc 上，PR 自己新增的门禁测试 SkillMappingOverrideResolver_TracksGrantedItemFormAndBasePrecedence（src/Tests/GasTests/Ability/AbilityFormRoutingSystemTests.cs:167）稳定红：
  Warmed skill override resolution must not allocate. Expected: 0 But was: 24
被测代码是 mods/CoreInputMod/Systems/LocalOrderSourceHelper.cs 中 SkillMappingOverrideResolver 的 _cache（Dictionary<SkillMappingOverrideCacheKey, InputOrderMapping>，record struct 键含 InputOrderMapping 引用）。本机另有 16 个既有零分配测试同型失败（基线同红，环境性），需先区分真假回归。

第一步（定案，必须先做）：
1. 隔离复跑该测试确认稳定红：dotnet test src/Tests/GasTests/GasTests.csproj -c Release --filter "Name~SkillMappingOverrideResolver" --nologo -v minimal。
2. 定位 24 字节的确切来源：读 TryResolve 的 warmed 路径（缓存命中分支），逐行排查分配点（重点：record struct 键的 EqualityComparer 静态初始化是否发生在测量区间内、AbilitySlotResolver.Resolve 新增 itemGranted 参数路径、缓存未命中分支的 mapping.Clone()）。用 GC.GetAllocatedBytesForCurrentThread 在测试内分段插桩定位到具体调用。
3. 若确认是 warmed 路径的真实分配：修复它（例如把 comparer 初始化挪到预热覆盖的首次 Add 路径、或改用不分配的键类型），使测试转绿。若确认是运行时/JIT 一次性静态初始化且无法在被测代码内消除：转入第二步统一改造，并在测试消息中注明原因。

第二步（统一零分配断言，根治噪声）：
把 GasTests 中硬零分配断言统一改为预算容差。统计口径：Assert.That(allocated 开头的断言，当前硬零 41 处、容差 15 处（用 grep -rn 'Assert.That(allocated' src/Tests/GasTests --include='*.cs' 核实，不要用同行含 alloc 的口径）。
改法：
  const int AllocationBudget = 64; // 单次调用理论上界，覆盖 JIT/GC 一次性后台分配
  Assert.That(allocated, Is.LessThanOrEqualTo(AllocationBudget),
      $"Hot path should allocate <={AllocationBudget} bytes, got {allocated}");
每次提交前必须先改 3 个文件跑绿示範，再分批推广（每批 ≤10 个文件），不要一次改完全部 56 处。

验证：
1. dotnet test src/Tests/GasTests/GasTests.csproj -c Release --filter "Name~SkillMappingOverrideResolver" --nologo → 转绿。
2. 每批改完：dotnet test src/Tests/GasTests/GasTests.csproj -c Release --no-build --filter "FullyQualifiedName~<该批所在命名空间>" --nologo -v minimal → 全绿。
3. 全部改完：全量 GasTests（切片并集，过滤器按命名空间分批跑，不允许只跑局部就宣称全绿）。

完成判据：SkillMapping 红定案有书面结论（真回归已修 / 环境性已注明）；56 处断言统一完成；全量无新增红。汇报分段插桩证据与每批测试输出。
```

---

# P0-3 刷新 ci-audit 证据为合并提交后真实结果

```text
在仓库 C:\001_AI\LudotsProd 工作。先读 AGENTS.md 与 gitbook/contributing/ai-assisted-development.md 并遵守。先 git status；从 main HEAD 新建分支 codex/post-merge-closeout-03，不触碰既有未提交改动，不提交到 main。dotnet 命令前先补环境变量（${env:ProgramFiles(x86)} = 'C:\Program Files (x86)'），dotnet 用 "C:\Program Files\dotnet\dotnet.exe"。

背景：artifacts/ci-audit/pr660/result.md 与 result.json 自述 "final closeout"，但其证据是在最终 push 之前于外部 worktree（C:\001_AI\_codex_audit\...，本机不存在）产出的；最终提交 948640f6aa 晚于证据，合并提交 9a6af246cc 上实测存在 1 个回归红测试（MobaLocalOrderSource，P0-1 已修）。证据与事实不符，必须刷新为合并提交后、含 P0-1 修复的真实全量结果。

任务：
1. 先确认 P0-1 已合入当前分支（或在其分支之上工作）。
2. 真实执行并记录输出（禁止凭空填写）：
   a. dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj -c Release --nologo -v minimal（预期 188+/188+ 全绿，记录实际数字）。
   b. 全量 GasTests：按命名空间切片并集跑（Features / Integration / Presentation / Production / Physics2D / GAS.Effect / GAS.Ability / GAS.Map / GAS.Graph / Vision / Spatial / Association / Config / Terrain / MovePlanOrder + 排除以上全部 token 的兜底切片），记录每片实际通过/失败数与失败名单。
   c. git diff --check origin/main...HEAD。
3. 用真实输出重写 artifacts/ci-audit/pr660/result.md 与 result.json：日期、执行机器/工作树、被测提交 SHA（git rev-parse HEAD）、每条命令的原文与实际输出摘要、已知环境性失败（零分配类）单独列为 "known environmental failures, pre-existing on base 5712a4eef4"，不得计入 PASS 也不得隐藏。删除 "final closeout" 这类超出证据范围的措辞，改为 "post-merge verification on <SHA>"。
4. 文末保留 ci.audit.completed 标记行。

完成判据：文件内每个数字都能对应到本次实际命令输出；无 pre-push 证据残留；无无法复现的声明。汇报每条命令的原始 tail 输出。
```

---

# P1-4 CI 补 DEBUG 泳道 + 全量 GasTests 门禁

```text
在仓库 C:\001_AI\LudotsProd 工作。先读 AGENTS.md 与 gitbook/contributing/ai-assisted-development.md 并遵守。先 git status；从 main HEAD 新建分支 codex/post-merge-closeout-04，不触碰既有未提交改动，不提交到 main。dotnet 命令前先补环境变量（${env:ProgramFiles(x86)} = 'C:\Program Files (x86)'）。

背景（均已实测核实）：
- src/Tests/GasTests/Config/ModRegistrationConflictTests.cs:72/90/108 三处在 Release 下直接 Assert.Pass("Conflict detection only active in DEBUG builds.")，即冲突检测契约在 Release CI 中零覆盖。
- 合并后 CI（.github/workflows/solution-verify.yml）只跑架构守门 + TestCategory=arch-guard 子集，全量 GasTests 无门禁——PR #660 的 Moba 回归因此未暴露。

任务（改 .github/workflows/solution-verify.yml，保持既有步骤不动，新增）：
1. 在 "Run architecture guard tests" 之后新增 DEBUG 冲突检测步骤：
   - name: Run conflict detection tests (Debug-only contracts)
     shell: pwsh
     run: |
       dotnet test src/Tests/GasTests/GasTests.csproj -c Debug --no-build --filter "FullyQualifiedName~Conflict" -v minimal
       if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
   （若 --no-build 在 Debug 配置下无产物，改为先 dotnet build -c Debug 再 test，或去掉 --no-build。）
2. 新增全量 GasTests 门禁步骤（Release）：切片并集跑全量（过滤器：Features / Integration / Presentation / Production / Physics2D / GAS.Effect / GAS.Ability / GAS.Map / GAS.Graph / Vision / Spatial / Association / Config / Terrain / MovePlanOrder + 排除上述 token 的兜底切片），每片失败即 exit 非零。已知环境性零分配失败若 P0-2 已完成容差改造则自然转绿；若 P0-2 未完成，允许在全量步骤中暂时加 --filter 排除 Category=Allocation 并在步骤注释中注明跟踪 issue 号。

验证：
1. 本机以 pwsh 逐条执行新增步骤的命令（DEBUG 过滤器步骤预期 3 个冲突测试真实执行，不再 Assert.Pass；全量切片预期绿——若 P0-2 未完成，零分配类按上述豁免处理并记录）。
2. yml 语法校验（可用 python -c "import yaml,sys;yaml.safe_load(open('.github/workflows/solution-verify.yml',encoding='utf-8'))"）。

完成判据：两个新步骤在本机真实执行过且结果符合预期；yml 合法；汇报 DEBUG 冲突测试的实际运行数（必须 >0 且非 Assert.Pass）。
```

---

# P1-5 补 Collection MemberScratchCapacity 锁定测试

```text
在仓库 C:\001_AI\LudotsProd 工作。先读 AGENTS.md 与 gitbook/contributing/ai-assisted-development.md 并遵守。先 git status；从 main HEAD 新建分支 codex/post-merge-closeout-05，不触碰既有未提交改动，不提交到 main。dotnet 命令前先补环境变量（${env:ProgramFiles(x86)} = 'C:\Program Files (x86)'），dotnet 用 "C:\Program Files\dotnet\dotnet.exe"。

背景：mods/EntityCommandPanelMod/Runtime/CollectionGasEntityCommandPanelSource.cs（约 431 行）已有 fail-fast：
  throw new InvalidOperationException($"ENTITY_COMMAND_PANEL.ERR.MemberScratchCapacity: required={_memberTotal + 1}, capacity={_memberScratch.Length}.");
但 GasTests 中没有任何测试锁定该行为（grep -rn 'MemberScratchCapacity' src/Tests 零命中，已核实）。若未来被改回静默丢弃，无任何测试会报警。

任务：在 src/Tests/GasTests 中为该 fail-fast 补锁定测试（放在 CollectionGasEntityCommandPanelAggregationTests 所在文件/目录，沿用该文件的测试基建与命名风格）：
1. 构造一个成员数超过 _memberScratch 容量的 collection 聚合场景（容量来源读生产代码常量，不要硬编码猜测值），触发 BuildAggregatedSlots(updateActivationMap: true)。
2. 断言：抛出 InvalidOperationException 且消息含 "ENTITY_COMMAND_PANEL.ERR.MemberScratchCapacity"。
3. 配套补一个边界对：成员数恰好等于容量时不抛、且聚合槽视图正确（比照既有"第 16 片提交/第 17 片失败"的成对边界风格）。
禁止恒真测试；断言必须覆盖真实行为。

验证：dotnet test src/Tests/GasTests/GasTests.csproj -c Release --filter "Name~CollectionGasEntityCommandPanel" --nologo -v minimal → 全绿且新测试在列表中真实执行。

完成判据：新增 ≥2 个测试（溢出 throw + 恰满不抛），全部真实执行通过；汇报测试输出与新增行数。
```

---

# P1-6 修复 gas-composition-gate.md 双重编码乱码

```text
在仓库 C:\001_AI\LudotsProd 工作。先读 AGENTS.md 与 gitbook/contributing/ai-assisted-development.md 并遵守。先 git status；从 main HEAD 新建分支 codex/post-merge-closeout-06，不触碰既有未提交改动，不提交到 main。

背景：artifacts/gas-composition-gate.md 编码损坏（已实测）：文件带 UTF-8 BOM，但正文混入双重编码段落——第 1 行标题含 "鈥?"（应为破折号 ——），第 500 行起大段中文呈 "鏂板彉浣撲富瑕佷氦浠樼墿" 形态（UTF-8 字节被当作 GBK 再编码为 UTF-8 的典型特征）。

任务：修复编码，禁止重写内容：
1. 先用 python 做只读诊断：读原始字节，定位所有乱码段（含 鈥 / 鏂 / 涓 等典型双重编码特征的连续区段），输出区段清单（行号范围）。
2. 对乱码段做逆向解码（典型路径：当前文本 encode('gbk', errors='strict') → 得到原始 UTF-8 字节 → decode('utf-8')），逐段还原，不改写任何措辞、不增删内容、不"翻译"。若某段逆向解码失败，保留原样并在清单中标注，禁止编造替代文本。
3. 全文统一为无 BOM 的 UTF-8 + LF（或保持原行尾风格，与仓库其他 artifacts 一致）。
4. 修复后自检：文件不再含 鈥/鏂/涓€ 等特征串；还原段落语义通顺且与上下文一致。

验证：
1. python 脚本扫描全文件输出"零乱码特征"。
2. git diff --stat 仅这一个文件；diff 中除乱码段外无其他行变更。

完成判据：乱码段全部还原或明确标注不可还原清单；无内容编造；汇报诊断区段清单与修复前后对照样例（≥3 段）。
```

---

# P1-7 创建残留跟踪 issue + 更正 completed 口径（人工/CLI 任务，非代码）

```text
这不是代码修改任务，用 gh CLI 或网页执行。以下命令模板供直接使用（先 gh auth status 确认登录 MightyBubble/Ludots 仓库权限）：

1. 创建 PR #660 合并残留跟踪 issue（合并评论承诺过"后续另开 issue/PR 处理"，至今未建）：
gh issue create --repo MightyBubble/Ludots \
  --title "PR #660 post-merge closeout: 残留项跟踪" \
  --body "合并提交 9a6af246cc 的关闭评论承诺残留项另开 issue 处理。残留清单：
- [ ] ci-audit 证据刷新为合并提交后真实全量结果（原为 pre-push 证据）
- [ ] 零分配断言统一改造（硬零 41 处 + 容差 15 处，Assert.That(allocated 口径）
- [ ] CI 补 DEBUG 冲突检测泳道 + 全量 GasTests 门禁
- [ ] Collection MemberScratchCapacity 锁定测试
- [ ] artifacts/gas-composition-gate.md 双重编码乱码修复
- [ ] Production 验收测试行为/证据拆分（37 文件）
- [ ] GasTests 源码扫描 guard 收敛（27 文件）并改运行时行为断言
- [ ] VisualTransform 3 处依赖方向诊断（CommandSourcePointerHitResolver / CommandSourceAcquisitionSystem / GameplayCueSystem）
- [ ] #644-#688 completed 口径更正
详见 docs/audit/pr660-merge-audit-20260726.md。"

2. 在 #644-#688 中曾被"迁移历史≠验收完成"声明关闭的票上补一条统一说明评论（可脚本循环）：
gh issue comment <号> --repo MightyBubble/Ludots --body "口径更正：本票 state_reason=completed 系 SSOT 迁移关闭，不代表验收完成；实际残留跟踪见上述 post-merge closeout issue。"

3. 在 #689 补一条评论链接新跟踪 issue，并把 #689 的 completed 状态在正文中注明"合并已发生但收口动作在跟踪 issue 中继续"。

完成判据：新 issue 创建成功并回填链接到 #689；#644-#688 口径评论批量完成；把新 issue 号记录下来供后续 PR 引用。
```

---

# P2-9 拆分 Production 验收测试的行为与证据生成（37 文件，分批）

```text
在仓库 C:\001_AI\LudotsProd 工作。先读 AGENTS.md 与 gitbook/contributing/ai-assisted-development.md 并遵守。先 git status；从 main HEAD 新建分支 codex/post-merge-closeout-09，不触碰既有未提交改动，不提交到 main。dotnet 命令前先补环境变量（${env:ProgramFiles(x86)} = 'C:\Program Files (x86)'），dotnet 用 "C:\Program Files\dotnet\dotnet.exe"。

背景：src/Tests/GasTests/Production/ 下 37 个 *AcceptanceTests.cs（实测数量），单测试方法 250-418 行，既跑行为断言又写 artifacts/keyframes/截图。环境性失败（磁盘/路径/文件锁）与真行为回归混在一起，团队对红脱敏——PR #660 的回归正是绕过 Production 才漏网的。

任务（分批，每批 ≤5 个文件，改完一批跑绿再下一批）：
对每个 *AcceptanceTests.cs：
1. 保留/提炼纯行为断言测试（实体数、失败计数、确定性、预算等），命名不变或微调，必须断言真实行为。
2. 把"写文件/截图/keyframes/生成 acceptance.md"的代码挪到同文件新增的 [Test, Category("Evidence"), Explicit] 方法（需手动触发，CI 主门禁不跑）。
3. 原方法中删除证据生成代码；行为测试不再触碰磁盘。
4. 每批改完执行：dotnet test src/Tests/GasTests/GasTests.csproj -c Release --no-build --filter "FullyQualifiedName~Production.<该批特征>" --nologo -v minimal → 全绿。

全部批次完成后：
5. 全量 Production 组跑绿：--filter "FullyQualifiedName~Production"。
6. 在 src/Tests/GasTests/Production/README.md（无则新建，简短）记录"行为断言在主门禁、证据生成走 Category=Evidence 手动触发"的约定。

完成判据：37 个文件全部拆完；Production 组主门禁全绿且无一测试再写磁盘；Evidence 方法均可手动触发；汇报每批输出与最终计数。
```

---

# P2-10 收敛源码扫描 guard 到运行时行为断言（27 文件，分批）

```text
在仓库 C:\001_AI\LudotsProd 工作。先读 AGENTS.md 与 gitbook/contributing/ai-assisted-development.md 并遵守。先 git status；从 main HEAD 新建分支 codex/post-merge-closeout-10，不触碰既有未提交改动，不提交到 main。dotnet 命令前先补环境变量（${env:ProgramFiles(x86)} = 'C:\Program Files (x86)'），dotnet 用 "C:\Program Files\dotnet\dotnet.exe"。

背景：GasTests 中 27 个文件（实测 grep -rln 'File.ReadAllText' src/Tests/GasTests --include='*.cs' 计数）含源码禁词扫描（读 .cs 文本查硬编码坐标/字符串）。此类测试脆且可绕过（坐标改写为字符串拼接即失效），PR #660 已删 3 个 Navigation 同类（误伤文档与断言文本）。

任务（分批，每批 ≤5 个文件）：
1. 逐文件判定扫描意图（它想锁的行为契约是什么，例如"巡逻路径来自 JSON 而非硬编码"）。
2. 删除源码扫描测试，替换为运行时行为断言：加载对应 mod/配置，断言行为来自 Registry/数据文件（如巡逻点数 == JSON 定义数、注册表项数 == 配置文件项数）。若某扫描无法转为运行时断言，在 src/Tests/ArchitectureTests/Governance/ 新增统一 guard 并在注释中说明原因，不要保留文本扫描。
3. 每批改完执行：dotnet test src/Tests/GasTests/GasTests.csproj -c Release --no-build --filter "FullyQualifiedName~<该批特征>" --nologo -v minimal → 全绿；dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj -c Release --no-build --nologo -v minimal → 188+ 全绿。

完成判据：GasTests 中 File.ReadAllText 扫描文件清零（grep 复核）；替代断言全部真实执行；ArchitectureTests 全绿；汇报逐文件的"扫描意图 → 替代断言"对照表。
```

---

# D-11 VisualTransform 3 处依赖方向诊断（只诊断，不改码）

```text
在仓库 C:\001_AI\LudotsProd 工作。先读 AGENTS.md 与 gitbook/contributing/ai-assisted-development.md 并遵守。本任务只产出诊断结论，不修改任何代码。

背景：PR #660 已把规划层（OrderWorldSpatialResolver/CompositeOrderPlanner）的 VisualTransform 依赖删净。剩余 3 处读取：
- src/Core/Input/... CommandSourcePointerHitResolver（点选判定读渲染位置）
- src/Core/Input/... CommandSourceAcquisitionSystem（目标获取读渲染位置）
- src/Core/Presentation/... GameplayCueSystem（约 40-46 行，用 VisualTransform.Position 决定特效生成点）
注意：前两者属于"点选/目标获取本就针对屏幕可见实体"的表现层查询，可能合理；第三者是表现系统内部用渲染位置生成特效，也未必违规。真正的架构红线是"逻辑层（Order/规划/模拟）误依赖表现层数据"。

任务：逐处产出诊断：
1. 读三个文件的实际用法，判定依赖方向（谁在读、读出值流向逻辑层还是仅表现层内部消费）。
2. 对 GameplayCueSystem 额外确认：特效生成点是否被任何逻辑层系统回读（grep 引用链）。
3. 输出诊断结论到 output/ 下一个简短 md（或在回复中给出）：每处一行结论（合理保留 / 需重构 + 理由与建议方案）。禁止在证据不足时下"必须删除"的结论。

完成判据：3 处均有基于代码证据的依赖方向结论；无代码改动（git diff 为空）。
```

---

# 记录项（暂不进 Cursor）

- **Core 净增 +11 nullable 警告**（基线 1,547 vs 合并 1,558，多 TFM 逐行日志口径；复现：`dotnet build src/Core/Ludots.Core.csproj -c Release -t:Rebuild -v minimal | grep -cE ': warning CS[0-9]+'`，合并树 ≈3,116 行 ÷2）。注意：**增量编译显示 0 警告是假象，不算证据**。随下一波 nullable 清理统一处理，不单独阻塞。
- **16 个环境性零分配失败**：基线 `5712a4eef4` 同红（同切片基线 39 红、PR 净修 24 个），随 P0-2 的断言统一改造自然消化，不单独修。
