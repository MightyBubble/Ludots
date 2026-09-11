#!/usr/bin/env bash
# 可视化编辑器入口 = 作者工作室（蓝图 / 行为树 / 状态机 / 对话 / 时间轴）
exec "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/run-authoring-studio.sh" "$@"
