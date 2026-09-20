#!/usr/bin/env bash
# 从 docs/audits/gas_graph_architecture_fix_plan.md 批量创建 Epic 与子 issue。
#
# 用法：
#   scripts/create-gas-graph-fix-issues.sh --dry-run     # 只打印将要创建什么（默认）
#   scripts/create-gas-graph-fix-issues.sh --apply       # 真的创建
#
# 前置：gh 已登录且有 issue 写权限（gh auth status 需显示 repo scope）。
# 每个子 issue 的正文从修复计划文档里按小节切出，因此文档是唯一内容源；
# 改任务书请改文档，不要改本脚本里的文案。

set -euo pipefail

REPO="${REPO:-MightyBubble/Ludots}"
PLAN="docs/audits/gas_graph_architecture_fix_plan.md"
REVIEW="docs/audits/gas_graph_architecture_review.md"
MODE="dry-run"

for arg in "$@"; do
  case "$arg" in
    --apply)    MODE="apply" ;;
    --dry-run)  MODE="dry-run" ;;
    *) echo "未知参数: $arg" >&2; exit 2 ;;
  esac
done

cd "$(dirname "$0")/.."
[[ -f "$PLAN" ]]   || { echo "找不到修复计划: $PLAN" >&2; exit 1; }
[[ -f "$REVIEW" ]] || { echo "找不到审查结论: $REVIEW" >&2; exit 1; }

# id|优先级|依赖|标题
SUBTASKS=$(cat <<'EOF'
S1|P0|-|图调用无界递归会杀进程：装载期拒环 + 深度上限 + 共享预算
S2|P0|-|属性写入的权威与强制手段：语义唯一 + 围栏 + id 约定统一
S3|P0|-|查询方言可从纯管道调起可挂起动作（合同 §3.4 红线）
S4|P1|-|事务收尾：回滚必须不可失败 + 销毁语义 + 边界对齐结算
S5|P1|-|容量三禁：静默丢弃 / 热路径扩容 / 死信号接线
S6|P1|-|退役展厅锁门 + 停止在 mod 运行时清空图编号表
S7|P1|-|展厅血条名实相符 + 删掉静默回卷
S8|P2|-|假防线集中整治：测了不断言 / 恒真断言 / 命名与门槛不符
S9|P2|S1|L2 宿主走 L1 正式执行前门（引入执行帧）
S10|P2|-|表现层不得决定玩法选中
S11|P2|-|覆盖表错误归因 + 守卫强度
S12|P2|S9|寄存器归属与指令 descriptor
S13|P2|S9,S12|Script 方言拓宽 + L2 数据作者面
S14|P3|-|分层物理化（拆程序集）— 需先出设计
S15|chore|-|验收页 SSOT 合并（docs-governance 红灯部分已随计划 PR 修掉）
EOF
)

# 从计划文档里切出某个子任务小节的正文（### Sxx · ... 到下一个 ### 或 ---\n## 之前）
extract_section() {
  local id="$1"
  awk -v id="$id" '
    $0 ~ "^### " id " · " { capture = 1 }
    capture && /^### / && $0 !~ "^### " id " · " { exit }
    capture && /^## / { exit }
    capture { print }
  ' "$PLAN"
}

echo "仓库      : $REPO"
echo "模式      : $MODE"
echo "内容源    : $PLAN"
echo "子任务数  : $(echo "$SUBTASKS" | wc -l | tr -d ' ')"
echo

if [[ "$MODE" == "apply" ]]; then
  if ! gh auth status >/dev/null 2>&1; then
    echo "gh 未登录或凭据无效。先跑 gh auth login，确认有 repo scope。" >&2
    exit 1
  fi
  if ! gh issue list --repo "$REPO" --limit 1 >/dev/null 2>&1; then
    echo "gh 无法读取 $REPO 的 issue（凭据可能是只读的）。创建会失败，已中止。" >&2
    exit 1
  fi
fi

# ---------- Epic ----------
EPIC_BODY=$(cat <<EOF
GAS 与图 VM 全栈架构审查的修复收口。审查结论见 \`$REVIEW\`，任务分配见 \`$PLAN\`。

## 结论摘要

骨架比收尾好得多。图 VM 执行核心、编译器前端、组件布局、零分配纪律都是认真做的，
**本 Epic 不包含任何重写**，全部是收口。

三条 P0 是「今天就能出事」，且**已实测证实**：

1. 一张自己调自己的图，登记全绿，执行时递归 1495 层栈溢出，**进程被杀**（退出码 134），
   \`try/catch\` 无法拦截。
2. 属性数值的权威随运行时状态翻转：同一个正式接口写入，在夹上限的属性上存活、
   在不夹上限且有活跃修正的属性上**被静默丢弃**，且覆盖时机与写入完全脱钩。
3. 查询方言允许直绑图号，作者可从 L1 纯管道调起可挂起动作，违反合同 §3.4，零测试覆盖。

## 开工顺序

S1 / S2 / S3 可以同时开三个 Agent，互不冲突。
唯一的硬顺序是 S13 必须在 S9 之后（今天三个 L2 宿主只填部分寄存器就能跑，
正是因为 Script 方言窄到用不到那些寄存器；反了必崩）。

\`\`\`text
S1 ──► S9 ──► S12 ──► S13
        │      │
        └──────┴────► S14（建议，非硬依赖）

S2  S3  S4  S5  S6  S7  S8  S10  S11  S15   ← 全部可独立并行开工
\`\`\`

## 子任务

<!-- SUBTASK_LIST -->

## 关单条件

见 \`$PLAN\` §6 的 Cucumber 验收：三条 P0 全部关闭、失败路径本身可靠、
容量与遥测不再说谎、防线是真的、玩家门名实相符。

## 修复时请勿顺手改坏

审查确认以下是**对的**，不在修复范围内：VM 的类型初始化完备性硬闸、
执行状态的 ref struct + Span 寄存器设计、编译器前端的四类检查、
\`AddFixed<T>\` 容量模式、派生属性写入围栏、缺 DirtyFlags 的失败关闭设计、
技能→效果→图 的类型级单向性、知识披露与头顶条那一侧。
EOF
)

EPIC_TITLE="Epic: GAS + Graph VM 架构收口（审查后修复）"

if [[ "$MODE" == "dry-run" ]]; then
  echo "──────── 将创建 Epic ────────"
  echo "标题: $EPIC_TITLE"
  echo "正文: $(echo "$EPIC_BODY" | wc -l | tr -d ' ') 行"
  echo
  echo "──────── 将创建子 issue ────────"
  while IFS='|' read -r id prio dep title; do
    [[ -z "$id" ]] && continue
    body_lines=$(extract_section "$id" | wc -l | tr -d ' ')
    printf '  %-4s [%-5s] deps=%-8s %s  (正文 %s 行)\n' "$id" "$prio" "$dep" "$title" "$body_lines"
    if [[ "$body_lines" -lt 10 ]]; then
      echo "        !! 正文切取异常，检查 $PLAN 里 '### $id · ' 小节标题格式" >&2
    fi
  done <<< "$SUBTASKS"
  echo
  echo "以上为预演。加 --apply 真的创建。"
  exit 0
fi

# ---------- 真的创建 ----------
declare -a CREATED_URLS=()
declare -a CREATED_IDS=()

while IFS='|' read -r id prio dep title; do
  [[ -z "$id" ]] && continue
  section=$(extract_section "$id")
  if [[ $(echo "$section" | wc -l) -lt 10 ]]; then
    echo "跳过 $id：正文切取异常" >&2
    continue
  fi

  dep_line=""
  [[ "$dep" != "-" ]] && dep_line="**依赖：** ${dep}（必须在它们之后开工）"$'\n\n'

  body="**优先级：** ${prio}"$'\n'"${dep_line}"$'\n'"审查依据：\`${REVIEW}\`　任务书出处：\`${PLAN}\` § ${id}"$'\n\n---\n\n'"${section}"

  url=$(gh issue create --repo "$REPO" --title "[${prio}][${id}] ${title}" --body "$body")
  echo "已创建 $id: $url"
  CREATED_URLS+=("$url")
  CREATED_IDS+=("$id")
done <<< "$SUBTASKS"

# Epic 正文里填入子任务清单（GitHub 的 task list 会自动渲染成进度条）
subtask_md=""
i=0
while IFS='|' read -r id prio dep title; do
  [[ -z "$id" ]] && continue
  url="${CREATED_URLS[$i]:-}"
  [[ -z "$url" ]] && { i=$((i+1)); continue; }
  dep_note=""
  [[ "$dep" != "-" ]] && dep_note=" _(依赖 ${dep})_"
  subtask_md+="- [ ] ${url} \`${prio}\` ${title}${dep_note}"$'\n'
  i=$((i+1))
done <<< "$SUBTASKS"

epic_body_final="${EPIC_BODY//<!-- SUBTASK_LIST -->/$subtask_md}"
epic_url=$(gh issue create --repo "$REPO" --title "$EPIC_TITLE" --body "$epic_body_final")
echo
echo "已创建 Epic: $epic_url"

# 尝试建立原生 sub-issue 层级（GitHub 的 addSubIssue）。
# 失败不影响结果 —— Epic 正文里的 task list 已经把关系表达清楚了，这里只是额外加成。
epic_num="${epic_url##*/}"
epic_node=$(gh api "repos/$REPO/issues/$epic_num" --jq .node_id 2>/dev/null || echo "")
if [[ -n "$epic_node" ]]; then
  linked=0
  for url in "${CREATED_URLS[@]}"; do
    num="${url##*/}"
    child_node=$(gh api "repos/$REPO/issues/$num" --jq .node_id 2>/dev/null || echo "")
    [[ -z "$child_node" ]] && continue
    if gh api graphql -f query='
      mutation($parent:ID!, $child:ID!) {
        addSubIssue(input:{issueId:$parent, subIssueId:$child}) { issue { number } }
      }' -f parent="$epic_node" -f child="$child_node" >/dev/null 2>&1; then
      linked=$((linked+1))
    fi
  done
  echo "原生 sub-issue 关联成功 $linked / ${#CREATED_URLS[@]} 条（未成功的仍在 Epic 的清单里）"
else
  echo "未能取到 Epic 的 node id，跳过原生 sub-issue 关联（Epic 清单不受影响）"
fi
