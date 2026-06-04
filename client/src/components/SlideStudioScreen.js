import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import ProgressCard from './ProgressCard';
import { useToast } from './common/ToastProvider';
import { useLanguage } from '../context/LanguageContext';
import { documentService, getApiErrorMessage, isApiNotFound, slideService } from '../services/api';
import { getProgressStageLabel, isActiveProgress, normalizeProgressState } from '../services/progress';
import {
  confirmGenerationReadiness,
  getReadinessLabel,
  getReadinessMessage,
  normalizeGenerationReadiness,
} from '../services/generationReadiness';
import DocumentUnderstandingPanel from './DocumentUnderstandingPanel';

const THEME_OPTIONS = [
  { key: 'editorial-sunrise', label: 'Editorial Sunrise', blurb: 'Ấm, cao cấp, dễ đọc và hợp với bài giảng tổng quan.' },
  { key: 'paper-mint', label: 'Paper Mint', blurb: 'Nhẹ, sạch, hợp với deck giảng giải và ghi chú học tập.' },
  { key: 'cobalt-grid', label: 'Cobalt Grid', blurb: 'Cứng cáp, kỹ thuật, hợp với nội dung hệ thống và quy trình.' },
  { key: 'midnight-signal', label: 'Midnight Signal', blurb: 'Tương phản mạnh, hợp với deck chiến lược hoặc executive.' },
];

const TONE_OPTIONS = [
  'Rõ ràng, hiện đại, dễ nhớ',
  'Học thuật nhưng dễ tiếp thu',
  'Tự tin, có nhấn mạnh',
  'Khơi gợi trí tò mò',
];

const AUDIENCE_OPTIONS = [
  'Sinh viên và người học',
  'Giáo viên / người thuyết trình',
  'Quản lý / lãnh đạo',
  'Người mới bắt đầu',
];

const LANGUAGE_STYLE_OPTIONS = [
  'Tiếng Việt ngắn gọn, chuyên nghiệp',
  'Tiếng Việt thân thiện, dễ đọc trên web',
  'Tiếng Việt học thuật, có cấu trúc',
  'Tiếng Việt thuyết trình, nhấn ý mạnh',
];

const DEFAULT_BRIEF = {
  themeKey: 'editorial-sunrise',
  audience: 'Sinh viên và người học',
  tone: 'Rõ ràng, hiện đại, dễ nhớ',
  narrativeGoal: 'Giúp người đọc nắm được cấu trúc và các ý chính của tài liệu trong một lần xem',
  languageStyle: 'Tiếng Việt ngắn gọn, chuyên nghiệp',
};

function SlideStudioScreen() {
  const { documentId } = useParams();
  const navigate = useNavigate();
  const { showToast } = useToast();
  const { language } = useLanguage();
  const [documentMeta, setDocumentMeta] = useState(null);
  const [generationReadiness, setGenerationReadiness] = useState(null);
  const [documentProgress, setDocumentProgress] = useState(null);
  const [deck, setDeck] = useState(null);
  const [progress, setProgress] = useState(null);
  const [jobId, setJobId] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [readingMode, setReadingMode] = useState(false);
  const [desiredSlideCount, setDesiredSlideCount] = useState(8);
  const [editingSlideId, setEditingSlideId] = useState(null);
  const [drafts, setDrafts] = useState({});
  const [deckBrief, setDeckBrief] = useState(DEFAULT_BRIEF);
  const [briefDirty, setBriefDirty] = useState(false);
  const [hideLowConfidence, setHideLowConfidence] = useState(false);
  const [exportingFormat, setExportingFormat] = useState('');

  const loadDocument = useCallback(async () => {
    try {
      const [meta, liveProgress] = await Promise.all([
        documentService.getDocument(documentId),
        documentService.getDocumentProgress(documentId),
      ]);

      setDocumentMeta(meta);
      setGenerationReadiness(normalizeGenerationReadiness(meta?.generationReadiness));
      setDocumentProgress(
        liveProgress || meta?.processingProgress
          ? normalizeProgressState(liveProgress || meta?.processingProgress, { documentId: Number(documentId) })
          : null
      );
      setDeckBrief((current) => ({
        ...current,
        narrativeGoal: briefDirty ? current.narrativeGoal : meta?.summary || current.narrativeGoal,
      }));
    } catch (err) {
      console.error(err);
      setError('Không tải được thông tin tài liệu.');
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
        const nextProgress = normalizeProgressState(data.generationProgress, { documentId: Number(documentId) });
        setProgress(nextProgress);
        setJobId(nextProgress.jobId || jobId);
      }
    } catch (err) {
      console.error(err);
      setError('Không tải được slide deck hiện tại.');
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

  useEffect(() => {
    if (!isActiveProgress(documentProgress)) {
      return undefined;
    }

    const interval = setInterval(async () => {
      try {
        const liveProgress = await documentService.getDocumentProgress(documentId);
        setDocumentProgress(normalizeProgressState(liveProgress, { documentId: Number(documentId) }));
        const meta = await documentService.getDocument(documentId);
        setDocumentMeta(meta);
      } catch (err) {
        console.error(err);
        if (isApiNotFound(err)) {
          await loadDeck({ silent: true });
          setJobId(null);
        }
      }
    }, 3000);

    return () => clearInterval(interval);
  }, [documentId, documentProgress]);

  const activeProgress = useMemo(() => {
    const rawProgress = progress || deck?.generationProgress;
    return rawProgress
      ? normalizeProgressState(rawProgress, { documentId: Number(documentId) })
      : null;
  }, [deck?.generationProgress, documentId, progress]);

  useEffect(() => {
    if (!jobId && !isActiveProgress(activeProgress) && !(deck && (deck.status === 'GeneratingSlides' || deck.status === 'GeneratingOutline'))) {
      return undefined;
    }

    const interval = setInterval(async () => {
      try {
        if (jobId) {
          const nextProgress = normalizeProgressState(
            await slideService.getGenerateProgress(jobId),
            { documentId: Number(documentId), jobId }
          );
          setProgress(nextProgress);
          setJobId(nextProgress.jobId || jobId);
        }

        await loadDeck({ silent: true });
      } catch (err) {
        console.error(err);
      }
    }, 1500);

    return () => clearInterval(interval);
  }, [activeProgress, deck, documentId, jobId, loadDeck]);

  const handleGenerate = async () => {
    try {
      setError('');
      const readinessDecision = confirmGenerationReadiness(generationReadiness, language);
      if (!readinessDecision.allowed) {
        return;
      }

      const response = await slideService.startGenerateSlides(documentId, {
        desiredSlideCount,
        ...deckBrief,
        confirmLowConfidence: readinessDecision.confirmed,
      });
      setGenerationReadiness(normalizeGenerationReadiness(response?.generationReadiness) || generationReadiness);
      const nextProgress = normalizeProgressState(response.progress || response, { documentId: Number(documentId), jobId: response.jobId });
      setJobId(response.jobId);
      setProgress(nextProgress);
      showToast({
        type: 'info',
        message: 'Đã bắt đầu tạo slide deck.',
        description: 'Tiến trình sẽ tiếp tục hiển thị trong progress card.',
      });
      await loadDeck({ silent: true });
    } catch (err) {
      console.error(err);
      setError('Không bắt đầu được quá trình sinh slide.');
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
      showToast({
        type: 'success',
        message: 'Đã lưu chỉnh sửa slide.',
      });
    } catch (err) {
      console.error(err);
      setError('Không lưu được thay đổi cho slide này.');
    }
  };

  const getThemeMeta = (themeKey) => THEME_OPTIONS.find((theme) => theme.key === themeKey) || THEME_OPTIONS[0];

  const normalizeSlideType = (slideType) => {
    if (typeof slideType === 'number' && Number.isFinite(slideType)) {
      switch (slideType) {
        case 0:
          return 'title';
        case 1:
          return 'sectiondivider';
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
      if (normalized) {
        return normalized;
      }
    }

    return 'content';
  };

  const getSlideTypeLabel = (slideType) => {
    switch (normalizeSlideType(slideType)) {
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

  const handleDownloadHtml = async () => {
    if (!deck || exportingFormat || isActiveProgress(activeProgress)) {
      return;
    }

    try {
      setExportingFormat('html');
      const result = await slideService.exportDeckHtml(deck.id);
      showToast({
        type: 'success',
        message: 'Da tai file HTML.',
        description: result.filename,
      });
    } catch (err) {
      console.error(err);
      setError(getApiErrorMessage(err, 'Khong the xuat HTML.'));
    } finally {
      setExportingFormat('');
    }
  };

  const handleOpenPrint = async () => {
    if (!deck || exportingFormat || isActiveProgress(activeProgress)) {
      return;
    }

    const printWindow = window.open('', '_blank');
    if (!printWindow) {
      setError('Trinh duyet da chan tab in. Hay cho phep popup va thu lai.');
      return;
    }
    printWindow.opener = null;

    try {
      setExportingFormat('print');
      const blob = await slideService.getDeckPrintHtml(deck.id);
      const url = window.URL.createObjectURL(blob);
      printWindow.location.href = url;
      window.setTimeout(() => window.URL.revokeObjectURL(url), 60000);
      showToast({ type: 'success', message: 'Da mo ban In / Luu PDF.' });
    } catch (err) {
      console.error(err);
      printWindow.close();
      setError(getApiErrorMessage(err, 'Khong the mo ban in.'));
    } finally {
      setExportingFormat('');
    }
  };

  const handleDownloadPptx = async () => {
    if (!deck || exportingFormat || isActiveProgress(activeProgress)) {
      return;
    }

    try {
      setExportingFormat('pptx');
      const result = await slideService.exportDeckPptx(deck.id);
      showToast({
        type: 'success',
        message: 'Da tai file PPTX.',
        description: result.filename,
      });
    } catch (err) {
      console.error(err);
      setError(getApiErrorMessage(err, 'Khong the xuat PPTX.'));
    } finally {
      setExportingFormat('');
    }
  };

  if (loading) {
    return (
      <div className="loading">
        <div className="spinner"></div>
        <p>Đang tải Slide Studio...</p>
      </div>
    );
  }

  const canGenerate = documentMeta?.status === 3;
  const isExportDisabled = !deck || exportingFormat || isActiveProgress(activeProgress);
  const outlineSlides = deck?.outline?.slides || [];
  const themeMeta = getThemeMeta(deckBrief.themeKey);
  const allPreviewItems = deck?.items || [];
  const previewItems = hideLowConfidence
    ? allPreviewItems.filter((item) => !item.quality?.isLowConfidence)
    : allPreviewItems;
  const completedSlides = previewItems.filter((item) => item.status === 'Completed').length;
  const lowConfidenceCount = deck?.qualitySummary?.lowConfidenceCount
    ?? allPreviewItems.filter((item) => item.quality?.isLowConfidence).length;
  const documentStage = getProgressStageLabel(documentProgress);
  const readinessMessage = getReadinessMessage(generationReadiness, language);

  return (
    <div className={`slide-studio gamma-studio theme-${themeMeta.key}`}>
      <section className="card gamma-hero-card">
        <div className="gamma-hero-copy">
          <button className="button button-secondary" onClick={() => navigate('/workspaces')}>Quay lại Workspaces</button>
          <span className="gamma-eyebrow">AI slide studio</span>
          <h2>{deck?.title || documentMeta?.fileName || 'Create a new gamma-style deck'}</h2>
          <p className="section-subtitle">
            Sinh outline trước, sinh từng slide dần dần, và chỉnh layout/nội dung ngay trong một workspace.
          </p>
        </div>

        <div className="gamma-hero-meta">
          <div className="gamma-mini-stat">
            <span>Tài liệu</span>
            <strong>{documentMeta?.fileName || 'Không có dữ liệu'}</strong>
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
            <span>Trạng thái</span>
            <strong>{getProgressStageLabel(activeProgress)}</strong>
          </div>
          {generationReadiness && (
            <div className="gamma-mini-stat">
              <span>Readiness</span>
              <strong>
                <span className={`generation-readiness-badge tone-${generationReadiness.tone}`}>
                  {getReadinessLabel(generationReadiness, language)}
                </span>
              </strong>
            </div>
          )}
        </div>
      </section>

      {readinessMessage && (
        <div className={`alert generation-readiness-card tone-${generationReadiness.tone}`}>
          <strong>{readinessMessage.title}</strong>
          <p>{readinessMessage.body}</p>
        </div>
      )}

      <DocumentUnderstandingPanel
        documentId={documentId}
        showEmpty
        compact
      />

      {!canGenerate && (
        <>
          <div className="alert alert-info">
            Tài liệu cần xử lý xong trước khi tạo slide. Trạng thái hiện tại: {documentStage}.
          </div>
          {documentProgress && (
            <ProgressCard
              title="Document progress"
              progress={documentProgress}
              context="document"
              className="progress-card-standalone"
            />
          )}
        </>
      )}

      {error && <div className="alert alert-error">{error}</div>}

      <div className="gamma-workspace">
        <aside className="gamma-sidebar">
          <section className="card gamma-brief-card">
            <div className="gamma-panel-head">
              <div>
                <span className="gamma-panel-kicker">Deck brief</span>
                <h3>Mô tả deck trước khi sinh</h3>
              </div>
              <span className="gamma-theme-pill">{themeMeta.label}</span>
            </div>

            <div className="gamma-brief-grid">
              <label className="gamma-field">
                <span>Số slide</span>
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
              <span>Mục tiêu deck</span>
              <textarea
                rows={4}
                value={deckBrief.narrativeGoal}
                onChange={(event) => handleBriefChange('narrativeGoal', event.target.value)}
                placeholder="Deck này cần giúp người đọc hiểu điều gì sau 2-3 phút?"
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
              <button className="button" onClick={handleGenerate} disabled={!canGenerate || isActiveProgress(activeProgress)}>
                {isActiveProgress(activeProgress) ? `Đang tạo... ${activeProgress.percent || 0}%` : deck ? 'Tạo lại deck' : 'Tạo deck bằng AI'}
              </button>
              <button className="button button-secondary" onClick={() => setReadingMode((current) => !current)}>
                {readingMode ? 'Tắt reading mode' : 'Bật reading mode'}
              </button>
              <button className="button button-secondary" onClick={() => setHideLowConfidence((current) => !current)}>
                {hideLowConfidence ? 'Hiện tất cả slide' : 'Ẩn slide điểm thấp'}
              </button>
              <button className="button button-secondary" onClick={handleDownloadHtml} disabled={isExportDisabled}>
                {exportingFormat === 'html' ? 'Dang tai HTML...' : 'Tai HTML'}
              </button>
              <button className="button button-secondary" onClick={handleOpenPrint} disabled={isExportDisabled}>
                {exportingFormat === 'print' ? 'Dang mo ban in...' : 'In / Luu PDF'}
              </button>
              <button className="button button-secondary" onClick={handleDownloadPptx} disabled={isExportDisabled}>
                {exportingFormat === 'pptx' ? 'Dang tai PPTX...' : 'Tai PPTX'}
              </button>
            </div>
          </section>

          {activeProgress && (activeProgress.status !== 'queued' || activeProgress.message || activeProgress.detail) && (
            <ProgressCard
              title="Slide generation"
              progress={activeProgress}
              context="slide"
              className="gamma-progress-card progress-card-standalone"
            />
          )}

          <section className="card gamma-outline-card">
            <div className="gamma-panel-head">
              <div>
                <span className="gamma-panel-kicker">Live outline</span>
                <h3>Cấu trúc deck</h3>
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
                <p>Outline sẽ xuất hiện tại đây ngay sau khi AI lập xong những slide đầu tiên.</p>
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

          {typeof lowConfidenceCount === 'number' && lowConfidenceCount > 0 && (
            <div className="alert alert-info">
              Đang có {lowConfidenceCount} slide cần rà soát do verifier score thấp.
            </div>
          )}

          <div className={`slide-preview gamma-preview ${readingMode ? 'reading-mode' : ''}`}>
            {previewItems.length === 0 && (
              <div className="card gamma-empty-canvas">
                <div className="gamma-empty-mockup">
                  <div className="gamma-empty-mockup-card"></div>
                  <div className="gamma-empty-mockup-card"></div>
                  <div className="gamma-empty-mockup-card"></div>
                </div>
                <h3>{allPreviewItems.length > 0 ? 'Tất cả slide hiện đang bị ẩn' : 'Chưa có deck'}</h3>
                <p>
                  {allPreviewItems.length > 0
                    ? 'Tắt bộ lọc ẩn low-confidence để xem lại toàn bộ slide.'
                    : <>Chọn theme, audience, tone, rồi bấm <strong>Tạo deck bằng AI</strong>. Hệ thống sẽ sinh outline trước,
                      sau đó từng slide sẽ hiện dần ở canvas này.</>}
                </p>
              </div>
            )}

            {previewItems.map((item) => {
              const isEditing = editingSlideId === item.id;
              const draft = drafts[item.id];
              const hasContent = (item.bodyBlocks || []).length > 0;

              return (
                <article key={item.id} className={`slide-preview-card gamma-slide-card slide-preview-${normalizeSlideType(item.slideType)} ${item.status?.toLowerCase?.() || ''}`}>
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
                        <button className="button" onClick={() => handleSave(item)}>Lưu slide</button>
                        <button className="button button-secondary" onClick={() => setEditingSlideId(null)}>Hủy</button>
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
                          <strong>{item.quality?.isLowConfidence ? 'Cần rà soát' : 'Chưa có verifier score'}</strong>
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
                          <button className="button button-secondary" onClick={() => handleEdit(item)}>Sửa slide</button>
                        ) : (
                          <button className="button button-secondary" disabled>Đang chờ nội dung</button>
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

export default SlideStudioScreen;
