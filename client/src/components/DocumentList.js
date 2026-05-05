import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useToast } from './common/ToastProvider';
import { documentService, questionService, slideService } from '../services/api';
import { isActiveProgress, normalizeProgressState } from '../services/progress';

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

function getDocumentKind(fileName = '') {
  const lower = fileName.toLowerCase();

  if (lower.endsWith('.pdf')) {
    return { label: 'PDF', tone: 'pdf' };
  }

  if (lower.endsWith('.docx') || lower.endsWith('.doc')) {
    return { label: 'DOC', tone: 'doc' };
  }

  if (lower.endsWith('.png') || lower.endsWith('.jpg') || lower.endsWith('.jpeg') || lower.endsWith('.webp')) {
    return { label: 'IMG', tone: 'image' };
  }

  return { label: 'FILE', tone: 'file' };
}

function ProgressPanel({ tone, kicker, title, summary, metaLines = [], percent = 0, subprogress = null }) {
  return (
    <section className={`documents-progress-card tone-${tone}`}>
      <div className="documents-progress-head">
        <div>
          <span className="documents-progress-kicker">{kicker}</span>
          <strong>{title}</strong>
        </div>
        <span className="documents-progress-percent">{Math.max(0, Math.min(100, percent))}%</span>
      </div>

      {summary && <p className="documents-progress-summary">{summary}</p>}

      {metaLines.length > 0 && (
        <div className="documents-progress-meta">
          {metaLines.map((line) => (
            <p key={line}>{line}</p>
          ))}
        </div>
      )}

      <div className="documents-progress-bar">
        <div className="documents-progress-fill" style={{ width: `${Math.max(0, Math.min(100, percent))}%` }}></div>
      </div>

      {subprogress !== null && (
        <div className="documents-progress-subbar">
          <div className="documents-progress-subfill" style={{ width: `${Math.max(0, Math.min(100, subprogress))}%` }}></div>
        </div>
      )}
    </section>
  );
}

function SourceItem({ doc, active, statusMeta, onClick }) {
  const kind = getDocumentKind(doc.fileName);

  return (
    <button type="button" className={`documents-source-item${active ? ' active' : ''}`} onClick={onClick}>
      <div className={`documents-source-icon tone-${kind.tone}`}>{kind.label}</div>
      <div className="documents-source-copy">
        <p>{doc.fileName}</p>
        <div className="documents-source-meta">
          <span className={`documents-source-badge tone-${statusMeta.tone}`}>{statusMeta.label}</span>
          <span>{statusMeta.detail}</span>
        </div>
      </div>
    </button>
  );
}

function ActionButton({ label, detail, onClick, disabled = false, tone = 'default', badge = '' }) {
  return (
    <button
      type="button"
      className={`documents-action-button tone-${tone}${disabled ? ' is-disabled' : ''}`}
      onClick={onClick}
      disabled={disabled}
    >
      <div className="documents-action-copy">
        <strong>{label}</strong>
        {detail && <span>{detail}</span>}
      </div>
      {badge && <span className="documents-action-badge">{badge}</span>}
    </button>
  );
}

function DocumentList() {
  const { currentUser } = useAuth();
  const { showToast } = useToast();
  const [documents, setDocuments] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [generating, setGenerating] = useState({});
  const [slideGenerating, setSlideGenerating] = useState({});
  const [slideDecks, setSlideDecks] = useState({});
  const [slideDeckAvailability, setSlideDeckAvailability] = useState({});
  const [showAnalysis, setShowAnalysis] = useState(null);
  const [refreshing, setRefreshing] = useState(false);
  const [lastUpdated, setLastUpdated] = useState(null);
  const [selectedDocumentId, setSelectedDocumentId] = useState(null);
  const [studioView, setStudioView] = useState('overview');
  const [filterValue, setFilterValue] = useState('');
  const navigate = useNavigate();

  const loadDocuments = useCallback(async (options = {}) => {
    const { silent = false } = options;

    if (!silent) {
      setRefreshing(true);
    }

    try {
      const docs = await documentService.getUserDocuments(String(currentUser?.id || ''));
      setDocuments(docs);
      setLastUpdated(new Date());
    } catch (err) {
      setError('Error loading documents');
      console.error(err);
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [currentUser?.id]);

  useEffect(() => {
    loadDocuments();
  }, [loadDocuments]);

  useEffect(() => {
    let cancelled = false;

    const syncSlideDecks = async () => {
      const targetDocuments = documents.filter((doc) =>
        doc.status === 3 && (
          typeof slideDeckAvailability[doc.id] === 'undefined'
          || slideGenerating[doc.id]?.running
          || ['queued', 'running'].includes(String(slideDecks[doc.id]?.generationProgress?.status || '').toLowerCase())
        ));

      if (targetDocuments.length === 0) {
        return;
      }

      const results = await Promise.allSettled(targetDocuments.map((doc) => slideService.getDeckByDocument(doc.id)));

      if (cancelled) {
        return;
      }

      setSlideDecks((current) => {
        const next = { ...current };

        results.forEach((result, index) => {
          const documentId = targetDocuments[index].id;

          if (result.status === 'fulfilled') {
            if (result.value) {
              next[documentId] = result.value;
            } else {
              delete next[documentId];
            }
          }
        });

        return next;
      });

      setSlideDeckAvailability((current) => {
        const next = { ...current };

        results.forEach((result, index) => {
          const documentId = targetDocuments[index].id;

          if (result.status === 'fulfilled') {
            next[documentId] = !!result.value;
          }
        });

        return next;
      });
    };

    syncSlideDecks();

    return () => {
      cancelled = true;
    };
  }, [documents, slideDeckAvailability, slideDecks, slideGenerating]);

  useEffect(() => {
    const hasProcessingDocs = documents.some((doc) => doc.status >= 0 && doc.status <= 2);
    const hasGeneratingSlides = Object.values(slideGenerating).some((state) => state?.running)
      || Object.values(slideDecks).some((deck) =>
        ['queued', 'running'].includes(String(deck?.generationProgress?.status || '').toLowerCase()));

    if (hasProcessingDocs || hasGeneratingSlides) {
      const interval = setInterval(() => {
        loadDocuments({ silent: true });
      }, 3000);

      return () => clearInterval(interval);
    }

    return undefined;
  }, [documents, loadDocuments, slideDecks, slideGenerating]);

  const handleGenerateQuestions = async (documentId) => {
    setGenerating((current) => ({
      ...current,
      [documentId]: {
        running: true,
        percent: 0,
        stage: 'queued',
        stageLabel: 'Chờ xử lý',
        message: 'Đang xếp hàng tạo bộ câu hỏi...',
      },
    }));
    showToast({
      type: 'info',
      message: 'Đã bắt đầu tạo bộ câu hỏi.',
      description: 'Tiến trình sẽ hiển thị trong progress card của tài liệu.',
    });

    try {
      const startResult = await questionService.startGenerateQuestions(documentId, 5);
      const jobId = startResult.jobId;
      const pollStartedAt = Date.now();
      const pollTimeoutMs = 5 * 60 * 1000;
      let completed = false;

      while (!completed) {
        if (Date.now() - pollStartedAt > pollTimeoutMs) {
          throw new Error('Timeout waiting for question generation progress');
        }

        const progressState = await questionService.getGenerateProgress(jobId);

        setGenerating((current) => ({
          ...current,
          [documentId]: {
            running: progressState.status === 'queued' || progressState.status === 'running',
            percent: progressState.percent ?? 0,
            stage: progressState.stage,
            stageLabel: progressState.stageLabel,
            message: progressState.message,
            detail: progressState.detail,
            current: progressState.current,
            total: progressState.total,
            unitLabel: progressState.unitLabel,
            stageIndex: progressState.stageIndex,
            stageCount: progressState.stageCount,
            topicTag: progressState.topicTag,
            elapsedSeconds: progressState.elapsedSeconds,
            estimatedRemainingSeconds: progressState.estimatedRemainingSeconds,
          },
        }));

        if (progressState.status === 'completed') {
          completed = true;
          showToast({
            type: 'success',
            message: `Đã tạo xong bộ câu hỏi mới (${progressState.questionsGenerated || 0} câu).`,
          });
          await loadDocuments({ silent: true });
          break;
        }

        if (progressState.status === 'failed') {
          throw new Error(progressState.error || 'Question generation failed');
        }

        await sleep(1200);
      }
    } catch (err) {
      showToast({
        type: 'error',
        message: 'Không tạo được câu hỏi.',
        description: 'Vui lòng thử lại.',
      });
      console.error(err);
    } finally {
      setGenerating((current) => {
        const next = { ...current };
        delete next[documentId];
        return next;
      });
    }
  };

  const handleGenerateSlides = async (documentId) => {
    setSlideGenerating((current) => ({
      ...current,
      [documentId]: {
        running: true,
        percent: 0,
        stage: 'queued',
        stageLabel: 'Chờ xử lý',
        message: 'Đang xếp hàng tạo slide deck...',
      },
    }));
    setSlideDeckAvailability((current) => ({
      ...current,
      [documentId]: true,
    }));
    showToast({
      type: 'info',
      message: 'Đã bắt đầu tạo slide deck.',
      description: 'Tiến trình sẽ hiển thị trong progress card của tài liệu.',
    });

    try {
      const startResult = await slideService.startGenerateSlides(documentId, 8);
      const jobId = startResult.jobId;
      const pollStartedAt = Date.now();
      const pollTimeoutMs = 8 * 60 * 1000;
      let completed = false;

      while (!completed) {
        if (Date.now() - pollStartedAt > pollTimeoutMs) {
          throw new Error('Timeout waiting for slide generation progress');
        }

        const progressState = await slideService.getGenerateProgress(jobId);

        setSlideGenerating((current) => ({
          ...current,
          [documentId]: {
            running: progressState.status === 'queued' || progressState.status === 'running',
            percent: progressState.percent ?? 0,
            stage: progressState.stage,
            stageLabel: progressState.stageLabel,
            message: progressState.message,
            detail: progressState.detail,
            current: progressState.current,
            total: progressState.total,
            unitLabel: progressState.unitLabel,
            stageIndex: progressState.stageIndex,
            stageCount: progressState.stageCount,
            elapsedSeconds: progressState.elapsedSeconds,
            estimatedRemainingSeconds: progressState.estimatedRemainingSeconds,
            slidesGenerated: progressState.slidesGenerated,
          },
        }));

        try {
          const deck = await slideService.getDeckByDocument(documentId);
          setSlideDecks((current) => {
            const next = { ...current };
            if (deck) {
              next[documentId] = deck;
            } else {
              delete next[documentId];
            }
            return next;
          });
          setSlideDeckAvailability((current) => ({
            ...current,
            [documentId]: !!deck,
          }));
        } catch (deckError) {
          console.error(deckError);
        }

        if (progressState.status === 'completed') {
          completed = true;
          showToast({
            type: 'success',
            message: 'Đã tạo xong slide deck và sẵn sàng mở Studio.',
          });
          await loadDocuments({ silent: true });
          break;
        }

        if (progressState.status === 'failed') {
          throw new Error(progressState.error || 'Slide generation failed');
        }

        await sleep(1200);
      }
    } catch (err) {
      showToast({
        type: 'error',
        message: 'Không tạo được slide deck.',
        description: 'Vui lòng kiểm tra log backend rồi thử lại.',
      });
      console.error(err);
    } finally {
      setSlideGenerating((current) => {
        const next = { ...current };
        delete next[documentId];
        return next;
      });
    }
  };

  const handleDelete = async (documentId) => {
    if (window.confirm('Are you sure you want to delete this document?')) {
      try {
        await documentService.deleteDocument(documentId);
        await loadDocuments();
      } catch (err) {
        showToast({
          type: 'error',
          message: 'Could not delete the document.',
        });
        console.error(err);
      }
    }
  };

  const closeAnalysisModal = () => {
    setShowAnalysis(null);
  };

  const normalizedFilter = filterValue.trim().toLowerCase();

  const filteredDocuments = useMemo(() => {
    if (!normalizedFilter) {
      return documents;
    }

    return documents.filter((doc) => {
      const topics = Array.isArray(doc.mainTopics) ? doc.mainTopics.join(' ').toLowerCase() : '';
      return doc.fileName.toLowerCase().includes(normalizedFilter) || topics.includes(normalizedFilter);
    });
  }, [documents, normalizedFilter]);

  useEffect(() => {
    if (documents.length === 0) {
      setSelectedDocumentId(null);
      return;
    }

    setSelectedDocumentId((current) => (documents.some((doc) => doc.id === current) ? current : documents[0].id));
  }, [documents]);

  useEffect(() => {
    if (filteredDocuments.length === 0) {
      return;
    }

    if (!filteredDocuments.some((doc) => doc.id === selectedDocumentId)) {
      setSelectedDocumentId(filteredDocuments[0].id);
    }
  }, [filteredDocuments, selectedDocumentId]);

  const selectedDocument = normalizedFilter && filteredDocuments.length === 0
    ? null
    : filteredDocuments.find((doc) => doc.id === selectedDocumentId)
      || documents.find((doc) => doc.id === selectedDocumentId)
      || filteredDocuments[0]
      || documents[0]
      || null;

  const selectedGenerationState = selectedDocument ? generating[selectedDocument.id] : null;
  const selectedQuestionRunning = !!selectedGenerationState?.running;
  const selectedSlideState = selectedDocument ? slideGenerating[selectedDocument.id] : null;
  const selectedSlideDeck = selectedDocument ? slideDecks[selectedDocument.id] : null;
  const selectedActiveSlideProgress = selectedSlideState || selectedSlideDeck?.generationProgress;
  const selectedSlidesRunning = ['queued', 'running'].includes(String(selectedActiveSlideProgress?.status || '').toLowerCase());
  const selectedProcessingState = selectedDocument?.processingProgress
    ? normalizeProgressState(selectedDocument.processingProgress, { documentId: selectedDocument.id })
    : null;
  const selectedProcessingRunning = isActiveProgress(selectedProcessingState);
  const selectedQuestionsReady = Boolean(selectedDocument?.questionsCount && selectedDocument.questionsCount > 0);
  const selectedSlideCount = selectedSlideDeck?.items?.length || selectedSlideDeck?.outline?.slides?.length || 0;

  const formatDateTime = (value) => {
    if (!value) {
      return '-';
    }

    return new Date(value).toLocaleString();
  };

  const formatRelativeTime = (value) => {
    if (!value) {
      return '-';
    }

    const diffMs = Date.now() - new Date(value).getTime();
    if (diffMs < 60_000) {
      return 'vừa cập nhật';
    }
    if (diffMs < 3_600_000) {
      return `${Math.max(1, Math.floor(diffMs / 60_000))} phút trước`;
    }
    if (diffMs < 86_400_000) {
      return `${Math.max(1, Math.floor(diffMs / 3_600_000))} giờ trước`;
    }

    return formatDateTime(value);
  };

  const formatFileSize = (bytes) => {
    if (typeof bytes !== 'number' || Number.isNaN(bytes)) {
      return '-';
    }

    const kilobytes = bytes / 1024;
    if (kilobytes < 1024) {
      return `${kilobytes >= 10 ? Math.round(kilobytes) : kilobytes.toFixed(1)} KB`;
    }

    return `${(kilobytes / 1024).toFixed(1)} MB`;
  };

  const formatDuration = (milliseconds) => {
    const totalSeconds = Math.max(0, Math.ceil(milliseconds / 1000));
    if (totalSeconds <= 59) {
      return `${totalSeconds}s`;
    }

    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
    return `${minutes}p ${seconds}s`;
  };

  const getStatusText = (status) => {
    switch (status) {
      case 0:
        return 'Uploaded';
      case 1:
        return 'Extracting';
      case 2:
        return 'Analyzing';
      case 3:
        return 'Completed';
      case 4:
        return 'Failed';
      default:
        return 'Unknown';
    }
  };

  const getStatusHint = (doc) => {
    if (generating[doc.id]?.running) {
      return 'AI đang đọc toàn bộ nội dùng và tạo bộ câu hỏi mới.';
    }

    if (doc.processingProgress?.status === 'running' && doc.processingProgress?.message) {
      return doc.processingProgress.message;
    }

    switch (doc.status) {
      case 0:
        return 'Tài liệu đã upload xong và đang chờ trích xuất nội dùng.';
      case 1:
        return 'Hệ thống đang trích xuất text và OCR nếu file là ảnh hoặc PDF scan.';
      case 2:
        return 'AI đang phân tích nội dùng, chia topic và tóm tắt tài liệu.';
      case 3:
        return doc.questionsCount > 0
          ? 'Đã sẵn sàng học bằng quiz hoặc flashcards.'
          : 'Tài liệu đã xử lý xong và sẵn sàng tạo đầu ra mới.';
      case 4:
        return 'Xử lý thất bại. Hãy thử upload lại hoặc kiểm tra file đầu vào.';
      default:
        return 'Đang cập nhật trạng thái tài liệu.';
    }
  };

  const getGenerationEta = (generationState) => {
    if (!isActiveProgress(generationState)) {
      return null;
    }

    if (typeof generationState.estimatedRemainingSeconds !== 'number') {
      return 'Đang ước tính...';
    }

    if (generationState.estimatedRemainingSeconds <= 0) {
      return 'Sắp xong...';
    }

    return `Ước tính còn ${formatDuration(generationState.estimatedRemainingSeconds * 1000)}`;
  };

  const getSubProgress = (state) => {
    if (
      typeof state?.current !== 'number'
      || typeof state?.total !== 'number'
      || state.total <= 0
    ) {
      return null;
    }

    return Math.max(0, Math.min(100, Math.round((state.current / state.total) * 100)));
  };

  const getRealtimeProgressLabel = (state) => {
    if (
      typeof state?.current !== 'number'
      || typeof state?.total !== 'number'
      || state.total <= 0
    ) {
      return null;
    }

    const unit = state.unitLabel || 'mục';
    const prefix = state.stage?.includes('ocr')
      ? 'OCR'
      : state.stage?.includes('analyzing')
        ? 'Phân tích'
        : 'Tiến trình';

    return `${prefix} ${unit}: ${state.current}/${state.total}`;
  };

  const getSourceStatusMeta = (doc) => {
    const activeQuestionState = generating[doc.id];
    const activeSlideState = slideGenerating[doc.id] || slideDecks[doc.id]?.generationProgress;

    if (doc.processingProgress?.status === 'running') {
      return {
        tone: 'active',
        label: `${doc.processingProgress.percent || 0}%`,
        detail: doc.processingProgress.stageLabel || 'Đang phân tích',
      };
    }

    if (activeSlideState?.running || ['queued', 'running'].includes(String(activeSlideState?.status || '').toLowerCase())) {
      return {
        tone: 'active',
        label: `${activeSlideState.percent || 0}%`,
        detail: 'Đang tạo slide',
      };
    }

    if (activeQuestionState?.running) {
      return {
        tone: 'active',
        label: `${activeQuestionState.percent || 0}%`,
        detail: 'Đang tạo câu hỏi',
      };
    }

    if (doc.status === 3) {
      return { tone: 'completed', label: 'Ready', detail: `${doc.questionsCount || 0} câu hỏi` };
    }

    if (doc.status === 4) {
      return { tone: 'failed', label: 'Fail', detail: 'Cần xem lại dữ liệu' };
    }

    return { tone: 'uploaded', label: getStatusText(doc.status), detail: getStatusHint(doc) };
  };

  const selectedTopbarState = (() => {
    if (!selectedDocument) {
      return 'Chưa chọn tài liệu';
    }

    if (selectedProcessingRunning) {
      return `Đang phân tích ${selectedProcessingState.percent || 0}%`;
    }

    if (selectedSlidesRunning) {
      return `Đang tạo slide ${selectedActiveSlideProgress?.percent || 0}%`;
    }

    if (selectedQuestionRunning) {
      return `Đang tạo câu hỏi ${selectedGenerationState?.percent || 0}%`;
    }

    return 'Sẵn sàng cho workflow tiếp theo';
  })();

  const analysisTopics = selectedDocument?.mainTopics?.slice(0, 4) || [];
  const analysisPoints = selectedDocument?.keyPoints?.slice(0, 3) || [];
  const slideOutline = selectedSlideDeck?.outline?.slides?.slice(0, 3) || [];
  const selectedHint = selectedProcessingRunning
    ? 'AI đang đọc và tổng hợp nội dùng từ tài liệu. Bạn có thể theo dõi tiến trình ngay trong workspace này.'
    : selectedSlidesRunning
      ? 'Deck slide đang được tạo. Ngay khi có slide đầu tiên, bạn có thể mở Studio để tinh chỉnh.'
      : selectedQuestionRunning
        ? 'Question bank đang được tạo. Quiz và Flashcards sẽ sẵn sàng ngay sau khi pipeline hoàn tất.'
        : selectedSlideDeck
          ? 'Deck slide đã sẵn sàng. Bạn có thể tiếp tục mở Studio, export HTML/PDF hoặc nối các luồng xuất bản sau này.'
          : selectedQuestionsReady
            ? 'Question bank đã sẵn sàng. Đây là lúc thuận lợi để nối tiếp luồng quiz, flashcards và đánh giá nhanh.'
            : 'Co the bat dau bang cach tạo slide deck, tạo bộ câu hỏi hoac mo bang phân tích chi tiet.';

  const renderCanvasBody = () => {
    if (!selectedDocument) {
      return (
        <div className="documents-canvas-empty">
          <h3>Chưa có tài liệu nào</h3>
          <p>Thêm tài liệu mới để khởi tạo workspace, phân tích nội dùng và nối tiếp các workflow học tập.</p>
          <button type="button" className="documents-mini-primary" onClick={() => navigate('/')}>
            Thêm nguồn
          </button>
        </div>
      );
    }

    const kind = getDocumentKind(selectedDocument.fileName);

    const previewTitle = studioView === 'analysis'
      ? `Bảng phân tích: ${selectedDocument.fileName}`
      : studioView === 'slides'
        ? `Slide deck: ${selectedSlideDeck?.title || selectedDocument.fileName}`
        : studioView === 'study'
          ? `Học tập từ tài liệu: ${selectedDocument.fileName}`
          : `Tổng quan tài liệu: ${selectedDocument.fileName}`;

    const previewRows = studioView === 'analysis'
      ? (analysisPoints.length > 0
        ? analysisPoints
        : analysisTopics.length > 0
          ? analysisTopics
          : ['Đang chờ AI tổng hợp ý chính từ tài liệu.'])
      : studioView === 'slides'
        ? (slideOutline.length > 0
          ? slideOutline.map((slide) => slide.heading || `Slide ${slide.slideIndex}`)
          : [selectedSlideDeck ? 'Deck đã sẵn sàng nhưng chưa có outline chi tiết.' : 'Chưa có slide deck cho tài liệu này.'])
        : studioView === 'study'
          ? [
              selectedQuestionsReady ? `${selectedDocument.questionsCount || 0} câu hỏi đã được tạo.` : 'Chưa có question bank.',
              selectedQuestionsReady ? 'Quiz tương tác đã có thể mở.' : 'Quiz se san sang sau khi tạo bộ câu hỏi.',
              selectedQuestionsReady ? 'Flashcards đã có thể mở.' : 'Flashcards sẽ nối tiếp từ question bank.',
            ]
          : [
              `Trạng thái hiện tại: ${getStatusText(selectedDocument.status)}`,
              selectedSlideDeck ? `Slide deck: ${selectedSlideCount} slide sẵn sàng.` : 'Slide deck: chua tao.',
              selectedQuestionsReady ? `Study kit: ${selectedDocument.questionsCount || 0} câu hỏi san sang.` : 'Study kit: chưa tạo.',
            ];

    return (
      <>
        <div className="documents-preview-card">
          <div className="documents-preview-layout">
            <div className="documents-preview-copy">
              <h2>{previewTitle}</h2>

              {studioView === 'analysis' && selectedDocument.summary && (
                <p className="documents-preview-summary">{selectedDocument.summary}</p>
              )}

              <div className="documents-preview-list">
                {previewRows.map((row, index) => (
                  <div key={`${row}-${index}`} className={`documents-preview-row${index === 1 ? ' active' : ''}`}>
                    <p>{row}</p>
                  </div>
                ))}
              </div>
            </div>

            <div className="documents-preview-sidecard">
              <span>{kind.label}</span>
              <strong>{getStatusText(selectedDocument.status)}</strong>
              <small>{selectedSlideDeck ? `${selectedSlideCount} slide` : formatFileSize(selectedDocument.fileSize)}</small>
            </div>
          </div>

          <div className="documents-preview-hint">
            <span>AI hint</span>
            <p>{selectedHint}</p>
          </div>
        </div>

        {(selectedProcessingRunning || selectedQuestionRunning || selectedSlidesRunning) && (
          <div className="documents-preview-panels">
            {selectedProcessingRunning && (
              <ProgressPanel
                tone="processing"
                kicker="Pipeline"
                title="Đang phân tích tài liệu"
                summary={selectedProcessingState?.message || 'Hệ thống đang OCR và trích xuất nội dùng.'}
                metaLines={[
                  selectedProcessingState?.stageLabel || null,
                  getGenerationEta(selectedProcessingState),
                  getRealtimeProgressLabel(selectedProcessingState),
                  selectedProcessingState?.detail || null,
                ].filter(Boolean)}
                percent={selectedProcessingState?.percent || 0}
                subprogress={getSubProgress(selectedProcessingState)}
              />
            )}

            {selectedQuestionRunning && (
              <ProgressPanel
                tone="questions"
                kicker="Question bank"
                title="Đang tạo bộ câu hỏi"
                summary={selectedGenerationState?.message || 'AI dang tổng hợp bo câu hỏi moi.'}
                metaLines={[
                  selectedGenerationState?.stageLabel || null,
                  getGenerationEta(selectedGenerationState),
                  typeof selectedGenerationState?.current === 'number' && typeof selectedGenerationState?.total === 'number'
                    ? `${selectedGenerationState.current}/${selectedGenerationState.total} ${selectedGenerationState.unitLabel || 'mục'}`
                    : null,
                ].filter(Boolean)}
                percent={selectedGenerationState?.percent || 0}
                subprogress={getSubProgress(selectedGenerationState)}
              />
            )}

            {selectedSlidesRunning && (
              <ProgressPanel
                tone="slides"
                kicker="Slide deck"
                title="Đang tạo slide"
                summary={selectedActiveSlideProgress?.message || 'Đang tạo deck từ tài liệu được chọn.'}
                metaLines={[
                  selectedActiveSlideProgress?.stageLabel || null,
                  getGenerationEta(selectedActiveSlideProgress),
                  typeof selectedActiveSlideProgress?.current === 'number' && typeof selectedActiveSlideProgress?.total === 'number'
                    ? `${selectedActiveSlideProgress.current}/${selectedActiveSlideProgress.total} ${selectedActiveSlideProgress.unitLabel || 'slide'}`
                    : null,
                ].filter(Boolean)}
                percent={selectedActiveSlideProgress?.percent || 0}
                subprogress={getSubProgress(selectedActiveSlideProgress)}
              />
            )}
          </div>
        )}
      </>
    );
  };

  const renderContextActions = () => {
    if (!selectedDocument) {
      return null;
    }

    const sharedProps = {
      documentReady: selectedDocument.status === 3,
      hasDeck: Boolean(selectedSlideDeck),
      hasQuestions: selectedQuestionsReady,
    };

    const actions = [];

    if (studioView === 'overview') {
      actions.push(
        <ActionButton
          key="overview-slides"
          label={sharedProps.hasDeck ? 'Mở slide deck' : 'Tạo slide deck'}
          detail={sharedProps.hasDeck ? `${selectedSlideCount} slide đã sẵn sàng.` : 'Khởi tạo bộ slide từ nội dùng tài liệu.'}
          tone="primary"
          disabled={!sharedProps.documentReady || selectedSlidesRunning}
          onClick={() => (sharedProps.hasDeck ? navigate(`/slides/${selectedDocument.id}`) : handleGenerateSlides(selectedDocument.id))}
        />,
        <ActionButton
          key="overview-study"
          label={sharedProps.hasQuestions ? 'Mở bộ học tập' : 'Tạo question bank'}
          detail={sharedProps.hasQuestions ? `${selectedDocument.questionsCount || 0} câu hỏi đã sẵn sàng.` : 'Sinh quiz và flashcards từ tài liệu.'}
          disabled={!sharedProps.documentReady || selectedQuestionRunning}
          onClick={() => (sharedProps.hasQuestions ? setStudioView('study') : handleGenerateQuestions(selectedDocument.id))}
        />,
        <ActionButton
          key="overview-analysis"
          label="Xem phân tích"
          detail="Mở tóm tắt, topic và ý chính trong modal."
          onClick={() => setShowAnalysis(selectedDocument)}
        />,
      );
    }

    if (studioView === 'analysis') {
      actions.push(
        <ActionButton
          key="analysis-modal"
          label="Mở bản phân tích đầy đủ"
          detail="Xem tóm tắt, topic, key points và văn bản trích xuất."
          onClick={() => setShowAnalysis(selectedDocument)}
        />,
        <ActionButton
          key="analysis-study"
          label={sharedProps.hasQuestions ? 'Chuyển sang học tập' : 'Tạo question bank'}
          detail={sharedProps.hasQuestions ? 'Mở nhanh quiz và flashcards từ tài liệu này.' : 'Dùng ý chính hiện tại để tạo bộ câu hỏi.'}
          disabled={!sharedProps.documentReady || selectedQuestionRunning}
          onClick={() => (sharedProps.hasQuestions ? setStudioView('study') : handleGenerateQuestions(selectedDocument.id))}
        />,
      );
    }

    if (studioView === 'slides') {
      actions.push(
        <ActionButton
          key="slides-main"
          label={sharedProps.hasDeck ? 'Mở Slide Studio' : 'Tạo slide deck'}
          detail={sharedProps.hasDeck ? 'Chỉnh sửa và xem deck ở route hiện có.' : 'Khởi động luồng tạo slide từ tài liệu.'}
          tone="primary"
          disabled={!sharedProps.documentReady || selectedSlidesRunning}
          onClick={() => (sharedProps.hasDeck ? navigate(`/slides/${selectedDocument.id}`) : handleGenerateSlides(selectedDocument.id))}
        />,
        <ActionButton
          key="slides-export"
          label="Xuất HTML / PDF"
          detail="Mở bản export hiện có của slide deck."
          disabled={!sharedProps.hasDeck}
          onClick={() => window.open(slideService.getDeckHtmlUrl(selectedDocument.id), '_blank', 'noopener,noreferrer')}
        />,
      );
    }

    if (studioView === 'study') {
      actions.push(
        <ActionButton
          key="study-quiz"
          label={sharedProps.hasQuestions ? 'Mở Quiz' : 'Tạo question bank'}
          detail={sharedProps.hasQuestions ? 'Bắt đầu quiz tương tác với tài liệu này.' : 'Sinh bo câu hỏi de kich hoat che do học tập.'}
          tone={sharedProps.hasQuestions ? 'primary' : 'default'}
          disabled={!sharedProps.documentReady || selectedQuestionRunning}
          onClick={() => (sharedProps.hasQuestions ? navigate(`/study/${selectedDocument.id}/quiz`) : handleGenerateQuestions(selectedDocument.id))}
        />,
        <ActionButton
          key="study-flashcards"
          label="Mở Flashcards"
          detail="Ôn nhanh bằng thẻ ghi nhớ từ bộ câu hỏi hiện có."
          disabled={!sharedProps.hasQuestions}
          onClick={() => navigate(`/study/${selectedDocument.id}/flashcards`)}
        />,
      );
    }

    actions.push(
      <ActionButton
        key="manage-delete"
        label="Xóa tài liệu"
        detail="Xóa tài liệu đang chọn khoi he thong."
        tone="danger"
        onClick={() => handleDelete(selectedDocument.id)}
      />,
    );

    return (
      <div className="documents-action-section" style={{ width: '100%', maxWidth: 720, padding: 0 }}>
        {actions}
      </div>
    );
  };

  if (loading) {
    return (
      <div className="loading">
        <div className="spinner"></div>
        <p>Loading documents...</p>
      </div>
    );
  }

  if (error) {
    return <div className="alert alert-error">{error}</div>;
  }

  return (
    <div className="documents-studio-page">
      <section className="documents-studio-shell">
        <div className="documents-studio-topbar">
          <button type="button" className="documents-mini-btn" onClick={() => navigate('/')}>
            &larr;
          </button>

          <div className="documents-topbar-copy">
            <strong>{selectedDocument ? selectedDocument.fileName : 'My Documents'}</strong>
            <div className="documents-topbar-meta">
              <span>{documents.length} tài liệu</span>
              <span>{selectedDocument?.questionsCount || 0} câu hỏi</span>
              <span>{selectedSlideCount} slide</span>
              <span>Cập nhật: {formatRelativeTime(lastUpdated)}</span>
              <span className="documents-live-inline">{selectedTopbarState}</span>
            </div>
          </div>

          <div className="documents-topbar-actions">
            <div className="documents-topbar-avatar">GV</div>
            <button
              type="button"
              className="documents-mini-btn"
              onClick={() => loadDocuments()}
              disabled={refreshing}
            >
              {refreshing ? 'Đang đồng bộ' : 'Đồng bộ'}
            </button>
            <button
              type="button"
              className="documents-mini-primary"
              onClick={() => selectedDocument && navigate(`/slides/${selectedDocument.id}`)}
              disabled={!selectedDocument || selectedDocument.status !== 3}
            >
              Mở Studio
            </button>
          </div>
        </div>

        <div className="documents-studio-main documents-studio-main-compact">
          <aside className="documents-studio-sidebar">
            <div className="documents-panel-title">Nguồn / Documents</div>

            <div className="documents-filter-row">
              <input
                type="text"
                value={filterValue}
                onChange={(event) => setFilterValue(event.target.value)}
                placeholder="Filter documents"
              />
              <button type="button" className="documents-mini-btn" onClick={() => setFilterValue('')}>
                x
              </button>
            </div>

            <div className="documents-sidebar-cta">
              <button type="button" className="documents-side-button" onClick={() => navigate('/')}>
                + Thêm nguồn
              </button>
            </div>

            <div className="documents-source-list">
              {filteredDocuments.length > 0 ? filteredDocuments.map((doc) => (
                <SourceItem
                  key={doc.id}
                  doc={doc}
                  active={doc.id === selectedDocument?.id}
                  statusMeta={getSourceStatusMeta(doc)}
                  onClick={() => setSelectedDocumentId(doc.id)}
                />
              )) : (
                <div className="documents-sidebar-empty">Không tìm thấy tài liệu phù hợp bộ lọc.</div>
              )}
            </div>

          </aside>

          <div className="documents-studio-center">
            <div className="documents-studio-toolbar">
              <button type="button" className={`documents-toolbar-btn${studioView === 'overview' ? ' active' : ''}`} onClick={() => setStudioView('overview')}>
                Tổng quan
              </button>
              <button type="button" className={`documents-toolbar-btn${studioView === 'analysis' ? ' active' : ''}`} onClick={() => setStudioView('analysis')}>
                Phân tích
              </button>
              <button type="button" className={`documents-toolbar-btn${studioView === 'slides' ? ' active' : ''}`} onClick={() => setStudioView('slides')}>
                Slides
              </button>
              <button type="button" className={`documents-toolbar-btn${studioView === 'study' ? ' active' : ''}`} onClick={() => setStudioView('study')}>
                Học tập
              </button>
            </div>

            <div className="documents-studio-canvas">
              {renderCanvasBody()}
              {selectedDocument && (
                <div className="documents-preview-card documents-quick-actions-card">
                  <div className="documents-preview-layout documents-quick-actions-layout">
                    <div className="documents-preview-copy">
                      <h2 className="documents-quick-actions-title">Tác vụ nhanh</h2>
                      <p className="documents-preview-summary documents-quick-actions-summary">
                        {studioView === 'overview'
                          ? 'Chỉ hiển thị các thao tác cần thiết nhất cho tài liệu đang chọn.'
                          : studioView === 'analysis'
                            ? 'Tập trung vào xem nhanh insight và mở tiếp bộ học tập.'
                            : studioView === 'slides'
                              ? 'Deck slide và export được đặt chung trong một cụm thao tác.'
                              : 'Quiz và flashcards được gom vào cùng một khu học tập.'}
                      </p>
                    </div>
                    <div className="documents-preview-sidecard">
                      <span>Workspace</span>
                      <strong>{selectedTopbarState}</strong>
                      <small>Cập nhật {formatRelativeTime(lastUpdated)}</small>
                    </div>
                  </div>
                  <div className="documents-quick-actions-body">
                    {renderContextActions()}
                  </div>
                </div>
              )}
            </div>
          </div>
        </div>
      </section>

      {showAnalysis && (
        <div className="modal-overlay" onClick={closeAnalysisModal}>
          <div className="modal-content" onClick={(event) => event.stopPropagation()}>
            <div className="modal-header">
              <h2>Phân tích nội dùng: {showAnalysis.fileName}</h2>
              <button className="close-btn" onClick={closeAnalysisModal}>x</button>
            </div>
            <div className="modal-body">
              {showAnalysis.mainTopics && showAnalysis.mainTopics.length > 0 && (
                <div className="analysis-section">
                  <h3>Chủ đề chính</h3>
                  <div className="topics-list">
                    {showAnalysis.mainTopics.map((topic, index) => (
                      <span key={index} className="topic-tag">{topic}</span>
                    ))}
                  </div>
                </div>
              )}

              {showAnalysis.keyPoints && showAnalysis.keyPoints.length > 0 && (
                <div className="analysis-section">
                  <h3>Ý chính</h3>
                  <ul className="key-points-list">
                    {showAnalysis.keyPoints.map((point, index) => (
                      <li key={index}>{point}</li>
                    ))}
                  </ul>
                </div>
              )}

              {showAnalysis.summary && (
                <div className="analysis-section">
                  <h3>Tóm tắt</h3>
                  <p className="summary-text">{showAnalysis.summary}</p>
                </div>
              )}

              {showAnalysis.language && (
                <div className="analysis-section">
                  <h3>Ngôn ngữ</h3>
                  <p><strong>{showAnalysis.language}</strong></p>
                </div>
              )}

              {showAnalysis.extractedText && (
                <div className="analysis-section">
                  <h3>Văn bản đã trích xuất</h3>
                  <div className="extracted-text-preview">
                    {showAnalysis.extractedText.substring(0, 1000)}
                    {showAnalysis.extractedText.length > 1000 && '...'}
                  </div>
                </div>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default DocumentList;
