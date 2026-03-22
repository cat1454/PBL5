import React, { useCallback, useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { documentService, questionService, slideService } from '../services/api';

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

function DocumentList() {
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
  const [feedback, setFeedback] = useState(null);
  const [currentTime, setCurrentTime] = useState(Date.now());
  const navigate = useNavigate();

  const loadDocuments = useCallback(async (options = {}) => {
    const { silent = false } = options;

    if (!silent) {
      setRefreshing(true);
    }

    try {
      const userId = 'demo-user'; // In real app, get from auth context
      const docs = await documentService.getUserDocuments(userId);
      setDocuments(docs);
      setLastUpdated(new Date());
    } catch (err) {
      setError('Error loading documents');
      console.error(err);
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, []);

  useEffect(() => {
    loadDocuments();
  }, [loadDocuments]);

  useEffect(() => {
    const interval = setInterval(() => {
      setCurrentTime(Date.now());
    }, 1000);

    return () => clearInterval(interval);
  }, []);

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

      const results = await Promise.allSettled(
        targetDocuments.map((doc) => slideService.getDeckByDocument(doc.id))
      );

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
            return;
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

  // Auto-refresh when documents are being processed
  useEffect(() => {
    const hasProcessingDocs = documents.some(doc => doc.status >= 0 && doc.status <= 2);
    const hasGeneratingSlides = Object.values(slideGenerating).some((state) => state?.running)
      || Object.values(slideDecks).some((deck) =>
        ['queued', 'running'].includes(String(deck?.generationProgress?.status || '').toLowerCase()));
    
    if (hasProcessingDocs || hasGeneratingSlides) {
      const interval = setInterval(() => {
        loadDocuments();
      }, 3000); // Refresh every 3 seconds

      return () => clearInterval(interval);
    }
  }, [documents, loadDocuments, slideDecks, slideGenerating]);

  const handleGenerateQuestions = async (documentId) => {
    setGenerating((current) => ({
      ...current,
      [documentId]: {
        running: true,
        percent: 0,
        stage: 'queued',
        stageLabel: 'Cho xu ly',
        message: 'Dang xep hang tao bo cau hoi...',
      },
    }));
    setFeedback({ type: 'info', text: 'Dang tao bo cau hoi moi theo tien trinh realtime.' });

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
          setFeedback({
            type: 'success',
            text: `Da tao xong bo cau hoi moi (${progressState.questionsGenerated || 0} cau). Ban co the vao Quiz hoac Flashcards ngay bay gio.`,
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
      setFeedback({ type: 'error', text: 'Khong tao duoc cau hoi. Vui long thu lai va kiem tra backend log/progress.' });
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
        stageLabel: 'Cho xu ly',
        message: 'Dang xep hang tao slide deck...',
      },
    }));
    setSlideDeckAvailability((current) => ({
      ...current,
      [documentId]: true,
    }));
    setFeedback({ type: 'info', text: 'Dang tao slide deck va se hien dan ngay trong danh sach tai lieu.' });

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
          setFeedback({
            type: 'success',
            text: `Da tao xong slide deck. Slide se tiep tuc hien trong card nay va co the mo Slide Studio de sua chi tiet.`,
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
      setFeedback({ type: 'error', text: 'Khong tao duoc slide deck. Vui long kiem tra progress va backend log.' });
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
        alert('Error deleting document');
        console.error(err);
      }
    }
  };

  const handleViewAnalysis = (doc) => {
    setShowAnalysis(doc);
  };

  const closeAnalysisModal = () => {
    setShowAnalysis(null);
  };

  const processingCount = documents.filter((doc) => doc.status >= 0 && doc.status <= 2).length;
  const readyCount = documents.filter((doc) => doc.status === 3).length;
  const totalQuestions = documents.reduce((sum, doc) => sum + (doc.questionsCount || 0), 0);

  const formatDateTime = (value) => new Date(value).toLocaleString();

  const getStageDurationMs = (status) => {
    switch (status) {
      case 0:
        return 10000;
      case 1:
        return 25000;
      case 2:
        return 35000;
      default:
        return 0;
    }
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

  const getGenerationEta = (generationState) => {
    if (!generationState?.running) {
      return null;
    }

    if (typeof generationState.estimatedRemainingSeconds !== 'number') {
      return 'Dang tinh thoi gian con lai...';
    }

    if (generationState.estimatedRemainingSeconds <= 0) {
      return 'Sap xong...';
    }

    return `Uoc tinh con ${formatDuration(generationState.estimatedRemainingSeconds * 1000)}`;
  };

  const getGenerationSubProgress = (generationState) => {
    if (
      typeof generationState?.current !== 'number'
      || typeof generationState?.total !== 'number'
      || generationState.total <= 0
    ) {
      return null;
    }

    return Math.max(0, Math.min(100, Math.round((generationState.current / generationState.total) * 100)));
  };

  const getSlideEta = (slideState) => {
    if (!slideState?.running) {
      return null;
    }

    if (typeof slideState.estimatedRemainingSeconds !== 'number') {
      return 'Dang tinh thoi gian con lai...';
    }

    if (slideState.estimatedRemainingSeconds <= 0) {
      return 'Sap xong...';
    }

    return `Uoc tinh con ${formatDuration(slideState.estimatedRemainingSeconds * 1000)}`;
  };

  const getSlideSubProgress = (slideState) => {
    if (
      typeof slideState?.current !== 'number'
      || typeof slideState?.total !== 'number'
      || slideState.total <= 0
    ) {
      return null;
    }

    return Math.max(0, Math.min(100, Math.round((slideState.current / slideState.total) * 100)));
  };

  const getRealtimeEta = (state) => {
    if (!state || (state.status !== 'queued' && state.status !== 'running')) {
      return null;
    }

    if (typeof state.estimatedRemainingSeconds !== 'number') {
      return 'Dang tinh thoi gian con lai...';
    }

    if (state.estimatedRemainingSeconds <= 0) {
      return 'Sap xong...';
    }

    return `Uoc tinh con ${formatDuration(state.estimatedRemainingSeconds * 1000)}`;
  };

  const getRealtimeSubProgress = (state) => {
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

    const unit = state.unitLabel || 'muc';
    const prefix = state.stage?.includes('ocr')
      ? 'OCR'
      : state.stage?.includes('analyzing')
        ? 'Phan tich'
        : 'Tien trinh';

    return `${prefix} ${unit}: ${state.current}/${state.total}`;
  };

  const getEstimatedTimeRemaining = (doc) => {
    const processingState = doc.processingProgress;
    const realtimeEta = getRealtimeEta(processingState);
    if (realtimeEta) {
      return realtimeEta;
    }

    if (doc.status < 0 || doc.status > 2) {
      return null;
    }

    const stageStartedAt = new Date(doc.updatedAt || doc.createdAt).getTime();
    const stageDurationMs = getStageDurationMs(doc.status);
    const remainingMs = stageDurationMs - (currentTime - stageStartedAt);

    if (remainingMs <= 0) {
      return 'Sap xong...';
    }

    return `Uoc tinh con ${formatDuration(remainingMs)}`;
  };

  const getStatusHint = (doc) => {
    if (generating[doc.id]?.running) {
      return 'AI dang doc toan bo noi dung va tao bo cau hoi moi. Ban co the cho 2-3 phut de lay ket qua tot hon.';
    }

    if (doc.processingProgress?.status === 'running' && doc.processingProgress?.message) {
      return doc.processingProgress.message;
    }

    switch (doc.status) {
      case 0:
        return 'Tai lieu da upload xong va dang cho trich xuat noi dung.';
      case 1:
        return 'He thong dang trich xuat text va OCR neu file la anh hoac PDF scan.';
      case 2:
        return 'AI dang phan tich noi dung, chia topic va tom tat tai lieu.';
      case 3:
        return doc.questionsCount > 0
          ? 'Da san sang hoc bang quiz hoac flashcards.'
          : 'Tai lieu da xu ly xong. Ban co the tao bo cau hoi moi ngay bay gio.';
      case 4:
        return 'Xu ly that bai. Thu upload lai hoac kiem tra file dau vao.';
      default:
        return 'Dang cap nhat trang thai tai lieu.';
    }
  };

  const getStatusColor = (status) => {
    switch (status) {
      case 0: return '#ffc107'; // Uploaded
      case 1: return '#17a2b8'; // Extracting
      case 2: return '#007bff'; // Analyzing
      case 3: return '#28a745'; // Completed
      case 4: return '#dc3545'; // Failed
      default: return '#6c757d';
    }
  };

  const getStatusText = (status) => {
    switch (status) {
      case 0: return 'Uploaded';
      case 1: return 'Extracting...';
      case 2: return 'Analyzing...';
      case 3: return 'Completed';
      case 4: return 'Failed';
      default: return 'Unknown';
    }
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
    <div>
      <div className="card">
        <div className="section-header">
          <div>
            <h2>📚 My Documents</h2>
            <p className="section-subtitle">Theo doi tai lieu, qua trinh AI phan tich va tao cau hoi o mot cho duy nhat.</p>
          </div>
          <div className="header-actions">
            <button
              className="button button-secondary"
              onClick={() => loadDocuments()}
              disabled={refreshing}
            >
              {refreshing ? 'Dang lam moi...' : '↻ Lam moi'}
            </button>
            {documents.some(doc => doc.status >= 0 && doc.status <= 2) && (
              <div className="live-indicator">
                <div className="spinner-small"></div>
                Tu dong cap nhat
              </div>
            )}
          </div>
        </div>

        <div className="stats-grid">
          <div className="stat-card">
            <span className="stat-value">{documents.length}</span>
            <span className="stat-label">Tong tai lieu</span>
          </div>
          <div className="stat-card">
            <span className="stat-value">{processingCount}</span>
            <span className="stat-label">Dang xu ly</span>
          </div>
          <div className="stat-card">
            <span className="stat-value">{readyCount}</span>
            <span className="stat-label">San sang hoc</span>
          </div>
          <div className="stat-card">
            <span className="stat-value">{totalQuestions}</span>
            <span className="stat-label">Tong cau hoi</span>
          </div>
        </div>

        {lastUpdated && (
          <p className="timestamp-note">Cap nhat lan cuoi: {formatDateTime(lastUpdated)}</p>
        )}

        {feedback && (
          <div className={`alert ${feedback.type === 'success' ? 'alert-success' : feedback.type === 'error' ? 'alert-error' : 'alert-info'}`}>
            {feedback.text}
          </div>
        )}

        {documents.length === 0 ? (
          <div className="empty-state">
            <h3>Chua co tai lieu nao</h3>
            <p>Upload PDF, DOCX hoac anh de AI phan tich noi dung va tao bo cau hoi tu dong.</p>
          </div>
        ) : (
          <div className="document-list">
            {documents.map((doc) => (
              (() => {
                const generationState = generating[doc.id];
                const isGenerating = !!generationState?.running;
                const slideState = slideGenerating[doc.id];
                const slideDeck = slideDecks[doc.id];
                const activeSlideProgress = slideState || slideDeck?.generationProgress;
                const isGeneratingSlides = ['queued', 'running'].includes(String(activeSlideProgress?.status || '').toLowerCase());
                const inlineSlideItems = slideDeck?.items?.slice(0, 3) || [];
                const inlineOutlineItems = slideDeck?.outline?.slides?.slice(0, 4) || [];
                const placeholderSlides = inlineOutlineItems.length > 0
                  ? inlineOutlineItems.slice(0, Math.min(3, inlineOutlineItems.length))
                  : Array.from({ length: 3 }, (_, index) => ({ slideIndex: index + 1 }));
                const processingState = doc.processingProgress;
                const isProcessingRealtime = !!processingState && (processingState.status === 'queued' || processingState.status === 'running');

                return (
              <div key={doc.id} className="document-item">
                <div className="document-info">
                  <div className="document-title-row">
                    <h3>{doc.fileName}</h3>
                    <span className="status-badge" style={{ backgroundColor: getStatusColor(doc.status) }}>
                      {getStatusText(doc.status)}
                    </span>
                  </div>

                  <p className="document-meta">
                    <span>{formatDateTime(doc.createdAt)}</span>
                    <span>{(doc.fileSize / 1024).toFixed(0)} KB</span>
                    <span>{doc.questionsCount || 0} cau hoi</span>
                  </p>

                  <p className="status-hint">{getStatusHint(doc)}</p>

                  {getEstimatedTimeRemaining(doc) && (
                    <p className="status-eta">⏱️ {getEstimatedTimeRemaining(doc)}</p>
                  )}

                  {isProcessingRealtime && (
                    <div className="generation-panel processing-panel">
                      <div className="spinner-small"></div>
                      <div>
                        <strong>Dang xu ly tai lieu ({processingState.percent || 0}%)</strong>
                        <p>{processingState.message || 'He thong dang OCR va phan tich tai lieu.'}</p>
                        {processingState.stageLabel && (
                          <p className="generation-progress-meta">
                            Buoc hien tai: {processingState.stageLabel}
                            {typeof processingState.stageIndex === 'number' && typeof processingState.stageCount === 'number'
                              ? ` (${processingState.stageIndex}/${processingState.stageCount})`
                              : ''}
                          </p>
                        )}
                        {processingState.detail && (
                          <p className="generation-progress-detail">{processingState.detail}</p>
                        )}
                        {getRealtimeEta(processingState) && (
                          <p className="generation-progress-meta">{getRealtimeEta(processingState)}</p>
                        )}
                        {getRealtimeProgressLabel(processingState) && (
                          <p className="generation-progress-meta">
                            {getRealtimeProgressLabel(processingState)}
                          </p>
                        )}
                        <div className="generation-progress-bar">
                          <div
                            className="generation-progress-fill"
                            style={{ width: `${Math.max(0, Math.min(100, processingState.percent || 0))}%` }}
                          ></div>
                        </div>
                        {getRealtimeSubProgress(processingState) !== null && (
                          <div className="generation-subprogress">
                            <div
                              className="generation-subprogress-fill"
                              style={{ width: `${getRealtimeSubProgress(processingState)}%` }}
                            ></div>
                          </div>
                        )}
                      </div>
                    </div>
                  )}

                  {doc.mainTopics && doc.mainTopics.length > 0 && (
                    <div className="inline-topics">
                      {doc.mainTopics.slice(0, 5).map((topic, index) => (
                        <span key={index} className="mini-topic-tag">{topic}</span>
                      ))}
                    </div>
                  )}

                  {isGenerating && (
                    <div className="generation-panel">
                      <div className="spinner-small"></div>
                      <div>
                        <strong>Dang tao bo cau hoi moi ({generationState.percent || 0}%)</strong>
                        <p>{generationState.message || 'He thong dang xu ly va sinh cau hoi.'}</p>
                        {generationState.stageLabel && (
                          <p className="generation-progress-meta">
                            Buoc hien tai: {generationState.stageLabel}
                            {typeof generationState.stageIndex === 'number' && typeof generationState.stageCount === 'number'
                              ? ` (${generationState.stageIndex}/${generationState.stageCount})`
                              : ''}
                          </p>
                        )}
                        {generationState.detail && (
                          <p className="generation-progress-detail">{generationState.detail}</p>
                        )}
                        {getGenerationEta(generationState) && (
                          <p className="generation-progress-meta">{getGenerationEta(generationState)}</p>
                        )}
                        {typeof generationState.current === 'number' && typeof generationState.total === 'number' && (
                          <p className="generation-progress-meta">
                            Tien trinh {generationState.unitLabel || 'muc'}: {generationState.current}/{generationState.total}
                          </p>
                        )}
                        {generationState.topicTag && <p>Topic-tag hien tai: {generationState.topicTag}</p>}
                        <div className="generation-progress-bar">
                          <div
                            className="generation-progress-fill"
                            style={{ width: `${Math.max(0, Math.min(100, generationState.percent || 0))}%` }}
                          ></div>
                        </div>
                        {getGenerationSubProgress(generationState) !== null && (
                          <div className="generation-subprogress">
                            <div
                              className="generation-subprogress-fill"
                              style={{ width: `${getGenerationSubProgress(generationState)}%` }}
                            ></div>
                          </div>
                        )}
                      </div>
                    </div>
                  )}

                  {(slideDeck || isGeneratingSlides) && (
                    <div className="slide-inline-panel">
                      <div className="slide-inline-header">
                        <div>
                          <strong>
                            {isGeneratingSlides
                              ? `Dang tao slide deck (${activeSlideProgress?.percent || 0}%)`
                              : `Slide deck: ${slideDeck?.title || 'Da tao xong'}`}
                          </strong>
                          <p>
                            {activeSlideProgress?.message
                              || slideDeck?.subtitle
                              || 'Outline va cac slide se hien dan ngay tai day.'}
                          </p>
                          {activeSlideProgress?.stageLabel && (
                            <p className="generation-progress-meta">
                              Buoc hien tai: {activeSlideProgress.stageLabel}
                              {typeof activeSlideProgress.stageIndex === 'number' && typeof activeSlideProgress.stageCount === 'number'
                                ? ` (${activeSlideProgress.stageIndex}/${activeSlideProgress.stageCount})`
                                : ''}
                            </p>
                          )}
                          {activeSlideProgress?.detail && (
                            <p className="generation-progress-detail">{activeSlideProgress.detail}</p>
                          )}
                          {getSlideEta(activeSlideProgress) && (
                            <p className="generation-progress-meta">{getSlideEta(activeSlideProgress)}</p>
                          )}
                          {typeof activeSlideProgress?.current === 'number' && typeof activeSlideProgress?.total === 'number' && (
                            <p className="generation-progress-meta">
                              Tien trinh {activeSlideProgress.unitLabel || 'slide'}: {activeSlideProgress.current}/{activeSlideProgress.total}
                            </p>
                          )}
                        </div>
                        <div className="slide-inline-actions">
                          <button
                            className="button button-secondary"
                            onClick={() => navigate(`/slides/${doc.id}`)}
                          >
                            Mo Studio
                          </button>
                          {slideDeck && (
                            <button
                              className="button button-secondary"
                              onClick={() => window.open(slideService.getDeckHtmlUrl(doc.id), '_blank', 'noopener,noreferrer')}
                            >
                              HTML/PDF
                            </button>
                          )}
                        </div>
                      </div>

                      {isGeneratingSlides && (
                        <>
                          <div className="generation-progress-bar">
                            <div
                              className="generation-progress-fill"
                              style={{ width: `${Math.max(0, Math.min(100, activeSlideProgress?.percent || 0))}%` }}
                            ></div>
                          </div>
                          {getSlideSubProgress(activeSlideProgress) !== null && (
                            <div className="generation-subprogress">
                              <div
                                className="generation-subprogress-fill"
                                style={{ width: `${getSlideSubProgress(activeSlideProgress)}%` }}
                              ></div>
                            </div>
                          )}
                        </>
                      )}

                      {inlineOutlineItems.length > 0 && (
                        <div className="slide-inline-outline">
                          {inlineOutlineItems.map((slide) => (
                            <div key={`${doc.id}-${slide.slideIndex}-${slide.heading}`} className="slide-inline-outline-item">
                              <span>{slide.slideIndex}</span>
                              <div>
                                <strong>{slide.heading}</strong>
                                <p>{slide.goal}</p>
                              </div>
                            </div>
                          ))}
                        </div>
                      )}

                      {inlineSlideItems.length > 0 ? (
                        <div className="slide-inline-preview-grid">
                          {inlineSlideItems.map((item) => (
                            <article key={item.id} className={`slide-inline-card slide-inline-${String(item.slideType || '').toLowerCase()}`}>
                              <div className="slide-inline-card-meta">
                                <span>Slide {item.slideIndex}</span>
                                <span>{item.slideType}</span>
                              </div>
                              <h4>{item.heading || `Slide ${item.slideIndex}`}</h4>
                              {item.subheading && <p className="slide-inline-subheading">{item.subheading}</p>}
                              {(item.bodyBlocks || []).length > 0 ? (
                                <div className="slide-inline-body">
                                  {(item.bodyBlocks || []).slice(0, 2).map((block, index) => (
                                    <div key={index} className="slide-inline-bullet">{block}</div>
                                  ))}
                                </div>
                              ) : (
                                <div className="slide-inline-skeleton">
                                  <span></span>
                                  <span></span>
                                </div>
                              )}
                            </article>
                          ))}
                        </div>
                      ) : isGeneratingSlides ? (
                        <div className="slide-inline-preview-grid">
                          {placeholderSlides.map((slide) => (
                            <article key={`${doc.id}-placeholder-${slide.slideIndex}`} className="slide-inline-card slide-inline-pending">
                              <div className="slide-inline-card-meta">
                                <span>Slide {slide.slideIndex}</span>
                                <span>{slide.heading ? 'Outline' : 'Pending'}</span>
                              </div>
                              <h4>{slide.heading || 'Dang cho slide dau tien...'}</h4>
                              {slide.goal && <p className="slide-inline-subheading">{slide.goal}</p>}
                              <div className="slide-skeleton slide-inline-skeleton">
                                <span></span>
                                <span></span>
                                <span></span>
                              </div>
                            </article>
                          ))}
                        </div>
                      ) : null}

                      {!slideDeck && isGeneratingSlides && (
                        <p className="slide-inline-footnote">
                          Outline se xuat hien truoc. Ngay khi backend luu slide 1, card nay se render de ban doc truoc.
                        </p>
                      )}
                    </div>
                  )}
                </div>
                <div className="document-actions">
                  {doc.status === 3 && (
                    <>
                      <button
                        className="button"
                        style={{ backgroundColor: '#6366f1' }}
                        onClick={() => handleViewAnalysis(doc)}
                      >
                        📊 View Analysis
                      </button>
                      <button
                        className="button"
                        style={{ backgroundColor: '#b45309' }}
                        onClick={() => navigate(`/slides/${doc.id}`)}
                      >
                        Slide Studio
                      </button>
                      <button
                        className="button"
                        style={{ backgroundColor: '#0f766e' }}
                        onClick={() => handleGenerateSlides(doc.id)}
                        disabled={isGeneratingSlides}
                      >
                        {isGeneratingSlides
                          ? `Dang tao slide... ${activeSlideProgress?.percent || 0}%`
                          : slideDeck
                            ? 'Tao lai slide'
                            : 'Tao slide dan dan'}
                      </button>
                      <button
                        className="button"
                        onClick={() => handleGenerateQuestions(doc.id)}
                        disabled={isGenerating}
                      >
                        {isGenerating ? `Dang tao... ${generationState.percent || 0}%` : '🎯 Tao bo cau hoi'}
                      </button>
                      <button
                        className="button"
                        onClick={() => navigate(`/quiz/${doc.id}`)}
                        disabled={!doc.questionsCount || doc.questionsCount === 0}
                        style={{ 
                          opacity: (!doc.questionsCount || doc.questionsCount === 0) ? 0.5 : 1,
                          cursor: (!doc.questionsCount || doc.questionsCount === 0) ? 'not-allowed' : 'pointer'
                        }}
                      >
                        🎮 Quiz
                      </button>
                      <button
                        className="button"
                        onClick={() => navigate(`/flashcards/${doc.id}`)}
                        disabled={!doc.questionsCount || doc.questionsCount === 0}
                        style={{ 
                          opacity: (!doc.questionsCount || doc.questionsCount === 0) ? 0.5 : 1,
                          cursor: (!doc.questionsCount || doc.questionsCount === 0) ? 'not-allowed' : 'pointer'
                        }}
                      >
                        🃏 Flashcards
                      </button>
                    </>
                  )}
                  <button
                    className="button"
                    style={{ backgroundColor: '#dc3545' }}
                    onClick={() => handleDelete(doc.id)}
                  >
                    🗑️ Delete
                  </button>
                </div>
              </div>
                );
              })()
            ))}
          </div>
        )}
      </div>

      {/* Analysis Modal */}
      {showAnalysis && (
        <div className="modal-overlay" onClick={closeAnalysisModal}>
          <div className="modal-content" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h2>📊 Phan tich noi dung: {showAnalysis.fileName}</h2>
              <button className="close-btn" onClick={closeAnalysisModal}>✕</button>
            </div>
            <div className="modal-body">
              {/* Main Topics */}
              {showAnalysis.mainTopics && showAnalysis.mainTopics.length > 0 && (
                <div className="analysis-section">
                  <h3>🎯 Chu de chinh</h3>
                  <div className="topics-list">
                    {showAnalysis.mainTopics.map((topic, index) => (
                      <span key={index} className="topic-tag">{topic}</span>
                    ))}
                  </div>
                </div>
              )}

              {/* Key Points */}
              {showAnalysis.keyPoints && showAnalysis.keyPoints.length > 0 && (
                <div className="analysis-section">
                  <h3>💡 Y chinh</h3>
                  <ul className="key-points-list">
                    {showAnalysis.keyPoints.map((point, index) => (
                      <li key={index}>{point}</li>
                    ))}
                  </ul>
                </div>
              )}

              {/* Summary */}
              {showAnalysis.summary && (
                <div className="analysis-section">
                  <h3>📝 Tom tat</h3>
                  <p className="summary-text">{showAnalysis.summary}</p>
                </div>
              )}

              {/* Language */}
              {showAnalysis.language && (
                <div className="analysis-section">
                  <h3>🌐 Ngon ngu</h3>
                  <p><strong>{showAnalysis.language}</strong></p>
                </div>
              )}

              {/* Extracted Text Preview */}
              {showAnalysis.extractedText && (
                <div className="analysis-section">
                  <h3>📄 Van ban da trich xuat</h3>
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
