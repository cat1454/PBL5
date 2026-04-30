import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { documentService, questionService, slideService } from '../services/api';

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
      const docs = await documentService.getUserDocuments('demo-user');
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
            text: `Da tao xong bo cau hoi moi (${progressState.questionsGenerated || 0} cau).`,
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
      setFeedback({ type: 'error', text: 'Khong tao duoc cau hoi. Vui long thu lai.' });
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
    setFeedback({ type: 'info', text: 'Dang tao slide deck tu noi dung tai lieu.' });

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
          setFeedback({ type: 'success', text: 'Da tao xong slide deck va san sang mo Studio.' });
          await loadDocuments({ silent: true });
          break;
        }

        if (progressState.status === 'failed') {
          throw new Error(progressState.error || 'Slide generation failed');
        }

        await sleep(1200);
      }
    } catch (err) {
      setFeedback({ type: 'error', text: 'Khong tao duoc slide deck. Vui long kiem tra log backend.' });
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
  const selectedProcessingState = selectedDocument?.processingProgress;
  const selectedProcessingRunning = !!selectedProcessingState && (selectedProcessingState.status === 'queued' || selectedProcessingState.status === 'running');
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
      return 'vua cap nhat';
    }
    if (diffMs < 3_600_000) {
      return `${Math.max(1, Math.floor(diffMs / 60_000))} phut truoc`;
    }
    if (diffMs < 86_400_000) {
      return `${Math.max(1, Math.floor(diffMs / 3_600_000))} gio truoc`;
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
      return 'AI dang doc toan bo noi dung va tao bo cau hoi moi.';
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
          : 'Tai lieu da xu ly xong va san sang tao output moi.';
      case 4:
        return 'Xu ly that bai. Thu upload lai hoac kiem tra file dau vao.';
      default:
        return 'Dang cap nhat trang thai tai lieu.';
    }
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

    const unit = state.unitLabel || 'muc';
    const prefix = state.stage?.includes('ocr')
      ? 'OCR'
      : state.stage?.includes('analyzing')
        ? 'Phan tich'
        : 'Tien trinh';

    return `${prefix} ${unit}: ${state.current}/${state.total}`;
  };

  const getSourceStatusMeta = (doc) => {
    const activeQuestionState = generating[doc.id];
    const activeSlideState = slideGenerating[doc.id] || slideDecks[doc.id]?.generationProgress;

    if (doc.processingProgress?.status === 'running') {
      return {
        tone: 'active',
        label: `${doc.processingProgress.percent || 0}%`,
        detail: doc.processingProgress.stageLabel || 'Dang phan tich',
      };
    }

    if (activeSlideState?.running || ['queued', 'running'].includes(String(activeSlideState?.status || '').toLowerCase())) {
      return {
        tone: 'active',
        label: `${activeSlideState.percent || 0}%`,
        detail: 'Dang tao slides',
      };
    }

    if (activeQuestionState?.running) {
      return {
        tone: 'active',
        label: `${activeQuestionState.percent || 0}%`,
        detail: 'Dang tao cau hoi',
      };
    }

    if (doc.status === 3) {
      return { tone: 'completed', label: 'Ready', detail: `${doc.questionsCount || 0} cau hoi` };
    }

    if (doc.status === 4) {
      return { tone: 'failed', label: 'Fail', detail: 'Can xem lai du lieu' };
    }

    return { tone: 'uploaded', label: getStatusText(doc.status), detail: getStatusHint(doc) };
  };

  const selectedTopbarState = (() => {
    if (!selectedDocument) {
      return 'Chua chon tai lieu';
    }

    if (selectedProcessingRunning) {
      return `Dang phan tich ${selectedProcessingState.percent || 0}%`;
    }

    if (selectedSlidesRunning) {
      return `Dang tao slide ${selectedActiveSlideProgress?.percent || 0}%`;
    }

    if (selectedQuestionRunning) {
      return `Dang tao cau hoi ${selectedGenerationState?.percent || 0}%`;
    }

    return 'San sang cho workflow tiep theo';
  })();

  const analysisTopics = selectedDocument?.mainTopics?.slice(0, 4) || [];
  const analysisPoints = selectedDocument?.keyPoints?.slice(0, 3) || [];
  const slideOutline = selectedSlideDeck?.outline?.slides?.slice(0, 3) || [];
  const selectedHint = selectedProcessingRunning
    ? 'AI dang doc va tong hop noi dung tu tai lieu. Ban co the theo doi tien trinh ngay trong workspace nay.'
    : selectedSlidesRunning
      ? 'Deck slide dang duoc tao. Ngay khi co slide dau tien, ban co the mo Studio de tinh chinh.'
      : selectedQuestionRunning
        ? 'Question bank dang duoc tao. Quiz va Flashcards se san sang ngay sau khi pipeline hoan tat.'
        : selectedSlideDeck
          ? 'Deck slide da san sang. Ban co the tiep tuc mo Studio, export HTML/PDF hoac noi cac luong xuat ban sau nay.'
          : selectedQuestionsReady
            ? 'Question bank da san sang. Day la luc thuan loi de noi tiep luong quiz, flashcards va danh gia nhanh.'
            : 'Co the bat dau bang cach tao slide deck, tao bo cau hoi hoac mo bang phan tich chi tiet.';

  const renderCanvasBody = () => {
    if (!selectedDocument) {
      return (
        <div className="documents-canvas-empty">
          <h3>Chua co tai lieu nao</h3>
          <p>Them tai lieu moi de khoi tao workspace, phan tich noi dung va noi tiep cac workflow hoc tap.</p>
          <button type="button" className="documents-mini-primary" onClick={() => navigate('/')}>
            Them nguon
          </button>
        </div>
      );
    }

    const kind = getDocumentKind(selectedDocument.fileName);

    const previewTitle = studioView === 'analysis'
      ? `Bang phan tich: ${selectedDocument.fileName}`
      : studioView === 'slides'
        ? `Slide deck: ${selectedSlideDeck?.title || selectedDocument.fileName}`
        : studioView === 'study'
          ? `Hoc tap tu tai lieu: ${selectedDocument.fileName}`
          : `Tong quan tai lieu: ${selectedDocument.fileName}`;

    const previewRows = studioView === 'analysis'
      ? (analysisPoints.length > 0
        ? analysisPoints
        : analysisTopics.length > 0
          ? analysisTopics
          : ['Dang cho AI tong hop y chinh tu tai lieu.'])
      : studioView === 'slides'
        ? (slideOutline.length > 0
          ? slideOutline.map((slide) => slide.heading || `Slide ${slide.slideIndex}`)
          : [selectedSlideDeck ? 'Deck da san sang nhung chua co outline chi tiet.' : 'Chua co slide deck cho tai lieu nay.'])
        : studioView === 'study'
          ? [
              selectedQuestionsReady ? `${selectedDocument.questionsCount || 0} cau hoi da duoc tao.` : 'Chua co question bank.',
              selectedQuestionsReady ? 'Quiz tuong tac da co the mo.' : 'Quiz se san sang sau khi tao bo cau hoi.',
              selectedQuestionsReady ? 'Flashcards da co the mo.' : 'Flashcards se noi tiep tu question bank.',
            ]
          : [
              `Trang thai hien tai: ${getStatusText(selectedDocument.status)}`,
              selectedSlideDeck ? `Slide deck: ${selectedSlideCount} slide san sang.` : 'Slide deck: chua tao.',
              selectedQuestionsReady ? `Study kit: ${selectedDocument.questionsCount || 0} cau hoi san sang.` : 'Study kit: chua tao.',
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
                title="Dang phan tich tai lieu"
                summary={selectedProcessingState?.message || 'He thong dang OCR va trich xuat noi dung.'}
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
                title="Dang tao bo cau hoi"
                summary={selectedGenerationState?.message || 'AI dang tong hop bo cau hoi moi.'}
                metaLines={[
                  selectedGenerationState?.stageLabel || null,
                  getGenerationEta(selectedGenerationState),
                  typeof selectedGenerationState?.current === 'number' && typeof selectedGenerationState?.total === 'number'
                    ? `${selectedGenerationState.current}/${selectedGenerationState.total} ${selectedGenerationState.unitLabel || 'muc'}`
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
                title="Dang tao slides"
                summary={selectedActiveSlideProgress?.message || 'Dang tao deck tu tai lieu duoc chon.'}
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
          label={sharedProps.hasDeck ? 'Mo slide deck' : 'Tao slide deck'}
          detail={sharedProps.hasDeck ? `${selectedSlideCount} slide da san sang.` : 'Khoi tao bo slide tu noi dung tai lieu.'}
          tone="primary"
          disabled={!sharedProps.documentReady || selectedSlidesRunning}
          onClick={() => (sharedProps.hasDeck ? navigate(`/slides/${selectedDocument.id}`) : handleGenerateSlides(selectedDocument.id))}
        />,
        <ActionButton
          key="overview-study"
          label={sharedProps.hasQuestions ? 'Mo bo hoc tap' : 'Tao question bank'}
          detail={sharedProps.hasQuestions ? `${selectedDocument.questionsCount || 0} cau hoi da san sang.` : 'Sinh quiz va flashcards tu tai lieu.'}
          disabled={!sharedProps.documentReady || selectedQuestionRunning}
          onClick={() => (sharedProps.hasQuestions ? setStudioView('study') : handleGenerateQuestions(selectedDocument.id))}
        />,
        <ActionButton
          key="overview-analysis"
          label="Xem phan tich"
          detail="Mo tom tat, topic va y chinh trong modal."
          onClick={() => setShowAnalysis(selectedDocument)}
        />,
      );
    }

    if (studioView === 'analysis') {
      actions.push(
        <ActionButton
          key="analysis-modal"
          label="Mo ban phan tich day du"
          detail="Xem tom tat, topic, key points va van ban trich xuat."
          onClick={() => setShowAnalysis(selectedDocument)}
        />,
        <ActionButton
          key="analysis-study"
          label={sharedProps.hasQuestions ? 'Chuyen sang hoc tap' : 'Tao question bank'}
          detail={sharedProps.hasQuestions ? 'Mo nhanh quiz va flashcards tu tai lieu nay.' : 'Dung y chinh hien tai de tao bo cau hoi.'}
          disabled={!sharedProps.documentReady || selectedQuestionRunning}
          onClick={() => (sharedProps.hasQuestions ? setStudioView('study') : handleGenerateQuestions(selectedDocument.id))}
        />,
      );
    }

    if (studioView === 'slides') {
      actions.push(
        <ActionButton
          key="slides-main"
          label={sharedProps.hasDeck ? 'Mo Slide Studio' : 'Tao slide deck'}
          detail={sharedProps.hasDeck ? 'Chinh sua va xem deck o route hien co.' : 'Khoi dong luong tao slide tu tai lieu.'}
          tone="primary"
          disabled={!sharedProps.documentReady || selectedSlidesRunning}
          onClick={() => (sharedProps.hasDeck ? navigate(`/slides/${selectedDocument.id}`) : handleGenerateSlides(selectedDocument.id))}
        />,
        <ActionButton
          key="slides-export"
          label="Xuat HTML / PDF"
          detail="Mo ban export dang co cua slide deck."
          disabled={!sharedProps.hasDeck}
          onClick={() => window.open(slideService.getDeckHtmlUrl(selectedDocument.id), '_blank', 'noopener,noreferrer')}
        />,
      );
    }

    if (studioView === 'study') {
      actions.push(
        <ActionButton
          key="study-quiz"
          label={sharedProps.hasQuestions ? 'Mo Quiz' : 'Tao question bank'}
          detail={sharedProps.hasQuestions ? 'Bat dau quiz tuong tac voi tai lieu nay.' : 'Sinh bo cau hoi de kich hoat che do hoc tap.'}
          tone={sharedProps.hasQuestions ? 'primary' : 'default'}
          disabled={!sharedProps.documentReady || selectedQuestionRunning}
          onClick={() => (sharedProps.hasQuestions ? navigate(`/study/${selectedDocument.id}/quiz`) : handleGenerateQuestions(selectedDocument.id))}
        />,
        <ActionButton
          key="study-flashcards"
          label="Mo Flashcards"
          detail="On nhanh bang the ghi nho tu bo cau hoi hien co."
          disabled={!sharedProps.hasQuestions}
          onClick={() => navigate(`/study/${selectedDocument.id}/flashcards`)}
        />,
      );
    }

    actions.push(
      <ActionButton
        key="manage-delete"
        label="Xoa tai lieu"
        detail="Xoa tai lieu dang chon khoi he thong."
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
              <span>{documents.length} tai lieu</span>
              <span>{selectedDocument?.questionsCount || 0} cau hoi</span>
              <span>{selectedSlideCount} slide</span>
              <span>Cap nhat: {formatRelativeTime(lastUpdated)}</span>
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
              {refreshing ? 'Dang dong bo' : 'Dong bo'}
            </button>
            <button
              type="button"
              className="documents-mini-primary"
              onClick={() => selectedDocument && navigate(`/slides/${selectedDocument.id}`)}
              disabled={!selectedDocument || selectedDocument.status !== 3}
            >
              Mo Studio
            </button>
          </div>
        </div>

        {feedback && (
          <div className={`alert ${feedback.type === 'success' ? 'alert-success' : feedback.type === 'error' ? 'alert-error' : 'alert-info'}`}>
            {feedback.text}
          </div>
        )}

        <div className="documents-studio-main documents-studio-main-compact">
          <aside className="documents-studio-sidebar">
            <div className="documents-panel-title">Nguon / Documents</div>

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
                + Them nguon
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
                <div className="documents-sidebar-empty">Khong tim thay tai lieu phu hop bo loc.</div>
              )}
            </div>

          </aside>

          <div className="documents-studio-center">
            <div className="documents-studio-toolbar">
              <button type="button" className={`documents-toolbar-btn${studioView === 'overview' ? ' active' : ''}`} onClick={() => setStudioView('overview')}>
                Tong quan
              </button>
              <button type="button" className={`documents-toolbar-btn${studioView === 'analysis' ? ' active' : ''}`} onClick={() => setStudioView('analysis')}>
                Phan tich
              </button>
              <button type="button" className={`documents-toolbar-btn${studioView === 'slides' ? ' active' : ''}`} onClick={() => setStudioView('slides')}>
                Slides
              </button>
              <button type="button" className={`documents-toolbar-btn${studioView === 'study' ? ' active' : ''}`} onClick={() => setStudioView('study')}>
                Hoc tap
              </button>
            </div>

            <div className="documents-studio-canvas">
              {renderCanvasBody()}
              {selectedDocument && (
                <div className="documents-preview-card documents-quick-actions-card">
                  <div className="documents-preview-layout documents-quick-actions-layout">
                    <div className="documents-preview-copy">
                      <h2 className="documents-quick-actions-title">Tac vu nhanh</h2>
                      <p className="documents-preview-summary documents-quick-actions-summary">
                        {studioView === 'overview'
                          ? 'Chi hien cac thao tac can thiet nhat cho tai lieu dang chon.'
                          : studioView === 'analysis'
                            ? 'Tap trung vao xem nhanh insight va mo tiep bo hoc tap.'
                            : studioView === 'slides'
                              ? 'Deck slide va export duoc dat chung trong mot cum thao tac.'
                              : 'Quiz va flashcards duoc gom vao cung mot khu hoc tap.'}
                      </p>
                    </div>
                    <div className="documents-preview-sidecard">
                      <span>Workspace</span>
                      <strong>{selectedTopbarState}</strong>
                      <small>Cap nhat {formatRelativeTime(lastUpdated)}</small>
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
              <h2>Phan tich noi dung: {showAnalysis.fileName}</h2>
              <button className="close-btn" onClick={closeAnalysisModal}>x</button>
            </div>
            <div className="modal-body">
              {showAnalysis.mainTopics && showAnalysis.mainTopics.length > 0 && (
                <div className="analysis-section">
                  <h3>Chu de chinh</h3>
                  <div className="topics-list">
                    {showAnalysis.mainTopics.map((topic, index) => (
                      <span key={index} className="topic-tag">{topic}</span>
                    ))}
                  </div>
                </div>
              )}

              {showAnalysis.keyPoints && showAnalysis.keyPoints.length > 0 && (
                <div className="analysis-section">
                  <h3>Y chinh</h3>
                  <ul className="key-points-list">
                    {showAnalysis.keyPoints.map((point, index) => (
                      <li key={index}>{point}</li>
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
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default DocumentList;
