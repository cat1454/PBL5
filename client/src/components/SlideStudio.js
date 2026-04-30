import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { documentService, slideService } from '../services/api';
import { buildSlideImageViewModel } from '../services/slideImages';
import { useLanguage } from '../context/LanguageContext';

function SlideStudio({ documentId: propDocumentId }) {
  const { t, language } = useLanguage();
  const params = useParams();
  const documentId = propDocumentId || params.documentId;
  const navigate = useNavigate();
  const slideRefs = useRef({});
  const [documentMeta, setDocumentMeta] = useState(null);
  const [deck, setDeck] = useState(null);
  const [progress, setProgress] = useState(null);
  const [jobId, setJobId] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [generationError, setGenerationError] = useState('');
  const [feedback, setFeedback] = useState('');
  const [desiredSlideCount, setDesiredSlideCount] = useState(8);
  const [editingSlideId, setEditingSlideId] = useState(null);
  const [drafts, setDrafts] = useState({});
  const [briefDirty, setBriefDirty] = useState(false);
  const [hideLowConfidence, setHideLowConfidence] = useState(false);
  const [expandedMediaSlideId, setExpandedMediaSlideId] = useState(null);
  const [mediaBusySlideId, setMediaBusySlideId] = useState(null);
  const [activeLeftTab, setActiveLeftTab] = useState('outline');
  const [selectedSlideId, setSelectedSlideId] = useState(null);
  const [isInspectorOpen, setIsInspectorOpen] = useState(true);
  const [canvasZoom, setCanvasZoom] = useState('fit');

  const audienceOptions = t('slides.options.audiences');
  const toneOptions = t('slides.options.tones');
  const languageStyleOptions = t('slides.options.languageStyles');

  const themeOptions = useMemo(() => ([
    {
      key: 'editorial-sunrise',
      label: t('slides.themeNames.editorialSunrise'),
      blurb: t('slides.themes.editorialSunrise'),
    },
    {
      key: 'paper-mint',
      label: t('slides.themeNames.paperMint'),
      blurb: t('slides.themes.paperMint'),
    },
    {
      key: 'cobalt-grid',
      label: t('slides.themeNames.cobaltGrid'),
      blurb: t('slides.themes.cobaltGrid'),
    },
    {
      key: 'midnight-signal',
      label: t('slides.themeNames.midnightSignal'),
      blurb: t('slides.themes.midnightSignal'),
    },
  ]), [t]);

  const defaultBrief = useMemo(() => ({
    themeKey: 'editorial-sunrise',
    audience: audienceOptions[0],
    tone: toneOptions[0],
    narrativeGoal: language === 'vi'
      ? 'Giúp người đọc nắm cấu trúc và ý chính của tài liệu chỉ trong một lần xem.'
      : 'Help the reader grasp the structure and key ideas of the source in one pass.',
    languageStyle: languageStyleOptions[0],
  }), [audienceOptions, language, languageStyleOptions, toneOptions]);

  const [deckBrief, setDeckBrief] = useState(defaultBrief);

  useEffect(() => {
    if (!briefDirty) {
      setDeckBrief(defaultBrief);
    }
  }, [briefDirty, defaultBrief]);

  const loadDocument = useCallback(async () => {
    try {
      const data = await documentService.getDocument(documentId);
      setDocumentMeta(data);
      setDeckBrief((current) => ({
        ...current,
        narrativeGoal: briefDirty
          ? current.narrativeGoal
          : data?.summary || current.narrativeGoal,
      }));
    } catch (err) {
      console.error(err);
      setError(t('slides.errors.loadDocument'));
    }
  }, [briefDirty, documentId, t]);

  const loadDeck = useCallback(async ({ silent = false } = {}) => {
    if (!silent) {
      setLoading(true);
    }

    try {
      const data = await slideService.getDeckByDocument(documentId);
      if (!data) {
        setDeck(null);
        return;
      }

      setDeck(data);
      if (data?.outline?.brief && !briefDirty) {
        setDeckBrief({
          themeKey: data.outline.brief.themeKey || defaultBrief.themeKey,
          audience: data.outline.brief.audience || defaultBrief.audience,
          tone: data.outline.brief.tone || defaultBrief.tone,
          narrativeGoal: data.outline.brief.narrativeGoal || defaultBrief.narrativeGoal,
          languageStyle: data.outline.brief.languageStyle || defaultBrief.languageStyle,
        });
      }
      if (data?.generationProgress) {
        setProgress(data.generationProgress);
        setJobId(data.generationProgress.jobId || data.generationProgress.JobId || jobId);
      }
    } catch (err) {
      console.error(err);
      setError(t('slides.errors.loadDeck'));
    } finally {
      if (!silent) {
        setLoading(false);
      }
    }
  }, [briefDirty, defaultBrief, documentId, jobId, t]);

  useEffect(() => {
    let cancelled = false;

    const bootstrap = async () => {
      setLoading(true);
      setError('');
      setFeedback('');
      setBriefDirty(false);
      await loadDocument();
      if (!cancelled) {
        await loadDeck({ silent: true });
        setLoading(false);
      }
    };

    bootstrap();
    return () => {
      cancelled = true;
    };
  }, [loadDeck, loadDocument]);

  const isGenerating = progress && (progress.status === 'queued' || progress.status === 'running');

  useEffect(() => {
    if (!jobId && !isGenerating && !(deck && (deck.status === 'GeneratingSlides' || deck.status === 'GeneratingOutline'))) {
      return undefined;
    }

    const interval = setInterval(async () => {
      try {
        if (jobId) {
          const nextProgress = await slideService.getGenerateProgress(jobId);
          setProgress(nextProgress);
          if (nextProgress.slideDeckId) {
            setJobId(nextProgress.jobId || jobId);
          }
          if (nextProgress.status === 'failed') {
            setGenerationError(nextProgress.error || nextProgress.detail || t('slides.generationStatus.failedFallback'));
          } else {
            setGenerationError('');
          }
          if (nextProgress.status === 'completed') {
            await loadDeck({ silent: true });
            setJobId(null);
            return;
          }
        }

        await loadDeck({ silent: true });
      } catch (err) {
        console.error(err);
        setGenerationError(t('slides.generationStatus.pollFailed'));
      }
    }, 1500);

    return () => clearInterval(interval);
  }, [deck, isGenerating, jobId, loadDeck, t]);

  const handleGenerate = async () => {
    try {
      setError('');
      setGenerationError('');
      const response = await slideService.startGenerateSlides(documentId, {
        desiredSlideCount,
        ...deckBrief,
      });
      setJobId(response.jobId);
      setProgress({
        status: response.status,
        percent: 0,
        stageLabel: 'Queued',
        message: t('slides.feedback.jobCreated'),
      });
      await loadDeck({ silent: true });
    } catch (err) {
      console.error(err);
      setError(t('slides.errors.generate'));
    }
  };

  const handleEdit = useCallback((item) => {
    setEditingSlideId(item.id);
    setSelectedSlideId(item.id);
    setIsInspectorOpen(true);
    setDrafts((current) => ({
      ...current,
      [item.id]: {
        heading: item.heading || '',
        subheading: item.subheading || '',
        goal: item.goal || '',
        bodyText: (item.bodyBlocks || []).join('\n'),
        speakerNotes: item.speakerNotes || '',
        accentTone: item.accentTone || '',
      },
    }));
  }, []);

  const handleDraftChange = (itemId, field, value) => {
    setDrafts((current) => ({
      ...current,
      [itemId]: {
        ...current[itemId],
        [field]: value,
      },
    }));
  };

  const handleBriefChange = (field, value) => {
    setBriefDirty(true);
    setDeckBrief((current) => ({
      ...current,
      [field]: value,
    }));
  };

  const handleCancelEdit = () => {
    setEditingSlideId(null);
  };

  const handleSave = async (item) => {
    const draft = drafts[item.id];
    if (!draft || !deck) {
      return;
    }

    try {
      const updated = await slideService.updateSlideItem(deck.id, item.id, {
        heading: draft.heading,
        subheading: draft.subheading,
        goal: draft.goal,
        bodyBlocks: draft.bodyText.split('\n').map((line) => line.trim()).filter(Boolean),
        speakerNotes: draft.speakerNotes,
        accentTone: draft.accentTone,
      });

      setDeck((current) => ({
        ...current,
        items: current.items.map((slide) => (slide.id === item.id ? updated : slide)),
      }));
      setEditingSlideId(null);
      setFeedback(t('slides.feedback.saved'));
    } catch (err) {
      console.error(err);
      setError(t('slides.errors.save'));
    }
  };

  const handleRefreshImages = async (item) => {
    if (!deck) {
      return;
    }

    try {
      setMediaBusySlideId(item.id);
      const updated = await slideService.refreshSlideItemImages(deck.id, item.id);
      setDeck((current) => ({
        ...current,
        items: current.items.map((slide) => (slide.id === item.id ? updated : slide)),
      }));
      setFeedback(t('slides.feedback.refreshed', { index: item.slideIndex }));
    } catch (err) {
      console.error(err);
      setError(t('slides.errors.refreshImages'));
    } finally {
      setMediaBusySlideId(null);
    }
  };

  const handleSelectImage = async (item, candidateKey) => {
    if (!deck) {
      return;
    }

    try {
      setMediaBusySlideId(item.id);
      const updated = await slideService.selectSlideItemImage(deck.id, item.id, candidateKey);
      setDeck((current) => ({
        ...current,
        items: current.items.map((slide) => (slide.id === item.id ? updated : slide)),
      }));
      setFeedback(t('slides.feedback.selectedImage', { index: item.slideIndex }));
    } catch (err) {
      console.error(err);
      setError(t('slides.errors.selectImage'));
    } finally {
      setMediaBusySlideId(null);
    }
  };

  const formatEta = (seconds) => {
    if (typeof seconds !== 'number') {
      return t('slides.etaCalculating');
    }

    if (seconds <= 0) {
      return t('slides.etaAlmostDone');
    }

    if (seconds < 60) {
      return `${seconds}s`;
    }

    const minutes = Math.floor(seconds / 60);
    const remain = seconds % 60;
    return `${minutes}m ${remain}s`;
  };

  const getThemeMeta = (themeKey) => themeOptions.find((theme) => theme.key === themeKey) || themeOptions[0];

  const normalizeSlideType = (slideType) => {
    if (typeof slideType === 'number' && Number.isFinite(slideType)) {
      switch (slideType) {
        case 0:
          return 'cover';
        case 1:
          return 'section';
        case 2:
          return 'content';
        case 3:
          return 'quote';
        case 4:
          return 'highlight';
        case 5:
          return 'stat';
        default:
          return 'content';
      }
    }

    if (typeof slideType === 'string') {
      const normalized = slideType.trim().toLowerCase().replace(/[\s_-]+/g, '');
      if (normalized === 'title') {
        return 'cover';
      }
      if (normalized === 'sectiondivider') {
        return 'section';
      }
      return normalized || 'content';
    }

    return 'content';
  };

  const getSlideTypeLabel = (slideType) => {
    switch (normalizeSlideType(slideType)) {
      case 'cover':
        return t('slides.relativeTypes.cover');
      case 'section':
        return t('slides.relativeTypes.section');
      case 'quote':
        return t('slides.relativeTypes.quote');
      case 'highlight':
        return t('slides.relativeTypes.highlight');
      case 'stat':
        return t('slides.relativeTypes.stat');
      default:
        return t('slides.relativeTypes.content');
    }
  };

  const getFriendlyStatus = useCallback((status) => {
    const normalized = typeof status === 'string' ? status.trim().toLowerCase() : '';
    switch (normalized) {
      case 'completed':
        return t('slides.slideStatuses.completed');
      case 'generating':
        return t('slides.slideStatuses.generating');
      case 'needsreview':
        return t('slides.slideStatuses.needsReview');
      case 'failed':
        return t('slides.slideStatuses.failed');
      case 'pending':
        return t('slides.slideStatuses.pending');
      default:
        return status || t('slides.notCreated');
    }
  }, [t]);

  const getProgressStageLabel = useCallback((activeProgress) => {
    if (!activeProgress) {
      return t('slides.notCreated');
    }

    const stage = String(activeProgress?.stage || activeProgress?.stageKey || activeProgress?.status || '').toLowerCase();
    if (stage.includes('outline')) {
      return t('slides.stageLabels.generatingOutline');
    }
    if (stage.includes('slide')) {
      return t('slides.stageLabels.generatingSlides');
    }
    if (stage.includes('queued')) {
      return t('slides.stageLabels.queued');
    }
    if (stage.includes('completed')) {
      return t('slides.stageLabels.completed');
    }
    return activeProgress?.stageLabel || t('slides.generatingSlides');
  }, [t]);

  const getZoomStyle = (zoomValue) => {
    switch (zoomValue) {
      case '75':
        return { '--studio-canvas-width': '75%' };
      case '100':
        return { '--studio-canvas-width': '100%' };
      case 'fit':
      default:
        return { '--studio-canvas-width': 'min(100%, 1080px)' };
    }
  };

  const canGenerate = documentMeta?.status === 3;
  const outlineSlides = deck?.outline?.slides || [];
  const activeProgress = progress || deck?.generationProgress;
  const themeMeta = getThemeMeta(deckBrief.themeKey);
  const allPreviewItems = deck?.items || [];
  const previewItems = hideLowConfidence
    ? allPreviewItems.filter((item) => !item.quality?.isLowConfidence)
    : allPreviewItems;
  const completedSlides = allPreviewItems.filter((item) => item.status === 'Completed').length;
  const slidesWithSelectedMedia = allPreviewItems.filter((item) => buildSlideImageViewModel(item, t).selectedImage).length;
  const lowConfidenceCount = deck?.qualitySummary?.lowConfidenceCount
    ?? allPreviewItems.filter((item) => item.quality?.isLowConfidence).length;

  useEffect(() => {
    if (!previewItems.length) {
      setSelectedSlideId(null);
      return;
    }

    const stillVisible = previewItems.some((item) => item.id === selectedSlideId);
    if (!stillVisible) {
      setSelectedSlideId(previewItems[0].id);
    }
  }, [previewItems, selectedSlideId]);

  if (loading) {
    return (
      <div className="loading">
        <div className="spinner"></div>
        <p>{t('slides.loading')}</p>
      </div>
    );
  }

  const selectedSlide = previewItems.find((item) => item.id === selectedSlideId) || previewItems[0] || null;
  const selectedImageVm = selectedSlide ? buildSlideImageViewModel(selectedSlide, t) : null;
  const selectedSlideDraft = selectedSlide ? drafts[selectedSlide.id] : null;
  const isEditingSelectedSlide = selectedSlide && editingSlideId === selectedSlide.id;

  const handleSelectSlide = (item) => {
    setSelectedSlideId(item.id);
    if (slideRefs.current[item.id]) {
      slideRefs.current[item.id].scrollIntoView({ behavior: 'smooth', inline: 'center', block: 'nearest' });
    }
  };

  return (
    <div className={`slide-studio gamma-studio theme-${themeMeta.key}`}>
      <section className="studio-header-bar card">
        <div className="studio-header-main">
          <button className="button button-secondary studio-back-button" onClick={() => navigate('/workspaces')}>
            <span aria-hidden="true">←</span>
            <span>{t('slides.back')}</span>
          </button>
          <div className="studio-title-stack">
            <span className="studio-kicker">{t('slides.eyebrow')}</span>
            <h2>{deck?.title || documentMeta?.fileName || t('slides.heroFallbackTitle')}</h2>
            <p>
              {documentMeta?.fileName || t('slides.noData')}
              <span className="studio-inline-dot">•</span>
              {completedSlides}/{allPreviewItems.length || desiredSlideCount} {t('slides.slideUnit')}
              <span className="studio-inline-dot">•</span>
              {getProgressStageLabel(activeProgress)}
            </p>
          </div>
        </div>

        <div className="studio-header-actions">
          <button className="button button-secondary" onClick={() => setIsInspectorOpen((current) => !current)}>
            <span aria-hidden="true">☰</span>
            <span>{isInspectorOpen ? t('slides.hideInspector') : t('slides.showInspector')}</span>
          </button>
          <button className="button button-secondary" onClick={() => setHideLowConfidence((current) => !current)}>
            <span aria-hidden="true">◐</span>
            <span>{hideLowConfidence ? t('slides.showAllSlides') : t('slides.hideLowConfidence')}</span>
          </button>
          {deck && (
            <button className="button button-secondary" onClick={() => window.open(slideService.getDeckHtmlUrl(documentId), '_blank', 'noopener,noreferrer')}>
              <span aria-hidden="true">⇱</span>
              <span>{t('slides.export')}</span>
            </button>
          )}
          <button className="button" onClick={handleGenerate} disabled={!canGenerate || isGenerating}>
            <span aria-hidden="true">↻</span>
            <span>
              {isGenerating
                ? t('slides.generating', { percent: activeProgress?.percent || 0 })
                : deck
                  ? t('slides.regenerate')
                  : t('slides.generate')}
            </span>
          </button>
        </div>
      </section>

      {!canGenerate && (
        <div className="alert alert-info">
          {t('slides.processingRequired', { status: documentMeta?.status })}
        </div>
      )}

      {error && <div className="alert alert-error">{error}</div>}
      {feedback && !isGenerating && <div className="alert alert-info">{feedback}</div>}

      <div className={`studio-workspace${isInspectorOpen ? ' inspector-open' : ' inspector-closed'}`}>
        <aside className="studio-left-panel">
          <section className="card studio-navigator-card">
            <div className="studio-tabs" role="tablist" aria-label={t('slides.navigationTabs')}>
              <button
                type="button"
                className={`studio-tab${activeLeftTab === 'outline' ? ' active' : ''}`}
                onClick={() => setActiveLeftTab('outline')}
              >
                <span aria-hidden="true">▤</span>
                <span>{t('slides.outlineTab')}</span>
              </button>
              <button
                type="button"
                className={`studio-tab${activeLeftTab === 'source' ? ' active' : ''}`}
                onClick={() => setActiveLeftTab('source')}
              >
                <span aria-hidden="true">≣</span>
                <span>{t('slides.sourceTab')}</span>
              </button>
            </div>

            {activeLeftTab === 'outline' ? (
              <div className="studio-tab-panel">
                <div className="studio-panel-heading">
                  <div>
                    <strong>{t('slides.deckStructure')}</strong>
                    <p>{t('slides.outlinePanelBody')}</p>
                  </div>
                  <span className="studio-count-pill">{outlineSlides.length || desiredSlideCount}</span>
                </div>

                {outlineSlides.length > 0 ? (
                  <div className="studio-outline-list">
                    {outlineSlides.map((slide) => {
                      const isActive = selectedSlide?.slideIndex === slide.slideIndex;
                      return (
                        <button
                          key={`${slide.slideIndex}-${slide.heading}`}
                          type="button"
                          className={`studio-outline-item${isActive ? ' active' : ''}`}
                          onClick={() => {
                            const matchedItem = previewItems.find((item) => item.slideIndex === slide.slideIndex);
                            if (matchedItem) {
                              handleSelectSlide(matchedItem);
                            }
                          }}
                        >
                          <span className="studio-outline-number">{slide.slideIndex}</span>
                          <span className="studio-outline-copy">
                            <strong>{slide.heading}</strong>
                            <small>{slide.goal || getSlideTypeLabel(slide.slideType)}</small>
                          </span>
                          <span aria-hidden="true">›</span>
                        </button>
                      );
                    })}
                  </div>
                ) : (
                  <div className="studio-empty-block">
                    <strong>{t('slides.outlineEmptyTitle')}</strong>
                    <p>{t('slides.outlineEmpty')}</p>
                  </div>
                )}
              </div>
            ) : (
              <div className="studio-tab-panel">
                <div className="studio-panel-heading">
                  <div>
                    <strong>{t('slides.sourcePanelTitle')}</strong>
                    <p>{t('slides.sourcePanelBody')}</p>
                  </div>
                </div>

                <div className="studio-source-card">
                  <span className="studio-source-label">{t('slides.document')}</span>
                  <strong>{documentMeta?.fileName || t('slides.noData')}</strong>
                  <p>{documentMeta?.summary || t('slides.sourceFallbackBody')}</p>
                </div>

                <div className="studio-source-meta-grid">
                  <div className="studio-source-meta">
                    <span>{t('slides.status')}</span>
                    <strong>{getProgressStageLabel(activeProgress)}</strong>
                  </div>
                  <div className="studio-source-meta">
                    <span>{t('slides.theme')}</span>
                    <strong>{themeMeta.label}</strong>
                  </div>
                  <div className="studio-source-meta">
                    <span>{t('slides.mediaReadyLabel')}</span>
                    <strong>{slidesWithSelectedMedia}</strong>
                  </div>
                  <div className="studio-source-meta">
                    <span>{t('slides.reviewQueue')}</span>
                    <strong>{lowConfidenceCount}</strong>
                  </div>
                </div>

                {activeProgress && (
                  <div className="studio-progress-card">
                    <div className="studio-progress-head">
                      <strong>{getProgressStageLabel(activeProgress)}</strong>
                      <span>{activeProgress.percent || 0}%</span>
                    </div>
                    <div className="generation-progress-bar">
                      <div className="generation-progress-fill" style={{ width: `${Math.max(0, Math.min(100, activeProgress.percent || 0))}%` }}></div>
                    </div>
                    <p>{formatEta(activeProgress.estimatedRemainingSeconds)}</p>
                  </div>
                )}
              </div>
            )}
          </section>
        </aside>

        <section className="studio-canvas-column">
          <section className="card studio-canvas-toolbar">
            <div>
              <span className="studio-kicker">{t('slides.previewCanvas')}</span>
              <h3>{selectedSlide?.heading || deck?.title || t('slides.previewFallbackTitle')}</h3>
              <p>{selectedSlide?.subheading || deck?.subtitle || deckBrief.narrativeGoal}</p>
            </div>

            <div className="studio-toolbar-actions">
              <div className="studio-zoom-group" role="group" aria-label={t('slides.zoomLabel')}>
                {['fit', '75', '100'].map((zoomOption) => (
                  <button
                    key={zoomOption}
                    type="button"
                    className={`studio-zoom-button${canvasZoom === zoomOption ? ' active' : ''}`}
                    onClick={() => setCanvasZoom(zoomOption)}
                  >
                    {t(`slides.zoomOptions.${zoomOption}`)}
                  </button>
                ))}
              </div>
              {selectedSlide && (
                <button className="button button-secondary" onClick={() => handleEdit(selectedSlide)}>
                  <span aria-hidden="true">✎</span>
                  <span>{t('slides.editSlide')}</span>
                </button>
              )}
            </div>
          </section>

          <div className="studio-canvas-body">
            {activeProgress && isGenerating && (
              <div className="studio-canvas-progress-shell">
                <div className="studio-progress-card studio-progress-card-large">
                  <div className="studio-progress-head">
                    <strong>{getProgressStageLabel(activeProgress)}</strong>
                    <span>{Math.max(0, Math.min(100, activeProgress.percent || 0))}%</span>
                  </div>
                  <div className="generation-progress-bar">
                    <div className="generation-progress-fill" style={{ width: `${Math.max(0, Math.min(100, activeProgress.percent || 0))}%` }}></div>
                  </div>
                  <p>{activeProgress.message || activeProgress.stageLabel || t('slides.generationStatus.runningFallback')}</p>
                  <small>{formatEta(activeProgress.estimatedRemainingSeconds)}</small>
                </div>
              </div>
            )}

            {generationError && (
              <div className="studio-progress-card studio-progress-card-large tone-error">
                <div className="studio-progress-head">
                  <strong>{t('slides.generationStatus.failedTitle')}</strong>
                </div>
                <p>{generationError}</p>
              </div>
            )}

            {selectedSlide ? (
              <div className="studio-canvas-stage" style={getZoomStyle(canvasZoom)}>
                <article className={`studio-slide-frame slide-preview-${normalizeSlideType(selectedSlide.slideType)}`}>
                  <div className="studio-slide-meta">
                    <span>{t('slides.slideLabel', { index: selectedSlide.slideIndex })}</span>
                    <div className="quality-toolbar">
                      <span>{getSlideTypeLabel(selectedSlide.slideType)}</span>
                      {selectedSlide.quality?.score !== undefined && selectedSlide.quality?.score !== null && (
                        <span className={`quality-chip ${selectedSlide.quality?.isLowConfidence ? 'low' : 'good'}`}>
                          {selectedSlide.quality.score}/100
                        </span>
                      )}
                    </div>
                  </div>

                  <div className="studio-slide-content">
                    <div className="studio-slide-text">
                      <h3>{selectedSlide.heading}</h3>
                      {selectedSlide.subheading && <p className="studio-slide-subheading">{selectedSlide.subheading}</p>}
                      {selectedSlide.goal && <div className="studio-slide-goal">{selectedSlide.goal}</div>}
                      {(selectedSlide.bodyBlocks || []).length > 0 ? (
                        <div className={`studio-slide-body studio-body-type-${normalizeSlideType(selectedSlide.slideType)}`}>
                          {(selectedSlide.bodyBlocks || []).map((block, index) => (
                            <div key={index} className="studio-slide-bullet">{block}</div>
                          ))}
                        </div>
                      ) : (
                        <div className="slide-skeleton">
                          <span></span>
                          <span></span>
                          <span></span>
                        </div>
                      )}
                      {selectedSlide.speakerNotes && <p className="studio-slide-notes">{selectedSlide.speakerNotes}</p>}
                    </div>

                    <div className={`studio-media-frame tone-${selectedImageVm?.badgeTone || 'muted'}${selectedImageVm?.selectedImage ? ' has-image' : ''}`}>
                      {selectedImageVm?.selectedImage?.localAssetUrl ? (
                        <img
                          src={selectedImageVm.selectedImage.localAssetUrl}
                          alt={selectedImageVm.selectedImage.altText || selectedSlide.heading || t('slides.slideLabel', { index: selectedSlide.slideIndex })}
                        />
                      ) : (
                        <div className="studio-media-placeholder">
                          <strong>{selectedImageVm?.badgeLabel}</strong>
                          <span>{selectedImageVm?.statusLabel}</span>
                        </div>
                      )}
                    </div>
                  </div>

                  {(selectedSlide.quality?.isLowConfidence || selectedSlide.quality?.isUnknown) && (
                    <div className="quality-warning compact">
                      <strong>{selectedSlide.quality?.isLowConfidence ? t('slides.reviewNeeded') : t('slides.noVerifier')}</strong>
                      {Array.isArray(selectedSlide.quality?.issues) && selectedSlide.quality.issues.length > 0 && (
                        <ul className="quality-issues">
                          {selectedSlide.quality.issues.slice(0, 2).map((issue) => (
                            <li key={issue}>{issue}</li>
                          ))}
                        </ul>
                      )}
                    </div>
                  )}
                </article>
              </div>
            ) : (
              <div className="card gamma-empty-canvas studio-empty-state">
                <div className="gamma-empty-mockup">
                  <div className="gamma-empty-mockup-card"></div>
                  <div className="gamma-empty-mockup-card"></div>
                  <div className="gamma-empty-mockup-card"></div>
                </div>
                <h3>{allPreviewItems.length > 0 ? t('slides.hiddenSlidesTitle') : t('slides.noDeckTitle')}</h3>
                <p>{allPreviewItems.length > 0 ? t('slides.hiddenSlidesBody') : t('slides.noDeckBody')}</p>
              </div>
            )}

            {previewItems.length > 0 && (
              <div className="studio-filmstrip" role="list" aria-label={t('slides.slideRail')}>
                {previewItems.map((item) => (
                  <button
                    key={item.id}
                    ref={(node) => {
                      slideRefs.current[item.id] = node;
                    }}
                    type="button"
                    className={`studio-filmstrip-card${selectedSlide?.id === item.id ? ' active' : ''}`}
                    onClick={() => handleSelectSlide(item)}
                  >
                    <span>{t('slides.slideLabel', { index: item.slideIndex })}</span>
                    <strong>{item.heading || t('slides.untitledSlide')}</strong>
                    <small>{getFriendlyStatus(item.status)}</small>
                  </button>
                ))}
              </div>
            )}
          </div>
        </section>

        <aside className={`studio-inspector${isInspectorOpen ? ' open' : ''}`}>
          <section className="card studio-inspector-card">
            <div className="studio-panel-heading">
              <div>
                <strong>{isEditingSelectedSlide ? t('slides.editPanelTitle') : t('slides.inspectorTitle')}</strong>
                <p>{isEditingSelectedSlide ? t('slides.editPanelBody') : t('slides.inspectorBody')}</p>
              </div>
              <button type="button" className="studio-icon-button" onClick={() => setIsInspectorOpen(false)} aria-label={t('slides.hideInspector')}>
                <span aria-hidden="true">×</span>
              </button>
            </div>

            {selectedSlide ? (
              <>
                {isEditingSelectedSlide && selectedSlideDraft ? (
                  <div className="slide-edit-form">
                    <label className="gamma-field">
                      <span>{t('slides.headingLabel')}</span>
                      <input value={selectedSlideDraft.heading} onChange={(event) => handleDraftChange(selectedSlide.id, 'heading', event.target.value)} />
                    </label>
                    <label className="gamma-field">
                      <span>{t('slides.subheadingLabel')}</span>
                      <input value={selectedSlideDraft.subheading} onChange={(event) => handleDraftChange(selectedSlide.id, 'subheading', event.target.value)} placeholder={t('slides.subheadingPlaceholder')} />
                    </label>
                    <label className="gamma-field">
                      <span>{t('slides.goalLabel')}</span>
                      <input value={selectedSlideDraft.goal} onChange={(event) => handleDraftChange(selectedSlide.id, 'goal', event.target.value)} placeholder={t('slides.goalPlaceholder')} />
                    </label>
                    <label className="gamma-field">
                      <span>{t('slides.bodyLabel')}</span>
                      <textarea value={selectedSlideDraft.bodyText} onChange={(event) => handleDraftChange(selectedSlide.id, 'bodyText', event.target.value)} rows={7} />
                    </label>
                    <label className="gamma-field">
                      <span>{t('slides.notesLabel')}</span>
                      <textarea value={selectedSlideDraft.speakerNotes} onChange={(event) => handleDraftChange(selectedSlide.id, 'speakerNotes', event.target.value)} rows={4} />
                    </label>
                    <label className="gamma-field">
                      <span>{t('slides.accentToneLabel')}</span>
                      <input value={selectedSlideDraft.accentTone} onChange={(event) => handleDraftChange(selectedSlide.id, 'accentTone', event.target.value)} placeholder={t('slides.accentTonePlaceholder')} />
                    </label>
                    <div className="slide-edit-actions sticky">
                      <button className="button" onClick={() => handleSave(selectedSlide)}>
                        <span aria-hidden="true">✓</span>
                        <span>{t('slides.saveSlide')}</span>
                      </button>
                      <button className="button button-secondary" onClick={handleCancelEdit}>
                        <span aria-hidden="true">×</span>
                        <span>{t('slides.cancel')}</span>
                      </button>
                    </div>
                  </div>
                ) : (
                  <div className="studio-inspector-stack">
                    <div className="studio-inspector-block">
                      <span className="studio-kicker">{t('slides.selectedSlide')}</span>
                      <strong>{selectedSlide.heading || t('slides.untitledSlide')}</strong>
                      <p>{selectedSlide.subheading || selectedSlide.goal || t('slides.selectedSlideHint')}</p>
                    </div>

                    <div className="studio-inspector-meta-grid">
                      <div className="studio-source-meta">
                        <span>{t('slides.slideTypeLabel')}</span>
                        <strong>{getSlideTypeLabel(selectedSlide.slideType)}</strong>
                      </div>
                      <div className="studio-source-meta">
                        <span>{t('slides.status')}</span>
                        <strong>{getFriendlyStatus(selectedSlide.status)}</strong>
                      </div>
                    </div>

                    <div className="studio-inspector-block">
                      <div className="studio-inspector-block-head">
                        <strong>{t('slides.mediaPanelTitle')}</strong>
                        <button
                          className="button button-secondary"
                          type="button"
                          onClick={() => setExpandedMediaSlideId(expandedMediaSlideId === selectedSlide.id ? null : selectedSlide.id)}
                        >
                          <span aria-hidden="true">▣</span>
                          <span>{expandedMediaSlideId === selectedSlide.id ? t('slides.hideMedia') : t('slides.manageMedia')}</span>
                        </button>
                      </div>
                      <p>{selectedImageVm?.helperText}</p>
                      {selectedImageVm?.attributionText && <small>{selectedImageVm.attributionText}</small>}
                    </div>

                    {expandedMediaSlideId === selectedSlide.id && (
                      <div className="studio-media-manager">
                        {selectedImageVm?.needsImage && (
                          <button
                            className="button button-secondary"
                            onClick={() => handleRefreshImages(selectedSlide)}
                            disabled={mediaBusySlideId === selectedSlide.id}
                          >
                            <span aria-hidden="true">↻</span>
                            <span>
                              {mediaBusySlideId === selectedSlide.id
                                ? t('slides.searchingImage')
                                : (selectedImageVm?.hasCandidates ? t('slides.refindImage') : t('slides.findImage'))}
                            </span>
                          </button>
                        )}

                        {selectedImageVm?.hasCandidates ? (
                          <div className="slide-media-thumb-grid">
                            {selectedImageVm.candidates.map((candidate) => (
                              <article key={candidate.key} className={`slide-media-thumb ${candidate.key === selectedImageVm.selectedImage?.key ? 'selected' : ''}`}>
                                <div className="slide-media-thumb-figure">
                                  {candidate.localAssetUrl ? (
                                    <img src={candidate.localAssetUrl} alt={candidate.altText || `Candidate ${candidate.key}`} />
                                  ) : (
                                    <div className="slide-media-thumb-placeholder">{t('slides.noPreview')}</div>
                                  )}
                                </div>
                                <div className="slide-media-thumb-meta">
                                  <span className={`slide-media-badge tone-${candidate.sourceType === 'generated' ? 'generated' : 'web'}`}>
                                    {candidate.sourceType === 'generated' ? t('slides.generatedImage') : t('slides.webImage')}
                                  </span>
                                  <strong>{candidate.provider}</strong>
                                  {(candidate.licenseLabel || candidate.attributionText) && (
                                    <small>{[candidate.licenseLabel, candidate.attributionText].filter(Boolean).join(' • ')}</small>
                                  )}
                                  <button
                                    className="button button-secondary"
                                    onClick={() => handleSelectImage(selectedSlide, candidate.key)}
                                    disabled={mediaBusySlideId === selectedSlide.id || candidate.key === selectedImageVm.selectedImage?.key}
                                  >
                                    {candidate.key === selectedImageVm.selectedImage?.key ? t('slides.selected') : t('slides.chooseThisImage')}
                                  </button>
                                </div>
                              </article>
                            ))}
                          </div>
                        ) : (
                          <div className="slide-media-empty">
                            <strong>{t('slides.noImageCandidates')}</strong>
                            <p>{t('slides.noImageCandidatesBody')}</p>
                          </div>
                        )}
                      </div>
                    )}

                    <button className="button" onClick={() => handleEdit(selectedSlide)}>
                      <span aria-hidden="true">✎</span>
                      <span>{t('slides.editSlide')}</span>
                    </button>
                  </div>
                )}

                <div className="studio-inspector-divider"></div>

                <div className="studio-inspector-stack">
                  <div className="studio-panel-heading compact">
                    <div>
                      <strong>{t('slides.settingsPanelTitle')}</strong>
                      <p>{t('slides.settingsPanelBody')}</p>
                    </div>
                  </div>

                  <div className="gamma-brief-grid single-column">
                    <label className="gamma-field">
                      <span>{t('slides.desiredSlides')}</span>
                      <input
                        type="number"
                        min="5"
                        max="12"
                        value={desiredSlideCount}
                        onChange={(event) => setDesiredSlideCount(Number(event.target.value))}
                      />
                    </label>

                    <label className="gamma-field">
                      <span>{t('slides.audience')}</span>
                      <select value={deckBrief.audience} onChange={(event) => handleBriefChange('audience', event.target.value)}>
                        {audienceOptions.map((option) => (
                          <option key={option} value={option}>{option}</option>
                        ))}
                      </select>
                    </label>

                    <label className="gamma-field">
                      <span>{t('slides.tone')}</span>
                      <select value={deckBrief.tone} onChange={(event) => handleBriefChange('tone', event.target.value)}>
                        {toneOptions.map((option) => (
                          <option key={option} value={option}>{option}</option>
                        ))}
                      </select>
                    </label>

                    <label className="gamma-field">
                      <span>{t('slides.languageStyle')}</span>
                      <select value={deckBrief.languageStyle} onChange={(event) => handleBriefChange('languageStyle', event.target.value)}>
                        {languageStyleOptions.map((option) => (
                          <option key={option} value={option}>{option}</option>
                        ))}
                      </select>
                    </label>
                  </div>

                  <label className="gamma-field">
                    <span>{t('slides.narrativeGoal')}</span>
                    <textarea
                      rows={4}
                      value={deckBrief.narrativeGoal}
                      onChange={(event) => handleBriefChange('narrativeGoal', event.target.value)}
                      placeholder={t('slides.narrativePlaceholder')}
                    />
                  </label>

                  <div className="gamma-theme-grid single-column">
                    {themeOptions.map((theme) => (
                      <button
                        key={theme.key}
                        type="button"
                        className={`gamma-theme-card ${deckBrief.themeKey === theme.key ? 'active' : ''}`}
                        onClick={() => handleBriefChange('themeKey', theme.key)}
                      >
                        <strong>{theme.label}</strong>
                        <span>{theme.blurb}</span>
                      </button>
                    ))}
                  </div>
                </div>
              </>
            ) : (
              <div className="studio-empty-block">
                <strong>{t('slides.noSlideSelectedTitle')}</strong>
                <p>{t('slides.noSlideSelectedBody')}</p>
              </div>
            )}
          </section>
        </aside>
      </div>
    </div>
  );
}

export default SlideStudio;
