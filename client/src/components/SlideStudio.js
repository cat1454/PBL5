import React, { useCallback, useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { documentService, slideService } from '../services/api';

const THEME_OPTIONS = [
  {
    key: 'editorial-sunrise',
    label: 'Editorial Sunrise',
    blurb: 'Am, premium, de doc va hop voi bai giang tong quan.',
  },
  {
    key: 'paper-mint',
    label: 'Paper Mint',
    blurb: 'Nhe, sach, hop voi deck giang giai va note hoc tap.',
  },
  {
    key: 'cobalt-grid',
    label: 'Cobalt Grid',
    blurb: 'Cung cap, ky thuat, hop voi noi dung he thong va quy trinh.',
  },
  {
    key: 'midnight-signal',
    label: 'Midnight Signal',
    blurb: 'Tuong phan manh, hop voi deck chien luoc hoac executive.',
  },
];

const TONE_OPTIONS = [
  'Ro rang, hien dai, de nho',
  'Hoc thuat nhung de tiep thu',
  'Tu tin, co nhan manh',
  'Kich thich tri to mo',
];

const AUDIENCE_OPTIONS = [
  'Sinh vien va nguoi hoc',
  'Giao vien / nguoi thuyet trinh',
  'Quan ly / lanh dao',
  'Nguoi moi bat dau',
];

const LANGUAGE_STYLE_OPTIONS = [
  'Tieng Viet ngan gon, chuyen nghiep',
  'Tieng Viet than thien, de doc tren web',
  'Tieng Viet hoc thuat, co cau truc',
  'Tieng Viet thuyet trinh, nhan y manh',
];

const DEFAULT_BRIEF = {
  themeKey: 'editorial-sunrise',
  audience: 'Sinh vien va nguoi hoc',
  tone: 'Ro rang, hien dai, de nho',
  narrativeGoal: 'Giup nguoi doc nam duoc cau truc va cac y chinh cua tai lieu trong mot lan xem',
  languageStyle: 'Tieng Viet ngan gon, chuyen nghiep',
};

function SlideStudio() {
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
  const [deckBrief, setDeckBrief] = useState(DEFAULT_BRIEF);
  const [briefDirty, setBriefDirty] = useState(false);
  const [hideLowConfidence, setHideLowConfidence] = useState(false);

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
      setError('Khong tai duoc thong tin tai lieu.');
    }
  }, [briefDirty, documentId]);

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
          themeKey: data.outline.brief.themeKey || DEFAULT_BRIEF.themeKey,
          audience: data.outline.brief.audience || DEFAULT_BRIEF.audience,
          tone: data.outline.brief.tone || DEFAULT_BRIEF.tone,
          narrativeGoal: data.outline.brief.narrativeGoal || DEFAULT_BRIEF.narrativeGoal,
          languageStyle: data.outline.brief.languageStyle || DEFAULT_BRIEF.languageStyle,
        });
      }
      if (data?.generationProgress) {
        setProgress(data.generationProgress);
        setJobId(data.generationProgress.jobId || data.generationProgress.JobId || jobId);
      }
    } catch (err) {
      console.error(err);
      setError('Khong tai duoc slide deck hien tai.');
    } finally {
      if (!silent) {
        setLoading(false);
      }
    }
  }, [briefDirty, documentId, jobId]);

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
      setFeedback('Dang tao outline va sinh deck theo brief moi...');
      const response = await slideService.startGenerateSlides(documentId, {
        desiredSlideCount,
        ...deckBrief,
      });
      setJobId(response.jobId);
      setProgress({
        status: response.status,
        percent: 0,
        stageLabel: 'Cho xu ly',
        message: 'Da tao job sinh slide',
      });
      await loadDeck({ silent: true });
    } catch (err) {
      console.error(err);
      setError('Khong bat dau duoc qua trinh sinh slide.');
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
      setFeedback('Da luu chinh sua slide.');
    } catch (err) {
      console.error(err);
      setError('Khong luu duoc thay doi cho slide nay.');
    }
  };

  const formatEta = (seconds) => {
    if (typeof seconds !== 'number') {
      return 'Dang tinh ETA...';
    }

    if (seconds <= 0) {
      return 'Sap xong...';
    }

    if (seconds < 60) {
      return `${seconds}s`;
    }

    const minutes = Math.floor(seconds / 60);
    const remain = seconds % 60;
    return `${minutes}p ${remain}s`;
  };

  const getThemeMeta = (themeKey) => THEME_OPTIONS.find((theme) => theme.key === themeKey) || THEME_OPTIONS[0];

  const getSlideTypeLabel = (slideType) => {
    switch ((slideType || '').toLowerCase()) {
      case 'title':
        return 'Cover';
      case 'sectiondivider':
        return 'Section';
      case 'quote':
        return 'Quote';
      case 'highlight':
        return 'Highlight';
      case 'stat':
        return 'Stat';
      default:
        return 'Content';
    }
  };

  if (loading) {
    return (
      <div className="loading">
        <div className="spinner"></div>
        <p>Dang tai Slide Studio...</p>
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
  const lowConfidenceCount = deck?.qualitySummary?.lowConfidenceCount
    ?? allPreviewItems.filter((item) => item.quality?.isLowConfidence).length;

  return (
    <div className={`slide-studio gamma-studio theme-${themeMeta.key}`}>
      <section className="card gamma-hero-card">
        <div className="gamma-hero-copy">
          <button className="button button-secondary" onClick={() => navigate('/documents')}>Quay lai Documents</button>
          <span className="gamma-eyebrow">AI slide studio</span>
          <h2>{deck?.title || documentMeta?.fileName || 'Create a new gamma-style deck'}</h2>
          <p className="section-subtitle">
            Sinh outline truoc, sinh tung slide dan dan, va chinh layout/noi dung ngay trong mot workspace.
          </p>
        </div>

        <div className="gamma-hero-meta">
          <div className="gamma-mini-stat">
            <span>Tai lieu</span>
            <strong>{documentMeta?.fileName || 'Khong co du lieu'}</strong>
          </div>
          <div className="gamma-mini-stat">
            <span>Theme</span>
            <strong>{themeMeta.label}</strong>
          </div>
          <div className="gamma-mini-stat">
            <span>Slides</span>
            <strong>{completedSlides}/{previewItems.length || desiredSlideCount}</strong>
          </div>
          <div className="gamma-mini-stat">
            <span>Trang thai</span>
            <strong>{activeProgress?.stageLabel || deck?.status || 'Chua tao'}</strong>
          </div>
        </div>
      </section>

      {!canGenerate && (
        <div className="alert alert-info">
          Tai lieu can xu ly xong truoc khi tao slide. Trang thai hien tai: {documentMeta?.status}
        </div>
      )}

      {error && <div className="alert alert-error">{error}</div>}
      {feedback && <div className="alert alert-info">{feedback}</div>}

      <div className="gamma-workspace">
        <aside className="gamma-sidebar">
          <section className="card gamma-brief-card">
            <div className="gamma-panel-head">
              <div>
                <span className="gamma-panel-kicker">Deck brief</span>
                <h3>Mo ta deck truoc khi sinh</h3>
              </div>
              <span className="gamma-theme-pill">{themeMeta.label}</span>
            </div>

            <div className="gamma-brief-grid">
              <label className="gamma-field">
                <span>So slide</span>
                <input
                  type="number"
                  min="5"
                  max="12"
                  value={desiredSlideCount}
                  onChange={(event) => setDesiredSlideCount(Number(event.target.value))}
                />
              </label>

              <label className="gamma-field">
                <span>Audience</span>
                <select value={deckBrief.audience} onChange={(event) => handleBriefChange('audience', event.target.value)}>
                  {AUDIENCE_OPTIONS.map((option) => (
                    <option key={option} value={option}>{option}</option>
                  ))}
                </select>
              </label>

              <label className="gamma-field">
                <span>Tone</span>
                <select value={deckBrief.tone} onChange={(event) => handleBriefChange('tone', event.target.value)}>
                  {TONE_OPTIONS.map((option) => (
                    <option key={option} value={option}>{option}</option>
                  ))}
                </select>
              </label>

              <label className="gamma-field">
                <span>Language style</span>
                <select value={deckBrief.languageStyle} onChange={(event) => handleBriefChange('languageStyle', event.target.value)}>
                  {LANGUAGE_STYLE_OPTIONS.map((option) => (
                    <option key={option} value={option}>{option}</option>
                  ))}
                </select>
              </label>
            </div>

            <label className="gamma-field">
              <span>Muc tieu deck</span>
              <textarea
                rows={4}
                value={deckBrief.narrativeGoal}
                onChange={(event) => handleBriefChange('narrativeGoal', event.target.value)}
                placeholder="Deck nay can giup nguoi doc hieu dieu gi sau 2-3 phut?"
              />
            </label>

            <div className="gamma-theme-grid">
              {THEME_OPTIONS.map((theme) => (
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
                {isGenerating ? `Dang tao... ${activeProgress?.percent || 0}%` : deck ? 'Tao lai deck' : 'Tao deck bang AI'}
              </button>
              <button className="button button-secondary" onClick={() => setReadingMode((current) => !current)}>
                {readingMode ? 'Tat reading mode' : 'Bat reading mode'}
              </button>
              <button className="button button-secondary" onClick={() => setHideLowConfidence((current) => !current)}>
                {hideLowConfidence ? 'Hien tat ca slide' : 'An slide diem thap'}
              </button>
              {deck && (
                <button className="button button-secondary" onClick={() => window.open(slideService.getDeckHtmlUrl(documentId), '_blank', 'noopener,noreferrer')}>
                  Export HTML/PDF
                </button>
              )}
            </div>
          </section>

          {activeProgress && (
            <section className="card gamma-progress-card">
              <div className="gamma-panel-head">
                <div>
                  <span className="gamma-panel-kicker">Live generation</span>
                  <h3>{activeProgress.stageLabel || 'Dang sinh slide'}</h3>
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
                  {activeProgress.current}/{activeProgress.total} {activeProgress.unitLabel || 'slide'}
                </p>
              )}
              {typeof lowConfidenceCount === 'number' && lowConfidenceCount > 0 && (
                <p className="generation-progress-meta">Dang co {lowConfidenceCount} slide can review do verifier score thap.</p>
              )}
            </section>
          )}

          <section className="card gamma-outline-card">
            <div className="gamma-panel-head">
              <div>
                <span className="gamma-panel-kicker">Live outline</span>
                <h3>Cau truc deck</h3>
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
                <p>Outline se xuat hien tai day ngay sau khi AI lap xong nhung slide dau tien.</p>
              </div>
            )}
          </section>
        </aside>

        <section className="gamma-canvas">
          <section className="card gamma-canvas-head">
            <div>
              <span className="gamma-panel-kicker">Preview canvas</span>
              <h3>{deck?.title || 'Gamma-style deck preview'}</h3>
              <p>{deck?.subtitle || deckBrief.narrativeGoal}</p>
            </div>
            <div className="gamma-canvas-badges">
              <span>{themeMeta.label}</span>
              <span>{deckBrief.audience}</span>
              <span>{deckBrief.tone}</span>
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
                <h3>{allPreviewItems.length > 0 ? 'Tat ca slide hien dang bi an' : 'Chua co deck'}</h3>
                <p>
                  {allPreviewItems.length > 0
                    ? 'Tat bo loc an low-confidence de xem lai toan bo slide.'
                    : <>Chon theme, audience, tone, roi bam <strong>Tao deck bang AI</strong>. He thong se sinh outline truoc,
                      sau do tung slide se hien dan o canvas nay.</>}
                </p>
              </div>
            )}

            {previewItems.map((item) => {
              const isEditing = editingSlideId === item.id;
              const draft = drafts[item.id];
              const hasContent = (item.bodyBlocks || []).length > 0;

              return (
                <article key={item.id} className={`slide-preview-card gamma-slide-card slide-preview-${String(item.slideType || '').toLowerCase()} ${item.status?.toLowerCase?.() || ''}`}>
                  <div className="slide-preview-meta">
                    <span>Slide {item.slideIndex}</span>
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
                      <input value={draft.subheading} onChange={(event) => handleDraftChange(item.id, 'subheading', event.target.value)} placeholder="Subheading" />
                      <input value={draft.goal} onChange={(event) => handleDraftChange(item.id, 'goal', event.target.value)} placeholder="Goal" />
                      <textarea value={draft.bodyText} onChange={(event) => handleDraftChange(item.id, 'bodyText', event.target.value)} rows={6} />
                      <textarea value={draft.speakerNotes} onChange={(event) => handleDraftChange(item.id, 'speakerNotes', event.target.value)} rows={4} />
                      <input value={draft.accentTone} onChange={(event) => handleDraftChange(item.id, 'accentTone', event.target.value)} placeholder="Accent tone" />
                      <div className="slide-edit-actions">
                        <button className="button" onClick={() => handleSave(item)}>Luu slide</button>
                        <button className="button button-secondary" onClick={() => setEditingSlideId(null)}>Huy</button>
                      </div>
                    </div>
                  ) : (
                    <>
                      <h3>{item.heading}</h3>
                      {item.subheading && <p className="slide-preview-subheading">{item.subheading}</p>}
                      {item.goal && <div className="slide-preview-goal">{item.goal}</div>}

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
                          <strong>{item.quality?.isLowConfidence ? 'Can review' : 'Chua co verifier score'}</strong>
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
                        {item.status === 'Completed' || hasContent ? (
                          <button className="button button-secondary" onClick={() => handleEdit(item)}>Sua slide</button>
                        ) : (
                          <button className="button button-secondary" disabled>Dang cho noi dung</button>
                        )}
                        <span className={`slide-status slide-status-${String(item.status || '').toLowerCase()}`}>{item.status}</span>
                      </div>
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
