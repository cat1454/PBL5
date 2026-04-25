import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { documentService, slideService } from '../services/api';
import { buildSlideImageViewModel } from '../services/slideImages';
import { useLanguage } from '../context/LanguageContext';

function SlideStudio() {
  const { t, language } = useLanguage();
  const { documentId } = useParams();
  const navigate = useNavigate();
  const [documentMeta, setDocumentMeta] = useState(null);
  const [deck, setDeck] = useState(null);
  const [progress, setProgress] = useState(null);
  const [jobId, setJobId] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [feedback, setFeedback] = useState('');
  const [readingMode, setReadingMode] = useState(false);
  const [desiredSlideCount, setDesiredSlideCount] = useState(8);
  const [editingSlideId, setEditingSlideId] = useState(null);
  const [drafts, setDrafts] = useState({});
  const [briefDirty, setBriefDirty] = useState(false);
  const [hideLowConfidence, setHideLowConfidence] = useState(false);
  const [expandedMediaSlideId, setExpandedMediaSlideId] = useState(null);
  const [mediaBusySlideId, setMediaBusySlideId] = useState(null);

  const audienceOptions = t('slides.options.audiences');
  const toneOptions = t('slides.options.tones');
  const languageStyleOptions = t('slides.options.languageStyles');

  const themeOptions = useMemo(() => ([
    {
      key: 'editorial-sunrise',
      label: 'Editorial Sunrise',
      blurb: t('slides.themes.editorialSunrise'),
    },
    {
      key: 'paper-mint',
      label: 'Paper Mint',
      blurb: t('slides.themes.paperMint'),
    },
    {
      key: 'cobalt-grid',
      label: 'Cobalt Grid',
      blurb: t('slides.themes.cobaltGrid'),
    },
    {
      key: 'midnight-signal',
      label: 'Midnight Signal',
      blurb: t('slides.themes.midnightSignal'),
    },
  ]), [t]);

  const defaultBrief = useMemo(() => ({
    themeKey: 'editorial-sunrise',
    audience: audienceOptions[0],
    tone: toneOptions[0],
    narrativeGoal: language === 'vi'
      ? 'Giúp người đọc nắm được cấu trúc và các ý chính của tài liệu trong một lần xem'
      : 'Help the reader understand the structure and key ideas of the document in one pass',
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
        }

        await loadDeck({ silent: true });
      } catch (err) {
        console.error(err);
      }
    }, 1500);

    return () => clearInterval(interval);
  }, [deck, isGenerating, jobId, loadDeck]);

  const handleGenerate = async () => {
    try {
      setError('');
      setFeedback(t('slides.feedback.generating'));
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

  const handleEdit = (item) => {
    setEditingSlideId(item.id);
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
  };

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
    return `${minutes}p ${remain}s`;
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

  if (loading) {
    return (
      <div className="loading">
        <div className="spinner"></div>
        <p>{t('slides.loading')}</p>
      </div>
    );
  }

  const canGenerate = documentMeta?.status === 3;
  const outlineSlides = deck?.outline?.slides || [];
  const activeProgress = progress || deck?.generationProgress;
  const themeMeta = getThemeMeta(deckBrief.themeKey);
  const allPreviewItems = deck?.items || [];
  const previewItems = hideLowConfidence
    ? allPreviewItems.filter((item) => !item.quality?.isLowConfidence)
    : allPreviewItems;
  const completedSlides = previewItems.filter((item) => item.status === 'Completed').length;
  const slidesWithSelectedMedia = allPreviewItems.filter((item) => buildSlideImageViewModel(item).selectedImage).length;
  const lowConfidenceCount = deck?.qualitySummary?.lowConfidenceCount
    ?? allPreviewItems.filter((item) => item.quality?.isLowConfidence).length;

  return (
    <div className={`slide-studio gamma-studio theme-${themeMeta.key}`}>
      <section className="card gamma-hero-card">
        <div className="gamma-hero-copy">
          <button className="button button-secondary" onClick={() => navigate('/workspaces')}>{t('slides.back')}</button>
          <span className="gamma-eyebrow">{t('slides.eyebrow')}</span>
          <h2>{deck?.title || documentMeta?.fileName || t('slides.heroFallbackTitle')}</h2>
          <p className="section-subtitle">{t('slides.heroSubtitle')}</p>
        </div>

        <div className="gamma-hero-meta">
          <div className="gamma-mini-stat">
            <span>{t('slides.document')}</span>
            <strong>{documentMeta?.fileName || t('slides.noData')}</strong>
          </div>
          <div className="gamma-mini-stat">
            <span>{t('slides.theme')}</span>
            <strong>{themeMeta.label}</strong>
          </div>
          <div className="gamma-mini-stat">
            <span>{t('slides.slides')}</span>
            <strong>{completedSlides}/{previewItems.length || desiredSlideCount}</strong>
          </div>
          <div className="gamma-mini-stat">
            <span>{t('slides.status')}</span>
            <strong>{activeProgress?.stageLabel || deck?.status || t('slides.notCreated')}</strong>
          </div>
        </div>
      </section>

      {!canGenerate && (
        <div className="alert alert-info">
          {t('slides.processingRequired', { status: documentMeta?.status })}
        </div>
      )}

      {error && <div className="alert alert-error">{error}</div>}
      {feedback && <div className="alert alert-info">{feedback}</div>}

      <div className="gamma-workspace">
        <aside className="gamma-sidebar">
          <section className="card gamma-brief-card">
            <div className="gamma-panel-head">
              <div>
                <span className="gamma-panel-kicker">{t('slides.deckBrief')}</span>
                <h3>{t('slides.deckDescription')}</h3>
              </div>
              <span className="gamma-theme-pill">{themeMeta.label}</span>
            </div>

            <div className="gamma-brief-grid">
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

            <div className="gamma-theme-grid">
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

            <div className="gamma-action-row">
              <button className="button" onClick={handleGenerate} disabled={!canGenerate || isGenerating}>
                {isGenerating
                  ? t('slides.generating', { percent: activeProgress?.percent || 0 })
                  : deck
                    ? t('slides.regenerate')
                    : t('slides.generate')}
              </button>
              <button className="button button-secondary" onClick={() => setReadingMode((current) => !current)}>
                {readingMode ? t('slides.disableReadingMode') : t('slides.enableReadingMode')}
              </button>
              <button className="button button-secondary" onClick={() => setHideLowConfidence((current) => !current)}>
                {hideLowConfidence ? t('slides.showAllSlides') : t('slides.hideLowConfidence')}
              </button>
              {deck && (
                <button className="button button-secondary" onClick={() => window.open(slideService.getDeckHtmlUrl(documentId), '_blank', 'noopener,noreferrer')}>
                  {t('slides.export')}
                </button>
              )}
            </div>
          </section>

          {activeProgress && (
            <section className="card gamma-progress-card">
              <div className="gamma-panel-head">
                <div>
                  <span className="gamma-panel-kicker">{t('slides.liveGeneration')}</span>
                  <h3>{activeProgress.stageLabel || t('slides.generatingSlides')}</h3>
                </div>
                <div className="gamma-progress-summary">
                  <strong>{activeProgress.percent || 0}%</strong>
                  <span>{formatEta(activeProgress.estimatedRemainingSeconds)}</span>
                </div>
              </div>
              <p>{activeProgress.message}</p>
              {activeProgress.detail && <p className="generation-progress-detail">{activeProgress.detail}</p>}
              <div className="generation-progress-bar">
                <div className="generation-progress-fill" style={{ width: `${Math.max(0, Math.min(100, activeProgress.percent || 0))}%` }}></div>
              </div>
              {typeof activeProgress.current === 'number' && typeof activeProgress.total === 'number' && (
                <p className="generation-progress-meta">
                  {activeProgress.current}/{activeProgress.total} {activeProgress.unitLabel || t('slides.slides').toLowerCase()}
                </p>
              )}
              {typeof lowConfidenceCount === 'number' && lowConfidenceCount > 0 && (
                <p className="generation-progress-meta">{t('slides.lowConfidenceNotice', { count: lowConfidenceCount })}</p>
              )}
            </section>
          )}

          <section className="card gamma-outline-card">
            <div className="gamma-panel-head">
              <div>
                <span className="gamma-panel-kicker">{t('slides.liveOutline')}</span>
                <h3>{t('slides.deckStructure')}</h3>
              </div>
              <span className="gamma-outline-count">{outlineSlides.length || desiredSlideCount} slides</span>
            </div>

            {outlineSlides.length > 0 ? (
              <div className="gamma-outline-list">
                {outlineSlides.map((slide) => (
                  <div key={`${slide.slideIndex}-${slide.heading}`} className="gamma-outline-item">
                    <span>{slide.slideIndex}</span>
                    <div>
                      <strong>{slide.heading}</strong>
                      <p>{slide.goal}</p>
                      <small>{getSlideTypeLabel(slide.slideType)}</small>
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <div className="gamma-outline-empty">
                <p>{t('slides.outlineEmpty')}</p>
              </div>
            )}
          </section>
        </aside>

        <section className="gamma-canvas">
          <section className="card gamma-canvas-head">
            <div>
              <span className="gamma-panel-kicker">{t('slides.previewCanvas')}</span>
              <h3>{deck?.title || t('slides.previewFallbackTitle')}</h3>
              <p>{deck?.subtitle || deckBrief.narrativeGoal}</p>
            </div>
            <div className="gamma-canvas-badges">
              <span>{themeMeta.label}</span>
              <span>{deckBrief.audience}</span>
              <span>{deckBrief.tone}</span>
              <span>{t('slides.mediaReady', { count: slidesWithSelectedMedia })}</span>
            </div>
          </section>

          <div className={`slide-preview gamma-preview ${readingMode ? 'reading-mode' : ''}`}>
            {previewItems.length === 0 && (
              <div className="card gamma-empty-canvas">
                <div className="gamma-empty-mockup">
                  <div className="gamma-empty-mockup-card"></div>
                  <div className="gamma-empty-mockup-card"></div>
                  <div className="gamma-empty-mockup-card"></div>
                </div>
                <h3>{allPreviewItems.length > 0 ? t('slides.hiddenSlidesTitle') : t('slides.noDeckTitle')}</h3>
                <p>{allPreviewItems.length > 0 ? t('slides.hiddenSlidesBody') : t('slides.noDeckBody')}</p>
              </div>
            )}

            {previewItems.map((item) => {
              const isEditing = editingSlideId === item.id;
              const draft = drafts[item.id];
              const hasContent = (item.bodyBlocks || []).length > 0;
              const imageVm = buildSlideImageViewModel(item);
              const isMediaOpen = expandedMediaSlideId === item.id;
              const isMediaBusy = mediaBusySlideId === item.id;

              return (
                <article key={item.id} className={`slide-preview-card gamma-slide-card slide-preview-${normalizeSlideType(item.slideType)} ${item.status?.toLowerCase?.() || ''}`}>
                  <div className="slide-preview-meta">
                    <span>{t('slides.slideLabel', { index: item.slideIndex })}</span>
                    <div className="quality-toolbar">
                      <span>{getSlideTypeLabel(item.slideType)}</span>
                      {item.quality?.score !== undefined && item.quality?.score !== null && (
                        <span className={`quality-chip ${item.quality?.isLowConfidence ? 'low' : 'good'}`}>
                          {item.quality.score}/100
                        </span>
                      )}
                    </div>
                  </div>

                  {isEditing ? (
                    <div className="slide-edit-form">
                      <input value={draft.heading} onChange={(event) => handleDraftChange(item.id, 'heading', event.target.value)} />
                      <input value={draft.subheading} onChange={(event) => handleDraftChange(item.id, 'subheading', event.target.value)} placeholder={t('slides.subheadingPlaceholder')} />
                      <input value={draft.goal} onChange={(event) => handleDraftChange(item.id, 'goal', event.target.value)} placeholder={t('slides.goalPlaceholder')} />
                      <textarea value={draft.bodyText} onChange={(event) => handleDraftChange(item.id, 'bodyText', event.target.value)} rows={6} />
                      <textarea value={draft.speakerNotes} onChange={(event) => handleDraftChange(item.id, 'speakerNotes', event.target.value)} rows={4} />
                      <input value={draft.accentTone} onChange={(event) => handleDraftChange(item.id, 'accentTone', event.target.value)} placeholder={t('slides.accentTonePlaceholder')} />
                      <div className="slide-edit-actions">
                        <button className="button" onClick={() => handleSave(item)}>{t('slides.saveSlide')}</button>
                        <button className="button button-secondary" onClick={() => setEditingSlideId(null)}>{t('slides.cancel')}</button>
                      </div>
                    </div>
                  ) : (
                    <>
                      <h3>{item.heading}</h3>
                      {item.subheading && <p className="slide-preview-subheading">{item.subheading}</p>}
                      {item.goal && <div className="slide-preview-goal">{item.goal}</div>}

                      <div className={`slide-media-shell slide-media-shell-preview slide-media-shell-${imageVm.badgeTone}${imageVm.selectedImage ? ' has-image' : ''}`}>
                        {imageVm.selectedImage?.localAssetUrl ? (
                          <img src={imageVm.selectedImage.localAssetUrl} alt={imageVm.selectedImage.altText || item.heading || t('slides.slideLabel', { index: item.slideIndex })} />
                        ) : (
                          <div className="slide-media-placeholder">
                            <strong>{imageVm.badgeLabel}</strong>
                            <span>{imageVm.statusLabel}</span>
                          </div>
                        )}
                      </div>

                      <div className="slide-media-meta">
                        <span className={`slide-media-badge tone-${imageVm.badgeTone}`}>{imageVm.badgeLabel}</span>
                        {imageVm.selectedImage?.provider && (
                          <span className="slide-media-source">{imageVm.selectedImage.provider}</span>
                        )}
                      </div>

                      <p className="slide-media-helper">{imageVm.helperText}</p>
                      {imageVm.attributionText && <p className="slide-media-attribution">{imageVm.attributionText}</p>}

                      {!hasContent && (item.status === 'Pending' || item.status === 'Generating') ? (
                        <div className="slide-skeleton">
                          <span></span>
                          <span></span>
                          <span></span>
                        </div>
                      ) : (
                        <div className="slide-preview-body">
                          {(item.bodyBlocks || []).map((block, index) => (
                            readingMode ? <p key={index}>{block}</p> : <div key={index} className="slide-preview-bullet">{block}</div>
                          ))}
                        </div>
                      )}

                      {item.speakerNotes && <p className="slide-preview-notes">{item.speakerNotes}</p>}

                      {(item.quality?.isLowConfidence || item.quality?.isUnknown) && (
                        <div className="quality-warning compact">
                          <strong>{item.quality?.isLowConfidence ? t('slides.reviewNeeded') : t('slides.noVerifier')}</strong>
                          {Array.isArray(item.quality?.issues) && item.quality.issues.length > 0 && (
                            <ul className="quality-issues">
                              {item.quality.issues.slice(0, 2).map((issue) => (
                                <li key={issue}>{issue}</li>
                              ))}
                            </ul>
                          )}
                        </div>
                      )}

                      <div className="slide-preview-actions">
                        <div className="slide-preview-action-group">
                          {item.status === 'Completed' || hasContent ? (
                            <button className="button button-secondary" onClick={() => handleEdit(item)}>{t('slides.editSlide')}</button>
                          ) : (
                            <button className="button button-secondary" disabled>{t('slides.waitingContent')}</button>
                          )}
                          <button
                            className="button button-secondary"
                            onClick={() => setExpandedMediaSlideId(isMediaOpen ? null : item.id)}
                          >
                            {isMediaOpen
                              ? t('slides.hideMedia')
                              : imageVm.needsImage
                                ? (imageVm.hasCandidates || imageVm.selectedImage ? t('slides.swapImage') : t('slides.mediaZone'))
                                : t('slides.textOnly')}
                          </button>
                          {imageVm.needsImage && (
                            <button
                              className="button button-secondary"
                              onClick={() => handleRefreshImages(item)}
                              disabled={isMediaBusy}
                            >
                              {isMediaBusy ? t('slides.searchingImage') : (imageVm.hasCandidates ? t('slides.refindImage') : t('slides.findImage'))}
                            </button>
                          )}
                        </div>
                        <span className={`slide-status slide-status-${String(item.status || '').toLowerCase()}`}>{item.status}</span>
                      </div>

                      {isMediaOpen && (
                        <div className="slide-media-tray">
                          <div className="slide-media-tray-head">
                            <div>
                              <strong>{t('slides.mediaInspector')}</strong>
                              <p>{t('slides.mediaInspectorBody', { index: item.slideIndex })}</p>
                            </div>
                            <button className="button button-secondary" onClick={() => setExpandedMediaSlideId(null)}>
                              {t('slides.close')}
                            </button>
                          </div>

                          {imageVm.hasCandidates ? (
                            <div className="slide-media-thumb-grid">
                              {imageVm.candidates.map((candidate) => (
                                <article key={candidate.key} className={`slide-media-thumb ${candidate.key === imageVm.selectedImage?.key ? 'selected' : ''}`}>
                                  <div className="slide-media-thumb-figure">
                                    {candidate.localAssetUrl ? (
                                      <img src={candidate.localAssetUrl} alt={candidate.altText || `Candidate ${candidate.key}`} />
                                    ) : (
                                      <div className="slide-media-thumb-placeholder">No preview</div>
                                    )}
                                  </div>
                                  <div className="slide-media-thumb-meta">
                                    <span className={`slide-media-badge tone-${candidate.sourceType === 'generated' ? 'generated' : 'web'}`}>
                                      {candidate.sourceType === 'generated' ? 'AI Generated' : 'Web'}
                                    </span>
                                    <strong>{candidate.provider}</strong>
                                    {(candidate.licenseLabel || candidate.attributionText) && (
                                      <small>{[candidate.licenseLabel, candidate.attributionText].filter(Boolean).join(' · ')}</small>
                                    )}
                                    <button
                                      className="button button-secondary"
                                      onClick={() => handleSelectImage(item, candidate.key)}
                                      disabled={isMediaBusy || candidate.key === imageVm.selectedImage?.key}
                                    >
                                      {candidate.key === imageVm.selectedImage?.key ? t('slides.selected') : t('slides.chooseThisImage')}
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
                    </>
                  )}
                </article>
              );
            })}
          </div>
        </section>
      </div>
    </div>
  );
}

export default SlideStudio;
