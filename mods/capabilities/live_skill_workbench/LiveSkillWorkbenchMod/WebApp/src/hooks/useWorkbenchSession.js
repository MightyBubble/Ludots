import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  createLudotsDataPlaneClient,
  ensureLudotsDataPlaneTransport
} from '@ludots/dataplane-client';
import { LSW_COMMANDS, LSW_TOPIC, resolveWorkbenchBootMode } from '../dataplane/lswClient.js';
import { applyPreviewStageEdit, createFireballPreviewSnapshot } from '../preview/fireballFixture.js';
import { buildDraftValues, collectDirtyEdits } from './descriptorForm.js';

const EMPTY_SNAPSHOT = {
  ready: false,
  preview: false,
  connectionState: 'disconnected',
  modName: 'LiveSkillWorkbenchMod',
  sessionId: '',
  revision: 0,
  stateVersion: 0,
  isDirty: false,
  hasDocument: false,
  documentSourceUri: null,
  selectedCatalogId: null,
  selectedCatalogKind: null,
  applyMode: 'NotClassified',
  applySupported: false,
  applyStatusLabel: '尚未预检；不会应用',
  catalog: [],
  fields: [],
  changes: [],
  diagnostics: [],
  graph: null,
  effectChain: [],
  unavailableActions: [],
  error: null
};

export function useWorkbenchSession() {
  const [boot] = useState(() => resolveWorkbenchBootMode());
  const [snapshot, setSnapshot] = useState(() => (
    boot.mode === 'preview' ? createFireballPreviewSnapshot() : EMPTY_SNAPSHOT
  ));
  const [connection, setConnection] = useState(() => ({
    phase: boot.mode === 'preview' ? 'preview' : boot.mode === 'missing-host' ? 'error' : 'boot',
    error: boot.error ?? '',
    transport: boot.hostPresent ? 'host' : 'none',
    sessionId: boot.mode === 'preview' ? 'preview-session' : 'pending'
  }));
  const [draftValues, setDraftValues] = useState(() => buildDraftValues(snapshot.fields));
  const [validationErrors, setValidationErrors] = useState([]);
  const [activeTab, setActiveTab] = useState('numeric');
  const [catalogQuery, setCatalogQuery] = useState('');
  const [localError, setLocalError] = useState(boot.error ?? '');
  const clientRef = useRef(null);
  const fieldsKey = useMemo(
    () => (snapshot.fields ?? []).map((field) => `${field.fieldPath}:${field.numericValue}`).join('|'),
    [snapshot.fields]
  );

  useEffect(() => {
    setDraftValues(buildDraftValues(snapshot.fields));
    setValidationErrors([]);
  }, [fieldsKey, snapshot.selectedCatalogId]);

  useEffect(() => {
    if (boot.mode !== 'host') {
      return undefined;
    }

    let cancelled = false;
    let client;

    async function connect() {
      try {
        const { transport, hostBacked } = ensureLudotsDataPlaneTransport();
        client = createLudotsDataPlaneClient({
          transport,
          hostBacked,
          defaultTopic: LSW_TOPIC,
          sessionId: `lsw-web-${Date.now().toString(16)}`
        });
        clientRef.current = client;
        setConnection((prev) => ({ ...prev, phase: 'connecting', error: '' }));
        await client.handshake({ app: 'live-skill-workbench' });
        if (cancelled) {
          return;
        }

        await client.subscribe(LSW_TOPIC, (event) => {
          if (cancelled || !event?.payload) {
            return;
          }
          setSnapshot(event.payload);
          setConnection((prev) => ({
            ...prev,
            phase: 'connected',
            sessionId: event.payload.sessionId ?? prev.sessionId,
            transport: 'host',
            error: ''
          }));
          setLocalError('');
        });
      } catch (error) {
        if (cancelled) {
          return;
        }
        const message = error instanceof Error ? error.message : String(error);
        setConnection({ phase: 'error', error: message, transport: 'none', sessionId: 'none' });
        setLocalError(message);
        setSnapshot((prev) => ({ ...prev, ready: false, error: message, connectionState: 'error' }));
      }
    }

    connect();
    return () => {
      cancelled = true;
      client?.close();
      clientRef.current = null;
    };
  }, [boot.mode]);

  const updateDraftValue = useCallback((fieldPath, value) => {
    setDraftValues((prev) => ({ ...prev, [fieldPath]: value }));
    setValidationErrors((prev) => prev.filter((error) => error.fieldPath !== fieldPath));
  }, []);

  const stageDrafts = useCallback(async () => {
    const { edits, validationErrors: errors } = collectDirtyEdits(
      snapshot.fields,
      draftValues,
      snapshot.selectedCatalogId
    );
    setValidationErrors(errors);
    if (errors.length > 0) {
      setLocalError(errors.map((error) => error.message).join(' '));
      return;
    }
    if (edits.length === 0) {
      return;
    }

    if (boot.mode === 'preview') {
      let next = snapshot;
      for (const edit of edits) {
        next = applyPreviewStageEdit(next, edit);
      }
      setSnapshot(next);
      setLocalError('');
      return;
    }

    const client = clientRef.current;
    if (!client) {
      setLocalError('DataPlane client is not connected.');
      return;
    }

    try {
      for (const edit of edits) {
        await client.command(LSW_COMMANDS.stageEdit, edit, { topic: LSW_TOPIC });
      }
      setLocalError('');
    } catch (error) {
      setLocalError(error instanceof Error ? error.message : String(error));
    }
  }, [boot.mode, draftValues, snapshot]);

  const selectCatalogItem = useCallback(async (catalogId) => {
    if (boot.mode === 'preview') {
      const fixture = createFireballPreviewSnapshot();
      const item = fixture.catalog.find((entry) => entry.id === catalogId);
      const showAbilityFields = catalogId === 'ability.Fireball';
      const showGraph = catalogId === 'ability.Fireball' || catalogId === 'graph.FireballCast';
      setSnapshot((prev) => ({
        ...prev,
        selectedCatalogId: catalogId,
        selectedCatalogKind: item?.kind ?? null,
        fields: showAbilityFields ? fixture.fields : [],
        graph: showGraph ? fixture.graph : null,
        stateVersion: Number(prev.stateVersion ?? 0) + 1
      }));
      return;
    }

    const client = clientRef.current;
    if (!client) {
      setLocalError('DataPlane client is not connected.');
      return;
    }

    try {
      await client.command(LSW_COMMANDS.selectCatalogItem, { catalogId }, { topic: LSW_TOPIC });
      setLocalError('');
    } catch (error) {
      setLocalError(error instanceof Error ? error.message : String(error));
    }
  }, [boot.mode]);

  const runUnsupportedCommand = useCallback(async (commandName, fallbackMessage) => {
    if (boot.mode === 'preview') {
      setLocalError(fallbackMessage);
      setSnapshot((prev) => ({
        ...prev,
        stateVersion: Number(prev.stateVersion ?? 0) + 1,
        diagnostics: [
          ...(prev.diagnostics ?? []),
          { severity: 'Warning', code: 'LSWUI-PREVIEW', message: fallbackMessage }
        ]
      }));
      return;
    }

    const client = clientRef.current;
    if (!client) {
      setLocalError('DataPlane client is not connected.');
      return;
    }

    try {
      await client.command(commandName, {}, { topic: LSW_TOPIC });
    } catch (error) {
      setLocalError(error instanceof Error ? error.message : String(error));
    }
  }, [boot.mode]);

  const precheck = useCallback(
    () => runUnsupportedCommand(
      LSW_COMMANDS.precheck,
      '预览模式：预检不会写真实运行时；宿主连接后走 LiveGasEditPipeline。'
    ),
    [runUnsupportedCommand]
  );

  const applyNextCast = useCallback(
    () => runUnsupportedCommand(
      LSW_COMMANDS.applyNextCast,
      '预览模式：应用不会写真实运行时；宿主连接后走安全帧 NextCast。'
    ),
    [runUnsupportedCommand]
  );

  const generateAiDraft = useCallback(
    async () => {
      if (boot.mode === 'preview') {
        setLocalError('预览模式：AI 草稿不会调用生产生成器。');
        return;
      }
      const client = clientRef.current;
      if (!client) {
        setLocalError('DataPlane client is not connected.');
        return;
      }
      try {
        await client.command(
          LSW_COMMANDS.generateAiDraft,
          { prompt: 'draft from workbench' },
          { topic: LSW_TOPIC }
        );
        setLocalError('');
      } catch (error) {
        setLocalError(error instanceof Error ? error.message : String(error));
      }
    },
    [boot.mode]
  );

  const previewSave = useCallback(
    () => runUnsupportedCommand(
      LSW_COMMANDS.previewSave,
      '预览模式：保存预览不会写盘。'
    ),
    [runUnsupportedCommand]
  );

  const saveToMod = useCallback(
    () => runUnsupportedCommand(
      LSW_COMMANDS.saveToMod,
      '预览模式：保存不会写盘。'
    ),
    [runUnsupportedCommand]
  );

  const refreshEffectChain = useCallback(
    () => runUnsupportedCommand(
      LSW_COMMANDS.refreshEffectChain,
      '预览模式：效果链不会刷新真实 Tracer。'
    ),
    [runUnsupportedCommand]
  );

  return {
    boot,
    snapshot,
    connection,
    draftValues,
    validationErrors,
    updateDraftValue,
    stageDrafts,
    selectCatalogItem,
    precheck,
    applyNextCast,
    generateAiDraft,
    previewSave,
    saveToMod,
    refreshEffectChain,
    activeTab,
    setActiveTab,
    catalogQuery,
    setCatalogQuery,
    localError
  };
}
