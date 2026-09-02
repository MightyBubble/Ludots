import type { LiveDebugEntryStory } from '../liveDebugStory';

/** Night-raid Flow entry stories for Live Debug dock (player-facing beats). */
export const nightRaidFlowStories: {
  graphId: string;
  entries: Record<string, LiveDebugEntryStory>;
} = {
  graphId: 'Graph.NightRaid.Flow',
  entries: {
    on_raider_died: {
      title: '杀敌刷 Boss',
      summary: '敌人倒下 → 击杀数 +1 → 够门槛就刷 Boss',
      beats: [
        {
          id: 'count',
          nodes: ['rd_scope', 'rd_read', 'rd_one', 'rd_add', 'rd_write'],
          text: '有人倒下了，击杀数记上一笔',
        },
        {
          id: 'check',
          nodes: ['rd_read2', 'rd_thresh', 'rd_lt', 'rd_wait'],
          text: '对照击杀门槛，看够不够开门',
        },
        {
          id: 'boss',
          nodes: [
            'stage_boss',
            'store_stage_boss',
            'invoke_write_stage_boss',
            'boss_scope',
            'boss_x',
            'boss_y',
            'spawn_boss',
            'boss_alert_panel',
            'boss_alert_show',
            'rd_done_const',
            'rd_done',
          ],
          text: '够数了：营地刷出 Boss，并弹出提醒',
        },
        {
          id: 'wait',
          nodes: ['rd_halt_const', 'rd_halt'],
          text: '还没够数，先收工等下一刀',
        },
      ],
    },
    on_elite_raider_died: {
      title: '杀敌刷 Boss',
      summary: '精锐倒下同样计入击杀；够门槛就刷 Boss',
      beats: [
        {
          id: 'count',
          nodes: ['rd_scope', 'rd_read', 'rd_one', 'rd_add', 'rd_write'],
          text: '精锐倒下了，击杀数记上一笔',
        },
        {
          id: 'check',
          nodes: ['rd_read2', 'rd_thresh', 'rd_lt', 'rd_wait'],
          text: '对照击杀门槛，看够不够开门',
        },
        {
          id: 'boss',
          nodes: [
            'stage_boss',
            'store_stage_boss',
            'invoke_write_stage_boss',
            'boss_scope',
            'boss_x',
            'boss_y',
            'spawn_boss',
            'boss_alert_panel',
            'boss_alert_show',
            'rd_done_const',
            'rd_done',
          ],
          text: '够数了：营地刷出 Boss，并弹出提醒',
        },
        {
          id: 'wait',
          nodes: ['rd_halt_const', 'rd_halt'],
          text: '还没够数，先收工等下一刀',
        },
      ],
    },
    on_wave1_cleared: {
      title: '第一波清完',
      summary: '场上第一波敌人清零 → 刷出精锐行',
      beats: [
        {
          id: 'stage',
          nodes: ['wave1_clear_scope', 'stage_three', 'store_stage_three', 'invoke_write_stage_three'],
          text: '第一波清空，推进阶段',
        },
        {
          id: 'spawn',
          nodes: [
            'wave2_scope',
            'w2x1',
            'w2y1',
            'w2x2',
            'w2y2',
            'store_w2x1',
            'store_w2y1',
            'store_w2x2',
            'store_w2y2',
            'invoke_spawn_elite',
            'wave2_spawn_ok',
            'wave2_spawn_done',
          ],
          text: '精锐行进场',
        },
      ],
    },
    on_boss_died: {
      title: 'Boss 倒下',
      summary: 'Boss 死亡 → 两拍停顿 → 胜利面板',
      beats: [
        {
          id: 'stage',
          nodes: ['hero_scope', 'stage_four', 'store_stage_four', 'invoke_write_stage_four'],
          text: 'Boss 倒下，先记一笔阶段',
        },
        {
          id: 'wait',
          nodes: ['wait_beat_a', 'wait_beat_b'],
          text: '留两拍呼吸',
        },
        {
          id: 'victory',
          nodes: [
            'stage_five',
            'store_stage_five',
            'invoke_write_stage_five',
            'victory_panel',
            'victory_show',
            'bd_ok',
            'bd_done',
          ],
          text: '胜利面板亮起',
        },
      ],
    },
  },
};
