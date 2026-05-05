import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { documentService, gameService, learningService, questionService } from '../services/api';
import { useLanguage } from '../context/LanguageContext';
import { formatTopicForDisplay } from '../services/topicDisplay';

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

function StudyHub({ documentId: providedDocumentId, forcedMode, showShell = true }) {
  const { language, t } = useLanguage();
  const { documentId: routeDocumentId, mode: routeMode } = useParams();
  const navigate = useNavigate();
  const location = useLocation();
  const documentId = providedDocumentId || routeDocumentId;
  const shouldShowShell = showShell && !providedDocumentId;
  const [refreshToken, setRefreshToken] = useState(0);
  const [metaLoading, setMetaLoading] = useState(shouldShowShell);
  const [documentName, setDocumentName] = useState('');
  const [questionCount, setQuestionCount] = useState(0);
  const [metaError, setMetaError] = useState('');
  const [isRegenerating, setIsRegenerating] = useState(false);
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
        back: 'Quay lại',
        backToWorkspace: 'Về Workspace',
        sourceFallback: 'Tài liệu đang học',
        emptySource: 'Chưa có tên source',
        statusReady: 'Question bank sẵn sàng',
        statusMissing: 'Chưa có question bank',
        statusRefreshing: 'Đang cập nhật question bank...',
        countLabel: 'Số câu hỏi',
        sourceLabel: 'Source hiện tại',
        bankLabel: 'Question bank',
        regenerate: 'Tạo lại câu hỏi',
        regenerating: 'Đang tạo lại...',
        generated: 'Đã làm mới bộ câu hỏi.',
        modeSwitcher: 'Chế độ học',
        quizTab: 'Quiz',
        flashTab: 'Flashcards',
        testTab: 'Test',
        testHint: 'Làm bài kiểm tra liền mạch và xem điểm ở cuối.',
        testStartTitle: 'Sẵn sàng làm Test',
        testStartBody: 'Bài test ghi nhận kết quả đánh giá năng lực. Trong khi làm bài sẽ không hiện đáp án hay giải thích.',
        testStartCta: 'Bắt đầu test',
        testSubmitting: 'Đang nộp test...',
        masteryAfterTest: 'Mastery sau test',
        duration: 'Thời lượng',
        reviewWeakQuestions: 'Ôn lại câu yếu',
        noWeakQuestions: 'Không có câu yếu trong lần test này.',
        weakReviewHint: 'Ôn tập các câu bị sai trong test vừa rồi, có phản hồi sau mỗi câu.',
        weakReviewCompleteHint: 'Đã hoàn thành phiên ôn lại câu yếu.',
        testSubmitError: 'Chưa thể nộp test. Hãy thử lại.',
        testCompletedTitle: 'Hoàn thành Test',
        testCompleteHint: 'Bài test đã hoàn tất. Kết quả chỉ được hiển thị sau câu cuối.',
        testEmptyHint: 'Hãy tạo question bank trước khi vào Test mode.',
        progressLabel: 'Tiến độ học',
        progressLoading: 'Đang tải tiến độ...',
        progressError: 'Chưa tải được tiến độ.',
        totalQuestions: 'Tổng số câu',
        attemptedQuestions: 'Đã làm',
        averageMastery: 'Mastery TB',
        averageMemory: 'Memory TB',
        weakQuestions: 'Câu yếu',
        masteredQuestions: 'Đã vững',
        streakTab: 'Streak',
        quizHint: 'Ôn nhanh bằng câu hỏi trắc nghiệm.',
        flashHint: 'Lật thẻ để ghi nhớ đáp án.',
        streakHint: 'Giữ chuỗi đúng liên tiếp thật gọn và tập trung.',
        quizCompleteHint: 'Xem kết quả rồi đổi mode ngay trong cùng một khu học tập.',
        quizEmptyHint: 'Chưa có câu hỏi khả dụng cho source này.',
        streakEmptyHint: 'Hãy tạo question bank trước khi vào streak mode.',
        progressAria: (percent) => `Tiến độ streak ${percent} phần trăm`,
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
      countLabel: 'Question count',
      sourceLabel: 'Current source',
      bankLabel: 'Question bank',
      regenerate: 'Regenerate questions',
      regenerating: 'Regenerating...',
      generated: 'Question bank refreshed.',
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
      progressError: 'Progress is not available yet.',
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
      setProgressError(error?.response?.data?.message || error?.message || copy.progressError);
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

  useEffect(() => {
    if (!shouldShowShell || !documentId) {
      return;
    }

    let cancelled = false;

    const loadMeta = async () => {
      setMetaLoading(true);
      setMetaError('');

      try {
        const [documentData, summaryData] = await Promise.all([
          documentService.getDocument(documentId),
          learningService.getDocumentSummary(documentId).catch(() => null),
        ]);

        if (cancelled) {
          return;
        }

        setDocumentName(documentData?.fileName || documentData?.name || `${copy.sourceFallback} #${documentId}`);

        setQuestionCount(Number(summaryData?.totalQuestions || 0));
      } catch (error) {
        if (cancelled) {
          return;
        }

        setMetaError(error?.message || '');
        setDocumentName(`${copy.sourceFallback} #${documentId}`);
        setQuestionCount(0);
      } finally {
        if (!cancelled) {
          setMetaLoading(false);
        }
      }
    };

    loadMeta();

    return () => {
      cancelled = true;
    };
  }, [copy.sourceFallback, documentId, refreshToken, shouldShowShell]);

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

  const handleRegenerate = async () => {
    if (!documentId || isRegenerating) {
      return;
    }

    setIsRegenerating(true);
    setRegenerateMessage('');

    try {
      await questionService.generateQuestions(documentId, DEFAULT_QUESTION_COUNT);
      setRegenerateMessage(copy.generated);
      setRefreshToken((current) => current + 1);
    } catch (error) {
      setRegenerateMessage(error?.response?.data?.message || error?.message || t('workspace.study.failed'));
    } finally {
      setIsRegenerating(false);
    }
  };

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
              questionCount={questionCount}
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
  questionCount,
  regenerateMessage,
  regenerating,
}) {
  const bankStatus = regenerating
    ? copy.statusRefreshing
    : questionCount > 0
      ? copy.statusReady
      : copy.statusMissing;

  return (
    <aside className="study-sidebar">
      <div className="study-sidebar-card">
        <span className="study-sidebar-label">{copy.bankLabel}</span>
        <strong>{bankStatus}</strong>
        <p>{metaError || regenerateMessage || (questionCount > 0 ? `${questionCount}` : '0')}</p>
      </div>

      <div className="study-sidebar-card">
        <span className="study-sidebar-label">{copy.countLabel}</span>
        <strong>{questionCount}</strong>
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
      </div>

      <div className="study-sidebar-actions">
        <button type="button" className="button button-secondary" onClick={onBack}>
          {copy.backToWorkspace}
        </button>
        <button type="button" className="button" onClick={onRegenerate} disabled={regenerating}>
          {regenerating ? copy.regenerating : copy.regenerate}
        </button>
      </div>

      {metaLoading && <p className="study-sidebar-note">...</p>}
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
  const [testResult, setTestResult] = useState(null);
  const [testSubmitting, setTestSubmitting] = useState(false);
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
        const data = await gameService.getQuizGame(documentId, DEFAULT_QUESTION_COUNT, {
          includeAnswers: !isTestMode,
        });
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
    setTestResult(null);
    setTestSubmitting(false);
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
    setTestSubmitting(true);
    setTestSubmitError('');

    try {
      const submittedResult = await learningService.submitTestResult({
        documentId: Number(documentId),
        testType: LEARNING_TEST_TYPE_VALUES.practiceTest,
        startedAt: testStartedAt?.toISOString(),
        durationMs: testStartedAt ? Math.max(0, Date.now() - testStartedAt.getTime()) : null,
        attemptsAlreadyRecorded: false,
        answers: nextAnswers.map((answer) => ({
          questionId: answer.questionId,
          selectedAnswer: answer.selectedAnswer,
          responseTimeMs: answer.responseTimeMs,
        })),
      });

      setTestResult(submittedResult);
      setFinalScore(Math.round(Number(submittedResult?.score || 0)));
      if (onAttemptRecorded) {
        onAttemptRecorded();
      }
    } catch (error) {
      console.warn('Could not submit learning test.', error);
      setTestSubmitError(error?.response?.data?.message || error?.message || copy.testSubmitError);
    } finally {
      setTestSubmitting(false);
    }
  };

  const handleSubmitAnswer = async () => {
    if (!selectedAnswer || !currentQuestion || testSubmitting) {
      return;
    }

    const isCorrect = currentQuestion.correctAnswer === selectedAnswer;
    const responseTimeMs = getResponseTimeMs();

    if (isAssessmentTest) {
      const nextAnswers = [
        ...answers,
        {
          questionId: currentQuestion.id,
          selectedAnswer,
          isCorrect: false,
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
    setCurrentQuestionIndex(0);
    setSelectedAnswer(null);
    setShowResult(false);
    setAnswers([]);
    setFinalScore(null);
    setTestState(isTestMode ? 'ready' : 'practice');
    setTestStartedAt(null);
    setTestResult(null);
    setTestSubmitting(false);
    setTestSubmitError('');
    setWeakReviewQuestionIds(null);
    setCurrentStreak(0);
    setBestStreak(0);
    setStreakBump(false);
    submittedQuestionKeysRef.current = new Set();
    questionStartTimeRef.current = Date.now();
  };

  const startTest = () => {
    setCurrentQuestionIndex(0);
    setSelectedAnswer(null);
    setShowResult(false);
    setAnswers([]);
    setFinalScore(null);
    setTestResult(null);
    setTestSubmitError('');
    setWeakReviewQuestionIds(null);
    setTestStartedAt(new Date());
    setTestState('inProgress');
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

  if (questions.length === 0) {
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
            <ProgressSummaryItem label={copy.totalQuestions} value={questions.length} />
            <ProgressSummaryItem label={copy.weakQuestions} value={copy.reviewWeakQuestions} />
          </div>
          <div className="study-action-row">
            <button className="button" onClick={startTest}>
              {copy.testStartCta}
            </button>
            {!showShell && (
              <button className="button button-secondary" onClick={onBack}>
                {copy.backToWorkspace}
              </button>
            )}
          </div>
        </div>
      </div>
    );
  }

  if (finalScore !== null) {
    const totalCorrect = answers.filter((answer) => answer.isCorrect).length;
    const completedTitle = isStreakMode
      ? t('streak.completedTitle')
      : isAssessmentTest
        ? copy.testCompletedTitle
        : t('quiz.completed');
    const completedHint = isStreakMode
      ? t('streak.completedSubtitle')
      : isAssessmentTest
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
