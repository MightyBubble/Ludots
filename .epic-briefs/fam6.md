# 家族方案 ⑥ 算术与比较（13 op）

> 核心命题：**让算式本身成为世界内的东西**。全家族统一改**真结算**——图尾巴 `featured→flip(NegFloat 翻号)→hit(ModifyAttributeAdd, Health)`（op 组合、不加糖节点），driver 新模式 `applyTo:"graphSettled"`（每拍先复位→执行→回读，不再直写血条），9 处"不是结算出来的伤"免责全删。
> 诚实结论：LoadTargetPosX 输出 int cm 且词汇表无 IntToFloat——DivFloat"真距离"组不出来，改用真实木桩数分摊（见下）。

### P4 算式台统一设计（一张设计 13 op 共用）
- 锚在施法者(-2,0)与木桩(4,0)之间、轨道中心 (1.0,1.6)、轨长 4.8 单位；**轨道总长=100 且与血条同标尺**（每 10 点一根白刻度线）——台上块长=血条将掉的量，同一把尺读两处。
- 构件：输入段（白框黄芯 Box）、运算徽标（Cyan 圆环+符号）、结果段（红芯）、败者/上拍值（灰残影 P7）、墙/阈值线/零轴（粗白竖线）、流动脉冲。
- 节拍（0.35s/拍=21 帧）：f0-3 输入点亮→f4-9 脉冲流至徽标→f10-14 本 op 专属变换（对接/拉伸/切分/镜像/对折/撞墙/挑选/重掷）→f15-20 结果段沿仇恨线飞出、命中白圈、定格。
- 实现为共享渲染器（`ArithmeticConsolePresenter`），13 op 只是 13 份布局描述——一份代码全家复用。

### MaxFloat｜B(P4+P7+P6)｜S
- 零字幕画面：两块候选伤害段 12 与 28 并排（同血条刻度），"挑大"双箭头指向长块；28 转红飞出扣血，12 降灰残影。
- 真结算尾巴接好后 -28，100→72。文案：title「两刀里挑大的一刀」beat「两块刀伤 12 和 28 摆上台面，挑中的是更长的那块，打出去按它的长度掉血。」detail「更长的一刀是 {result}；木桩血条从 {healthBefore} 掉到 {healthAfter}。」

### MinFloat｜B(P4+P7)｜S
- 镜像：箭头指向短块 18；30 降灰，18 飞出。文案：title「两刀里挑小的一刀」beat「两块刀伤 30 和 18 摆上台面，挑中的是更短的那块，打出去按它的长度掉血。」detail「更短的一刀是 {result}；木桩血条从 {healthBefore} 掉到 {healthAfter}。」

### AddFloat｜B(P4)｜S
- 零字幕画面：30 段居左、12 段沿轨道右滑**端对端对接**（接缝细刻痕）合成 42 红段飞出。
- wiki 重生成修漂移。文案：title「两段伤害叠成一刀」beat「30 的一段先摆上，12 的一段接在尾巴上，接成的一整段有多长，木桩就掉多少血。」detail「接起来的一刀是 {result}；木桩血条从 {healthBefore} 掉到 {healthAfter}。」

### MulFloat｜B(P4+P7)｜S
- 零字幕画面：20 长黄块旁立"×1.5"徽标（一整格+半格刻度）；变换拍拉伸 1.5 倍，原长留灰虚影，30 红段飞出。
- 文案：title「伤害拉长一半」beat「20 的伤害段被拉长一半，原样留着影子，拉成多长就掉多少血。」detail「拉长后的一刀是 {result}；木桩血条从 {healthBefore} 掉到 {healthAfter}。」

### ClampFloat｜B(P4)｜S
- 零字幕画面：轨道 10 与 40 处立两面粗白**墙**；90 长黄块从右沿轨左推，撞上 40 的墙"哐"停住（墙闪白、块压缩一帧弹回贴墙）；40 红段飞出。钳制=撞墙停止。
- 文案：title「撞到上限就停」beat「90 的伤害段沿轨道左移，撞上 40 的墙就停住，打出去的是停下来的那一段。」detail「撞墙停下的一刀是 {result}；木桩血条从 {healthBefore} 掉到 {healthAfter}。」

### ConstFloat｜B(P4 铭牌变体+P2+P6)｜S
- 零字幕画面：**没有输入表盘**，只有一块厚边框石匾刻着 42 长凹槽段；每拍红段从匾中原样拓出、长度分毫不差；台侧一排等长刻记逐拍点亮——全部等长=永不变化。
- 链路：弃 `targetHealthSet`，改真结算尾巴按 -42 加算（100→58），"写死"由铭牌+等长刻记承担。文案：title「刻死的一刀」beat「台上没有表盘，只有一块刻好长度的铭牌；每一刀都和铭牌一样长。」detail「铭牌上刻死的一刀是 {result}；木桩血条从 {healthBefore} 掉到 {healthAfter}。」

### NegFloat｜B(P4 零轴+P1)｜S
- 零字幕画面：带中心零轴的水平轨；-8 灰"欠条"块贴零轴左侧，变换拍**沿零轴滑过翻到右侧** +8 转红（镜像翻转，箭头指示翻越方向），飞出扣 8。
- 链路（op 组合活示范）：featured NegFloat(-8→+8) 后再接一个 NegFloat 把 +8 翻成 -8 入 hit——一个图两次取负。100→92，poster 稳定首拍。文案：title「负债翻面成正数」beat「负 8 的欠条摆在零轴左边，沿零轴翻到右边变成正 8，翻过来的就是打出去的一刀。」detail「翻面后的一刀是 {result}；木桩血条从 {healthBefore} 掉到 {healthAfter}。」

### AbsFloat｜B(P4 对折)｜S
- 零字幕画面：与 NegFloat 共用零轴轨但动画可区分——**沿零轴对折**（左半轨像纸一样翻折过来，零轴是折痕；Neg 是滑过镜像），-8 灰块平贴到右侧 +8 转红。
- 文案：title「对折零轴取长度」beat「负 8 的修正段沿零轴对折，折过来的长度是多少就打多少。」detail「对折后的长度是 {result}；木桩血条从 {healthBefore} 掉到 {healthAfter}。」

### SubFloat｜B(P4+P10)｜S
- 零字幕画面：虚构"走远"改为**看得见的格挡**——50 长黄块送向木桩，木桩侧 12 长灰格挡块迎头**咬掉前端**（12 单位转灰留台上），38 红段继续飞进血条。
- 可选增强（后续迭代）：格挡 12 改 LoadAttribute(Shield) 真实护甲。文案：title「格挡先咬掉一截」beat「50 的伤害段送到木桩前，格挡块先咬掉头上的 12，剩下的才进血条。」detail「咬掉一截后剩下 {result}；木桩血条从 {healthBefore} 掉到 {healthAfter}。」

### DivFloat｜B(P4 切分+P8)｜M
- 现状：除数是写死常量，"距离翻倍摊一半"虚构。
- 零字幕画面：**除数改为真实的木桩数**——场上并排两根木桩；40 长黄块从正中**切开**分成两根 20 红段，各沿自己的线飞向左右木桩，两根血条同时 100→80。"÷2"的 2 就是观众数得出来的木桩数。
- 链路：graph 追加 explicitA→flipA→hitA(-20) 与 contextB(LoadContextTargetContext)→flipB→hitB(-20) 两条尾巴；vignette 加第二木桩（role:"context"），地图重生成。文案：title「一刀摊给两根木桩」beat「40 的伤害段从中间切开，两根木桩各接一半。」detail「摊开之后每一根木桩挨 {result}；两根血条都从 {healthBefore} 掉到 {healthAfter}。」

### RandomFloat01｜B(P4+P2+P6)｜M
- 现状：applyTo=none 从不掉血，beat 却承诺"每次掉的不同"。
- 零字幕画面：台上**骰子徽标**每拍重掷闪烁；结果表盘长度每拍重生成（0-30 标尺内），上拍长度留灰虚影；红段照常真扣血；台右一列**掷点史**叠放最近 6 拍长度条，长短错落——"每刀不一样"由不等长历史条作证。
- 链路（选真应用）：graph 追加 scale(ConstFloat 30)→mul→flip→hit，featured 仍指 RandomFloat01（{result}=0~1 原始掷值，血掉 r×30）；种子按 Wave 确定性可复现。文案：title「骰子决定这一刀」beat「每一拍重掷一次骰子，掷出多长这一刀就多长，一列掷点史里没有两根一样长。」detail「这一拍掷出 {result}；木桩血条从 {healthBefore} 掉到 {healthAfter}。」

### ConstBool｜B(P2 门闸+P6)｜M
- 零字幕画面：施法者与木桩间立**门闸**：许可为真时闸门敞开、头顶绿许可徽章、每拍挥刀穿门真扣 8；台上一排许可刻记逐拍全绿——一排全绿无一红点，就是"恒真"的全部画面证据。（恒真常量造不出"关"的对照拍，文案诚实说"每一拍都放行"。）
- 链路：graph 追加 JumpIfFalse(condition←permit)→真分支 explicit→hit(-8)；**风险点**：JumpIfFalse 在 Effect 图的线性编译路径需先验证（descriptor 掩码允许，现有 showcase 用 Script 图）；受阻则降 C 档图解页。文案：title「永远放行的许可」beat「门闩每一拍都开着，亮一个绿点放一刀，一排刻记里从来没有红点。」detail「这一拍的许可：{result}；放行的刀落下，木桩血条从 {healthBefore} 掉到 {healthAfter}。」

### CompareGtFloat｜B(P11+P9)｜L
- 零字幕画面：**砍不砍得死对照拍**——两根木桩（血厚 100 / 血薄 30），各血条上刻 50 白色阈值刻线；同一根 50 长伤害段同时比向两根：厚的刻线悬在中段、段长够不着底→不成立，刀落不下；薄的整条血量短于伤害段→成立，红段飞出直接清空（30→0 一刀没）。同一段伤害两种结局。
- 链路：graph 改双路 `explicitA→LoadAttribute(Health)→strike(50)→cmpA→jifA…hitA`、`contextB→LoadAttribute→cmpB→jifB…hitB`（featured 指 cmpA）；Effect 图 JumpIfFalse 路径同上验证。文案：title「砍不砍得死，比一下」beat「同样长的一刀，血条比它长的木桩挨不动，血条比它短的木桩一刀就没。」detail「对血薄木桩的判定：{result}；它从 {healthBefore} 掉到 {healthAfter}。」

## 家族小结
- 一次性基建 M：算式台渲染器 + `graphSettled` 模式 + poster 帧位修复 + 文案/测试/生成脚本同步（测试硬编码了 title/detail 与扣血断言，`showcase.registry.json`/wiki 靠生成器重生成；DivFloat/CompareGtFloat 加 actor 后重生成地图）。
- Poster 抓帧：根因 `record-graph-op-node-galleries.py:160` 永远 `pngs[-1]`；改拷第 2 张 still（frame 32，首拍结算已落+发射动画走完），加 `--poster-frame {first-settlement|last}` 参数——配合每拍复位，抓帧从碰运气变确定性。
- 统计：S×9（Max/Min/Add/Mul/Clamp/Const/Neg/Abs/Sub）、M×3（Div/ConstBool/Random）、L×1（CompareGtFloat，若 JumpIfFalse Effect 路径受阻降 M 级 C 档图解页）。

