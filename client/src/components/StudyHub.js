import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import {
  documentService,
  gameService,
  getApiErrorMessage,
  isApiJobNotFound,
  learningService,
  questionService,
} from '../services/api';
import { useLanguage } from '../context/LanguageContext';
import { formatTopicForDisplay } from '../services/topicDisplay';
import { isActiveProgress, normalizeProgressState } from '../services/progress';

const STUDY_MODES = ['quiz', 'flashcards', 'test', 'streak'];
const DEFAULT_QUESTION_COUNT = 10;
const LEARNING_MODE_VALUES = {
  flashcards: 1,
  quiz: 2,
  test: 3,
  streak: 4,
};
const LEARNING_TEST_TYPE_VALUES = {
  preTest: 1,
  postTest: 2,
  retention: 3,
  practiceTest: 4,
};

function getQuestionCountFromCollection(payload) {
  if (Array.isArray(payload)) {
    return payload.length;
  }

  if (Array.isArray(payload?.questions)) {
    return payload.questions.length;
  }

  return 0;
}

function StudyHub({ documentId: providedDocumentId, forcedMode, showShell = true }) {
  const { language, t } = useLanguage();
  const { documentId: routeDocumentId, mode: routeMode } = useParams();
  const navigate = useNavigate();
  const location = useLocation();
  const documentId = providedDocumentId || routeDocumentId;
  const shouldShowShell = showShell && !providedDocumentId;
  const [refreshToken, setRefreshToken] = useState(0);
  const [metaLoading, setMetaLoading] = useState(shouldShowShell);
  const [documentStatus, setDocumentStatus] = useState(null);
  const [documentName, setDocumentName] = useState('');
  const [questionCount, setQuestionCount] = useState(0);
  const [metaError, setMetaError] = useState('');
  const [questionJobId, setQuestionJobId] = useState(null);
  const [questionGenerationProgress, setQuestionGenerationProgress] = useState(null);
  const [questionGenerationError, setQuestionGenerationError] = useState('');
  const [questionGenerationRecovered, setQuestionGenerationRecovered] = useState(false);
  const [regenerateMessage, setRegenerateMessage] = useState('');
  const [progressSummary, setProgressSummary] = useState(null);
  const [progressLoading, setProgressLoading] = useState(shouldShowShell);
  const [progressError, setProgressError] = useState('');

  const routeModeFromLegacyPath = useMemo(() => {
    if (forcedMode) {
      return forcedMode;
    }
    if (routeMode && STUDY_MODES.includes(routeMode)) {
      return routeMode;
    }
    if (location.pathname.startsWith('/flashcards/')) {
      return 'flashcards';
    }
    if (location.pathname.startsWith('/streak/')) {
      return 'streak';
    }
    return 'quiz';
  }, [forcedMode, location.pathname, routeMode]);

  const [activeMode, setActiveMode] = useState(routeModeFromLegacyPath);

  const copy = useMemo(() => {
    if (language === 'vi') {
      return {
        back: 'Quay l?i',
        backToWorkspace: 'V? Workspace',
        sourceFallback: 'T?i li?u ?ang h?c',
        emptySource: 'Ch?a c? t?n source',
        statusReady: 'Question bank s?n s?ng',
        statusMissing: 'Ch?a c? question bank',
        statusRefreshing: '?ang c?p nh?t question bank...',
        statusError: 'Kh?ng t?i ???c study data',
        countLabel: 'S? c?u h?i',
        sourceLabel: 'Source hi?n t?i',
        bankLabel: 'Question bank',
        regenerate: 'T?o l?i c?u h?i',
        regenerating: '?ang t?o l?i...',
        generated: '?? l?m m?i b? c?u h?i.',
        generatedRecovered: '?? kh?i ph?c question bank sau khi m?t ti?n tr?nh.',
        regenerationReplaceHint: 'T?o l?i s? thay th? b? c?u h?i ?ang ho?t ??ng.',
        questionProgressLost: 'M?t ti?n tr?nh t?o c?u h?i. H?y ki?m tra question bank hi?n t?i ho?c th? l?i.',
        bankMissingBody: 'Ch?a c? question bank n?o. H?y t?o c?u h?i tr??c.',
        studyDataError: 'Kh?ng t?i ???c th?ng tin study cho source n?y.',
        studyDataLoading: '?ang t?i th?ng tin study...',
        modeSwitcher: 'Ch? ?? h?c',
        quizTab: 'Quiz',
        flashTab: 'Flashcards',
        testTab: 'Test',
        testHint: 'L?m b?i ki?m tra li?n m?ch v? xem ?i?m ? cu?i.',
        testStartTitle: 'S?n s?ng l?m Test',
        testStartBody: 'B?i test ghi nh?n k?t qu? ??nh gi? n?ng l?c. Trong khi l?m b?i s? kh?ng hi?n ??p ?n hay gi?i th?ch.',
        testStartCta: 'B?t ??u test',
        testSubmitting: '?ang n?p test...',
        masteryAfterTest: 'Mastery sau test',
        duration: 'Th?i l??ng',
        reviewWeakQuestions: '?n l?i c?u y?u',
        noWeakQuestions: 'Kh?ng c? c?u y?u trong l?n test n?y.',
        weakReviewHint: '?n t?p c?c c?u b? sai trong test v?a r?i, c? ph?n h?i sau m?i c?u.',
        weakReviewCompleteHint: '?? ho?n th?nh phi?n ?n l?i c?u y?u.',
        testSubmitError: 'Ch?a th? n?p test. H?y th? l?i.',
        testCompletedTitle: 'Ho?n th?nh Test',
        testCompleteHint: 'B?i test ?? ho?n t?t. K?t qu? ch? ???c hi?n th? sau c?u cu?i.',
        testEmptyHint: 'H?y t?o question bank tr??c khi v?o Test mode.',
        progressLabel: 'Ti?n ?? h?c',
        progressLoading: '?ang t?i ti?n ??...',
        progressError: 'Learning progress hi?n ch?a kh? d?ng.',
        totalQuestions: 'T?ng s? c?u',
        attemptedQuestions: '?? l?m',
        averageMastery: 'Mastery TB',
        averageMemory: 'Memory TB',
        weakQuestions: 'C?u y?u',
        masteredQuestions: '?? v?ng',
        streakTab: 'Streak',
        quizHint: '?n nhanh b?ng c?u h?i tr?c nghi?m.',
        flashHint: 'L?t th? ?? ghi nh? ??p ?n.',
        streakHint: 'Gi? chu?i ??ng li?n ti?p th?t g?n v? t?p trung.',
        quizCompleteHint: 'Xem k?t qu? r?i ??i mode ngay trong c?ng m?t khu h?c t?p.',
        quizEmptyHint: 'Ch?a c? c?u h?i kh? d?ng cho source n?y.',
        streakEmptyHint: 'H?y t?o question bank tr??c khi v?o streak mode.',
        progressAria: (percent) => `Ti?n ?? streak ${percent} ph?n tr?m`,
      };
    }

    return {
      back: 'Back',
      backToWorkspace: 'Back to Workspace',
      sourceFallback: 'Current study source',
      emptySource: 'No source name available',
      statusReady: 'Question bank ready',
      statusMissing: 'Question bank missing',
      statusRefreshing: 'Refreshing question bank...',
      statusError: 'Study data unavailable',
      countLabel: 'Question count',
      sourceLabel: 'Current source',
      bankLabel: 'Question bank',
      regenerate: 'Regenerate questions',
      regenerating: 'Regenerating...',
      generated: 'Question bank refreshed.',
      generatedRecovered: 'Recovered the question bank after progress tracking was lost.',
      regenerationReplaceHint: 'Regenerate will replace the active question bank.',
      questionProgressLost: 'Question generation progress was lost. Check the current question bank or try again.',
      bankMissingBody: 'No question bank is available yet. Generate questions first.',
      studyDataError: 'Could not load study data for this source.',
      studyDataLoading: 'Loading study data...',
      modeSwitcher: 'Study mode',
      quizTab: 'Quiz',
      flashTab: 'Flashcards',
      testTab: 'Test',
      testHint: 'Take a clean test flow and review the score at the end.',
      testStartTitle: 'Ready for Test Mode',
      testStartBody: 'This test records an assessment result. Answers and explanations stay hidden until you submit the full test.',
      testStartCta: 'Start test',
      testSubmitting: 'Submitting test...',
      masteryAfterTest: 'Mastery after test',
      duration: 'Duration',
      reviewWeakQuestions: 'Review weak questions',
      noWeakQuestions: 'No weak questions in this test run.',
      weakReviewHint: 'Practice the questions missed in the last test with feedback after each answer.',
      weakReviewCompleteHint: 'Weak-question review complete.',
      testSubmitError: 'Could not submit the test. Please try again.',
      testCompletedTitle: 'Test complete',
      testCompleteHint: 'Test complete. Results are revealed only after the final question.',
      testEmptyHint: 'Generate a question bank before entering Test mode.',
      progressLabel: 'Learning progress',
      progressLoading: 'Loading progress...',
      progressError: 'Learning progress is not available yet.',
      totalQuestions: 'Total questions',
      attemptedQuestions: 'Attempted',
      averageMastery: 'Avg mastery',
      averageMemory: 'Avg memory',
      weakQuestions: 'Weak',
      masteredQuestions: 'Mastered',
      streakTab: 'Streak',
      quizHint: 'Move quickly through focused multiple-choice practice.',
      flashHint: 'Flip cards and stay focused on recall.',
      streakHint: 'Keep the streak alive with a tighter practice flow.',
      quizCompleteHint: 'Review the result, then switch modes without leaving the study area.',
      quizEmptyHint: 'No study questions are available for this source yet.',
      streakEmptyHint: 'Generate a question bank before entering streak mode.',
      progressAria: (percent) => `Streak progress ${percent} percent`,
    };
  }, [language]);

  useEffect(() => {
    setActiveMode(routeModeFromLegacyPath);
  }, [routeModeFromLegacyPath]);

  const isRegenerating = isActiveProgress(questionGenerationProgress);

  const loadProgressSummary = useCallback(async ({ silent = false } = {}) => {
    if (!documentId) {
      setProgressSummary(null);
      setProgressLoading(false);
      setProgressError('');
      return;
    }

    if (!silent) {
      setProgressLoading(true);
      setProgressError('');
    }

    try {
      const summary = await learningService.getDocumentSummary(documentId);
      setProgressSummary(summary);
      setProgressError('');
    } catch (error) {
      setProgressError(getApiErrorMessage(error, copy.progressError));
    } finally {
      if (!silent) {
        setProgressLoading(false);
      }
    }
  }, [copy.progressError, documentId]);

  useEffect(() => {
    loadProgressSummary();
  }, [loadProgressSummary, refreshToken]);

  const handleAttemptRecorded = useCallback(() => {
    loadProgressSummary({ silent: true });
  }, [loadProgressSummary]);

  const loadDocumentMeta = useCallback(async ({ silent = false } = {}) => {
    if (!documentId) {
      setDocumentName('');
      setDocumentStatus(null);
      setQuestionCount(0);
      setMetaError('');
      setMetaLoading(false);
      return null;
    }

    if (!silent) {
      setMetaLoading(true);
      setMetaError('');
    }

    try {
      const documentData = await documentService.getDocument(documentId);
      const nextQuestionCount = Number(documentData?.questionsCount ?? documentData?.QuestionsCount ?? 0);

      setDocumentName(documentData?.fileName || documentData?.name || `${copy.sourceFallback} #${documentId}`);
      setDocumentStatus(documentData?.status ?? null);
      setQuestionCount(nextQuestionCount);
      setMetaError('');
      return documentData;
    } catch (error) {
      setDocumentName(`${copy.sourceFallback} #${documentId}`);
      setDocumentStatus(null);
      setQuestionCount(0);
      setMetaError(getApiErrorMessage(error, copy.studyDataError));
      return null;
    } finally {
      if (!silent) {
        setMetaLoading(false);
      }
    }
  }, [copy.sourceFallback, copy.studyDataError, documentId]);

  const refreshStudyState = useCallback(async ({ silentMeta = true, silentProgress = true } = {}) => {
    await Promise.all([
      loadDocumentMeta({ silent: silentMeta }),
      loadProgressSummary({ silent: silentProgress }),
    ]);
    setRefreshToken((current) => current + 1);
  }, [loadDocumentMeta, loadProgressSummary]);

  useEffect(() => {
    if (!shouldShowShell || !documentId) {
      return;
    }

    let cancelled = false;

    const loadMeta = async () => {
      await loadDocumentMeta();
      if (cancelled) {
        return;
      }
    };

    loadMeta();

    return () => {
      cancelled = true;
    };
  }, [documentId, loadDocumentMeta, refreshToken, shouldShowShell]);

  const handleModeChange = (nextMode) => {
    setActiveMode(nextMode);
    if (shouldShowShell) {
      navigate(`/study/${documentId}/${nextMode}`, { replace: location.pathname.startsWith('/study/') });
    }
  };

  const handleBack = () => {
    if (window.history.length > 1 && window.history.state?.idx > 0) {
      navigate(-1);
      return;
    }

    navigate('/workspaces');
  };

  const handleRegenerate = useCallback(async () => {
    if (!documentId || isRegenerating) {
      return;
    }

    setQuestionGenerationRecovered(false);
    setQuestionGenerationError('');
    setRegenerateMessage('');

    try {
      const startResult = await questionService.startGenerateQuestions(documentId, DEFAULT_QUESTION_COUNT);
      const nextJobId = startResult?.jobId || startResult?.progress?.jobId;
      if (!nextJobId) {
        throw new Error(copy.questionProgressLost);
      }

      setQuestionJobId(nextJobId);
      setQuestionGenerationProgress(normalizeProgressState(startResult?.progress, {
        documentId: Number(documentId),
        jobId: nextJobId,
        status: startResult?.status || 'queued',
        stage: 'queued',
        stageLabel: copy.regenerating,
        message: copy.regenerationReplaceHint,
        percent: 0,
      }));
    } catch (error) {
      const nextError = getApiErrorMessage(error, t('workspace.study.failed'));
      setQuestionGenerationError(nextError);
      setRegenerateMessage(nextError);
      setQuestionGenerationProgress(null);
      setQuestionJobId(null);
    }
  }, [copy.questionProgressLost, copy.regenerating, copy.regenerationReplaceHint, documentId, isRegenerating, t]);

  useEffect(() => {
    if (!questionJobId || !questionGenerationProgress || !isActiveProgress(questionGenerationProgress) || !documentId) {
      return undefined;
    }

    let cancelled = false;

    const finalizeCompletion = async ({ recovered = false, questionTotal = null, message = '' } = {}) => {
      if (cancelled) {
        return;
      }

      await refreshStudyState();
      if (cancelled) {
        return;
      }

      if (typeof questionTotal === 'number' && Number.isFinite(questionTotal)) {
        setQuestionCount(questionTotal);
      }

      setQuestionGenerationRecovered(recovered);
      setRegenerateMessage(message || (recovered ? copy.generatedRecovered : copy.generated));
      setQuestionGenerationError('');
      setQuestionGenerationProgress((current) => normalizeProgressState(current, {
        status: 'completed',
        percent: 100,
        message: message || (recovered ? copy.generatedRecovered : copy.generated),
      }));
      setQuestionJobId(null);
    };

    const recoverQuestionBank = async () => {
      const documentData = await loadDocumentMeta({ silent: true });
      const persistedCount = Number(documentData?.questionsCount ?? documentData?.QuestionsCount ?? 0);
      if (persistedCount > 0) {
        await finalizeCompletion({
          recovered: true,
          questionTotal: persistedCount,
          message: copy.generatedRecovered,
        });
        return true;
      }

      const questionsPayload = await questionService.getQuestionsByDocument(documentId);
      const fetchedCount = getQuestionCountFromCollection(questionsPayload);
      if (fetchedCount > 0) {
        await finalizeCompletion({
          recovered: true,
          questionTotal: fetchedCount,
          message: copy.generatedRecovered,
        });
        return true;
      }

      return false;
    };

    const pollQuestionGeneration = async () => {
      try {
        const nextProgress = normalizeProgressState(
          await questionService.getGenerateProgress(questionJobId),
          questionGenerationProgress
        );

        if (cancelled) {
          return;
        }

        setQuestionGenerationProgress(nextProgress);
        setQuestionGenerationError('');

        if (nextProgress.status === 'completed') {
          const documentData = await loadDocumentMeta({ silent: true });
          const persistedCount = Number(documentData?.questionsCount ?? documentData?.QuestionsCount ?? nextProgress.questionsGenerated ?? 0);
          await finalizeCompletion({
            recovered: false,
            questionTotal: persistedCount,
            message: copy.generated,
          });
          return;
        }

        if (nextProgress.status === 'failed') {
          const nextError = nextProgress.error || nextProgress.detail || nextProgress.message || t('workspace.study.failed');
          setQuestionGenerationError(nextError);
          setRegenerateMessage(nextError);
          setQuestionJobId(null);
        }
      } catch (error) {
        if (cancelled) {
          return;
        }

        if (isApiJobNotFound(error)) {
          try {
            const recovered = await recoverQuestionBank();
            if (!recovered) {
              setQuestionGenerationRecovered(false);
              setQuestionGenerationError(copy.questionProgressLost);
              setRegenerateMessage(copy.questionProgressLost);
              setQuestionGenerationProgress((current) => normalizeProgressState(current, {
                status: 'failed',
                error: copy.questionProgressLost,
                message: copy.questionProgressLost,
              }));
              setQuestionJobId(null);
            }
          } catch (recoveryError) {
            const nextError = getApiErrorMessage(recoveryError, copy.questionProgressLost);
            setQuestionGenerationError(nextError);
            setRegenerateMessage(nextError);
            setQuestionGenerationProgress((current) => normalizeProgressState(current, {
              status: 'failed',
              error: nextError,
              message: nextError,
            }));
            setQuestionJobId(null);
          }
          return;
        }

        const nextError = getApiErrorMessage(error, t('workspace.study.failed'));
        setQuestionGenerationError(nextError);
        setRegenerateMessage(nextError);
        setQuestionGenerationProgress((current) => normalizeProgressState(current, {
          status: 'failed',
          error: nextError,
          message: nextError,
        }));
        setQuestionJobId(null);
      }
    };

    pollQuestionGeneration();
    const interval = setInterval(pollQuestionGeneration, 1200);

    return () => {
      cancelled = true;
      clearInterval(interval);
    };
  }, [
    copy.generated,
    copy.generatedRecovered,
    copy.questionProgressLost,
    documentId,
    loadDocumentMeta,
    questionGenerationProgress,
    questionJobId,
    refreshStudyState,
    t,
  ]);


  return (
    <div className={`study-shell${shouldShowShell ? ' study-shell-route' : ' study-shell-embedded'}`}>
      {shouldShowShell && (
        <>
          <div className="study-compact-header">
            <div className="study-compact-header-main">
              <button type="button" className="study-back-button" onClick={handleBack}>
                <span aria-hidden="true">←</span>
                <span>{copy.back}</span>
              </button>
              <div className="study-compact-title-group">
                <h2>{documentName || `${copy.sourceFallback} #${documentId}`}</h2>
                <p>{getModeHint(activeMode, copy)}</p>
              </div>
            </div>
          </div>

          <div className="study-main-grid">
            <div className="study-main-column">
              <StudyModeSwitcher activeMode={activeMode} onModeChange={handleModeChange} copy={copy} />
              <StudyModePanel
                documentId={documentId}
                mode={activeMode}
                onBack={handleBack}
                t={t}
                copy={copy}
                showShell
                refreshToken={refreshToken}
                onAttemptRecorded={handleAttemptRecorded}
              />
            </div>

            <StudySidebar
              copy={copy}
              documentName={documentName}
              metaError={metaError}
              metaLoading={metaLoading}
              onBack={handleBack}
              onRegenerate={handleRegenerate}
              progressError={progressError}
              progressLoading={progressLoading}
              progressSummary={progressSummary}
              questionGenerationError={questionGenerationError}
              questionGenerationProgress={questionGenerationProgress}
              questionGenerationRecovered={questionGenerationRecovered}
              questionCount={questionCount}
              documentStatus={documentStatus}
              regenerateMessage={regenerateMessage}
              regenerating={isRegenerating}
            />
          </div>
        </>
      )}

      {!shouldShowShell && (
        <StudyModePanel
          documentId={documentId}
          mode={activeMode}
          onBack={handleBack}
          t={t}
          copy={copy}
          showShell={false}
          refreshToken={refreshToken}
          onAttemptRecorded={handleAttemptRecorded}
        />
      )}
    </div>
  );
}

function StudyModeSwitcher({ activeMode, onModeChange, copy }) {
  return (
    <div className="study-mode-switcher-wrap">
      <span className="study-mode-label">{copy.modeSwitcher}</span>
      <div className="study-mode-switcher" role="tablist" aria-label={copy.modeSwitcher}>
        {STUDY_MODES.map((mode) => (
          <button
            key={mode}
            type="button"
            role="tab"
            aria-selected={activeMode === mode}
            className={`study-mode-switcher-button${activeMode === mode ? ' active' : ''}`}
            onClick={() => onModeChange(mode)}
          >
            {getModeTabLabel(mode, copy)}
          </button>
        ))}
      </div>
    </div>
  );
}

function StudySidebar({
  copy,
  documentName,
  metaError,
  metaLoading,
  onBack,
  onRegenerate,
  progressError,
  progressLoading,
  progressSummary,
  questionGenerationError,
  questionGenerationProgress,
  questionGenerationRecovered,
  questionCount,
  documentStatus,
  regenerateMessage,
  regenerating,
}) {
  const bankStatus = metaError
    ? copy.statusError
    : regenerating
      ? copy.statusRefreshing
      : questionCount > 0
        ? copy.statusReady
        : copy.statusMissing;

  const bankDetail = metaError
    ? metaError
    : questionGenerationError
      ? questionGenerationError
      : regenerateMessage
        ? regenerateMessage
        : questionCount > 0
          ? `${questionCount}`
          : copy.bankMissingBody;

  return (
    <aside className="study-sidebar">
      <div className="study-sidebar-card">
        <span className="study-sidebar-label">{copy.bankLabel}</span>
        <strong>{bankStatus}</strong>
        <p>{bankDetail}</p>
      </div>

      <div className="study-sidebar-card">
        <span className="study-sidebar-label">{copy.countLabel}</span>
        <strong>{questionCount}</strong>
        <p>{copy.regenerationReplaceHint}</p>
      </div>

      <ProgressSummaryCard
        copy={copy}
        progressError={progressError}
        progressLoading={progressLoading}
        progressSummary={progressSummary}
      />

      <div className="study-sidebar-card">
        <span className="study-sidebar-label">{copy.sourceLabel}</span>
        <strong className="study-sidebar-source">{documentName || copy.emptySource}</strong>
        <p>{documentStatus === null ? copy.studyDataError : `${documentStatus}`}</p>
      </div>

      <div className="study-sidebar-actions">
        <button type="button" className="button button-secondary" onClick={onBack}>
          {copy.backToWorkspace}
        </button>
        <button type="button" className="button" onClick={onRegenerate} disabled={regenerating}>
          {regenerating ? copy.regenerating : copy.regenerate}
        </button>
      </div>

      {questionGenerationProgress && (
        <p className="study-sidebar-note">
          {Math.round(Number(questionGenerationProgress.percent || 0))}% ? {questionGenerationProgress.message || questionGenerationProgress.stageLabel || copy.regenerating}
          {questionGenerationRecovered ? ` ? ${copy.generatedRecovered}` : ''}
        </p>
      )}
      {metaLoading && <p className="study-sidebar-note">{copy.studyDataLoading}</p>}
    </aside>
  );
}

function ProgressSummaryCard({ copy, progressError, progressLoading, progressSummary }) {
  const formatScore = (value) => {
    if (value === undefined || value === null || Number.isNaN(Number(value))) {
      return '0%';
    }

    return `${Math.round(Number(value))}%`;
  };

  const summary = progressSummary || {};

  return (
    <div className="study-sidebar-card study-progress-summary-card">
      <span className="study-sidebar-label">{copy.progressLabel}</span>
      {progressLoading ? (
        <p>{copy.progressLoading}</p>
      ) : progressError ? (
        <p>{progressError || copy.progressError}</p>
      ) : (
        <div className="study-progress-summary-grid">
          <ProgressSummaryItem label={copy.totalQuestions} value={summary.totalQuestions || 0} />
          <ProgressSummaryItem label={copy.attemptedQuestions} value={summary.attemptedQuestions || 0} />
          <ProgressSummaryItem label={copy.averageMastery} value={formatScore(summary.averageMasteryScore)} />
          <ProgressSummaryItem label={copy.averageMemory} value={formatScore(summary.averageMemoryScore)} />
          <ProgressSummaryItem label={copy.weakQuestions} value={summary.weakCount || 0} />
          <ProgressSummaryItem label={copy.masteredQuestions} value={summary.masteredCount || 0} />
        </div>
      )}
    </div>
  );
}

function ProgressSummaryItem({ label, value }) {
  return (
    <div className="study-progress-summary-item">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function formatDurationMs(durationMs) {
  const totalSeconds = Math.max(0, Math.round(Number(durationMs || 0) / 1000));
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${seconds.toString().padStart(2, '0')}`;
}

function WeakQuestionsPanel({ copy, weakQuestions, onReviewWeakQuestions }) {
  return (
    <div className="study-weak-panel">
      <div className="study-weak-panel-head">
        <strong>{copy.weakQuestions}</strong>
        <span>{weakQuestions.length}</span>
      </div>
      {weakQuestions.length > 0 ? (
        <>
          <div className="study-weak-list">
            {weakQuestions.slice(0, 4).map((question) => (
              <div key={question.questionId} className="study-weak-item">
                <span>{question.questionText || `#${question.questionId}`}</span>
                <strong>{Math.round(Number(question.masteryScore || 0))}%</strong>
              </div>
            ))}
          </div>
          <button type="button" className="button" onClick={onReviewWeakQuestions}>
            {copy.reviewWeakQuestions}
          </button>
        </>
      ) : (
        <p>{copy.noWeakQuestions}</p>
      )}
    </div>
  );
}

function StudyModePanel({ documentId, mode, onBack, t, copy, showShell, refreshToken, onAttemptRecorded }) {
  if (mode === 'flashcards') {
    return (
      <FlashcardsPane
        documentId={documentId}
        onBack={onBack}
        t={t}
        copy={copy}
        refreshToken={refreshToken}
        showShell={showShell}
        onAttemptRecorded={onAttemptRecorded}
      />
    );
  }

  return (
    <QuestionModePane
      documentId={documentId}
      mode={mode}
      onBack={onBack}
      t={t}
      copy={copy}
      refreshToken={refreshToken}
      showShell={showShell}
      onAttemptRecorded={onAttemptRecorded}
    />
  );
}

function QuestionModePane({ documentId, mode, onBack, t, copy, refreshToken, showShell, onAttemptRecorded }) {
  const navigate = useNavigate();
  const [loading, setLoading] = useState(true);
  const [allQuestions, setAllQuestions] = useState([]);
  const [currentQuestionIndex, setCurrentQuestionIndex] = useState(0);
  const [selectedAnswer, setSelectedAnswer] = useState(null);
  const [showResult, setShowResult] = useState(false);
  const [answers, setAnswers] = useState([]);
  const [finalScore, setFinalScore] = useState(null);
  const [testState, setTestState] = useState(mode === 'test' ? 'ready' : 'practice');
  const [testStartedAt, setTestStartedAt] = useState(null);
  const [testSessionId, setTestSessionId] = useState(null);
  const [testResult, setTestResult] = useState(null);
  const [testSubmitting, setTestSubmitting] = useState(false);
  const [testStarting, setTestStarting] = useState(false);
  const [testSubmitError, setTestSubmitError] = useState('');
  const [weakReviewQuestionIds, setWeakReviewQuestionIds] = useState(null);
  const [hideLowConfidence, setHideLowConfidence] = useState(false);
  const [currentStreak, setCurrentStreak] = useState(0);
  const [bestStreak, setBestStreak] = useState(0);
  const [streakBump, setStreakBump] = useState(false);
  const bumpTimerRef = useRef(null);
  const questionStartTimeRef = useRef(Date.now());
  const submittedQuestionKeysRef = useRef(new Set());
  const isStreakMode = mode === 'streak';
  const isTestMode = mode === 'test';
  const isAssessmentTest = isTestMode && testState === 'inProgress';
  const isWeakReviewMode = isTestMode && testState === 'review';

  useEffect(() => {
    const loadQuiz = async () => {
      setLoading(true);
      try {
        if (isTestMode) {
          setAllQuestions([]);
          return;
        }

        const data = await gameService.getQuizGame(documentId, DEFAULT_QUESTION_COUNT);
        setAllQuestions(Array.isArray(data?.questions) ? data.questions : []);
      } catch (error) {
        alert(t('quiz.loadError'));
        console.error(error);
        navigate('/workspaces');
      } finally {
        setLoading(false);
      }
    };

    loadQuiz();
  }, [documentId, isTestMode, navigate, refreshToken, t]);

  useEffect(() => () => {
    if (bumpTimerRef.current) {
      window.clearTimeout(bumpTimerRef.current);
    }
  }, []);

  const questions = useMemo(
    () => {
      const visibleQuestions = (hideLowConfidence
      ? allQuestions.filter((question) => !question.quality?.isLowConfidence)
      : allQuestions);

      if (!weakReviewQuestionIds || weakReviewQuestionIds.size === 0) {
        return visibleQuestions;
      }

      return visibleQuestions.filter((question) => weakReviewQuestionIds.has(question.id));
    },
    [allQuestions, hideLowConfidence, weakReviewQuestionIds]
  );

  useEffect(() => {
    setCurrentQuestionIndex(0);
    setSelectedAnswer(null);
    setShowResult(false);
    setAnswers([]);
    setFinalScore(null);
    setTestState(mode === 'test' ? 'ready' : 'practice');
    setTestStartedAt(null);
    setTestSessionId(null);
    setTestResult(null);
    setTestSubmitting(false);
    setTestStarting(false);
    setTestSubmitError('');
    setWeakReviewQuestionIds(null);
    setCurrentStreak(0);
    setBestStreak(0);
    setStreakBump(false);
    submittedQuestionKeysRef.current = new Set();
    questionStartTimeRef.current = Date.now();
  }, [allQuestions, hideLowConfidence, mode]);

  const currentQuestion = questions[currentQuestionIndex];
  const progress = questions.length > 0 ? ((currentQuestionIndex + 1) / questions.length) * 100 : 0;
  const topicDisplay = currentQuestion ? formatTopicForDisplay(currentQuestion.topic) : null;
  const quality = currentQuestion?.quality || {};

  useEffect(() => {
    questionStartTimeRef.current = Date.now();
  }, [currentQuestion?.id, currentQuestionIndex, mode]);

  const handleAnswerSelect = (optionKey) => {
    if (!showResult) {
      setSelectedAnswer(optionKey);
    }
  };

  const isCurrentAnswerCorrect = () => currentQuestion?.correctAnswer === selectedAnswer;

  const revealAnsweredQuestion = (answerResult) => {
    if (!answerResult || !currentQuestion) {
      return;
    }

    setAllQuestions((currentQuestions) => currentQuestions.map((question) => (
      question.id === currentQuestion.id
        ? {
            ...question,
            correctAnswer: answerResult.correctAnswer,
            explanation: answerResult.explanation || question.explanation,
          }
        : question
    )));
  };

  const recordCurrentAttempt = async (isCorrect) => {
    if (!currentQuestion) {
      return false;
    }

    const submissionMode = isWeakReviewMode ? 'quiz' : mode;
    const submissionKey = `${submissionMode}:${currentQuestion.id}:${currentQuestionIndex}`;
    if (submittedQuestionKeysRef.current.has(submissionKey)) {
      return false;
    }

    submittedQuestionKeysRef.current.add(submissionKey);

    const responseTimeMs = Math.max(0, Date.now() - questionStartTimeRef.current);
    try {
      await learningService.recordAttempt({
        documentId: Number(documentId),
        questionId: currentQuestion.id,
        mode: LEARNING_MODE_VALUES[submissionMode],
        selectedAnswer,
        isCorrect,
        responseTimeMs,
      });
      if (onAttemptRecorded) {
        onAttemptRecorded();
      }
    } catch (error) {
      console.warn('Could not record learning attempt.', error);
    }

    return true;
  };

  const getResponseTimeMs = () => Math.max(0, Date.now() - questionStartTimeRef.current);

  const finishWithAnswers = (nextAnswers) => {
    const totalCorrect = nextAnswers.filter((answer) => answer.isCorrect).length;
    const score = questions.length > 0 ? Math.round((totalCorrect / questions.length) * 100) : 0;
    setFinalScore(score);
  };

  const submitTestWithAnswers = async (nextAnswers) => {
    if (!testSessionId) {
      setTestSubmitError(copy.testSubmitError);
      return;
    }

    setTestSubmitting(true);
    setTestSubmitError('');

    try {
      const submittedResult = await learningService.submitTestResult({
        testSessionId,
        durationMs: testStartedAt ? Math.max(0, Date.now() - testStartedAt.getTime()) : null,
        answers: nextAnswers.map((answer) => ({
          questionId: answer.questionId,
          selectedAnswer: answer.selectedAnswer,
          responseTimeMs: answer.responseTimeMs,
        })),
      });

      setTestResult(submittedResult);
      if (Array.isArray(submittedResult?.answers)) {
        const answerDetailsById = new Map(submittedResult.answers.map((answer) => [answer.questionId, answer]));
        setAllQuestions((currentQuestions) => currentQuestions.map((question) => {
          const answerDetails = answerDetailsById.get(question.id);
          return answerDetails
            ? { ...question, correctAnswer: answerDetails.correctAnswer }
            : question;
        }));
      }
      setFinalScore(Math.round(Number(submittedResult?.score || 0)));
      setTestState('completed');
      if (onAttemptRecorded) {
        onAttemptRecorded();
      }
    } catch (error) {
      console.warn('Could not submit learning test.', error);
      setTestSubmitError(getApiErrorMessage(error, copy.testSubmitError));
    } finally {
      setTestSubmitting(false);
    }
  };

  const handleSubmitAnswer = async () => {
    if (!selectedAnswer || !currentQuestion || testSubmitting) {
      return;
    }

    const responseTimeMs = getResponseTimeMs();

    if (isAssessmentTest) {
      if (currentQuestionIndex === questions.length - 1 && answers.length === questions.length) {
        await submitTestWithAnswers(answers);
        return;
      }

      const nextAnswers = [
        ...answers,
        {
          questionId: currentQuestion.id,
          selectedAnswer,
          responseTimeMs,
        },
      ];
      setAnswers(nextAnswers);

      if (currentQuestionIndex < questions.length - 1) {
        setCurrentQuestionIndex((current) => current + 1);
        setSelectedAnswer(null);
        setShowResult(false);
        return;
      }

      await submitTestWithAnswers(nextAnswers);
      return;
    }

    let answerResult;
    try {
      answerResult = await gameService.submitQuizAnswer(
        Number(documentId),
        currentQuestion.id,
        selectedAnswer
      );
      revealAnsweredQuestion(answerResult);
    } catch (error) {
      console.warn('Could not submit quiz answer.', error);
      setTestSubmitError(getApiErrorMessage(error, copy.testSubmitError));
      return;
    }

    const isCorrect = Boolean(answerResult?.isCorrect);

    if (!await recordCurrentAttempt(isCorrect)) {
      return;
    }

    const nextAnswers = [
      ...answers,
      {
        questionId: currentQuestion.id,
        selectedAnswer,
        isCorrect,
        responseTimeMs,
      },
    ];
    setAnswers(nextAnswers);

    if (isStreakMode) {
      const nextStreak = isCorrect ? currentStreak + 1 : 0;
      const nextBestStreak = Math.max(bestStreak, nextStreak);
      setCurrentStreak(nextStreak);
      setBestStreak(nextBestStreak);

      if (isCorrect) {
        setStreakBump(false);
        window.clearTimeout(bumpTimerRef.current);
        requestAnimationFrame(() => setStreakBump(true));
        bumpTimerRef.current = window.setTimeout(() => setStreakBump(false), 420);
      } else {
        setStreakBump(false);
      }
    }

    setShowResult(true);
  };

  const handleNextQuestion = () => {
    if (currentQuestionIndex < questions.length - 1) {
      setCurrentQuestionIndex((current) => current + 1);
      setSelectedAnswer(null);
      setShowResult(false);
      return;
    }

    finishWithAnswers(answers);
  };

  const resetSession = () => {
    if (isTestMode) {
      setAllQuestions([]);
    }

    setCurrentQuestionIndex(0);
    setSelectedAnswer(null);
    setShowResult(false);
    setAnswers([]);
    setFinalScore(null);
    setTestState(isTestMode ? 'ready' : 'practice');
    setTestStartedAt(null);
    setTestSessionId(null);
    setTestResult(null);
    setTestSubmitting(false);
    setTestStarting(false);
    setTestSubmitError('');
    setWeakReviewQuestionIds(null);
    setCurrentStreak(0);
    setBestStreak(0);
    setStreakBump(false);
    submittedQuestionKeysRef.current = new Set();
    questionStartTimeRef.current = Date.now();
  };

  const startTest = async () => {
    if (testStarting) {
      return;
    }

    setTestStarting(true);
    setTestSubmitError('');

    try {
      const started = await learningService.startTest({
        documentId: Number(documentId),
        count: DEFAULT_QUESTION_COUNT,
        testType: LEARNING_TEST_TYPE_VALUES.practiceTest,
      });

      setAllQuestions(Array.isArray(started?.questions) ? started.questions : []);
      setTestSessionId(started?.testSessionId || null);
      setTestStartedAt(started?.startedAt ? new Date(started.startedAt) : new Date());
      setTestState('inProgress');
    } catch (error) {
      console.warn('Could not start learning test.', error);
      setTestSubmitError(getApiErrorMessage(error, copy.testSubmitError));
      return;
    } finally {
      setTestStarting(false);
    }

    setCurrentQuestionIndex(0);
    setSelectedAnswer(null);
    setShowResult(false);
    setAnswers([]);
    setFinalScore(null);
    setTestResult(null);
    setWeakReviewQuestionIds(null);
    submittedQuestionKeysRef.current = new Set();
    questionStartTimeRef.current = Date.now();
  };

  const startWeakQuestionReview = () => {
    const weakIds = (testResult?.weakQuestions || []).map((question) => question.questionId);
    if (weakIds.length === 0) {
      return;
    }

    setWeakReviewQuestionIds(new Set(weakIds));
    setCurrentQuestionIndex(0);
    setSelectedAnswer(null);
    setShowResult(false);
    setAnswers([]);
    setFinalScore(null);
    setTestSubmitError('');
    setTestStartedAt(null);
    setTestState('review');
    submittedQuestionKeysRef.current = new Set();
    questionStartTimeRef.current = Date.now();
  };

  const getOptionClass = (optionKey) => {
    if (!showResult) {
      return selectedAnswer === optionKey ? 'option-button selected study-option-button' : 'option-button study-option-button';
    }

    if (optionKey === currentQuestion.correctAnswer) {
      return 'option-button correct study-option-button';
    }

    if (optionKey === selectedAnswer && selectedAnswer !== currentQuestion.correctAnswer) {
      return 'option-button incorrect study-option-button';
    }

    return 'option-button study-option-button';
  };

  if (loading) {
    return (
      <div className="loading">
        <div className="spinner"></div>
        <p>{t(isStreakMode ? 'streak.loading' : 'quiz.loading')}</p>
      </div>
    );
  }

  if (questions.length === 0 && !(isTestMode && testState === 'ready')) {
    return (
      <StudyEmptyState
        title={allQuestions.length > 0 ? t('quiz.allHiddenTitle') : t(isStreakMode ? 'streak.emptyTitle' : 'quiz.emptyTitle')}
        body={allQuestions.length > 0 ? t('quiz.allHiddenBody') : (isStreakMode ? copy.streakEmptyHint : isTestMode ? copy.testEmptyHint : copy.quizEmptyHint)}
        resetLabel={allQuestions.length > 0 ? t('quiz.showLowConfidence') : null}
        onReset={allQuestions.length > 0 ? () => setHideLowConfidence(false) : null}
        onBack={onBack}
        backLabel={copy.backToWorkspace}
      />
    );
  }

  if (isTestMode && testState === 'ready') {
    return (
      <div className="study-panel study-panel-test">
        <div className="card study-card study-test-start-card">
          <h2>{copy.testStartTitle}</h2>
          <p className="section-subtitle">{copy.testStartBody}</p>
          <div className="study-progress-summary-grid">
            <ProgressSummaryItem label={copy.totalQuestions} value={DEFAULT_QUESTION_COUNT} />
            <ProgressSummaryItem label={copy.weakQuestions} value={0} />
          </div>
          <div className="study-action-row">
            <button className="button" onClick={startTest} disabled={testStarting}>
              {testStarting ? t('quiz.loading') : copy.testStartCta}
            </button>
            {!showShell && (
              <button className="button button-secondary" onClick={onBack}>
                {copy.backToWorkspace}
              </button>
            )}
          </div>
          {testSubmitError && <p className="form-error">{testSubmitError}</p>}
        </div>
      </div>
    );
  }

  if (finalScore !== null) {
    const totalCorrect = answers.filter((answer) => answer.isCorrect).length;
    const completedTitle = isStreakMode
      ? t('streak.completedTitle')
      : testResult
        ? copy.testCompletedTitle
        : t('quiz.completed');
    const completedHint = isStreakMode
      ? t('streak.completedSubtitle')
      : testResult
        ? copy.testCompleteHint
        : isWeakReviewMode
          ? copy.weakReviewCompleteHint
        : copy.quizCompleteHint;
    const weakQuestions = testResult?.weakQuestions || [];
    const resultScore = testResult ? Math.round(Number(testResult.score || 0)) : finalScore;
    const resultCorrect = testResult?.correctCount ?? totalCorrect;
    const resultTotal = testResult?.totalQuestions ?? questions.length;

    return (
      <div className={`study-panel study-panel-${mode}`}>
        <div className="card study-card study-summary-card">
          <h2>{completedTitle}</h2>
          <p className="section-subtitle">
            {completedHint}
          </p>
          <div className="score-display">
            <h1 style={{ fontSize: '4em', color: resultScore >= 70 ? '#28a745' : '#dc3545' }}>
              {resultScore}%
            </h1>
            <p style={{ fontSize: '1.1em' }}>
              {t(isStreakMode ? 'streak.scoreLine' : 'quiz.scoreLine', { correct: resultCorrect, total: resultTotal })}
            </p>
            {isStreakMode && <p className="study-summary-inline-meta">{t('streak.bestStreakLine', { count: bestStreak })}</p>}
            {testResult && (
              <div className="study-test-result-metrics">
                <ProgressSummaryItem label={copy.duration} value={formatDurationMs(testResult.durationMs)} />
                <ProgressSummaryItem label={copy.masteryAfterTest} value={`${Math.round(Number(testResult.masteryScoreAfterTest || 0))}%`} />
                <ProgressSummaryItem label={copy.averageMemory} value={`${Math.round(Number(testResult.memoryScoreAfterTest || 0))}%`} />
              </div>
            )}
          </div>
          {testResult && (
            <WeakQuestionsPanel
              copy={copy}
              weakQuestions={weakQuestions}
              onReviewWeakQuestions={startWeakQuestionReview}
            />
          )}
          <div className="study-action-row">
            <button className="button" onClick={resetSession}>
              {t(isStreakMode ? 'streak.retry' : 'quiz.retry')}
            </button>
            <button className="button button-secondary" onClick={onBack}>
              {copy.backToWorkspace}
            </button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className={`study-panel study-panel-${mode}`}>
      <div className={`card study-card${isStreakMode ? ' streak-card' : ''}`}>
        <div className="study-card-toolbar">
          <div>
            <h3>{t('quiz.questionProgress', { current: currentQuestionIndex + 1, total: questions.length })}</h3>
            <p className="study-card-caption">{isWeakReviewMode ? copy.weakReviewHint : getModeHint(mode, copy)}</p>
          </div>
          <div className="study-card-tools">
            <button className="button button-secondary" onClick={() => setHideLowConfidence((current) => !current)}>
              {hideLowConfidence ? t('quiz.showAllQuestions') : t('quiz.hideLowConfidence')}
            </button>
            {quality.score !== undefined && quality.score !== null && (
              <span className={`quality-chip ${quality.isLowConfidence ? 'low' : 'good'}`}>
                Verifier {quality.score}/100
              </span>
            )}
          </div>
        </div>

        {isStreakMode ? (
          <StreakSummary
            currentStreak={currentStreak}
            bestStreak={bestStreak}
            currentQuestionIndex={currentQuestionIndex}
            total={questions.length}
            progress={progress}
            streakBump={streakBump}
            t={t}
            copy={copy}
          />
        ) : (
          <div className="study-progress-track">
            <div className="study-progress-fill" style={{ width: `${progress}%` }} />
          </div>
        )}

        <QuestionCard
          currentQuestion={currentQuestion}
          getOptionClass={getOptionClass}
          handleAnswerSelect={handleAnswerSelect}
          quality={quality}
          showResult={showResult}
          topicDisplay={topicDisplay}
          t={t}
          isCurrentAnswerCorrect={isCurrentAnswerCorrect()}
          isStreakMode={isStreakMode}
          currentStreak={currentStreak}
        />

        <div className="study-action-row">
          {!showResult ? (
            <button className="button" onClick={handleSubmitAnswer} disabled={!selectedAnswer || testSubmitting}>
              {testSubmitting
                ? copy.testSubmitting
                : isAssessmentTest
                  ? (currentQuestionIndex < questions.length - 1 ? t('quiz.next') : t('quiz.finish'))
                : t('quiz.submit')}
            </button>
          ) : (
            <button className="button" onClick={handleNextQuestion}>
              {currentQuestionIndex < questions.length - 1 ? t('quiz.next') : t('quiz.finish')}
            </button>
          )}

          {!showShell && (
            <button className="button button-secondary" onClick={onBack}>
              {copy.backToWorkspace}
            </button>
          )}
        </div>
        {testSubmitError && <p className="form-error">{testSubmitError}</p>}
      </div>
    </div>
  );
}

function FlashcardsPane({ documentId, onBack, t, copy, refreshToken, showShell, onAttemptRecorded }) {
  const navigate = useNavigate();
  const [loading, setLoading] = useState(true);
  const [allFlashcards, setAllFlashcards] = useState([]);
  const [currentIndex, setCurrentIndex] = useState(0);
  const [flipped, setFlipped] = useState(false);
  const [hideLowConfidence, setHideLowConfidence] = useState(false);
  const [assessing, setAssessing] = useState(false);
  const cardStartTimeRef = useRef(Date.now());

  useEffect(() => {
    const loadFlashcards = async () => {
      setLoading(true);
      try {
        const data = await gameService.getFlashcards(documentId);
        setAllFlashcards(Array.isArray(data?.flashcards) ? data.flashcards : []);
      } catch (error) {
        alert(t('flashcards.loadError'));
        console.error(error);
        navigate('/workspaces');
      } finally {
        setLoading(false);
      }
    };

    loadFlashcards();
  }, [documentId, navigate, refreshToken, t]);

  useEffect(() => {
    setCurrentIndex(0);
    setFlipped(false);
    cardStartTimeRef.current = Date.now();
  }, [allFlashcards, hideLowConfidence]);

  useEffect(() => {
    cardStartTimeRef.current = Date.now();
  }, [currentIndex]);

  const flashcards = hideLowConfidence
    ? allFlashcards.filter((card) => !card.quality?.isLowConfidence)
    : allFlashcards;

  if (loading) {
    return (
      <div className="loading">
        <div className="spinner"></div>
        <p>{t('flashcards.loading')}</p>
      </div>
    );
  }

  if (flashcards.length === 0) {
    return (
      <StudyEmptyState
        title={allFlashcards.length > 0 ? t('flashcards.allHiddenTitle') : t('flashcards.emptyTitle')}
        body={allFlashcards.length > 0 ? t('flashcards.allHiddenBody') : t('flashcards.emptyBody')}
        resetLabel={allFlashcards.length > 0 ? t('flashcards.showLowConfidence') : null}
        onReset={allFlashcards.length > 0 ? () => setHideLowConfidence(false) : null}
        onBack={onBack}
        backLabel={copy.backToWorkspace}
      />
    );
  }

  const currentCard = flashcards[currentIndex];
  const progress = ((currentIndex + 1) / flashcards.length) * 100;
  const topicDisplay = formatTopicForDisplay(currentCard.topic);
  const quality = currentCard.quality || {};
  const moveToNextCard = () => {
    if (currentIndex < flashcards.length - 1) {
      setCurrentIndex((current) => current + 1);
      setFlipped(false);
      return;
    }

    setFlipped(false);
  };

  const recordFlashcardAssessment = async (remembered) => {
    if (!currentCard || assessing) {
      return;
    }

    setAssessing(true);
    try {
      await learningService.recordAttempt({
        documentId: Number(documentId),
        questionId: currentCard.id,
        mode: LEARNING_MODE_VALUES.flashcards,
        selectedAnswer: remembered ? 'self:remembered' : 'self:review',
        isCorrect: remembered,
        responseTimeMs: Math.max(0, Date.now() - cardStartTimeRef.current),
      });
      if (onAttemptRecorded) {
        onAttemptRecorded();
      }
      moveToNextCard();
    } catch (error) {
      console.warn('Could not record flashcard assessment.', error);
    } finally {
      setAssessing(false);
    }
  };

  return (
    <div className="study-panel study-panel-flashcards">
      <div className="card study-card">
        <div className="study-card-toolbar">
          <div>
            <h3>{t('flashcards.cardProgress', { current: currentIndex + 1, total: flashcards.length })}</h3>
            <p className="study-card-caption">{copy.flashHint}</p>
          </div>
          <div className="study-card-tools">
            <button className="button button-secondary" onClick={() => setHideLowConfidence((current) => !current)}>
              {hideLowConfidence ? t('flashcards.showAllCards') : t('flashcards.hideLowConfidence')}
            </button>
            {quality.score !== undefined && quality.score !== null && (
              <span className={`quality-chip ${quality.isLowConfidence ? 'low' : 'good'}`}>
                Verifier {quality.score}/100
              </span>
            )}
          </div>
        </div>

        <div className="study-progress-track">
          <div className="study-progress-fill" style={{ width: `${progress}%` }} />
        </div>

        <StudyTopicChips topicDisplay={topicDisplay} />

        <div className="flashcard study-flashcard" onClick={() => setFlipped((current) => !current)}>
          <div className="flashcard-content study-flashcard-content">
            {(quality.isLowConfidence || quality.isUnknown) && (
              <div className="alert alert-info quality-warning">
                <strong>{quality.isLowConfidence ? t('flashcards.reviewNeeded') : t('flashcards.noVerifier')}</strong>
                <p>
                  {quality.isLowConfidence
                    ? t('flashcards.lowConfidenceBody', { score: quality.score })
                    : t('flashcards.noVerifierBody')}
                </p>
              </div>
            )}

            {!flipped ? (
              <div>
                <h3 className="study-flashcard-face-title">{t('flashcards.question')}</h3>
                <p>{currentCard.front}</p>
                <p className="study-flashcard-hint">{t('flashcards.tapToShow')}</p>
              </div>
            ) : (
              <div>
                <h3 className="study-flashcard-face-title study-flashcard-face-title-answer">{t('flashcards.answer')}</h3>
                <p><strong>{currentCard.back}</strong></p>
                {currentCard.explanation && (
                  <div className="flashcard-explanation">
                    <strong>{t('flashcards.explanation')}</strong>
                    <p>{currentCard.explanation}</p>
                  </div>
                )}
                <div className="study-action-row">
                  <button
                    type="button"
                    className="button button-secondary"
                    onClick={(event) => {
                      event.stopPropagation();
                      recordFlashcardAssessment(false);
                    }}
                    disabled={assessing}
                  >
                    {t('flashcards.markForReview')}
                  </button>
                  <button
                    type="button"
                    className="button"
                    onClick={(event) => {
                      event.stopPropagation();
                      recordFlashcardAssessment(true);
                    }}
                    disabled={assessing}
                  >
                    {assessing ? t('flashcards.recording') : t('flashcards.remembered')}
                  </button>
                </div>
                <p className="study-flashcard-hint">{t('flashcards.tapToReturn')}</p>
              </div>
            )}
          </div>
        </div>

        <div className="study-action-row study-action-row-spread">
          <button
            className="button"
            onClick={() => {
              if (currentIndex > 0) {
                setCurrentIndex((current) => current - 1);
                setFlipped(false);
              }
            }}
            disabled={currentIndex === 0}
          >
            {t('flashcards.previous')}
          </button>

          {!showShell && (
            <button className="button button-secondary" onClick={onBack}>
              {copy.backToWorkspace}
            </button>
          )}

          <button
            className="button"
            onClick={() => {
              moveToNextCard();
            }}
            disabled={currentIndex === flashcards.length - 1}
          >
            {t('flashcards.next')}
          </button>
        </div>

        {currentIndex === flashcards.length - 1 && (
          <div className="study-endcap">
            <p>{t('flashcards.reachedEnd')}</p>
            <button
              className="button"
              onClick={() => {
                setCurrentIndex(0);
                setFlipped(false);
              }}
            >
              {t('flashcards.restart')}
            </button>
          </div>
        )}
      </div>
    </div>
  );
}

function QuestionCard({
  currentQuestion,
  getOptionClass,
  handleAnswerSelect,
  quality,
  showResult,
  topicDisplay,
  t,
  isCurrentAnswerCorrect,
  isStreakMode,
  currentStreak,
}) {
  return (
    <div className={`question-card study-question-card${isStreakMode ? ' streak-question-card' : ''}`}>
      <StudyTopicChips topicDisplay={topicDisplay} />
      <h2>{currentQuestion.questionText}</h2>

      {(quality.isLowConfidence || quality.isUnknown) && (
        <div className="alert alert-info quality-warning">
          <strong>{quality.isLowConfidence ? t('quiz.reviewNeeded') : t('quiz.noVerifier')}</strong>
          <p>
            {quality.isLowConfidence
              ? t('quiz.lowConfidenceBody', { score: quality.score })
              : t('quiz.noVerifierBody')}
          </p>
        </div>
      )}

      <div className="options study-options">
        {currentQuestion.options.map((option) => (
          <button
            key={option.key}
            className={getOptionClass(option.key)}
            onClick={() => handleAnswerSelect(option.key)}
            disabled={showResult}
          >
            <strong>{option.key}.</strong> {option.text}
          </button>
        ))}
      </div>

      {showResult && (
        <div className={`alert study-answer-alert ${isCurrentAnswerCorrect ? 'alert-success' : 'alert-error'}`}>
          <strong>{isCurrentAnswerCorrect ? t(isStreakMode ? 'streak.correctTitle' : 'quiz.correct') : t(isStreakMode ? 'streak.incorrectTitle' : 'quiz.incorrect')}</strong>
          {isStreakMode && (
            <p>
              {isCurrentAnswerCorrect
                ? t('streak.correctBody', { count: currentStreak })
                : t('streak.incorrectBody')}
            </p>
          )}
          {currentQuestion.explanation && <p>{currentQuestion.explanation}</p>}
        </div>
      )}
    </div>
  );
}

function StreakSummary({ currentStreak, bestStreak, currentQuestionIndex, total, progress, streakBump, t, copy }) {
  return (
    <div className="streak-summary-wrap">
      <div className="streak-stats-row">
        <div className={`streak-stat-card streak-stat-primary${streakBump ? ' is-bumping' : ''}`}>
          <span>{t('streak.currentStreak')}</span>
          <strong>{currentStreak}</strong>
        </div>
        <div className="streak-stat-card">
          <span>{t('streak.bestStreak')}</span>
          <strong>{bestStreak}</strong>
        </div>
        <div className="streak-stat-card">
          <span>{t('streak.questionCounter')}</span>
          <strong>{currentQuestionIndex + 1}/{total}</strong>
        </div>
      </div>

      <div className="streak-progress" aria-label={copy.progressAria(Math.round(progress))}>
        <div className="streak-progress-fill" style={{ width: `${progress}%` }} />
      </div>
    </div>
  );
}

function StudyTopicChips({ topicDisplay }) {
  if (!topicDisplay) {
    return null;
  }

  const chips = [topicDisplay.subTopic, topicDisplay.mainTopic]
    .filter(Boolean)
    .slice(0, 2);

  if (chips.length === 0) {
    return null;
  }

  return (
    <div className="study-topic-chips">
      {chips.map((chip) => (
        <span key={chip} className="study-topic-chip" title={chip}>{chip}</span>
      ))}
    </div>
  );
}

function StudyEmptyState({ title, body, resetLabel, onReset, onBack, backLabel }) {
  return (
    <div className="study-panel">
      <div className="card study-card study-empty-card">
        <h2>{title}</h2>
        <p>{body}</p>
        <div className="study-action-row">
          {resetLabel && onReset && (
            <button className="button button-secondary" onClick={onReset}>
              {resetLabel}
            </button>
          )}
          <button className="button" onClick={onBack}>
            {backLabel}
          </button>
        </div>
      </div>
    </div>
  );
}

function getModeHint(mode, copy) {
  if (mode === 'flashcards') {
    return copy.flashHint;
  }
  if (mode === 'test') {
    return copy.testHint;
  }
  if (mode === 'streak') {
    return copy.streakHint;
  }
  return copy.quizHint;
}

function getModeTabLabel(mode, copy) {
  if (mode === 'flashcards') {
    return copy.flashTab;
  }
  if (mode === 'test') {
    return copy.testTab;
  }
  if (mode === 'streak') {
    return copy.streakTab;
  }
  return copy.quizTab;
}

export default StudyHub;
