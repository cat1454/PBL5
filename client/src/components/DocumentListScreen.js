import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { LuX } from 'react-icons/lu';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useLanguage } from '../context/LanguageContext';
import ProgressCard from './ProgressCard';
import { useToast } from './common/ToastProvider';
import { documentService, getApiErrorMessage, isApiNotFound, questionService, slideService } from '../services/api';
import { getProgressStageLabel, isActiveProgress, normalizeProgressState } from '../services/progress';
import {
  confirmGenerationReadiness,
  getDocumentReadiness,
  getReadinessLabel,
  getReadinessMessage,
} from '../services/generationReadiness';
import DocumentUnderstandingPanel from './DocumentUnderstandingPanel';

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

const normalizeDocumentProgressMap = (documents) => documents.reduce((accumulator, doc) => {
  accumulator[doc.id] = doc.processingProgress
    ? normalizeProgressState(doc.processingProgress, { documentId: doc.id })
    : null;
  return accumulator;
}, {});

const getDocumentBadgeColor = (progress) => {
  switch (progress?.status) {
    case 'completed':
      return '#28a745';
    case 'failed':
      return '#dc3545';
    case 'running':
      return '#2563eb';
    case 'queued':
    default:
      return '#b45309';
  }
};

const getDocumentReadyHint = (doc) => {
  if (doc.questionsCount > 0) {
    return 'Da san sang hoc bang Quiz hoac Flashcards.';
  }

  return 'Tai lieu da xu ly xong. Ban co the tao bo cau hoi hoac slide deck ngay bay gio.';
};

function DocumentListScreen() {
  const { currentUser } = useAuth();
  const { showToast } = useToast();
  const { language } = useLanguage();
  const [documents, setDocuments] = useState([]);
  const [documentProgress, setDocumentProgress] = useState({});
  const [questionProgress, setQuestionProgress] = useState({});
  const [slideProgress, setSlideProgress] = useState({});
  const [slideDecks, setSlideDecks] = useState({});
  const [slideDeckAvailability, setSlideDeckAvailability] = useState({});
  const [exportingDeck, setExportingDeck] = useState(null);
  const [showAnalysis, setShowAnalysis] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [refreshing, setRefreshing] = useState(false);
  const [lastUpdated, setLastUpdated] = useState(null);
  const navigate = useNavigate();

  const loadDocuments = useCallback(async ({ silent = false } = {}) => {
    if (!silent) {
      setRefreshing(true);
    }

    try {
      setError('');
      const docs = await documentService.getUserDocuments(String(currentUser?.id || ''));
      setDocuments(docs);
      setDocumentProgress((current) => ({
        ...normalizeDocumentProgressMap(docs),
        ...current,
      }));
      setLastUpdated(new Date());
      return docs;
    } catch (err) {
      setError(getApiErrorMessage(err, 'Error loading documents'));
      console.error(err);
      return [];
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [currentUser?.id]);

  useEffect(() => {
    loadDocuments();
  }, [loadDocuments]);

  const activeDocumentIds = useMemo(
    () => documents
      .filter((doc) => isActiveProgress(documentProgress[doc.id] || doc.processingProgress))
      .map((doc) => doc.id),
    [documentProgress, documents]
  );

  useEffect(() => {
    if (activeDocumentIds.length === 0) {
      return undefined;
    }

    let cancelled = false;

    const refreshProgress = async () => {
      const results = await Promise.allSettled(
        activeDocumentIds.map((documentId) => documentService.getDocumentProgress(documentId))
      );

      if (cancelled) {
        return;
      }

      setDocumentProgress((current) => {
        const next = { ...current };

        results.forEach((result, index) => {
          if (result.status === 'fulfilled') {
            next[activeDocumentIds[index]] = normalizeProgressState(result.value, { documentId: activeDocumentIds[index] });
          }
        });

        return next;
      });
    };

    refreshProgress();
    const interval = setInterval(refreshProgress, 3000);

    return () => {
      cancelled = true;
      clearInterval(interval);
    };
  }, [activeDocumentIds]);

  useEffect(() => {
    let cancelled = false;

    const syncSlideDecks = async () => {
      const targetDocuments = documents.filter((doc) =>
        doc.status === 3 && (
          typeof slideDeckAvailability[doc.id] === 'undefined'
          || isActiveProgress(slideProgress[doc.id])
          || isActiveProgress(slideDecks[doc.id]?.generationProgress)
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
          }
        });

        return next;
      });

      setSlideDeckAvailability((current) => {
        const next = { ...current };

        results.forEach((result, index) => {
          if (result.status === 'fulfilled') {
            next[targetDocuments[index].id] = !!result.value;
          }
        });

        return next;
      });
    };

    syncSlideDecks();

    return () => {
      cancelled = true;
    };
  }, [documents, slideDeckAvailability, slideDecks, slideProgress]);

  useEffect(() => {
    const hasActiveSlides = Object.values(slideProgress).some((progress) => isActiveProgress(progress))
      || Object.values(slideDecks).some((deck) => isActiveProgress(deck?.generationProgress));

    if (activeDocumentIds.length === 0 && !hasActiveSlides) {
      return undefined;
    }

    const interval = setInterval(() => {
      loadDocuments({ silent: true });
    }, 3000);

    return () => clearInterval(interval);
  }, [activeDocumentIds.length, loadDocuments, slideDecks, slideProgress]);

  const handleGenerateQuestions = async (documentId) => {
    const document = documents.find((doc) => Number(doc.id) === Number(documentId));
    const readinessDecision = confirmGenerationReadiness(getDocumentReadiness(document), language);
    if (!readinessDecision.allowed) {
      return;
    }

    setQuestionProgress((current) => ({
      ...current,
      [documentId]: normalizeProgressState({
        status: 'queued',
        stage: 'queued',
        stageLabel: 'Cho xu ly',
        message: 'Da tao job sinh cau hoi.',
        percent: 0,
      }, { documentId }),
    }));
    showToast({
      type: 'info',
      message: 'Đã bắt đầu tạo bộ câu hỏi.',
      description: 'Tiến trình sẽ tiếp tục hiển thị trong card tài liệu.',
    });

    try {
      const startResult = await questionService.startGenerateQuestions(documentId, 5, null, {
        confirmLowConfidence: readinessDecision.confirmed,
      });
      const jobId = startResult.jobId;
      const timeoutAt = Date.now() + 5 * 60 * 1000;
      let delayMs = 1200;

      while (Date.now() < timeoutAt) {
        let progressState;
        try {
          progressState = normalizeProgressState(
            await questionService.getGenerateProgress(jobId),
            { documentId, jobId }
          );
          delayMs = 1200;
        } catch (progressError) {
          if (!isApiNotFound(progressError)) {
            throw progressError;
          }

          const freshDocs = await loadDocuments({ silent: true });
          const recoveredDoc = freshDocs.find((doc) => doc.id === documentId);
          if ((recoveredDoc?.questionsCount || 0) <= 0) {
            throw new Error('Backend restarted and the question generation job is no longer available.');
          }

          progressState = normalizeProgressState({
            status: 'completed',
            stage: 'completed',
            stageLabel: 'Da khoi phuc',
            message: 'Backend da mat job trong RAM, nhung question bank da san sang.',
            percent: 100,
            questionsGenerated: recoveredDoc.questionsCount,
          }, { documentId, jobId });
        }

        setQuestionProgress((current) => ({
          ...current,
          [documentId]: progressState,
        }));

        if (progressState.status === 'completed') {
          showToast({
            type: 'success',
            message: `Đã tạo xong bộ câu hỏi (${progressState.questionsGenerated || 0} câu).`,
          });
          await loadDocuments({ silent: true });
          break;
        }

        if (progressState.status === 'failed') {
          throw new Error(progressState.error || progressState.message || 'Question generation failed');
        }

        await sleep(delayMs);
        delayMs = Math.min(delayMs + 500, 5000);
      }
    } catch (err) {
      showToast({
        type: 'error',
        message: 'Không tạo được câu hỏi.',
        description: getApiErrorMessage(err, 'Kiểm tra progress và backend log.'),
      });
      console.error(err);
    } finally {
      setQuestionProgress((current) => {
        const next = { ...current };
        if (next[documentId]?.status !== 'completed') {
          delete next[documentId];
        }
        return next;
      });
    }
  };

  const handleGenerateSlides = async (documentId) => {
    const document = documents.find((doc) => Number(doc.id) === Number(documentId));
    const readinessDecision = confirmGenerationReadiness(getDocumentReadiness(document), language);
    if (!readinessDecision.allowed) {
      return;
    }

    setSlideProgress((current) => ({
      ...current,
      [documentId]: normalizeProgressState({
        status: 'queued',
        stage: 'queued',
        stageLabel: 'Cho xu ly',
        message: 'Da tao job sinh slide.',
        percent: 0,
      }, { documentId }),
    }));
    setSlideDeckAvailability((current) => ({
      ...current,
      [documentId]: true,
    }));
    showToast({
      type: 'info',
      message: 'Đã bắt đầu tạo slide deck.',
      description: 'Tiến trình sẽ tiếp tục hiển thị trong card tài liệu.',
    });

    try {
      const startResult = await slideService.startGenerateSlides(documentId, {
        desiredSlideCount: 8,
        confirmLowConfidence: readinessDecision.confirmed,
      });
      const jobId = startResult.jobId;
      const timeoutAt = Date.now() + 8 * 60 * 1000;
      let delayMs = 1200;

      while (Date.now() < timeoutAt) {
        let progressState;
        try {
          progressState = normalizeProgressState(
            await slideService.getGenerateProgress(jobId),
            { documentId, jobId }
          );
          delayMs = 1200;
        } catch (progressError) {
          if (!isApiNotFound(progressError)) {
            throw progressError;
          }

          const deck = await slideService.getDeckByDocument(documentId);
          if (!deck) {
            throw new Error('Backend restarted and the slide generation job is no longer available.');
          }

          setSlideDecks((current) => ({
            ...current,
            [documentId]: deck,
          }));
          setSlideDeckAvailability((current) => ({
            ...current,
            [documentId]: true,
          }));

          const fallbackProgress = normalizeProgressState(deck.generationProgress, { documentId, jobId });
          if (deck.status === 'Completed' || fallbackProgress.status === 'completed') {
            progressState = {
              ...fallbackProgress,
              status: 'completed',
              stage: 'completed',
              stageLabel: 'Da khoi phuc',
              message: 'Backend da mat job trong RAM, nhung slide deck da san sang.',
              percent: 100,
            };
          } else if (isActiveProgress(fallbackProgress)) {
            progressState = fallbackProgress;
          } else {
            throw new Error('Backend restarted and the slide generation job is no longer available.');
          }
        }

        setSlideProgress((current) => ({
          ...current,
          [documentId]: progressState,
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
          showToast({
            type: 'success',
            message: `Đã tạo xong slide deck (${progressState.slidesGenerated || 0} slide đã hoàn tất).`,
          });
          await loadDocuments({ silent: true });
          break;
        }

        if (progressState.status === 'failed') {
          throw new Error(progressState.error || progressState.message || 'Slide generation failed');
        }

        await sleep(delayMs);
        delayMs = Math.min(delayMs + 500, 5000);
      }
    } catch (err) {
      showToast({
        type: 'error',
        message: 'Không tạo được slide deck.',
        description: getApiErrorMessage(err, 'Kiểm tra progress và backend log.'),
      });
      console.error(err);
    } finally {
      setSlideProgress((current) => {
        const next = { ...current };
        if (next[documentId]?.status !== 'completed') {
          delete next[documentId];
        }
        return next;
      });
    }
  };

  const handleDownloadDeckHtml = async (deck) => {
    if (!deck || exportingDeck) {
      return;
    }

    try {
      setExportingDeck(`${deck.id}:html`);
      const result = await slideService.exportDeckHtml(deck.id);
      showToast({
        type: 'success',
        message: 'Đã tải file HTML.',
        description: result.filename,
      });
    } catch (err) {
      showToast({
        type: 'error',
        message: getApiErrorMessage(err, 'Không thể xuất HTML.'),
      });
    } finally {
      setExportingDeck(null);
    }
  };

  const handleOpenDeckPrint = async (deck) => {
    if (!deck || exportingDeck) {
      return;
    }

    const printWindow = window.open('', '_blank');
    if (!printWindow) {
      showToast({
        type: 'error',
        message: 'Trinh duyet da chan tab in. Hay cho phep popup va thu lai.',
      });
      return;
    }
    printWindow.opener = null;

    try {
      setExportingDeck(`${deck.id}:print`);
      const blob = await slideService.getDeckPrintHtml(deck.id);
      const url = window.URL.createObjectURL(blob);
      printWindow.location.href = url;
      window.setTimeout(() => window.URL.revokeObjectURL(url), 60000);
      showToast({
        type: 'success',
        message: 'Da mo ban In / Luu PDF.',
      });
    } catch (err) {
      printWindow.close();
      showToast({
        type: 'error',
        message: getApiErrorMessage(err, 'Khong the mo ban in.'),
      });
    } finally {
      setExportingDeck(null);
    }
  };

  const handleDelete = async (documentId) => {
    if (!window.confirm('Are you sure you want to delete this document?')) {
      return;
    }

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
  };

  const processingCount = documents.filter((doc) => isActiveProgress(documentProgress[doc.id] || doc.processingProgress)).length;
  const readyCount = documents.filter((doc) => doc.status === 3).length;
  const totalQuestions = documents.reduce((sum, doc) => sum + (doc.questionsCount || 0), 0);
  const formatDateTime = (value) => new Date(value).toLocaleString();

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
            <h2>My Documents</h2>
            <p className="section-subtitle">Theo doi document, question generation, va slide generation trong mot dashboard.</p>
          </div>
          <div className="header-actions">
            <button className="button button-secondary" onClick={() => loadDocuments()} disabled={refreshing}>
              {refreshing ? 'Dang lam moi...' : 'Lam moi'}
            </button>
            {processingCount > 0 && (
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

        {lastUpdated && <p className="timestamp-note">Cap nhat lan cuoi: {formatDateTime(lastUpdated)}</p>}

        {documents.length === 0 ? (
          <div className="empty-state">
            <h3>Chua co tai lieu nao</h3>
            <p>Upload PDF, DOCX hoac anh de AI phan tich noi dung va tao bo cau hoi tu dong.</p>
          </div>
        ) : (
          <div className="document-list">
            {documents.map((doc) => {
              const docProgress = documentProgress[doc.id] || (doc.processingProgress
                ? normalizeProgressState(doc.processingProgress, { documentId: doc.id })
                : null);
              const activeQuestionProgress = questionProgress[doc.id];
              const slideDeck = slideDecks[doc.id];
              const activeSlideProgress = slideProgress[doc.id] || (slideDeck?.generationProgress
                ? normalizeProgressState(slideDeck.generationProgress, { documentId: doc.id })
                : null);
              const showDocumentProgress = isActiveProgress(docProgress) || docProgress?.status === 'failed';
              const showQuestionProgress = !!activeQuestionProgress;
              const showSlideProgress = !!slideDeck || isActiveProgress(activeSlideProgress) || activeSlideProgress?.status === 'failed';
              const inlineSlideItems = slideDeck?.items?.slice(0, 3) || [];
              const inlineOutlineItems = slideDeck?.outline?.slides?.slice(0, 4) || [];
              const placeholderSlides = inlineOutlineItems.length > 0
                ? inlineOutlineItems.slice(0, Math.min(3, inlineOutlineItems.length))
                : Array.from({ length: 3 }, (_, index) => ({ slideIndex: index + 1 }));
              const statusLabel = getProgressStageLabel(docProgress);
              const statusHint = docProgress.message || (doc.status === 3 ? getDocumentReadyHint(doc) : 'Dang cap nhat trang thai tai lieu.');
              const readiness = getDocumentReadiness(doc);
              const readinessMessage = getReadinessMessage(readiness, language);

              return (
                <div key={doc.id} className="document-item">
                  <div className="document-info">
                    <div className="document-title-row">
                      <h3>{doc.fileName}</h3>
                      <span className="status-badge" style={{ backgroundColor: getDocumentBadgeColor(docProgress) }}>
                        {statusLabel}
                      </span>
                    </div>

                    <p className="document-meta">
                      <span>{formatDateTime(doc.createdAt)}</span>
                      <span>{(doc.fileSize / 1024).toFixed(0)} KB</span>
                      <span>{doc.questionsCount || 0} cau hoi</span>
                    </p>

                    <p className="status-hint">{statusHint}</p>

                    {readiness && (
                      <span className={`generation-readiness-badge tone-${readiness.tone}`}>
                        {getReadinessLabel(readiness, language)}
                      </span>
                    )}

                    {readinessMessage && (
                      <div className={`generation-readiness-card tone-${readiness.tone}`}>
                        <strong>{readinessMessage.title}</strong>
                        <p>{readinessMessage.body}</p>
                      </div>
                    )}

                    <DocumentUnderstandingPanel
                      documentId={doc.id}
                      compact
                    />

                    {showDocumentProgress && (
                      <ProgressCard
                        title="Document progress"
                        progress={docProgress}
                        context="document"
                        className="progress-card-embedded"
                      />
                    )}

                    {doc.mainTopics && doc.mainTopics.length > 0 && (
                      <div className="inline-topics">
                        {doc.mainTopics.slice(0, 5).map((topic, index) => (
                          <span key={`${doc.id}-topic-${index}`} className="mini-topic-tag">{topic}</span>
                        ))}
                      </div>
                    )}

                    {showQuestionProgress && (
                      <ProgressCard
                        title="Question generation"
                        progress={activeQuestionProgress}
                        context="question"
                        className="progress-card-embedded"
                      />
                    )}

                    {showSlideProgress && (
                      <div className="slide-inline-panel">
                        <div className="slide-inline-header">
                          <div>
                            <strong>{slideDeck?.title || 'Slide deck'}</strong>
                            <p>{slideDeck?.subtitle || activeSlideProgress.message || 'Outline va cac slide se hien dan ngay tai day.'}</p>
                          </div>
                          <div className="slide-inline-actions">
                            <button className="button button-secondary" onClick={() => navigate(`/slides/${doc.id}`)}>
                              Mo Studio
                            </button>
                            {slideDeck && (
                              <>
                                <button
                                  className="button button-secondary"
                                  onClick={() => handleDownloadDeckHtml(slideDeck)}
                                  disabled={Boolean(exportingDeck)}
                                >
                                  HTML
                                </button>
                                <button
                                  className="button button-secondary"
                                  onClick={() => handleOpenDeckPrint(slideDeck)}
                                  disabled={Boolean(exportingDeck)}
                                >
                                  In / PDF
                                </button>
                              </>
                            )}
                          </div>
                        </div>

                        <ProgressCard
                          title="Slide generation"
                          progress={activeSlideProgress}
                          context="slide"
                          className="progress-card-embedded progress-card-inline-slide"
                        />

                        {inlineOutlineItems.length > 0 && (
                          <div className="slide-inline-outline">
                            {inlineOutlineItems.map((slide) => (
                              <div key={`${doc.id}-${slide.slideIndex}-${slide.heading || 'outline'}`} className="slide-inline-outline-item">
                                <span>{slide.slideIndex}</span>
                                <div>
                                  <strong>{slide.heading || `Slide ${slide.slideIndex}`}</strong>
                                  <p>{slide.goal || 'Dang cap nhat outline slide.'}</p>
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
                                      <div key={`${item.id}-block-${index}`} className="slide-inline-bullet">{block}</div>
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
                        ) : isActiveProgress(activeSlideProgress) ? (
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
                      </div>
                    )}
                  </div>

                  <div className="document-actions">
                    {doc.status === 3 && (
                      <>
                        <button className="button" style={{ backgroundColor: '#6366f1' }} onClick={() => setShowAnalysis(doc)}>
                          View Analysis
                        </button>
                        <button className="button" style={{ backgroundColor: '#b45309' }} onClick={() => navigate(`/slides/${doc.id}`)}>
                          Slide Studio
                        </button>
                        <button
                          className="button"
                          style={{ backgroundColor: '#0f766e' }}
                          onClick={() => handleGenerateSlides(doc.id)}
                          disabled={isActiveProgress(activeSlideProgress)}
                        >
                          {isActiveProgress(activeSlideProgress)
                            ? `Dang tao slide... ${activeSlideProgress.percent || 0}%`
                            : slideDeck
                              ? 'Tao lai slide'
                              : 'Tao slide dan dan'}
                        </button>
                        <button
                          className="button"
                          onClick={() => handleGenerateQuestions(doc.id)}
                          disabled={isActiveProgress(activeQuestionProgress)}
                        >
                          {isActiveProgress(activeQuestionProgress)
                            ? `Dang tao... ${activeQuestionProgress.percent || 0}%`
                            : 'Tao bo cau hoi'}
                        </button>
                        <button
                          className="button"
                          onClick={() => navigate(`/study/${doc.id}/quiz`)}
                          disabled={!doc.questionsCount}
                          style={{ opacity: doc.questionsCount ? 1 : 0.5, cursor: doc.questionsCount ? 'pointer' : 'not-allowed' }}
                        >
                          Quiz
                        </button>
                        <button
                          className="button"
                          onClick={() => navigate(`/study/${doc.id}/flashcards`)}
                          disabled={!doc.questionsCount}
                          style={{ opacity: doc.questionsCount ? 1 : 0.5, cursor: doc.questionsCount ? 'pointer' : 'not-allowed' }}
                        >
                          Flashcards
                        </button>
                      </>
                    )}
                    <button className="button" style={{ backgroundColor: '#dc3545' }} onClick={() => handleDelete(doc.id)}>
                      Delete
                    </button>
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>

      {showAnalysis && (
        <div className="modal-overlay" onClick={() => setShowAnalysis(null)}>
          <div className="modal-content" onClick={(event) => event.stopPropagation()}>
            <div className="modal-header">
              <h2>Phan tich noi dung: {showAnalysis.fileName}</h2>
              <button className="close-btn" onClick={() => setShowAnalysis(null)}>
                <LuX aria-hidden="true" />
              </button>
            </div>
            <div className="modal-body">
              {showAnalysis.mainTopics && showAnalysis.mainTopics.length > 0 && (
                <div className="analysis-section">
                  <h3>Chu de chinh</h3>
                  <div className="topics-list">
                    {showAnalysis.mainTopics.map((topic, index) => (
                      <span key={`analysis-topic-${index}`} className="topic-tag">{topic}</span>
                    ))}
                  </div>
                </div>
              )}

              {showAnalysis.keyPoints && showAnalysis.keyPoints.length > 0 && (
                <div className="analysis-section">
                  <h3>Y chinh</h3>
                  <ul className="key-points-list">
                    {showAnalysis.keyPoints.map((point, index) => (
                      <li key={`analysis-point-${index}`}>{point}</li>
                    ))}
                  </ul>
                </div>
              )}

              {showAnalysis.summary && (
                <div className="analysis-section">
                  <h3>Tom tat</h3>
                  <p className="summary-text">{showAnalysis.summary}</p>
                </div>
              )}

              {showAnalysis.language && (
                <div className="analysis-section">
                  <h3>Ngon ngu</h3>
                  <p><strong>{showAnalysis.language}</strong></p>
                </div>
              )}

              {showAnalysis.extractedText && (
                <div className="analysis-section">
                  <h3>Van ban da trich xuat</h3>
                  <div className="extracted-text-preview">
                    {showAnalysis.extractedText.substring(0, 1000)}
                    {showAnalysis.extractedText.length > 1000 && '...'}
                  </div>
                </div>
              )}
              <DocumentUnderstandingPanel
                documentId={showAnalysis.id}
                showEmpty
                defaultOpen
              />
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default DocumentListScreen;
