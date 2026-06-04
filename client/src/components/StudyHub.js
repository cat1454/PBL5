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
import {
  getReadinessLabel,
  getReadinessMessage,
  normalizeGenerationReadiness,
} from '../services/generationReadiness';
import { trackEvent } from '../services/analytics';
import FlashcardQueueTabs from './study/FlashcardQueueTabs';
import StreakSummary from './study/StreakSummary';
import StudySessionBrief from './study/StudySessionBrief';
import StudySessionRecap from './study/StudySessionRecap';

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
const FLASHCARD_QUEUE_KEYS = ['due', 'weak', 'new', 'mastered'];

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
  const [questionMetrics, setQuestionMetrics] = useState(null);
  const [generationReadiness, setGenerationReadiness] = useState(null);
  const [metricsLoading, setMetricsLoading] = useState(shouldShowShell);
  const [metricsError, setMetricsError] = useState('');
  const [detailsOpen, setDetailsOpen] = useState(false);

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
        language: 'vi',
        back: 'Quay lại',
        backToWorkspace: 'Về Workspace',
        backToStudyHub: 'Về Study Hub',
        retryLoad: 'Tải lại',
        sourceFallback: 'Tài liệu đang học',
        emptySource: 'Chưa có tên source',
        statusReady: 'Question bank sẵn sàng',
        statusMissing: 'Chưa có question bank',
        statusRefreshing: 'Đang cập nhật question bank...',
        statusError: 'Không tải được dữ liệu học tập',
        statusNeedsQuestions: 'Cần tạo question bank',
        countLabel: 'Số câu hỏi',
        sourceLabel: 'Source hiện tại',
        sourceStatusLabel: 'Trạng thái source',
        sourceStatusReady: 'Sẵn sàng để học',
        sourceStatusProcessing: 'Đang xử lý OCR/AI',
        sourceStatusProcessingDetail: 'Source vẫn đang được OCR/AI xử lý. Nội dung học sẽ sẵn sàng sau khi xử lý xong.',
        sourceStatusFailed: 'Xử lý thất bại',
        sourceStatusFailedDetail: 'Source xử lý thất bại. Hãy kiểm tra lại tài liệu hoặc thử upload lại.',
        sourceStatusUnknown: 'Chưa xác định',
        bankLabel: 'Question bank',
        regenerate: 'Tạo lại câu hỏi',
        generateQuestions: 'Tạo câu hỏi',
        regenerating: 'Đang tạo lại...',
        generated: 'Đã làm mới question bank.',
        generatedRecovered: 'Đã khôi phục question bank sau khi mất tiến trình.',
        regenerationReplaceHint: 'Tạo lại sẽ thay thế question bank hiện tại.',
        questionProgressLost: 'Mất tiến trình tạo câu hỏi. Hãy kiểm tra question bank hiện tại hoặc thử lại.',
        bankMissingBody: 'Chưa có question bank nào. Hãy tạo câu hỏi trước.',
        bankMissingReadyBody: 'Source đã xử lý xong nhưng chưa có question bank. Tạo câu hỏi để mở Quiz, Flashcards, Test và Streak.',
        bankGeneratingBody: 'Question bank đang được tạo. Bạn có thể ở lại để theo dõi tiến trình.',
        studyDataError: 'Không tải được thông tin Study Hub cho source này.',
        studyDataLoading: 'Đang tải thông tin Study Hub...',
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
        progressError: 'Tiến độ học tập hiện chưa khả dụng.',
        progressUnavailable: 'Chưa có dữ liệu tiến độ. Hãy bắt đầu một phiên học để tạo dữ liệu đầu tiên.',
        metricsLabel: 'Chất lượng AI',
        metricsLoading: 'Đang tải chỉ số AI...',
        metricsError: 'Chưa tải được chỉ số AI output.',
        metricsUnavailable: 'Chưa có chỉ số AI cho question bank này.',
        coverage: 'Coverage',
        validRate: 'Valid Rate',
        averageQualityScore: 'Quality TB',
        missingTopics: 'Topic còn thiếu',
        moreTopics: '+{{count}} topic khác',
        totalQuestions: 'Tổng số câu',
        attemptedQuestions: 'Đã làm',
        averageMastery: 'Mastery TB',
        averageMemory: 'Memory TB',
        weakQuestions: 'Câu yếu',
        masteredQuestions: 'Đã vững',
        streakTab: 'Streak',
        quizHint: 'Ôn nhanh bằng câu hỏi trắc nghiệm.',
        flashHint: 'Lật thẻ để ghi nhớ đáp án.',
        streakHint: 'Giữ chuỗi đúng liên tiếp để tập trung hơn.',
        quizCompleteHint: 'Xem kết quả rồi đổi mode ngay trong cùng một khu học tập.',
        quizEmptyHint: 'Chưa có câu hỏi khả dụng cho source này.',
        streakEmptyHint: 'Hãy tạo question bank trước khi vào streak mode.',
        loadErrorTitleQuiz: 'Không tải được bộ câu hỏi quiz',
        loadErrorTitleFlashcards: 'Không tải được bộ flashcards',
        loadErrorBody: 'Bạn vẫn đang ở trong luồng học hiện tại. Hãy thử tải lại hoặc quay lại Workspace để kiểm tra question bank.',
        progressAria: (percent) => `Tiến độ streak ${percent} phần trăm`,
        sessionBriefTitle: 'Mục tiêu phiên học',
        sessionBriefBody: 'Hoàn thành {{count}} câu trong khoảng {{minutes}} phút, xem phản hồi ngắn sau mỗi câu.',
        correctMicrocopy: 'Chính xác. Bạn đang giữ nhịp rất tốt.',
        incorrectMicrocopy: 'Chưa đúng. Xem giải thích ngắn rồi thử câu tiếp theo.',
        flashQueues: {
          due: 'Đến hạn',
          weak: 'Cần ôn',
          new: 'Thẻ mới',
          mastered: 'Đã vững',
        },
        flashAssessForgot: 'Quên rồi',
        flashAssessUnsure: 'Lưỡng lự',
        flashAssessKnown: 'Nhớ chắc',
        flashAssessing: 'Đang ghi nhận...',
        streakRecovery: 'Không sao, phiên này vẫn còn nhịp hồi phục. Xem giải thích rồi tiếp tục chuỗi hôm nay.',
        streakTier: 'Combo {{tier}}',
        showDetails: 'Chi tiết',
        hideDetails: 'Ẩn chi tiết',
        doneShort: 'xong',
        avgShort: 'TB',
      };
    }

    return {
      language: 'en',
      back: 'Back',
      backToWorkspace: 'Back to Workspace',
      backToStudyHub: 'Back to Study Hub',
      retryLoad: 'Retry',
      sourceFallback: 'Current study source',
      emptySource: 'No source name available',
      statusReady: 'Question bank ready',
      statusMissing: 'Question bank missing',
      statusRefreshing: 'Refreshing question bank...',
      statusError: 'Study data unavailable',
      statusNeedsQuestions: 'Questions needed',
      countLabel: 'Question count',
      sourceLabel: 'Current source',
      sourceStatusLabel: 'Source status',
      sourceStatusReady: 'Ready to study',
      sourceStatusProcessing: 'OCR and AI processing',
      sourceStatusProcessingDetail: 'The source is still being processed by OCR/AI. Study content will be ready after processing completes.',
      sourceStatusFailed: 'Processing failed',
      sourceStatusFailedDetail: 'Source processing failed. Check the document or upload it again.',
      sourceStatusUnknown: 'Status unavailable',
      bankLabel: 'Question bank',
      regenerate: 'Regenerate questions',
      generateQuestions: 'Generate questions',
      regenerating: 'Regenerating...',
      generated: 'Question bank refreshed.',
      generatedRecovered: 'Recovered the question bank after progress tracking was lost.',
      regenerationReplaceHint: 'Regenerate will replace the active question bank.',
      questionProgressLost: 'Question generation progress was lost. Check the current question bank or try again.',
      bankMissingBody: 'No question bank is available yet. Generate questions first.',
      bankMissingReadyBody: 'This source is processed, but it does not have a question bank yet. Generate questions to unlock Quiz, Flashcards, Test, and Streak.',
      bankGeneratingBody: 'The question bank is still generating. Stay here to monitor progress.',
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
      progressUnavailable: 'No learning history yet. Start a study session to populate this summary.',
      metricsLabel: 'AI output metrics',
      metricsLoading: 'Loading AI metrics...',
      metricsError: 'AI output metrics are not available yet.',
      metricsUnavailable: 'No AI metrics are available for this question bank yet.',
      coverage: 'Coverage',
      validRate: 'Valid Rate',
      averageQualityScore: 'Avg quality',
      missingTopics: 'Missing topics',
      moreTopics: '+{{count}} more topics',
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
      loadErrorTitleQuiz: 'Could not load the quiz question set',
      loadErrorTitleFlashcards: 'Could not load the flashcards set',
      loadErrorBody: 'You can retry here or go back to the workspace to inspect the current question bank.',
      progressAria: (percent) => `Streak progress ${percent} percent`,
      sessionBriefTitle: 'Session goal',
      sessionBriefBody: 'Finish {{count}} questions in about {{minutes}} minutes and review short feedback after each answer.',
      correctMicrocopy: 'Correct. You are keeping a strong rhythm.',
      incorrectMicrocopy: 'Not yet. Read the short explanation, then try the next one.',
      flashQueues: {
        due: 'Due now',
        weak: 'Weak cards',
        new: 'New cards',
        mastered: 'Mastered',
      },
      flashAssessForgot: 'Forgot it',
      flashAssessUnsure: 'Unsure',
      flashAssessKnown: 'Know it',
      flashAssessing: 'Recording...',
      streakRecovery: 'No worries, this session still has a recovery rhythm. Review the explanation and continue today.',
      streakTier: 'Combo {{tier}}',
      showDetails: 'Details',
      hideDetails: 'Hide details',
      doneShort: 'done',
      avgShort: 'Avg',
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

  const loadQuestionMetrics = useCallback(async ({ silent = false } = {}) => {
    if (!documentId || !shouldShowShell) {
      setQuestionMetrics(null);
      setMetricsLoading(false);
      setMetricsError('');
      return;
    }

    if (!silent) {
      setMetricsLoading(true);
      setMetricsError('');
    }

    try {
      const metrics = await questionService.getQuestionMetrics(documentId);
      setQuestionMetrics(metrics);
      setMetricsError('');
    } catch (error) {
      setQuestionMetrics(null);
      setMetricsError(getApiErrorMessage(error, copy.metricsError));
    } finally {
      if (!silent) {
        setMetricsLoading(false);
      }
    }
  }, [copy.metricsError, documentId, shouldShowShell]);

  useEffect(() => {
    loadQuestionMetrics();
  }, [loadQuestionMetrics, refreshToken]);

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
      setGenerationReadiness(normalizeGenerationReadiness(documentData?.generationReadiness));
      setQuestionCount(nextQuestionCount);
      setMetaError('');
      return documentData;
    } catch (error) {
      setDocumentName(`${copy.sourceFallback} #${documentId}`);
      setDocumentStatus(null);
      setGenerationReadiness(null);
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
      loadQuestionMetrics({ silent: true }),
    ]);
    setRefreshToken((current) => current + 1);
  }, [loadDocumentMeta, loadProgressSummary, loadQuestionMetrics]);

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
    trackEvent('study_mode_selected', { documentId, mode: nextMode });
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

    navigate(`/question-studio/${documentId}`);
  }, [documentId, isRegenerating, navigate]);

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
            <button
              type="button"
              className="button button-secondary study-details-toggle"
              onClick={() => setDetailsOpen((current) => !current)}
              aria-expanded={detailsOpen}
            >
              {detailsOpen ? copy.hideDetails : copy.showDetails}
            </button>
          </div>

          <div className={`study-main-grid${detailsOpen ? ' study-main-grid-with-details' : ''}`}>
            <div className="study-main-column">
              <StudyModeSwitcher
                activeMode={activeMode}
                onModeChange={handleModeChange}
                copy={copy}
                progressSummary={progressSummary}
                questionCount={questionCount}
              />
              <StudyModePanel
                documentId={documentId}
                mode={activeMode}
                onBack={handleBack}
                t={t}
                copy={copy}
                showShell
                refreshToken={refreshToken}
                onAttemptRecorded={handleAttemptRecorded}
                progressSummary={progressSummary}
              />
            </div>

            {detailsOpen && (
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
                metricsError={metricsError}
                metricsLoading={metricsLoading}
                questionMetrics={questionMetrics}
                questionGenerationError={questionGenerationError}
                questionGenerationProgress={questionGenerationProgress}
                questionGenerationRecovered={questionGenerationRecovered}
                questionCount={questionCount}
                documentStatus={documentStatus}
                generationReadiness={generationReadiness}
                regenerateMessage={regenerateMessage}
                regenerating={isRegenerating}
              />
            )}
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
          progressSummary={progressSummary}
        />
      )}
    </div>
  );
}

function StudyModeSwitcher({ activeMode, onModeChange, copy, progressSummary, questionCount }) {
  const tabStats = getStudyModeTabStats(copy, progressSummary, questionCount);

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
            <span>{getModeTabLabel(mode, copy)}</span>
            <small>{tabStats[mode]}</small>
          </button>
        ))}
      </div>
    </div>
  );
}

function getStudyModeTabStats(copy, progressSummary, questionCount) {
  const total = Number(progressSummary?.totalQuestions || questionCount || 0);
  const attempted = Number(progressSummary?.attemptedQuestions || 0);
  const mastered = Number(progressSummary?.masteredCount || 0);
  const weak = Number(progressSummary?.weakCount || 0);
  const currentStreak = Number(progressSummary?.currentStreakDays || progressSummary?.currentStreak || 0);

  return {
    quiz: total > 0 ? `${attempted}/${total}` : '0/0',
    flashcards: `${mastered} ${copy.doneShort}`,
    test: weak > 0 ? `${weak} ${copy.weakQuestions}` : `0 ${copy.weakQuestions}`,
    streak: `${currentStreak}`,
  };
}

function StudyMiniProgress({ copy, current, total, progress, progressSummary }) {
  const mastered = Number(progressSummary?.masteredCount || 0);
  const averageMastery = progressSummary?.averageMasteryScore === undefined || progressSummary?.averageMasteryScore === null
    ? 0
    : Math.round(Number(progressSummary.averageMasteryScore));

  return (
    <div className="study-mini-progress">
      <div className="study-mini-progress-main">
        <strong>{copy.countLabel} {current}/{total}</strong>
        <div className="study-progress-track">
          <div className="study-progress-fill" style={{ width: `${progress}%` }} />
        </div>
      </div>
      <div className="study-mini-progress-stats">
        <span>{copy.masteredQuestions} <strong>{mastered}</strong></span>
        <span>{copy.avgShort} <strong>{averageMastery}%</strong></span>
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
  metricsError,
  metricsLoading,
  questionMetrics,
  questionGenerationError,
  questionGenerationProgress,
  questionGenerationRecovered,
  questionCount,
  documentStatus,
  generationReadiness,
  regenerateMessage,
  regenerating,
}) {
  const sourceStatus = getStudySourceStatus(documentStatus, copy);
  const readinessMessage = getReadinessMessage(generationReadiness, copy.language);
  const hasQuestionBank = questionCount > 0;
  const bankStatus = metaError
    ? copy.statusError
    : regenerating
      ? copy.statusRefreshing
      : hasQuestionBank
        ? copy.statusReady
        : copy.statusNeedsQuestions;

  const bankDetail = metaError
    ? metaError
    : questionGenerationError
      ? questionGenerationError
      : regenerateMessage
        ? regenerateMessage
        : regenerating
          ? copy.bankGeneratingBody
        : hasQuestionBank
          ? `${questionCount}`
          : copy.bankMissingReadyBody;

  return (
    <aside className="study-sidebar">
      <div className="study-sidebar-card">
        <span className="study-sidebar-label">{copy.sourceStatusLabel}</span>
        <strong>{hasQuestionBank ? sourceStatus.title : copy.statusNeedsQuestions}</strong>
        <p>{hasQuestionBank ? sourceStatus.detail : copy.bankMissingReadyBody}</p>
        {generationReadiness && (
          <span className={`generation-readiness-badge tone-${generationReadiness.tone}`}>
            {getReadinessLabel(generationReadiness, copy.language)}
          </span>
        )}
      </div>

      {readinessMessage && (
        <div className={`study-sidebar-card generation-readiness-card tone-${generationReadiness.tone}`}>
          <span className="study-sidebar-label">{readinessMessage.title}</span>
          <p>{readinessMessage.body}</p>
        </div>
      )}

      <div className="study-sidebar-card">
        <span className="study-sidebar-label">{copy.bankLabel}</span>
        <strong>{bankStatus}</strong>
        <p>{bankDetail}</p>
      </div>

      <div className="study-sidebar-card">
        <span className="study-sidebar-label">{copy.countLabel}</span>
        <strong>{questionCount}</strong>
        <p>{hasQuestionBank ? copy.regenerationReplaceHint : copy.bankMissingBody}</p>
      </div>

      <ProgressSummaryCard
        copy={copy}
        progressError={progressError}
        progressLoading={progressLoading}
        progressSummary={progressSummary}
      />

      <QuestionMetricsCard
        copy={copy}
        metricsError={metricsError}
        metricsLoading={metricsLoading}
        questionMetrics={questionMetrics}
      />

      <div className="study-sidebar-card">
        <span className="study-sidebar-label">{copy.sourceLabel}</span>
        <strong className="study-sidebar-source">{documentName || copy.emptySource}</strong>
        <p>{metaError || sourceStatus.detail}</p>
      </div>

      <div className="study-sidebar-actions">
        <button type="button" className="button button-secondary" onClick={onBack}>
          {copy.backToWorkspace}
        </button>
        <button type="button" className="button" onClick={onRegenerate} disabled={regenerating}>
          {regenerating ? copy.regenerating : hasQuestionBank ? copy.regenerate : copy.generateQuestions}
        </button>
      </div>

      {questionGenerationProgress && (
        <p className="study-sidebar-note">
          {Math.round(Number(questionGenerationProgress.percent || 0))}% - {questionGenerationProgress.message || questionGenerationProgress.stageLabel || copy.regenerating}
          {questionGenerationRecovered ? ` - ${copy.generatedRecovered}` : ''}
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
      ) : !progressSummary ? (
        <p>{copy.progressUnavailable}</p>
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

function QuestionMetricsCard({ copy, metricsError, metricsLoading, questionMetrics }) {
  const formatPercent = (value) => {
    if (value === undefined || value === null || Number.isNaN(Number(value))) {
      return '0%';
    }

    return `${Math.round(Number(value))}%`;
  };

  const missingTopics = Array.isArray(questionMetrics?.missingTopics)
    ? questionMetrics.missingTopics.filter(Boolean)
    : [];
  const visibleMissingTopics = missingTopics.slice(0, 3).map((topic) => {
    const normalized = String(topic).replace(/\s+/g, ' ').trim();
    return normalized.length > 72 ? `${normalized.slice(0, 72)}...` : normalized;
  });
  const hiddenTopicCount = Math.max(0, missingTopics.length - visibleMissingTopics.length);

  return (
    <div className="study-sidebar-card study-question-metrics-card">
      <span className="study-sidebar-label">{copy.metricsLabel}</span>
      {metricsLoading ? (
        <p>{copy.metricsLoading}</p>
      ) : metricsError ? (
        <p>{metricsError || copy.metricsError}</p>
      ) : !questionMetrics ? (
        <p>{copy.metricsUnavailable}</p>
      ) : (
        <>
          <div className="study-question-metrics-grid">
            <ProgressSummaryItem label={copy.coverage} value={formatPercent(questionMetrics.coverage)} />
            <ProgressSummaryItem label={copy.validRate} value={formatPercent(questionMetrics.validRate)} />
            <ProgressSummaryItem label={copy.averageQualityScore} value={formatPercent(questionMetrics.averageQualityScore)} />
          </div>
          {missingTopics.length > 0 && (
            <div className="study-missing-topics">
              <span>{copy.missingTopics}</span>
              <div className="study-topic-chips study-missing-topic-chips">
                {visibleMissingTopics.map((topic) => (
                  <span key={topic} className="study-topic-chip" title={topic}>{topic}</span>
                ))}
                {hiddenTopicCount > 0 && (
                  <span className="study-topic-chip">{copy.moreTopics.replace('{{count}}', hiddenTopicCount)}</span>
                )}
              </div>
            </div>
          )}
        </>
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

function StudyModePanel({ documentId, mode, onBack, t, copy, showShell, refreshToken, onAttemptRecorded, progressSummary }) {
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
      progressSummary={progressSummary}
    />
  );
}

function QuestionModePane({ documentId, mode, onBack, t, copy, refreshToken, showShell, onAttemptRecorded, progressSummary }) {
  const navigate = useNavigate();
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState('');
  const [reloadKey, setReloadKey] = useState(0);
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
      setLoadError('');
      trackEvent('study_session_started', { documentId, mode });
      try {
        if (isTestMode) {
          setAllQuestions([]);
          return;
        }

        const data = await gameService.getQuizGame(documentId, DEFAULT_QUESTION_COUNT);
        setAllQuestions(Array.isArray(data?.questions) ? data.questions : []);
      } catch (error) {
        console.error(error);
        setLoadError(getApiErrorMessage(error, t('quiz.loadError')));
      } finally {
        setLoading(false);
      }
    };

    loadQuiz();
  }, [documentId, isTestMode, mode, refreshToken, reloadKey, t]);

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
  }, [documentId, mode, refreshToken, reloadKey]);

  useEffect(() => {
    setCurrentQuestionIndex(0);
    setSelectedAnswer(null);
    setShowResult(false);
    setAnswers([]);
    setFinalScore(null);
    setCurrentStreak(0);
    setBestStreak(0);
    setStreakBump(false);
    submittedQuestionKeysRef.current = new Set();
    questionStartTimeRef.current = Date.now();
  }, [hideLowConfidence]);

  const activeQuestionIndex = questions.length > 0
    ? Math.min(currentQuestionIndex, questions.length - 1)
    : 0;
  const currentQuestion = questions[activeQuestionIndex];
  const progress = questions.length > 0 ? ((activeQuestionIndex + 1) / questions.length) * 100 : 0;
  const topicDisplay = currentQuestion ? formatTopicForDisplay(currentQuestion.topic) : null;
  const quality = currentQuestion?.quality || {};

  useEffect(() => {
    questionStartTimeRef.current = Date.now();
  }, [currentQuestion?.id, activeQuestionIndex, mode]);

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
    const submissionKey = `${submissionMode}:${currentQuestion.id}:${activeQuestionIndex}`;
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
      trackEvent('study_question_answered', {
        documentId,
        mode: submissionMode,
        questionId: currentQuestion.id,
        isCorrect,
      });
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
    trackEvent('study_session_completed', {
      documentId,
      mode,
      score,
      totalQuestions: questions.length,
      correctCount: totalCorrect,
    });
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
      trackEvent('study_session_completed', {
        documentId,
        mode: 'test',
        score: Math.round(Number(submittedResult?.score || 0)),
        totalQuestions: submittedResult?.totalQuestions || nextAnswers.length,
        correctCount: submittedResult?.correctCount || 0,
      });
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
        trackEvent('streak_recovery_used', {
          documentId,
          questionId: currentQuestion.id,
          bestStreak: nextBestStreak,
        });
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

  if (loadError) {
    return (
      <StudyLoadErrorState
        title={copy.loadErrorTitleQuiz}
        body={loadError || copy.loadErrorBody}
        retryLabel={copy.retryLoad}
        onRetry={() => setReloadKey((current) => current + 1)}
        onBack={() => navigate(`/study/${documentId}`)}
        backLabel={copy.backToStudyHub}
        onWorkspace={() => navigate('/workspaces')}
        workspaceLabel={copy.backToWorkspace}
      />
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
          <StudySessionRecap
            title={completedTitle}
            subtitle={completedHint}
            scorePercent={resultScore}
            scoreTone={resultScore >= 70 ? '#28a745' : '#dc3545'}
            scoreLine={t(isStreakMode ? 'streak.scoreLine' : 'quiz.scoreLine', { correct: resultCorrect, total: resultTotal })}
            inlineMeta={isStreakMode ? t('streak.bestStreakLine', { count: bestStreak }) : null}
            testMetrics={testResult && (
              <div className="study-test-result-metrics">
                <ProgressSummaryItem label={copy.duration} value={formatDurationMs(testResult.durationMs)} />
                <ProgressSummaryItem label={copy.masteryAfterTest} value={`${Math.round(Number(testResult.masteryScoreAfterTest || 0))}%`} />
                <ProgressSummaryItem label={copy.averageMemory} value={`${Math.round(Number(testResult.memoryScoreAfterTest || 0))}%`} />
              </div>
            )}
            metrics={(
              <>
                <ProgressSummaryItem label={copy.totalQuestions} value={resultTotal} />
                <ProgressSummaryItem label={t('quiz.correct')} value={resultCorrect} />
                <ProgressSummaryItem label={t('quiz.incorrect')} value={Math.max(0, resultTotal - resultCorrect)} />
              </>
            )}
            weakQuestions={testResult && (
              <WeakQuestionsPanel
                copy={copy}
                weakQuestions={weakQuestions}
                onReviewWeakQuestions={startWeakQuestionReview}
              />
            )}
            actions={(
              <>
                <button className="button" onClick={resetSession}>
                  {t(isStreakMode ? 'streak.retry' : 'quiz.retry')}
                </button>
                <button className="button button-secondary" onClick={onBack}>
                  {copy.backToWorkspace}
                </button>
              </>
            )}
          />
        </div>
      </div>
    );
  }

  return (
    <div className={`study-panel study-panel-${mode}`}>
      <div className={`card study-card${isStreakMode ? ' streak-card' : ''}`}>
        {answers.length === 0 && !showResult && (
          <div className="study-session-brief">
            <div>
              <span>{copy.sessionBriefTitle}</span>
              <strong>{getModeTabLabel(mode, copy)}</strong>
              <p>{formatTemplate(copy.sessionBriefBody, { count: questions.length, minutes: Math.max(2, Math.ceil(questions.length * 0.75)) })}</p>
            </div>
            {isStreakMode && <span className="study-session-pill">{formatTemplate(copy.streakTier, { tier: Math.max(1, Math.floor(currentStreak / 3) + 1) })}</span>}
          </div>
        )}
        <div className="study-card-toolbar">
          <div>
            <h3>{t('quiz.questionProgress', { current: activeQuestionIndex + 1, total: questions.length })}</h3>
            <p className="study-card-caption">{isWeakReviewMode ? copy.weakReviewHint : getModeHint(mode, copy)}</p>
          </div>
          <div className="study-card-tools">
            <button className="button button-secondary" onClick={() => setHideLowConfidence((current) => !current)}>
              {hideLowConfidence ? t('quiz.showAllQuestions') : t('quiz.hideLowConfidence')}
            </button>
            {quality.score !== undefined && quality.score !== null && (
              <span className={`quality-chip ${quality.isLowConfidence ? 'low' : 'good'}`}>
                {quality.isLowConfidence ? t('quiz.reviewNeeded') : 'Verifier'} {quality.score}/100
              </span>
            )}
            {quality.isUnknown && (
              <span className="quality-chip low">
                {t('quiz.noVerifier')}
              </span>
            )}
          </div>
        </div>

        {isStreakMode ? (
          <StreakSummary
            currentStreak={currentStreak}
            bestStreak={bestStreak}
            currentQuestionIndex={activeQuestionIndex}
            total={questions.length}
            progress={progress}
            streakBump={streakBump}
            t={t}
            copy={copy}
          />
        ) : null}

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
          copy={copy}
        />

        <StudyMiniProgress
          copy={copy}
          current={activeQuestionIndex + 1}
          total={questions.length}
          progress={progress}
          progressSummary={progressSummary}
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
  const [loadError, setLoadError] = useState('');
  const [reloadKey, setReloadKey] = useState(0);
  const [allFlashcards, setAllFlashcards] = useState([]);
  const [progressByQuestionId, setProgressByQuestionId] = useState({});
  const [reviewQueue, setReviewQueue] = useState(null);
  const [activeQueue, setActiveQueue] = useState('due');
  const [currentIndex, setCurrentIndex] = useState(0);
  const [flipped, setFlipped] = useState(false);
  const [hideLowConfidence, setHideLowConfidence] = useState(false);
  const [assessing, setAssessing] = useState(false);
  const cardStartTimeRef = useRef(Date.now());

  useEffect(() => {
    const loadFlashcards = async () => {
      setLoading(true);
      setLoadError('');
      trackEvent('study_session_started', { documentId, mode: 'flashcards' });
      try {
        const [data, progress] = await Promise.all([
          gameService.getFlashcards(documentId),
          learningService.getDocumentProgress(documentId).catch(() => []),
        ]);
        setAllFlashcards(Array.isArray(data?.flashcards) ? data.flashcards : []);
        setProgressByQuestionId(Object.fromEntries((Array.isArray(progress) ? progress : []).map((item) => [item.questionId, item])));
        setReviewQueue(await learningService.getReviewQueue(documentId).catch(() => null));
      } catch (error) {
        console.error(error);
        setLoadError(getApiErrorMessage(error, t('flashcards.loadError')));
      } finally {
        setLoading(false);
      }
    };

    loadFlashcards();
  }, [documentId, refreshToken, reloadKey, t]);

  useEffect(() => {
    setCurrentIndex(0);
    setFlipped(false);
    cardStartTimeRef.current = Date.now();
  }, [activeQueue, allFlashcards, hideLowConfidence]);

  useEffect(() => {
    cardStartTimeRef.current = Date.now();
  }, [currentIndex]);

  const queueViewModel = reviewQueue
    ? buildFlashcardQueuesFromReviewQueue(allFlashcards, reviewQueue)
    : buildFlashcardQueues(allFlashcards, progressByQuestionId);
  const flashcards = (hideLowConfidence
    ? queueViewModel[activeQueue].cards.filter((card) => !card.quality?.isLowConfidence)
    : queueViewModel[activeQueue].cards);

  if (loading) {
    return (
      <div className="loading">
        <div className="spinner"></div>
        <p>{t('flashcards.loading')}</p>
      </div>
    );
  }

  if (loadError) {
    return (
      <StudyLoadErrorState
        title={copy.loadErrorTitleFlashcards}
        body={loadError || copy.loadErrorBody}
        retryLabel={copy.retryLoad}
        onRetry={() => setReloadKey((current) => current + 1)}
        onBack={() => navigate(`/study/${documentId}`)}
        backLabel={copy.backToStudyHub}
        onWorkspace={() => navigate('/workspaces')}
        workspaceLabel={copy.backToWorkspace}
      />
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

  const recordFlashcardAssessment = async (assessment) => {
    if (!currentCard || assessing) {
      return;
    }

    const remembered = assessment === 'known';
    const confidence = assessment === 'known' ? 'remembered' : assessment;
    setAssessing(true);
    try {
      await learningService.recordAttempt({
        documentId: Number(documentId),
        questionId: currentCard.id,
        mode: LEARNING_MODE_VALUES.flashcards,
        selectedAnswer: `self:${assessment}`,
        isCorrect: remembered,
        confidence,
        responseTimeMs: Math.max(0, Date.now() - cardStartTimeRef.current),
      });
      trackEvent('flashcard_assessed', {
        documentId,
        questionId: currentCard.id,
        assessment,
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
        <StudySessionBrief
          title={t('flashcards.cardProgress', { current: currentIndex + 1, total: flashcards.length })}
          caption={copy.flashHint}
          actions={(
            <>
            <button className="button button-secondary" onClick={() => setHideLowConfidence((current) => !current)}>
              {hideLowConfidence ? t('flashcards.showAllCards') : t('flashcards.hideLowConfidence')}
            </button>
            {quality.score !== undefined && quality.score !== null && (
              <span className={`quality-chip ${quality.isLowConfidence ? 'low' : 'good'}`}>
                Verifier {quality.score}/100
              </span>
            )}
            </>
          )}
        />

        <FlashcardQueueTabs
          queueKeys={FLASHCARD_QUEUE_KEYS}
          queueViewModel={queueViewModel}
          activeQueue={activeQueue}
          onSelectQueue={setActiveQueue}
          ariaLabel={copy.modeSwitcher}
          labels={copy.flashQueues}
        />

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
                      recordFlashcardAssessment('forgot');
                    }}
                    disabled={assessing}
                  >
                    {assessing ? copy.flashAssessing : copy.flashAssessForgot}
                  </button>
                  <button
                    type="button"
                    className="button button-secondary"
                    onClick={(event) => {
                      event.stopPropagation();
                      recordFlashcardAssessment('unsure');
                    }}
                    disabled={assessing}
                  >
                    {assessing ? copy.flashAssessing : copy.flashAssessUnsure}
                  </button>
                  <button
                    type="button"
                    className="button"
                    onClick={(event) => {
                      event.stopPropagation();
                      recordFlashcardAssessment('known');
                    }}
                    disabled={assessing}
                  >
                    {assessing ? copy.flashAssessing : copy.flashAssessKnown}
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
  copy,
}) {
  const correct = isCurrentAnswerCorrect;

  return (
    <div className={`question-card study-question-card${isStreakMode ? ' streak-question-card' : ''}`}>
      <StudyTopicChips topicDisplay={topicDisplay} />
      <h2>{currentQuestion.questionText}</h2>

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
        <div className={`alert study-answer-alert ${correct ? 'alert-success' : 'alert-error'}`}>
          <strong>{correct ? t(isStreakMode ? 'streak.correctTitle' : 'quiz.correct') : t(isStreakMode ? 'streak.incorrectTitle' : 'quiz.incorrect')}</strong>
          <p>{correct ? copy.correctMicrocopy : (isStreakMode ? copy.streakRecovery : copy.incorrectMicrocopy)}</p>
          {isStreakMode && (
            <p>
              {correct
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

function buildFlashcardQueuesFromReviewQueue(cards, reviewQueue) {
  const cardById = new Map((cards || []).map((card) => [card.id, card]));
  const mapItems = (items) => (Array.isArray(items) ? items : [])
    .map((item) => cardById.get(item.questionId))
    .filter(Boolean);

  const queues = {
    due: mapItems(reviewQueue.due),
    weak: mapItems(reviewQueue.weak),
    new: mapItems(reviewQueue.new),
    mastered: mapItems(reviewQueue.mastered),
  };

  if (queues.due.length === 0) {
    queues.due = [...queues.weak, ...queues.new].filter((card, index, list) => (
      list.findIndex((candidate) => candidate.id === card.id) === index
    ));
  }

  return Object.fromEntries(FLASHCARD_QUEUE_KEYS.map((key) => [key, { cards: queues[key] || [] }]));
}

function buildFlashcardQueues(cards, progressByQuestionId) {
  const queues = {
    due: [],
    weak: [],
    new: [],
    mastered: [],
  };
  const now = Date.now();

  (cards || []).forEach((card) => {
    const progress = progressByQuestionId?.[card.id];
    if (!progress || Number(progress.attemptCount || 0) === 0) {
      queues.new.push(card);
      queues.due.push(card);
      return;
    }

    const mastery = Number(progress.masteryScore || 0);
    const memory = Number(progress.memoryScore || 0);
    const lastReviewedAt = progress.lastReviewedAt ? new Date(progress.lastReviewedAt).getTime() : 0;
    const daysSinceReview = lastReviewedAt ? (now - lastReviewedAt) / 86_400_000 : 999;

    if (mastery >= 86 && memory >= 70) {
      queues.mastered.push(card);
    }

    if (mastery < 60 || Number(progress.wrongCount || 0) > Number(progress.correctCount || 0)) {
      queues.weak.push(card);
    }

    if (memory < 70 || daysSinceReview >= 1) {
      queues.due.push(card);
    }
  });

  if (queues.due.length === 0) {
    queues.due = [...queues.weak, ...queues.new].filter((card, index, list) => (
      list.findIndex((candidate) => candidate.id === card.id) === index
    ));
  }

  return Object.fromEntries(Object.entries(queues).map(([key, value]) => [key, { cards: value }]));
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

function StudyLoadErrorState({ title, body, retryLabel, onRetry, onBack, backLabel, onWorkspace, workspaceLabel }) {
  return (
    <div className="study-panel">
      <div className="card study-card study-empty-card">
        <h2>{title}</h2>
        <p>{body}</p>
        <div className="study-action-row">
          <button className="button" onClick={onRetry}>
            {retryLabel}
          </button>
          <button className="button button-secondary" onClick={onBack}>
            {backLabel}
          </button>
          <button className="button button-secondary" onClick={onWorkspace}>
            {workspaceLabel}
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

function formatTemplate(template, values = {}) {
  return String(template || '').replace(/\{\{(\w+)\}\}/g, (_, key) => (
    values[key] === undefined || values[key] === null ? '' : String(values[key])
  ));
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

function getStudySourceStatus(status, copy) {
  if (status === 3 || String(status) === 'Completed') {
    return {
      title: copy.sourceStatusReady,
      detail: copy.quizHint,
    };
  }

  if (status === 4 || String(status) === 'Failed') {
    return {
      title: copy.sourceStatusFailed,
      detail: copy.sourceStatusFailedDetail,
    };
  }

  if (status === 0 || status === 1 || status === 2 || ['Uploaded', 'Extracting', 'Analyzing'].includes(String(status))) {
    return {
      title: copy.sourceStatusProcessing,
      detail: copy.sourceStatusProcessingDetail,
    };
  }

  return {
    title: copy.sourceStatusUnknown,
    detail: copy.studyDataError,
  };
}

export default StudyHub;
