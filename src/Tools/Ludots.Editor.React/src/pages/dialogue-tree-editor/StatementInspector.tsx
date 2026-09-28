import { useState } from 'react';
import { STUDIO_CHROME } from '../authoring-studio/authoringTheme';
import { MarkupPreview, RichTextArea } from '../text-bank/RichTextArea';
import { choiceHandle, type DialogueNode, type DialogueTree, type LinePreview } from './dialogueTreeModel';
import {
  draftKeyForChoice,
  draftKeyForNode,
  type LineDraft,
  type SpeakerDraft,
  type SpeakerRow,
} from './inlineAuthoring';

export type StatementInspectorProps = {
  tree: DialogueTree;
  node: DialogueNode;
  lines: readonly LinePreview[];
  speakers: readonly SpeakerRow[];
  speakerNameOf: (speakerId: string) => string | undefined;
  defaultTextOf: (token: string) => string;
  drafts: Record<string, LineDraft>;
  onDraft: (key: string, patch: LineDraft) => void;
  onClearDraft: (key: string) => void;
  onQuickAddSpeaker: (draft: SpeakerDraft) => string | null;
  onChange: (node: DialogueNode) => void;
  onAddChoice: () => void;
  onRemoveChoice: (choiceId: string) => void;
  canRemove: boolean;
  onRemove: () => void;
};

function SpeakerPicker({
  speakers,
  speakerNameOf,
  value,
  onPick,
  onQuickAdd,
}: {
  speakers: readonly SpeakerRow[];
  speakerNameOf: (speakerId: string) => string | undefined;
  value: string;
  onPick: (speakerId: string) => void;
  onQuickAdd: (draft: SpeakerDraft) => string | null;
}) {
  const [adding, setAdding] = useState(false);
  const [form, setForm] = useState<SpeakerDraft>({ id: '', displayName: '', portraitImageId: '' });
  const [error, setError] = useState('');

  if (adding) {
    return (
      <div className="space-y-2 rounded-md border border-studio-elevated bg-studio-bg p-2">
        <label className={STUDIO_CHROME.label}>
          说话人 ID
          <input
            className={STUDIO_CHROME.field}
            value={form.id}
            placeholder="speaker.scout"
            onChange={(e) => setForm({ ...form, id: e.target.value })}
          />
        </label>
        <label className={STUDIO_CHROME.label}>
          显示名
          <input
            className={STUDIO_CHROME.field}
            value={form.displayName}
            placeholder="斥候"
            onChange={(e) => setForm({ ...form, displayName: e.target.value })}
          />
        </label>
        <label className={STUDIO_CHROME.label}>
          半身像资产 ID（可选）
          <input
            className={STUDIO_CHROME.field}
            value={form.portraitImageId ?? ''}
            placeholder="portrait.speaker.scout"
            onChange={(e) => setForm({ ...form, portraitImageId: e.target.value })}
          />
        </label>
        {error ? <p className="text-[10px] text-studio-red">{error}</p> : null}
        <div className="flex gap-2">
          <button
            type="button"
            className={`flex-1 ${STUDIO_CHROME.btnPrimary}`}
            onClick={() => {
              const problem = onQuickAdd({ ...form, id: form.id.trim(), displayName: form.displayName.trim() });
              if (problem) {
                setError(problem);
                return;
              }
              onPick(form.id.trim());
              setAdding(false);
              setError('');
            }}
          >
            建好并选用
          </button>
          <button type="button" className={`flex-1 ${STUDIO_CHROME.btnGhost}`} onClick={() => setAdding(false)}>
            取消
          </button>
        </div>
        <p className="text-[10px] text-studio-muted">显示名和词条保存时一起落语言表。</p>
      </div>
    );
  }

  return (
    <div>
      <select className={STUDIO_CHROME.field} value={value} onChange={(e) => onPick(e.target.value)}>
        <option value="">选说话人</option>
        {speakers.map((speaker) => (
          <option key={speaker.id} value={speaker.id}>
            {speakerNameOf(speaker.id) ? `${speakerNameOf(speaker.id)}（${speaker.id}）` : speaker.id}
          </option>
        ))}
      </select>
      <button
        type="button"
        className={`mt-1 w-full ${STUDIO_CHROME.btnGhost}`}
        onClick={() => {
          setForm({ id: '', displayName: '', portraitImageId: '' });
          setAdding(true);
        }}
      >
        + 新建说话人
      </button>
    </div>
  );
}

/**
 * One authorable statement (the say line or one choice line). Speaker and text
 * are edited inline; the underlying line/token/locale rows are materialized by
 * the save-time sync planner, never by this UI.
 */
function StatementLineEditor({
  label,
  tree,
  node,
  choiceId,
  lines,
  speakers,
  speakerNameOf,
  defaultTextOf,
  drafts,
  onDraft,
  onQuickAddSpeaker,
  onPatchLineId,
  extraAdvanced,
}: {
  label: string;
  tree: DialogueTree;
  node: DialogueNode;
  choiceId?: string;
  lines: readonly LinePreview[];
  speakers: readonly SpeakerRow[];
  speakerNameOf: (speakerId: string) => string | undefined;
  defaultTextOf: (token: string) => string;
  drafts: Record<string, LineDraft>;
  onDraft: (key: string, patch: LineDraft) => void;
  onQuickAddSpeaker: (draft: SpeakerDraft) => string | null;
  onPatchLineId: (nextLineId: string, draftKey: string) => void;
  extraAdvanced?: React.ReactNode;
}) {
  const boundLineId = choiceId === undefined ? node.lineId : (node.choices ?? []).find((choice) => choice.id === choiceId)?.lineId ?? '';
  const boundLine = lines.find((line) => line.id === boundLineId);
  const draftKey = choiceId === undefined ? draftKeyForNode(tree, node.id) : draftKeyForChoice(tree, node.id, choiceId);
  const effectiveKey = boundLine ? boundLine.id : draftKey;
  const draft = drafts[effectiveKey];
  const speakerValue = draft?.speakerId ?? boundLine?.speakerId ?? '';
  const textValue = draft?.text ?? (boundLine?.textToken ? defaultTextOf(boundLine.textToken) : '');
  const lineIds = lines.map((line) => line.id);

  return (
    <div className="space-y-2">
      <label className={STUDIO_CHROME.label}>
        说话人
        <SpeakerPicker
          speakers={speakers}
          speakerNameOf={speakerNameOf}
          value={speakerValue}
          onPick={(speakerId) => onDraft(effectiveKey, { speakerId })}
          onQuickAdd={onQuickAddSpeaker}
        />
      </label>
      <label className={STUDIO_CHROME.label}>
        {label}
        <RichTextArea
          value={textValue}
          onChange={(text) => onDraft(effectiveKey, { text })}
          rows={4}
          ariaLabel={label}
          placeholder="直接写正文；加粗 / 斜体 / 上色随选区。保存时自动进文本表。"
        />
      </label>
      <MarkupPreview source={textValue} />
      <details className="rounded-md border border-studio-elevated px-2 py-1.5">
        <summary className="cursor-pointer text-xs text-studio-muted">高级（绑定与词条）</summary>
        <div className="mt-2 space-y-2">
          <label className={STUDIO_CHROME.label}>
            台词绑定
            <select
              className={STUDIO_CHROME.field}
              value={boundLineId}
              onChange={(e) => onPatchLineId(e.target.value, effectiveKey)}
            >
              <option value="">（未绑定，保存时按正文新建）</option>
              {(boundLineId && !lineIds.includes(boundLineId) ? [boundLineId, ...lineIds] : lineIds).map((id) => (
                <option key={id} value={id}>
                  {id}
                </option>
              ))}
            </select>
          </label>
          <p className="text-[10px] text-studio-muted">
            词条：{boundLine?.textToken || '（保存时自动派生）'}
            {boundLine ? '' : '　台词：line.* 自动派生'}
          </p>
          {extraAdvanced}
        </div>
      </details>
    </div>
  );
}

export function StatementInspector(props: StatementInspectorProps) {
  const { tree, node, lines, speakers, speakerNameOf, defaultTextOf, drafts, onDraft, onClearDraft, onQuickAddSpeaker, onChange, onAddChoice, onRemoveChoice, canRemove, onRemove } = props;

  const patchLineId = (nextLineId: string, draftKey: string) => {
    if (drafts[draftKey] && nextLineId) {
      onDraft(nextLineId, drafts[draftKey]!);
      onClearDraft(draftKey);
    }
    onChange({ ...node, lineId: nextLineId });
  };

  const patchChoiceLineId = (choiceId: string, nextLineId: string, draftKey: string) => {
    const choices = (node.choices ?? []).map((choice) => {
      if (choice.id !== choiceId) return choice;
      return { ...choice, lineId: nextLineId };
    });
    if (drafts[draftKey] && nextLineId) {
      onDraft(nextLineId, drafts[draftKey]!);
      onClearDraft(draftKey);
    }
    onChange({ ...node, choices });
  };

  return (
    <div className="space-y-3">
      <label className={STUDIO_CHROME.label}>
        节点 ID
        <input className={STUDIO_CHROME.field} value={node.id} readOnly />
      </label>

      <StatementLineEditor
        label="台词"
        tree={tree}
        node={node}
        lines={lines}
        speakers={speakers}
        speakerNameOf={speakerNameOf}
        defaultTextOf={defaultTextOf}
        drafts={drafts}
        onDraft={onDraft}
        onQuickAddSpeaker={onQuickAddSpeaker}
        onPatchLineId={patchLineId}
        extraAdvanced={
          <>
            <label className={STUDIO_CHROME.label}>
              表现配置
              <input
                className={STUDIO_CHROME.field}
                value={node.presentationProfile ?? ''}
                onChange={(e) => onChange({ ...node, presentationProfile: e.target.value })}
              />
            </label>
            <label className={STUDIO_CHROME.label}>
              镜头
              <input
                className={STUDIO_CHROME.field}
                value={node.cameraId ?? ''}
                onChange={(e) => onChange({ ...node, cameraId: e.target.value })}
              />
            </label>
            <label className={STUDIO_CHROME.label}>
              进句动作图
              <input
                className={STUDIO_CHROME.field}
                value={node.onEnterActionGraphId ?? ''}
                onChange={(e) => onChange({ ...node, onEnterActionGraphId: e.target.value })}
              />
            </label>
            <label className={STUDIO_CHROME.label}>
              自动接下句（秒，0 表示等玩家）
              <input
                className={STUDIO_CHROME.field}
                type="number"
                min={0}
                step={0.1}
                value={node.autoAdvanceSeconds ?? 0}
                onChange={(e) => onChange({ ...node, autoAdvanceSeconds: Number(e.target.value) || 0 })}
              />
            </label>
          </>
        }
      />

      <div className="flex items-center justify-between">
        <div className="text-xs text-studio-muted">选项（黄头节点下口）</div>
        <button type="button" className={STUDIO_CHROME.btnGhost} onClick={onAddChoice}>
          + 加选项
        </button>
      </div>
      {(node.choices ?? []).map((choice) => (
        <div key={choice.id} className="space-y-2 rounded-md border border-studio-elevated p-2">
          <label className={STUDIO_CHROME.label}>
            选项 ID
            <input
              className={STUDIO_CHROME.field}
              value={choice.id}
              onChange={(e) => {
                const choices = (node.choices ?? []).slice();
                const index = choices.findIndex((row) => row.id === choice.id);
                if (index >= 0) choices[index] = { ...choice, id: e.target.value };
                onChange({ ...node, choices });
              }}
            />
          </label>
          <StatementLineEditor
            label="选项正文"
            tree={tree}
            node={node}
            choiceId={choice.id}
            lines={lines}
            speakers={speakers}
            speakerNameOf={speakerNameOf}
            defaultTextOf={defaultTextOf}
            drafts={drafts}
            onDraft={onDraft}
            onQuickAddSpeaker={onQuickAddSpeaker}
            onPatchLineId={(nextLineId, draftKey) => patchChoiceLineId(choice.id, nextLineId, draftKey)}
            extraAdvanced={
              <>
                <label className={STUDIO_CHROME.label}>
                  条件图
                  <input
                    className={STUDIO_CHROME.field}
                    value={choice.conditionGraphId ?? ''}
                    onChange={(e) => {
                      const choices = (node.choices ?? []).map((row) =>
                        row.id === choice.id ? { ...row, conditionGraphId: e.target.value } : row,
                      );
                      onChange({ ...node, choices });
                    }}
                  />
                </label>
                <label className={STUDIO_CHROME.label}>
                  动作图
                  <input
                    className={STUDIO_CHROME.field}
                    value={choice.actionGraphId ?? ''}
                    onChange={(e) => {
                      const choices = (node.choices ?? []).map((row) =>
                        row.id === choice.id ? { ...row, actionGraphId: e.target.value } : row,
                      );
                      onChange({ ...node, choices });
                    }}
                  />
                </label>
              </>
            }
          />
          <p className="text-[10px] text-studio-muted">下一句从选项节点「{choiceHandle(choice.id)}」口往下拉。</p>
          <button type="button" className={STUDIO_CHROME.btnDanger} onClick={() => onRemoveChoice(choice.id)}>
            删除此选项
          </button>
        </div>
      ))}
      <button type="button" className={STUDIO_CHROME.btnDanger} disabled={!canRemove} onClick={onRemove}>
        删除此句
      </button>
    </div>
  );
}
