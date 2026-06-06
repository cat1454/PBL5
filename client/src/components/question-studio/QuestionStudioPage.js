import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { LuPause, LuPlay, LuTrash2, LuX } from 'react-icons/lu';
import { documentService, getApiErrorMessage, questionService, questionStudioService } from '../../services/api';
import { trackEvent } from '../../services/analytics';
import { useLanguage } from '../../context/LanguageContext';
import {
  DEFAULT_PRESET_KEY,
  IMPORTABLE_DRAFT_STATUSES,
  PRESET_KEYS,
  PRESETS,
  getDefaultQuestionStudioForm,
  getImportableDraftIds,
  getVisibleImportableDraftIds,
} from './questionStudioHelpers';

const MODES = ['fast', 'balanced', 'quality', 'max_draft'];
const QUESTION_TYPES = ['MultipleChoice', 'Flashcard', 'ShortAnswer', 'TrueFalse', 'FillInTheBlank'];
const DIFFICULTIES = ['Easy', 'Medium', 'Hard'];
const STATUSES = ['', 'Draft', 'Verified', 'Borderline', 'Rejected', 'Quarantined', 'Imported'];
const TIMELINE_STEPS = [
  { key: 'source', stages: ['Created', 'ExtractingSourceUnits'] },
  { key: 'generate', stages: ['GeneratingCanonical', 'GeneratingVariants'] },
  { key: 'verify', stages: ['VerifyingCanonical', 'VerifyingVariants'] },
  { key: 'dedupe', stages: ['DeduplicatingCanonical', 'DeduplicatingVariants'] },
  { key: 'ready', terminal: true },
];

function QuestionStudioPage() {
  const { documentId: routeDocumentId } = useParams();
  const documentId = Number(routeDocumentId);
  const navigate = useNavigate();
  const { t } = useLanguage();
  const [documentMeta, setDocumentMeta] = useState(null);
  const [run, setRun] = useState(null);
  const [runId, setRunId] = useState(null);
  const [drafts, setDrafts] = useState([]);
  const [pagination, setPagination] = useState(null);
  const [selectedDraftIds, setSelectedDraftIds] = useState([]);
  const [form, setForm] = useState(getDefaultQuestionStudioForm);
  const [filters, setFilters] = useState({
    status: 'Verified',
    type: '',
    difficulty: '',
    minScore: '',
    page: 1,
    pageSize: 20,
  });
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [feedback, setFeedback] = useState('');
  const [editingDraft, setEditingDraft] = useState(null);
  const [selectedPreset, setSelectedPreset] = useState(DEFAULT_PRESET_KEY);
  const [importResult, setImportResult] = useState(null);

  const terminalRun = run?.status === 'Completed' || run?.status === 'Failed' || run?.status === 'Cancelled';
  const progressPercent = run
    ? Math.min(100, Math.max(0, Math.round(
      Number.isFinite(Number(run.progressPercent))
        ? Number(run.progressPercent)
        : (Number(run.generatedDraftCount || 0) / Math.max(1, Number(run.targetDraftCount || 1))) * 100
    )))
    : 0;

  const loadDrafts = useCallback(async (nextFilters, nextRunId) => {
    if (!documentId) {
      return;
    }

    const payload = await questionStudioService.listDrafts({
      documentId,
      runId: nextRunId || undefined,
      status: nextFilters.status || undefined,
      type: nextFilters.type || undefined,
      difficulty: nextFilters.difficulty || undefined,
      minScore: nextFilters.minScore || undefined,
      page: nextFilters.page,
      pageSize: nextFilters.pageSize,
    });
    setDrafts(Array.isArray(payload?.data) ? payload.data : []);
    setPagination(payload?.pagination || null);
    setSelectedDraftIds((current) => current.filter((id) => (payload?.data || []).some((draft) => draft.id === id)));
  }, [documentId]);

  useEffect(() => {
    let active = true;

    async function loadInitial() {
      setLoading(true);
      setError('');
      try {
        const [documentData, activeRun] = await Promise.all([
          documentService.getDocument(documentId),
          questionStudioService.getActiveRun(documentId),
        ]);
        if (!active) {
          return;
        }
        setDocumentMeta(documentData);
        if (activeRun?.runId) {
          setRun(activeRun);
          setRunId(activeRun.runId);
        }
      } catch (err) {
        if (active) {
          setError(getApiErrorMessage(err, t('questionStudio.errors.loadFailed')));
        }
      } finally {
        if (active) {
          setLoading(false);
        }
      }
    }

    loadInitial();
    return () => {
      active = false;
    };
  }, [documentId, t]);

  useEffect(() => {
    let active = true;

    async function loadFilteredDrafts() {
      try {
        await loadDrafts(filters, runId);
      } catch (err) {
        if (active) {
          setError(getApiErrorMessage(err, t('questionStudio.errors.loadDraftsFailed')));
        }
      }
    }

    loadFilteredDrafts();
    return () => {
      active = false;
    };
  }, [filters, loadDrafts, runId, t]);

  useEffect(() => {
    if (!runId || terminalRun) {
      return undefined;
    }

    const timer = window.setInterval(async () => {
      try {
        const nextRun = await questionStudioService.getRun(runId);
        setRun(nextRun);
        await loadDrafts(filters, runId);
      } catch (err) {
        setError(getApiErrorMessage(err, t('questionStudio.errors.progressFailed')));
      }
    }, 2500);

    return () => window.clearInterval(timer);
  }, [filters, loadDrafts, runId, terminalRun, t]);

  const selectedVerifiedCount = useMemo(
    () => drafts.filter((draft) => selectedDraftIds.includes(draft.id) && IMPORTABLE_DRAFT_STATUSES.includes(draft.status)).length,
    [drafts, selectedDraftIds]
  );

  const startRun = async () => {
    setBusy(true);
    setError('');
    setFeedback('');
    setImportResult(null);
    trackEvent('question_studio_run_started', {
      documentId,
      preset: selectedPreset,
      mode: form.mode,
      targetDraftCount: Number(form.targetDraftCount),
    });
    try {
      const result = await questionStudioService.startRun({
        documentId,
        targetDraftCount: Number(form.targetDraftCount),
        mode: form.mode,
        questionTypes: form.questionTypes,
        difficulties: form.difficulties,
      });
      setRunId(result.runId);
      setRun({ runId: result.runId, status: result.status, stage: 'Created', progressPercent: 0, targetDraftCount: form.targetDraftCount });
      setFeedback(t('questionStudio.feedback.started'));
      trackEvent('question_studio_run_created', { documentId, runId: result.runId });
    } catch (err) {
      setError(getApiErrorMessage(err, t('questionStudio.errors.startFailed')));
    } finally {
      setBusy(false);
    }
  };

  const applyFilters = async (patch) => {
    const nextFilters = { ...filters, ...patch, page: patch.page || 1 };
    setFilters(nextFilters);
    setError('');
  };

  const toggleDraftSelection = (draftId) => {
    setSelectedDraftIds((current) => (
      current.includes(draftId)
        ? current.filter((id) => id !== draftId)
        : [...current, draftId]
    ));
  };

  const selectVisible = () => {
    setSelectedDraftIds(getVisibleImportableDraftIds(drafts));
  };

  const controlRun = async (action) => {
    if (!runId || busy) {
      return;
    }

    setBusy(true);
    setError('');
    setFeedback('');
    try {
      const nextRun = action === 'pause'
        ? await questionStudioService.pauseRun(runId)
        : action === 'resume'
          ? await questionStudioService.resumeRun(runId)
          : await questionStudioService.cancelRun(runId);
      setRun(nextRun);
      const feedbackKey = action === 'pause' ? 'paused' : action === 'resume' ? 'resumed' : 'cancelled';
      setFeedback(t(`questionStudio.feedback.${feedbackKey}`));
    } catch (err) {
      setError(getApiErrorMessage(err, t('questionStudio.errors.controlFailed')));
    } finally {
      setBusy(false);
    }
  };

  const deleteQuestionBank = async () => {
    if (busy || !documentId || Number(documentMeta?.questionsCount || 0) <= 0) {
      return;
    }

    if (!window.confirm(t('questionStudio.confirmDeleteBank'))) {
      return;
    }

    setBusy(true);
    setError('');
    setFeedback('');
    try {
      await questionService.deleteQuestionBank(documentId);
      setDocumentMeta(await documentService.getDocument(documentId));
      setFeedback(t('questionStudio.feedback.bankDeleted'));
    } catch (err) {
      setError(getApiErrorMessage(err, t('questionStudio.errors.deleteBankFailed')));
    } finally {
      setBusy(false);
    }
  };

  const updateDraftAction = async (draftId, action) => {
    setBusy(true);
    setError('');
    try {
      if (action === 'accept') await questionStudioService.acceptDraft(draftId);
      if (action === 'reject') await questionStudioService.rejectDraft(draftId);
      if (action === 'quarantine') await questionStudioService.quarantineDraft(draftId);
      if (action === 'restore') await questionStudioService.restoreDraft(draftId);
      await loadDrafts(filters, runId);
    } catch (err) {
      setError(getApiErrorMessage(err, t('questionStudio.errors.actionFailed')));
    } finally {
      setBusy(false);
    }
  };

  const importSelected = async () => {
    setBusy(true);
    setError('');
    setFeedback('');
    const importableDraftIds = getImportableDraftIds(selectedDraftIds, drafts);
    const selectedDrafts = drafts.filter((draft) => importableDraftIds.includes(draft.id));
    try {
      const result = await questionStudioService.importDrafts({ documentId, draftIds: importableDraftIds });
      setFeedback(t('questionStudio.feedback.imported', { count: result.importedCount || 0, skipped: result.skippedCount || 0 }));
      setImportResult(buildImportResult(result, selectedDrafts));
      trackEvent('question_drafts_imported', {
        documentId,
        importedCount: result.importedCount || 0,
        skippedCount: result.skippedCount || 0,
      });
      setSelectedDraftIds([]);
      await loadDrafts(filters, runId);
      if (runId) {
        setRun(await questionStudioService.getRun(runId));
      }
    } catch (err) {
      setError(getApiErrorMessage(err, t('questionStudio.errors.importFailed')));
    } finally {
      setBusy(false);
    }
  };

  const applyPreset = (presetKey) => {
    const preset = PRESETS[presetKey];
    if (!preset) {
      return;
    }

    setSelectedPreset(presetKey);
    setForm({
      targetDraftCount: preset.targetDraftCount,
      mode: preset.mode,
      questionTypes: preset.questionTypes,
      difficulties: preset.difficulties,
    });
    trackEvent('question_studio_preset_selected', {
      documentId,
      preset: presetKey,
      mode: preset.mode,
    });
  };

  const saveEdit = async () => {
    if (!editingDraft) {
      return;
    }

    setBusy(true);
    setError('');
    try {
      await questionStudioService.updateDraft(editingDraft.id, {
        questionText: editingDraft.questionText,
        options: Array.isArray(editingDraft.options) ? editingDraft.options.map((option) => `${option.key}. ${option.text}`) : [],
        correctAnswer: editingDraft.correctAnswer,
        explanation: editingDraft.explanation,
        difficulty: editingDraft.difficulty,
        topicTag: editingDraft.topicTag,
      });
      setEditingDraft(null);
      await loadDrafts(filters, runId);
    } catch (err) {
      setError(getApiErrorMessage(err, t('questionStudio.errors.saveFailed')));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="question-studio-page">
      <header className="question-studio-header">
        <div>
          <button type="button" className="button button-secondary" onClick={() => navigate(-1)}>
            {t('questionStudio.back')}
          </button>
          <p className="question-studio-kicker">{t('questionStudio.kicker')}</p>
          <h1>{t('questionStudio.title')}</h1>
          <p>{documentMeta?.fileName || t('questionStudio.documentFallback')}</p>
        </div>
        <div className="question-studio-header-stats">
          <span>{t('questionStudio.target')}</span>
          <strong>{run?.targetDraftCount || form.targetDraftCount}</strong>
          <span>{t('questionStudio.generated')}</span>
          <strong>{run?.generatedDraftCount || drafts.length}</strong>
        </div>
      </header>

      {loading ? (
        <div className="card question-studio-state">{t('questionStudio.loading')}</div>
      ) : (
        <>
          {error && <div className="alert alert-error question-studio-alert">{error}</div>}
          {feedback && <div className="alert alert-success question-studio-alert">{feedback}</div>}
          {importResult && (
            <ImportSuccessSheet
              result={importResult}
              t={t}
              onQuiz={() => navigate(`/quiz/${documentId}`)}
              onFlashcards={() => navigate(`/flashcards/${documentId}`)}
            />
          )}

          <section className="question-studio-presets" aria-label={t('questionStudio.presets.title')}>
            <div className="question-studio-section-copy">
              <p className="question-studio-kicker">{t('questionStudio.presets.kicker')}</p>
              <h2>{t('questionStudio.presets.title')}</h2>
              <p>{t('questionStudio.presets.subtitle')}</p>
            </div>
            <div className="question-studio-preset-grid">
              {PRESET_KEYS.map((presetKey) => {
                const preset = PRESETS[presetKey];
                return (
                  <button
                    key={presetKey}
                    type="button"
                    className={`question-studio-preset${selectedPreset === presetKey ? ' active' : ''}`}
                    onClick={() => applyPreset(presetKey)}
                  >
                    <strong>{t(`questionStudio.presets.items.${presetKey}.title`)}</strong>
                    <span>{t(`questionStudio.presets.items.${presetKey}.body`)}</span>
                    <small>
                      {t('questionStudio.presets.meta', {
                        count: preset.targetDraftCount,
                        mode: t(`questionStudio.modes.${preset.mode}`),
                      })}
                    </small>
                  </button>
                );
              })}
            </div>
          </section>

          <section className="question-studio-grid">
            <div className="question-studio-panel">
              <h2>{t('questionStudio.advancedTitle')}</h2>
              <label>
                {t('questionStudio.targetDraftCount')}
                <input
                  type="number"
                  min="1"
                  max="300"
                  value={form.targetDraftCount}
                  onChange={(event) => setForm((current) => ({ ...current, targetDraftCount: event.target.value }))}
                />
              </label>
              <div className="question-studio-segmented">
                {MODES.map((mode) => (
                  <button
                    key={mode}
                    type="button"
                    className={form.mode === mode ? 'active' : ''}
                    onClick={() => setForm((current) => ({ ...current, mode }))}
                  >
                    {t(`questionStudio.modes.${mode}`)}
                  </button>
                ))}
              </div>
              <ToggleGroup
                title={t('questionStudio.types')}
                values={QUESTION_TYPES}
                selected={form.questionTypes}
                onChange={(questionTypes) => setForm((current) => ({ ...current, questionTypes }))}
              />
              <ToggleGroup
                title={t('questionStudio.difficulties')}
                values={DIFFICULTIES}
                selected={form.difficulties}
                onChange={(difficulties) => setForm((current) => ({ ...current, difficulties }))}
              />
              <button type="button" className="button" onClick={startRun} disabled={busy || Boolean(run && !terminalRun) || form.questionTypes.length === 0 || form.difficulties.length === 0}>
                {busy ? t('questionStudio.busy') : t('questionStudio.start')}
              </button>
            </div>

            <div className="question-studio-panel">
              <h2>{t('questionStudio.progressTitle')}</h2>
              <div className={`question-studio-progress${run && !terminalRun ? ' is-active' : ''}`}>
                <div style={{ width: `${progressPercent}%` }} />
              </div>
              <div className="question-studio-progress-meta">
                <strong>{run?.status || t('questionStudio.noRun')}</strong>
                <span>{run?.stage || '-'}</span>
                <span>{progressPercent}%</span>
              </div>
              <div className="question-studio-metrics">
                <Metric label={t('questionStudio.metrics.verified')} value={run?.verifiedDraftCount || 0} />
                <Metric label={t('questionStudio.metrics.borderline')} value={run?.borderlineCount || 0} />
                <Metric label={t('questionStudio.metrics.rejected')} value={run?.rejectedCount || 0} />
                <Metric label={t('questionStudio.metrics.imported')} value={run?.importedCount || 0} />
              </div>
              <div className="question-studio-run-actions">
                {(run?.status === 'Pending' || run?.status === 'Running') && (
                  <button type="button" className="button button-secondary" onClick={() => controlRun('pause')} disabled={busy}>
                    <LuPause aria-hidden="true" />
                    <span>{t('questionStudio.pauseRun')}</span>
                  </button>
                )}
                {run?.status === 'Paused' && (
                  <button type="button" className="button button-secondary" onClick={() => controlRun('resume')} disabled={busy}>
                    <LuPlay aria-hidden="true" />
                    <span>{t('questionStudio.resumeRun')}</span>
                  </button>
                )}
                {run && !terminalRun && (
                  <button type="button" className="button button-secondary tone-danger" onClick={() => controlRun('cancel')} disabled={busy}>
                    <LuX aria-hidden="true" />
                    <span>{t('questionStudio.cancelRun')}</span>
                  </button>
                )}
                <button
                  type="button"
                  className="button button-secondary tone-danger"
                  onClick={deleteQuestionBank}
                  disabled={busy || Number(documentMeta?.questionsCount || 0) <= 0}
                >
                  <LuTrash2 aria-hidden="true" />
                  <span>{t('questionStudio.deleteBank')}</span>
                </button>
              </div>
              <ProgressTimeline run={run} terminalRun={terminalRun} t={t} />
            </div>
          </section>

          <section className="question-studio-toolbar">
            <select value={filters.status} onChange={(event) => applyFilters({ status: event.target.value })}>
              {STATUSES.map((status) => <option key={status || 'all'} value={status}>{status || t('questionStudio.allStatuses')}</option>)}
            </select>
            <select value={filters.type} onChange={(event) => applyFilters({ type: event.target.value })}>
              <option value="">{t('questionStudio.allTypes')}</option>
              {QUESTION_TYPES.map((type) => <option key={type} value={type}>{type}</option>)}
            </select>
            <select value={filters.difficulty} onChange={(event) => applyFilters({ difficulty: event.target.value })}>
              <option value="">{t('questionStudio.allDifficulties')}</option>
              {DIFFICULTIES.map((difficulty) => <option key={difficulty} value={difficulty}>{difficulty}</option>)}
            </select>
            <input
              type="number"
              step="0.05"
              min="0"
              max="1"
              placeholder={t('questionStudio.minScore')}
              value={filters.minScore}
              onChange={(event) => applyFilters({ minScore: event.target.value })}
            />
            <button type="button" className="button button-secondary" onClick={selectVisible}>{t('questionStudio.selectVisible')}</button>
            <button type="button" className="button" onClick={importSelected} disabled={busy || selectedVerifiedCount === 0}>
              {t('questionStudio.importSelected', { count: selectedVerifiedCount })}
            </button>
          </section>

          <section className="question-studio-list">
            {drafts.map((draft) => (
              <DraftCard
                key={draft.id}
                draft={draft}
                selected={selectedDraftIds.includes(draft.id)}
                onSelect={() => toggleDraftSelection(draft.id)}
                onEdit={() => setEditingDraft(draft)}
                onAction={(action) => updateDraftAction(draft.id, action)}
                t={t}
              />
            ))}
            {drafts.length === 0 && <div className="card question-studio-state">{t('questionStudio.empty')}</div>}
          </section>

          {pagination && (
            <div className="question-studio-pagination">
              <button type="button" className="button button-secondary" disabled={filters.page <= 1} onClick={() => applyFilters({ page: filters.page - 1 })}>
                {t('questionStudio.previous')}
              </button>
              <span>{pagination.page} / {pagination.totalPages || 1}</span>
              <button type="button" className="button button-secondary" disabled={pagination.page >= pagination.totalPages} onClick={() => applyFilters({ page: filters.page + 1 })}>
                {t('questionStudio.next')}
              </button>
            </div>
          )}
        </>
      )}

      {editingDraft && (
        <div className="question-studio-modal" role="dialog" aria-modal="true">
          <div className="question-studio-modal-panel">
            <h2>{t('questionStudio.editTitle')}</h2>
            <label>
              {t('questionStudio.questionText')}
              <textarea value={editingDraft.questionText || ''} onChange={(event) => setEditingDraft((current) => ({ ...current, questionText: event.target.value }))} />
            </label>
            <label>
              {t('questionStudio.correctAnswer')}
              <input value={editingDraft.correctAnswer || ''} onChange={(event) => setEditingDraft((current) => ({ ...current, correctAnswer: event.target.value }))} />
            </label>
            <label>
              {t('questionStudio.explanation')}
              <textarea value={editingDraft.explanation || ''} onChange={(event) => setEditingDraft((current) => ({ ...current, explanation: event.target.value }))} />
            </label>
            <label>
              {t('questionStudio.topicTag')}
              <input maxLength={200} value={editingDraft.topicTag || ''} onChange={(event) => setEditingDraft((current) => ({ ...current, topicTag: event.target.value }))} />
            </label>
            <div className="question-studio-modal-actions">
              <button type="button" className="button button-secondary" onClick={() => setEditingDraft(null)}>{t('questionStudio.cancel')}</button>
              <button type="button" className="button" onClick={saveEdit} disabled={busy}>{t('questionStudio.save')}</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function ToggleGroup({ title, values, selected, onChange }) {
  return (
    <fieldset className="question-studio-toggle-group">
      <legend>{title}</legend>
      {values.map((value) => (
        <label key={value}>
          <input
            type="checkbox"
            checked={selected.includes(value)}
            onChange={(event) => {
              onChange(event.target.checked
                ? [...selected, value]
                : selected.filter((item) => item !== value));
            }}
          />
          <span>{value}</span>
        </label>
      ))}
    </fieldset>
  );
}

function Metric({ label, value }) {
  return (
    <div>
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function ProgressTimeline({ run, terminalRun, t }) {
  const activeStage = run?.stage || 'Created';
  const status = String(run?.status || '').toLowerCase();

  return (
    <div className="question-studio-timeline">
      {TIMELINE_STEPS.map((step, index) => {
        const isDone = step.terminal
          ? terminalRun && status === 'completed'
          : TIMELINE_STEPS.slice(index + 1).some((nextStep) => nextStep.stages?.includes(activeStage)) || status === 'completed';
        const isActive = step.terminal
          ? terminalRun
          : step.stages?.includes(activeStage);
        return (
          <div key={step.key} className={`question-studio-timeline-step${isDone ? ' is-done' : ''}${isActive ? ' is-active' : ''}`}>
            <span>{isDone ? 'OK' : index + 1}</span>
            <div>
              <strong>{t(`questionStudio.timeline.${step.key}.title`)}</strong>
              <p>{t(`questionStudio.timeline.${step.key}.body`)}</p>
            </div>
          </div>
        );
      })}
    </div>
  );
}

function buildImportResult(result, selectedDrafts) {
  const importedCount = Number(result?.importedCount || 0);
  const skippedCount = Number(result?.skippedCount || 0);
  const importedDrafts = selectedDrafts.slice(0, importedCount || selectedDrafts.length);
  const byType = countBy(importedDrafts, (draft) => draft.questionType || 'Unknown');
  const byDifficulty = countBy(importedDrafts, (draft) => draft.difficulty || 'Unknown');

  return {
    importedCount,
    skippedCount,
    byType,
    byDifficulty,
  };
}

function countBy(items, selector) {
  return items.reduce((accumulator, item) => {
    const key = selector(item);
    accumulator[key] = (accumulator[key] || 0) + 1;
    return accumulator;
  }, {});
}

function ImportSuccessSheet({ result, t, onQuiz, onFlashcards }) {
  return (
    <section className="question-studio-import-sheet">
      <div>
        <span className="question-studio-quality-badge excellent">{t('questionStudio.importSuccess.badge')}</span>
        <h2>{t('questionStudio.importSuccess.title', { count: result.importedCount })}</h2>
        <p>{t('questionStudio.importSuccess.body', { skipped: result.skippedCount })}</p>
      </div>
      <div className="question-studio-import-breakdown">
        <Breakdown title={t('questionStudio.importSuccess.byType')} data={result.byType} />
        <Breakdown title={t('questionStudio.importSuccess.byDifficulty')} data={result.byDifficulty} />
      </div>
      <div className="question-studio-import-actions">
        <button type="button" className="button" onClick={onQuiz}>{t('questionStudio.importSuccess.quizCta')}</button>
        <button type="button" className="button button-secondary" onClick={onFlashcards}>{t('questionStudio.importSuccess.flashcardsCta')}</button>
      </div>
    </section>
  );
}

function Breakdown({ title, data }) {
  const entries = Object.entries(data || {});
  return (
    <div>
      <strong>{title}</strong>
      {entries.length === 0 ? <p>-</p> : (
        <div className="question-studio-breakdown-chips">
          {entries.map(([key, value]) => <span key={key}>{key}: {value}</span>)}
        </div>
      )}
    </div>
  );
}

function getQualityBadge(score) {
  const normalized = Math.round(Number(score || 0) * 100);
  if (normalized >= 90) {
    return { tone: 'excellent', labelKey: 'questionStudio.quality.excellent' };
  }
  if (normalized >= 75) {
    return { tone: 'good', labelKey: 'questionStudio.quality.good' };
  }
  if (normalized >= 60) {
    return { tone: 'review', labelKey: 'questionStudio.quality.review' };
  }
  return { tone: 'risk', labelKey: 'questionStudio.quality.risk' };
}

function DraftCard({ draft, selected, onSelect, onEdit, onAction, t }) {
  const qualityBadge = getQualityBadge(draft.overallScore);

  return (
    <article className={`question-studio-card status-${String(draft.status || '').toLowerCase()}`}>
      <div className="question-studio-card-select">
        <input type="checkbox" checked={selected} onChange={onSelect} />
      </div>
      <div className="question-studio-card-body">
        <div className="question-studio-card-head">
          <span>{draft.status}</span>
          <span>{draft.questionType}</span>
          <span>{draft.difficulty}</span>
          <strong className={`question-studio-quality-badge ${qualityBadge.tone}`}>
            {t(qualityBadge.labelKey)} {Math.round(Number(draft.overallScore || 0) * 100)}%
          </strong>
        </div>
        <h3>{draft.questionText}</h3>
        <p>{draft.explanation}</p>
        <div className="question-studio-score-row">
          <span>G {Math.round(Number(draft.groundingScore || 0) * 100)}%</span>
          <span>A {Math.round(Number(draft.answerScore || 0) * 100)}%</span>
          <span>C {Math.round(Number(draft.clarityScore || 0) * 100)}%</span>
          {draft.duplicateWarning && <span>{t('questionStudio.duplicate')}</span>}
        </div>
        <details>
          <summary>{t('questionStudio.sourceEvidence')}</summary>
          <p>{draft.sourceEvidence || '-'}</p>
        </details>
      </div>
      <div className="question-studio-card-actions">
        <button type="button" onClick={onEdit}>{t('questionStudio.edit')}</button>
        <button type="button" onClick={() => onAction('accept')}>{t('questionStudio.accept')}</button>
        <button type="button" onClick={() => onAction('reject')}>{t('questionStudio.reject')}</button>
        <button type="button" onClick={() => onAction('quarantine')}>{t('questionStudio.quarantine')}</button>
        <button type="button" onClick={() => onAction('restore')}>{t('questionStudio.restore')}</button>
      </div>
    </article>
  );
}

export default QuestionStudioPage;
