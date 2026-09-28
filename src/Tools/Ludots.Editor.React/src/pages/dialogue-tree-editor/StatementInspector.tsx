import { useState } from 'react';
import { MarkupPreview, RichTextArea } from '../text-bank/RichTextArea';
import { Button } from '@/components/ui/Button';
import { Collapse } from '@/components/ui/Collapse';
import { labelClass } from '@/components/ui/chrome';
import { fieldControlClass } from '@/components/ui/Field';
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
  portraitAssetIds: readonly string[];
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
        <label className={labelClass}>
          说话人 ID
          <input
            className={fieldControlClass}
            value={form.id}
            placeholder="speaker.scout"
            onChange={(e) => setForm({ ...form, id: e.target.value })}
          />
        </label>
        <label className={labelClass}>
          显示名
          <input
            className={fieldControlClass}
            value={form.displayName}
            placeholder="斥候"
            onChange={(e) => setForm({ ...form, displayName: e.target.value })}
          />
        </label>
        <label className={labelClass}>
          半身像资产 ID（可选）
          <input
            className={fieldControlClass}
            value={form.portraitImageId ?? ''}
            placeholder="portrait.speaker.scout"
            onChange={(e) => setForm({ ...form, portraitImageId: e.target.value })}
          />
        </label>
        {error ? <p className="text-[10px] text-studio-red">{error}</p> : null}
        <div className="flex gap-2">
          <Button
            variant="primary"
            className="flex-1"
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
          </Button>
          <Button variant="ghost" className="flex-1" onClick={() => setAdding(false)}>
            取消
          </Button>
        </div>
        <p className="text-[10px] text-studio-muted">显示名和词条保存时一起落语言表。</p>
      </div>
    );
  }

  return (
    <div>
      <select className={fieldControlClass} value={value} onChange={(e) => onPick(e.target.value)}>
        <option value="">选说话人</option>
        {speakers.map((speaker) => (
          <option key={speaker.id} value={speaker.id}>
            {speakerNameOf(speaker.id) ? `${speakerNameOf(speaker.id)}（${speaker.id}）` : speaker.id}
          </option>
        ))}
      </select>
      <Button
        variant="ghost"
        className="mt-1 w-full"
        onClick={() => {
          setForm({ id: '', displayName: '', portraitImageId: '' });
          setAdding(true);
        }}
      >
        + 新建说话人
      </Button>
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
      <label className={labelClass}>
        说话人
        <SpeakerPicker
          speakers={speakers}
          speakerNameOf={speakerNameOf}
          value={speakerValue}
          onPick={(speakerId) => onDraft(effectiveKey, { speakerId })}
          onQuickAdd={onQuickAddSpeaker}
        />
      </label>
      <label className={labelClass}>
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
      <Collapse summary="高级（绑定与词条）">
        <div>
          <label className={labelClass}>
            台词绑定
            <select
              className={fieldControlClass}
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
      </Collapse>
    </div>
  );
}

export function StatementInspector(props: StatementInspectorProps) {
  const { tree, node, lines, speakers, speakerNameOf, defaultTextOf, portraitAssetIds, drafts, onDraft, onClearDraft, onQuickAddSpeaker, onChange, onAddChoice, onRemoveChoice, canRemove, onRemove } = props;

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
      <label className={labelClass}>
        节点 ID
        <input className={fieldControlClass} value={node.id} readOnly />
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
            <label className={labelClass}>
              立绘差分
              <select
                className={fieldControlClass}
                value={node.portraitImageId ?? ''}
                onChange={(e) =>
                  onChange(e.target.value ? { ...node, portraitImageId: e.target.value } : { ...node, portraitImageId: undefined })
                }
              >
                <option value="">跟随说话人</option>
                {portraitAssetIds.map((id) => (
                  <option key={id} value={id}>
                    {id}
                  </option>
                ))}
                {node.portraitImageId && !portraitAssetIds.includes(node.portraitImageId) ? (
                  <option value={node.portraitImageId}>{node.portraitImageId}</option>
                ) : null}
              </select>
            </label>
            <label className={labelClass}>
              表现配置
              <input
                className={fieldControlClass}
                value={node.presentationProfile ?? ''}
                onChange={(e) => onChange({ ...node, presentationProfile: e.target.value })}
              />
            </label>
            <label className={labelClass}>
              镜头
              <input
                className={fieldControlClass}
                value={node.cameraId ?? ''}
                onChange={(e) => onChange({ ...node, cameraId: e.target.value })}
              />
            </label>
            <label className={labelClass}>
              进句动作图
              <input
                className={fieldControlClass}
                value={node.onEnterActionGraphId ?? ''}
                onChange={(e) => onChange({ ...node, onEnterActionGraphId: e.target.value })}
              />
            </label>
            <label className={labelClass}>
              自动接下句（秒，0 表示等玩家）
              <input
                className={fieldControlClass}
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
        <Button variant="ghost" onClick={onAddChoice}>
          + 加选项
        </Button>
      </div>
      {(node.choices ?? []).map((choice) => (
        <div key={choice.id} className="space-y-2 rounded-md border border-studio-elevated p-2">
          <label className={labelClass}>
            选项 ID
            <input
              className={fieldControlClass}
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
                <label className={labelClass}>
                  条件图
                  <input
                    className={fieldControlClass}
                    value={choice.conditionGraphId ?? ''}
                    onChange={(e) => {
                      const choices = (node.choices ?? []).map((row) =>
                        row.id === choice.id ? { ...row, conditionGraphId: e.target.value } : row,
                      );
                      onChange({ ...node, choices });
                    }}
                  />
                </label>
                <label className={labelClass}>
                  动作图
                  <input
                    className={fieldControlClass}
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
          <Button variant="danger" onClick={() => onRemoveChoice(choice.id)}>
            删除此选项
          </Button>
        </div>
      ))}
      <Button variant="danger" disabled={!canRemove} onClick={onRemove}>
        删除此句
      </Button>
    </div>
  );
}
