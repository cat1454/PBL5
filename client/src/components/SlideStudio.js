import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import {
  LuArrowLeft,
  LuCheck,
  LuChevronLeft,
  LuChevronRight,
  LuDownload,
  LuEye,
  LuEyeOff,
  LuFileDown,
  LuFileSearch,
  LuImage,
  LuListTree,
  LuPanelRightClose,
  LuPanelRightOpen,
  LuPencil,
  LuPresentation,
  LuPrinter,
  LuRefreshCw,
  LuSparkles,
  LuX,
} from 'react-icons/lu';
import { documentService, getApiErrorMessage, isApiNotFound, isSlideSchemaUnavailable, slideService } from '../services/api';
import { buildSlideImageViewModel } from '../services/slideImages';
import { normalizeProgressState } from '../services/progress';
import {
  confirmGenerationReadiness,
  getReadinessLabel,
  getReadinessMessage,
  normalizeGenerationReadiness,
} from '../services/generationReadiness';
import { formatUnderstandingConfidence, normalizeDocumentUnderstanding } from '../services/documentUnderstanding';
import { useAnimatedProgress } from '../hooks/useAnimatedProgress';
import { useToast } from './common/ToastProvider';
import { useLanguage } from '../context/LanguageContext';
import DocumentUnderstandingPanel from './DocumentUnderstandingPanel';
import SlideCanvas from './slide-studio/SlideCanvas';
import PropertiesPanel from './slide-studio/PropertiesPanel';
import useSlideEditorAutosave from './slide-studio/useSlideEditorAutosave';
import {
  getBoundedPresentationIndex,
  getNextPresentationIndex,
  getPresentationCanvasScale,
  getPresentationStartIndex,
  isPresentationTextInputTarget,
} from './slide-studio/presentationMode';
import { exportEditablePptx } from '../utils/exportEditablePptx';
import {
  addEditorElement,
  buildSlideFromEditorState,
  createImageElement,
  findEditorElement,
  normalizeEditorState as normalizeSlideEditorState,
  patchEditorCanvas,
  patchEditorElement,
} from './slide-studio/editorState';

const IMPORT_IMAGE_MAX_BYTES = 5 * 1024 * 1024;
const IMPORT_IMAGE_TYPES = new Set(['image/png', 'image/jpeg', 'image/webp', 'image/gif']);
const SLIDE_AUTOSAVE_DEBOUNCE_MS = 400;
const SLIDE_BACKGROUND_PRESETS = ['#111827', '#0f172a', '#1d4ed8', '#f8fafc', '#fff7ed', '#ecfdf5'];

const isHexColor = (value) => /^#(?:[0-9a-f]{3}|[0-9a-f]{6})$/i.test(String(value || '').trim());

const getBackgroundPickerValue = (background) => (
  isHexColor(background) ? String(background).trim() : SLIDE_BACKGROUND_PRESETS[0]
);

const normalizeTextToken = (value) => {
  if (typeof value !== 'string') {
    return '';
  }

  return value.trim().toLowerCase().replace(/[\s-]+/g, '_');
};

const isTextOnlyValue = (value) => normalizeTextToken(value) === 'text_only';

const isTextOnlySlide = (item) => {
  if (!item) {
    return false;
  }

  const visualSlot = item.visualSlot || item.VisualSlot || {};
  const explicitTextOnly = [
    item.visualType,
    item.VisualType,
    item.imageStrategy,
    item.ImageStrategy,
    visualSlot.type,
    visualSlot.Type,
    visualSlot.kind,
    visualSlot.Kind,
  ].some(isTextOnlyValue);

  if (explicitTextOnly) {
    return true;
  }

  return item.imageState?.needsImage === false
    || item.ImageState?.NeedsImage === false
    || item.image?.needsImage === false
    || item.Image?.NeedsImage === false
    || item.slideImage?.needsImage === false
    || item.SlideImage?.NeedsImage === false;
};

const splitBodyText = (text) => text
  .split(/(?<=[.!?。])\s+|[\r\n]+/)
  .map((block) => block.trim())
  .filter(Boolean);

const normalizeBodyBlocks = (bodyBlocks) => {
  if (!bodyBlocks) {
    return [];
  }

  if (Array.isArray(bodyBlocks)) {
    const normalized = bodyBlocks
      .map((block) => (typeof block === 'string' ? block.trim() : String(block ?? '').trim()))
      .filter(Boolean);

    if (normalized.length <= 2 && normalized.some((block) => block.length > 220)) {
      return normalized.flatMap(splitBodyText).filter(Boolean).slice(0, 5);
    }

    return normalized.slice(0, 5);
  }

  if (typeof bodyBlocks === 'string') {
    return splitBodyText(bodyBlocks).slice(0, 5);
  }

  return [];
};

const bodyBlocksToDraftText = (bodyBlocks) => normalizeBodyBlocks(bodyBlocks).join('\n');

const asArray = (value) => (Array.isArray(value) ? value : []);

const getSlideEvidenceDebug = (item) => item?.evidenceDebug || item?.EvidenceDebug || {};

const buildSlideGroundingViewModel = (item, t) => {
  const debug = getSlideEvidenceDebug(item);
  const selectedChunks = asArray(debug.selectedChunks || debug.SelectedChunks);
  const reviewWarnings = asArray(debug.reviewWarnings || debug.ReviewWarnings);
  const suggestedActions = asArray(debug.suggestedActions || debug.SuggestedActions);
  const needsChartReview = Boolean(debug.needsChartReview ?? debug.NeedsChartReview);
  const chartIntent = debug.chartIntent || debug.ChartIntent || '';
  const rhythm = debug.rhythm || debug.Rhythm || '';
  const visualRole = debug.visualRole || debug.VisualRole || '';
  const groundingStatus = debug.groundingStatus || debug.GroundingStatus || (selectedChunks.length ? 'good' : 'unknown');
  const groundingConfidence = debug.groundingConfidence ?? debug.GroundingConfidence;

  return {
    selectedChunks,
    reviewWarnings,
    suggestedActions,
    needsChartReview,
    chartIntent,
    rhythm,
    visualRole,
    groundingStatus,
    groundingConfidence,
    statusLabel: t(`slides.grounding.statuses.${groundingStatus}`) || groundingStatus,
  };
};

const buildSlideBadges = (item, imageVm, t) => {
  const grounding = buildSlideGroundingViewModel(item, t);
  const bodyBlocks = normalizeBodyBlocks(item?.bodyBlocks);
  const badges = [];

  if (grounding.needsChartReview) {
    badges.push({ key: 'chart-review', tone: 'review', label: t('slides.grounding.badges.chartReview') });
  }

  if (item?.quality?.isLowConfidence || grounding.groundingStatus === 'weak' || grounding.reviewWarnings.length > 0) {
    badges.push({ key: 'weak-evidence', tone: 'low', label: t('slides.grounding.badges.weakEvidence') });
  }

  if (imageVm?.needsImage) {
    badges.push({ key: 'image-suggested', tone: 'info', label: t('slides.grounding.badges.imageSuggested') });
  }

  if (grounding.rhythm === 'dense' || bodyBlocks.length >= 4) {
    badges.push({ key: 'dense', tone: 'muted', label: t('slides.grounding.badges.denseSlide') });
  }

  if (!item?.quality?.isLowConfidence && grounding.groundingStatus === 'good') {
    badges.push({ key: 'good-grounding', tone: 'good', label: t('slides.grounding.badges.goodGrounding') });
  }

  return badges.slice(0, 4);
};

function SlideStudio({ documentId: propDocumentId }) {
  const { t, language } = useLanguage();
  const { showToast } = useToast();
  const params = useParams();
  const documentId = propDocumentId || params.documentId;
  const navigate = useNavigate();
  const location = useLocation();
  const slideRefs = useRef({});
  const centerPanelRef = useRef(null);
  const importImageInputRef = useRef(null);
  const deckRef = useRef(null);
  const isMountedRef = useRef(true);
  const [documentMeta, setDocumentMeta] = useState(null);
  const [sourceUnderstanding, setSourceUnderstanding] = useState(null);
  const [generationReadiness, setGenerationReadiness] = useState(null);
  const [deck, setDeck] = useState(null);
  const [progress, setProgress] = useState(null);
  const [jobId, setJobId] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [generationError, setGenerationError] = useState('');
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
  const [canvasMode, setCanvasMode] = useState('preview');
  const [selectedElementId, setSelectedElementId] = useState(null);
  const [layoutDirtySlideIds, setLayoutDirtySlideIds] = useState([]);
  const [layoutSavingSlideId, setLayoutSavingSlideId] = useState(null);
  const [exportingFormat, setExportingFormat] = useState('');
  const [isPresenting, setIsPresenting] = useState(false);
  const [presentSlideIndex, setPresentSlideIndex] = useState(0);
  const [presentationViewport, setPresentationViewport] = useState({
    width: typeof window === 'undefined' ? 1280 : window.innerWidth,
    height: typeof window === 'undefined' ? 720 : window.innerHeight,
  });
  const presentationRequestedFullscreenRef = useRef(false);

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

  useEffect(() => () => {
    isMountedRef.current = false;
  }, []);

  useEffect(() => {
    deckRef.current = deck;
  }, [deck]);

  useEffect(() => {
    if (!briefDirty) {
      setDeckBrief(defaultBrief);
    }
  }, [briefDirty, defaultBrief]);

  const saveEditorState = useCallback(async (slideId, editorState) => {
    const currentDeck = deckRef.current;
    if (!currentDeck || !slideId || !editorState) {
      return null;
    }

    const sourceSlide = currentDeck.items?.find((slide) => slide.id === slideId);
    const savedRevision = Number(editorState?.revision || 0);
    const updated = await slideService.updateSlideItem(currentDeck.id, slideId, {
      editorState,
      accentTone: sourceSlide?.accentTone,
    });

    if (!isMountedRef.current) {
      return updated;
    }

    const latestSlide = deckRef.current?.items?.find((slide) => slide.id === slideId);
    const latestRevision = latestSlide ? normalizeSlideEditorState(latestSlide).revision : savedRevision;
    if (latestRevision <= savedRevision) {
      setDeck((current) => {
        if (!current) {
          return current;
        }

        const nextDeck = {
          ...current,
          items: current.items.map((slide) => (slide.id === slideId ? updated : slide)),
        };
        deckRef.current = nextDeck;
        return nextDeck;
      });
      setLayoutDirtySlideIds((current) => current.filter((dirtySlideId) => dirtySlideId !== slideId));
    }

    return updated;
  }, []);

  const {
    flushSave: flushEditorAutosave,
    scheduleSave: scheduleEditorAutosave,
    statusBySlideId: autosaveStatusBySlideId,
  } = useSlideEditorAutosave({
    debounceMs: SLIDE_AUTOSAVE_DEBOUNCE_MS,
    onSave: saveEditorState,
  });


  const loadDocument = useCallback(async () => {
    try {
      const data = await documentService.getDocument(documentId);
      setDocumentMeta(data);
      setGenerationReadiness(normalizeGenerationReadiness(data?.generationReadiness));
      try {
        const understanding = await documentService.getLatestUnderstanding(documentId);
        setSourceUnderstanding(normalizeDocumentUnderstanding(understanding));
      } catch {
        setSourceUnderstanding(null);
      }
      setDeckBrief((current) => ({
        ...current,
        narrativeGoal: briefDirty
          ? current.narrativeGoal
          : data?.summary || current.narrativeGoal,
      }));
    } catch (err) {
      console.error(err);
      setError(getApiErrorMessage(err, t('slides.errors.loadDocument')));
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
        const rawProgress = data.generationProgress;
        setProgress((current) => normalizeProgressState(rawProgress, current || {}));
        setJobId(rawProgress.jobId || rawProgress.JobId || jobId);
      }
    } catch (err) {
      console.error(err);
      setError(isSlideSchemaUnavailable(err)
        ? t('slides.errors.schemaUnavailable')
        : getApiErrorMessage(err, t('slides.errors.loadDeck')));
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
          const rawProgress = await slideService.getGenerateProgress(jobId);
          const nextProgress = normalizeProgressState(rawProgress);
          setProgress((current) => normalizeProgressState(rawProgress, current || {}));
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
        if (isApiNotFound(err)) {
          await loadDeck({ silent: true });
          setJobId(null);
          setGenerationError('');
        } else {
          setGenerationError(getApiErrorMessage(err, t('slides.generationStatus.pollFailed')));
        }
      }
    }, 1500);

    return () => clearInterval(interval);
  }, [deck, isGenerating, jobId, loadDeck, t]);

  const handleGenerate = async () => {
    try {
      setError('');
      setGenerationError('');
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
      setJobId(response.jobId);
      setProgress(normalizeProgressState(response.progress, {
        jobId: response.jobId,
        status: response.status,
        percent: 0,
        stageLabel: 'Queued',
        message: t('slides.feedback.jobCreated'),
      }));
      showToast({
        type: 'info',
        message: t('slides.feedback.jobCreated'),
        description: t('slides.feedback.generating'),
      });
      await loadDeck({ silent: true });
    } catch (err) {
      console.error(err);
      setError(isSlideSchemaUnavailable(err)
        ? t('slides.errors.schemaUnavailable')
        : getApiErrorMessage(err, t('slides.errors.generate')));
    }
  };

  const handleEdit = useCallback((item) => {
    setCanvasMode('text');
    setEditingSlideId(null);
    setSelectedSlideId(item.id);
    setIsInspectorOpen(true);
    const editorState = normalizeSlideEditorState(item);
    const firstTextElement = editorState?.elements?.find((element) => element.type === 'text');
    setSelectedElementId(firstTextElement?.id || null);
    setDrafts((current) => ({
      ...current,
      [item.id]: {
        heading: item.heading || '',
        subheading: item.subheading || '',
        goal: item.goal || '',
        bodyText: bodyBlocksToDraftText(item.bodyBlocks),
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
      showToast({
        type: 'success',
        message: t('slides.feedback.saved'),
      });
    } catch (err) {
      console.error(err);
      setError(getApiErrorMessage(err, t('slides.errors.save')));
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
      showToast({
        type: 'success',
        message: t('slides.feedback.refreshed', { index: item.slideIndex }),
      });
    } catch (err) {
      console.error(err);
      setError(getApiErrorMessage(err, t('slides.errors.refreshImages')));
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
      showToast({
        type: 'success',
        message: t('slides.feedback.selectedImage', { index: item.slideIndex }),
      });
    } catch (err) {
      console.error(err);
      setError(getApiErrorMessage(err, t('slides.errors.selectImage')));
    } finally {
      setMediaBusySlideId(null);
    }
  };

  const handleBack = useCallback(() => {
    const fromPath = location.state?.fromPath;
    const fromWorkspaceId = location.state?.fromWorkspaceId || documentMeta?.workspaceId || documentMeta?.folderProjectId;

    if (fromPath) {
      navigate(fromPath);
      return;
    }

    if (fromWorkspaceId) {
      navigate(`/workspaces/${fromWorkspaceId}`);
      return;
    }

    navigate('/workspaces');
  }, [documentMeta?.folderProjectId, documentMeta?.workspaceId, location.state, navigate]);

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

  const getCanvasScale = (zoomValue) => {
    switch (zoomValue) {
      case '75':
        return 0.75;
      case '100':
        return 1;
      case 'fit':
      default:
        return 0.675;
    }
  };

  const canGenerate = documentMeta?.status === 3;
  const outlineSlides = deck?.outline?.slides || [];
  const activeProgress = progress || (deck?.generationProgress ? normalizeProgressState(deck.generationProgress) : null);
  const activeProgressPercent = Math.max(0, Math.min(100, Number(activeProgress?.percent || 0)));
  const displayedProgressPercent = useAnimatedProgress(activeProgressPercent);
  const themeMeta = getThemeMeta(deckBrief.themeKey);
  const allPreviewItems = deck?.items || [];
  const previewItems = hideLowConfidence
    ? allPreviewItems.filter((item) => !item.quality?.isLowConfidence)
    : allPreviewItems;
  const completedSlides = allPreviewItems.filter((item) => item.status === 'Completed').length;
  const slidesWithSelectedMedia = allPreviewItems.filter((item) => buildSlideImageViewModel(item, t).selectedImage).length;
  const lowConfidenceCount = deck?.qualitySummary?.lowConfidenceCount
    ?? allPreviewItems.filter((item) => item.quality?.isLowConfidence).length;
  const readinessMessage = getReadinessMessage(generationReadiness, language);
  const canPresent = previewItems.length > 0 && !isGenerating;

  useEffect(() => {
    if (!previewItems.length) {
      setSelectedSlideId(null);
      setIsPresenting(false);
      return;
    }

    const stillVisible = previewItems.some((item) => item.id === selectedSlideId);
    if (!stillVisible) {
      setSelectedSlideId(previewItems[0].id);
    }
  }, [previewItems, selectedSlideId]);

  useEffect(() => {
    if (!isPresenting) {
      return;
    }

    setPresentSlideIndex((current) => getBoundedPresentationIndex(current, previewItems.length));
    if (!previewItems.length) {
      setIsPresenting(false);
    }
  }, [isPresenting, previewItems.length]);

  useEffect(() => {
    if (!selectedSlideId || !centerPanelRef.current) {
      return;
    }

    centerPanelRef.current.scrollTo({
      top: 0,
      behavior: 'smooth',
    });
  }, [selectedSlideId]);

  const closePresentation = useCallback(() => {
    setIsPresenting(false);
    if (
      presentationRequestedFullscreenRef.current
      && document.fullscreenElement
      && typeof document.exitFullscreen === 'function'
    ) {
      document.exitFullscreen().catch(() => {});
    }
    presentationRequestedFullscreenRef.current = false;
  }, []);

  const movePresentation = useCallback((direction) => {
    setPresentSlideIndex((current) => getNextPresentationIndex(current, previewItems.length, direction));
  }, [previewItems.length]);

  const handleOpenPresentation = useCallback(() => {
    if (!previewItems.length) {
      return;
    }

    setPresentSlideIndex(getPresentationStartIndex(previewItems, selectedSlideId));
    setIsPresenting(true);

    if (
      !document.fullscreenElement
      && document.documentElement
      && typeof document.documentElement.requestFullscreen === 'function'
    ) {
      document.documentElement.requestFullscreen()
        .then(() => {
          presentationRequestedFullscreenRef.current = true;
        })
        .catch(() => {
          presentationRequestedFullscreenRef.current = false;
          showToast({
            type: 'info',
            message: t('slides.feedback.presentationFullscreenFallback'),
          });
        });
    }
  }, [previewItems, selectedSlideId, showToast, t]);

  useEffect(() => {
    if (!isPresenting) {
      return undefined;
    }

    const handleKeyDown = (event) => {
      if (isPresentationTextInputTarget(event.target)) {
        return;
      }

      if (event.key === 'Escape') {
        event.preventDefault();
        closePresentation();
        return;
      }

      if (event.key === 'ArrowLeft') {
        event.preventDefault();
        movePresentation(-1);
        return;
      }

      if (event.key === 'ArrowRight' || event.key === ' ' || event.key === 'Enter') {
        event.preventDefault();
        movePresentation(1);
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [closePresentation, isPresenting, movePresentation]);

  useEffect(() => {
    if (!isPresenting) {
      return undefined;
    }

    const handleResize = () => {
      setPresentationViewport({
        width: window.innerWidth,
        height: window.innerHeight,
      });
    };

    handleResize();
    window.addEventListener('resize', handleResize);
    return () => window.removeEventListener('resize', handleResize);
  }, [isPresenting]);

  if (loading) {
    return (
      <div className="loading">
        <div className="spinner"></div>
        <p>{t('slides.loading')}</p>
      </div>
    );
  }

  const selectedSlide = previewItems.find((item) => item.id === selectedSlideId) || null;
  const selectedImageVm = selectedSlide ? buildSlideImageViewModel(selectedSlide, t) : null;
  const selectedSlideIsTextOnly = isTextOnlySlide(selectedSlide) || selectedImageVm?.needsImage === false;
  const selectedSlideNeedsMedia = !selectedSlideIsTextOnly && selectedImageVm?.needsImage !== false;
  const selectedSlideDraft = selectedSlide ? drafts[selectedSlide.id] : null;
  const isEditingSelectedSlide = selectedSlide && editingSlideId === selectedSlide.id;
  const isExportDisabled = !deck || isGenerating || Boolean(exportingFormat);
  const selectedSlideGrounding = selectedSlide ? buildSlideGroundingViewModel(selectedSlide, t) : null;
  const selectedSlideBadges = selectedSlide ? buildSlideBadges(selectedSlide, selectedImageVm, t) : [];
  const sourceReviewHints = sourceUnderstanding?.presentation?.uxReviewHints || [];
  const highSeveritySourceHints = sourceReviewHints.filter((hint) => String(hint.severity || '').toLowerCase() === 'high');
  const selectedEditorState = selectedSlide ? normalizeSlideEditorState(selectedSlide) : null;
  const selectedElement = selectedEditorState ? findEditorElement(selectedEditorState, selectedElementId) : null;
  const presentationIndex = getBoundedPresentationIndex(presentSlideIndex, previewItems.length);
  const presentationSlide = isPresenting ? previewItems[presentationIndex] : null;
  const presentationEditorState = presentationSlide ? normalizeSlideEditorState(presentationSlide) : null;
  const presentationImageVm = presentationSlide ? buildSlideImageViewModel(presentationSlide, t) : null;
  const presentationSlideIsTextOnly = isTextOnlySlide(presentationSlide) || presentationImageVm?.needsImage === false;
  const presentationCanvasScale = presentationEditorState
    ? getPresentationCanvasScale({
      viewportWidth: presentationViewport.width,
      viewportHeight: presentationViewport.height,
      canvasWidth: presentationEditorState.canvas.width,
      canvasHeight: presentationEditorState.canvas.height,
    })
    : 1;
  const isLayoutEditMode = canvasMode === 'layout';
  const isTextEditMode = canvasMode === 'text';
  const isLayoutDirty = selectedSlide ? layoutDirtySlideIds.includes(selectedSlide.id) : false;
  const isSavingLayout = selectedSlide ? layoutSavingSlideId === selectedSlide.id : false;
  const selectedAutosaveStatus = selectedSlide ? autosaveStatusBySlideId[selectedSlide.id] : null;
  const selectedBackgroundValue = getBackgroundPickerValue(selectedEditorState?.canvas?.background);
  const canvasScale = getCanvasScale(canvasZoom);
  const canvasLabels = {
    emptyText: t('slides.canvas.emptyText'),
    imageAlt: selectedSlide?.heading || t('slides.canvas.imageAlt'),
    imagePlaceholderTitle: t('slides.canvas.imagePlaceholderTitle'),
    imagePlaceholderBody: t('slides.canvas.imagePlaceholderBody'),
  };
  const propertyLabels = {
    title: t('slides.canvas.propertiesTitle'),
    empty: t('slides.canvas.propertiesEmpty'),
    text: t('slides.canvas.text'),
    fontSize: t('slides.canvas.fontSize'),
    color: t('slides.canvas.color'),
    backgroundTitle: t('slides.canvas.backgroundTitle'),
    backgroundColor: t('slides.canvas.backgroundColor'),
    backgroundPresets: t('slides.canvas.backgroundPresets'),
    autosaveStatus: {
      dirty: t('slides.editorAutosave.dirty'),
      saving: t('slides.editorAutosave.saving'),
      saved: t('slides.editorAutosave.saved'),
      error: t('slides.editorAutosave.error'),
    },
    style: t('slides.canvas.style'),
    bold: t('slides.canvas.bold'),
    alignLeft: t('slides.canvas.alignLeft'),
    alignCenter: t('slides.canvas.alignCenter'),
    alignRight: t('slides.canvas.alignRight'),
    effect: t('slides.canvas.effect'),
    effectPresets: {
      none: t('slides.canvas.effects.none'),
      'soft-shadow': t('slides.canvas.effects.softShadow'),
      'neon-glow': t('slides.canvas.effects.neonGlow'),
      'glass-frame': t('slides.canvas.effects.glassFrame'),
      'paper-cut': t('slides.canvas.effects.paperCut'),
      duotone: t('slides.canvas.effects.duotone'),
    },
    lock: t('slides.canvas.lock'),
    unlock: t('slides.canvas.unlock'),
    roles: {
      title: t('slides.canvas.roles.title'),
      subtitle: t('slides.canvas.roles.subtitle'),
      goal: t('slides.canvas.roles.goal'),
      body: t('slides.canvas.roles.body'),
      notes: t('slides.canvas.roles.notes'),
      image: t('slides.canvas.roles.image'),
    },
  };

  const handleSelectSlide = (item) => {
    if (selectedSlide && selectedEditorState && layoutDirtySlideIds.includes(selectedSlide.id)) {
      flushEditorAutosave(selectedSlide.id, selectedEditorState).catch(() => {});
    }
    setSelectedSlideId(item.id);
    setSelectedElementId(null);
    if (slideRefs.current[item.id]) {
      slideRefs.current[item.id].scrollIntoView({ behavior: 'smooth', inline: 'center', block: 'nearest' });
    }
  };

  const handleSetCanvasMode = (mode) => {
    if (selectedSlide && selectedEditorState && mode === 'preview' && layoutDirtySlideIds.includes(selectedSlide.id)) {
      flushEditorAutosave(selectedSlide.id, selectedEditorState).catch(() => {});
    }
    setCanvasMode(mode);
    if (mode === 'layout' && selectedSlide) {
      setEditingSlideId(null);
      setIsInspectorOpen(true);
      const firstElement = selectedEditorState?.elements?.[0];
      setSelectedElementId((current) => current || firstElement?.id || null);
    } else if (mode === 'text' && selectedSlide) {
      setEditingSlideId(null);
      setIsInspectorOpen(true);
      const firstTextElement = selectedEditorState?.elements?.find((element) => element.type === 'text');
      setSelectedElementId((current) => current || firstTextElement?.id || null);
    } else {
      setEditingSlideId(null);
      setSelectedElementId(null);
    }
  };

  const handlePatchElement = (elementId, patch) => {
    if (!selectedSlide || !selectedEditorState) {
      return;
    }

    const nextEditorState = patchEditorElement(selectedEditorState, elementId, patch);
    const nextSlide = buildSlideFromEditorState(selectedSlide, nextEditorState);

    setDeck((current) => {
      if (!current) {
        return current;
      }

      const nextDeck = {
        ...current,
        items: current.items.map((slide) => (slide.id === selectedSlide.id ? nextSlide : slide)),
      };
      deckRef.current = nextDeck;
      return nextDeck;
    });
    setSelectedElementId(elementId);
    setLayoutDirtySlideIds((current) => (
      current.includes(selectedSlide.id) ? current : [...current, selectedSlide.id]
    ));
    scheduleEditorAutosave(selectedSlide.id, nextEditorState);
  };

  const handlePatchCanvas = (patch) => {
    if (!selectedSlide || !selectedEditorState) {
      return;
    }

    const nextEditorState = patchEditorCanvas(selectedEditorState, patch);
    const nextSlide = buildSlideFromEditorState(selectedSlide, nextEditorState);

    setDeck((current) => {
      if (!current) {
        return current;
      }

      const nextDeck = {
        ...current,
        items: current.items.map((slide) => (slide.id === selectedSlide.id ? nextSlide : slide)),
      };
      deckRef.current = nextDeck;
      return nextDeck;
    });
    setLayoutDirtySlideIds((current) => (
      current.includes(selectedSlide.id) ? current : [...current, selectedSlide.id]
    ));
    scheduleEditorAutosave(selectedSlide.id, nextEditorState);
  };

  const handleImportImageClick = () => {
    if (!selectedSlide || (!isLayoutEditMode && !isTextEditMode)) {
      return;
    }

    importImageInputRef.current?.click();
  };

  const readImportedImage = (file) => new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result || ''));
    reader.onerror = () => reject(reader.error || new Error('Could not read image.'));
    reader.readAsDataURL(file);
  });

  const handleImportImageChange = async (event) => {
    const file = event.target.files?.[0];
    event.target.value = '';

    if (!file || !selectedSlide || !selectedEditorState) {
      return;
    }

    if (!IMPORT_IMAGE_TYPES.has(file.type)) {
      showToast({ type: 'error', message: t('slides.errors.importImageType') });
      return;
    }

    if (file.size > IMPORT_IMAGE_MAX_BYTES) {
      showToast({ type: 'error', message: t('slides.errors.importImageTooLarge') });
      return;
    }

    try {
      const src = await readImportedImage(file);
      const element = createImageElement(selectedEditorState, { src, name: file.name });
      const nextEditorState = addEditorElement(selectedEditorState, element);
      const nextSlide = buildSlideFromEditorState(selectedSlide, nextEditorState);

      setDeck((current) => {
        if (!current) {
          return current;
        }

        const nextDeck = {
          ...current,
          items: current.items.map((slide) => (slide.id === selectedSlide.id ? nextSlide : slide)),
        };
        deckRef.current = nextDeck;
        return nextDeck;
      });
      setCanvasMode('layout');
      setIsInspectorOpen(true);
      setSelectedElementId(element.id);
      setLayoutDirtySlideIds((current) => (
        current.includes(selectedSlide.id) ? current : [...current, selectedSlide.id]
      ));
      scheduleEditorAutosave(selectedSlide.id, nextEditorState);
      showToast({
        type: 'success',
        message: t('slides.feedback.imageImported'),
        description: file.name,
      });
    } catch (err) {
      console.error(err);
      const message = t('slides.errors.importImageFailed');
      setError(message);
      showToast({ type: 'error', message });
    }
  };

  const handleSaveLayout = async () => {
    if (!deck || !selectedSlide || !selectedEditorState) {
      return;
    }

    try {
      setLayoutSavingSlideId(selectedSlide.id);
      await flushEditorAutosave(selectedSlide.id, selectedEditorState);
      showToast({
        type: 'success',
        message: t('slides.feedback.layoutSaved'),
      });
    } catch (err) {
      console.error(err);
      setError(getApiErrorMessage(err, t('slides.errors.saveLayout')));
    } finally {
      setLayoutSavingSlideId(null);
    }
  };

  const handleGroundingQuickAction = (action) => {
    if (!selectedSlide) {
      return;
    }

    handleEdit(selectedSlide);
    showToast({
      type: 'info',
      message: t('slides.grounding.quickActionToast'),
      description: t(`slides.grounding.actions.${action}`) || action,
    });
  };

  const handleDownloadHtml = async () => {
    if (!deck || isExportDisabled) {
      return;
    }

    try {
      setExportingFormat('html');
      const result = await slideService.exportDeckHtml(deck.id);
      showToast({
        type: 'success',
        message: t('slides.feedback.htmlExported'),
        description: result.filename,
      });
    } catch (err) {
      console.error(err);
      const message = getApiErrorMessage(err, t('slides.errors.exportFailed'));
      setError(message);
      showToast({ type: 'error', message });
    } finally {
      setExportingFormat('');
    }
  };

  const handleOpenPrint = async () => {
    if (!deck || isExportDisabled) {
      return;
    }

    const printWindow = window.open('', '_blank');
    if (!printWindow) {
      showToast({ type: 'error', message: t('slides.errors.printBlocked') });
      return;
    }
    printWindow.opener = null;

    try {
      setExportingFormat('print');
      const blob = await slideService.getDeckPrintHtml(deck.id);
      const url = window.URL.createObjectURL(blob);
      printWindow.location.href = url;
      window.setTimeout(() => window.URL.revokeObjectURL(url), 60000);
      showToast({ type: 'success', message: t('slides.feedback.printOpened') });
    } catch (err) {
      console.error(err);
      printWindow.close();
      const message = getApiErrorMessage(err, t('slides.errors.exportFailed'));
      setError(message);
      showToast({ type: 'error', message });
    } finally {
      setExportingFormat('');
    }
  };

  const handleDownloadPptx = async () => {
    if (!deck || isExportDisabled) {
      return;
    }

    try {
      setExportingFormat('pptx');
      const result = await exportEditablePptx({ deck, documentMeta, t });
      showToast({
        type: 'success',
        message: t('slides.feedback.pptxExported'),
        description: result.skippedImages > 0
          ? t('slides.feedback.pptxExportedWithSkippedImages', { count: result.skippedImages })
          : result.filename,
      });
    } catch (err) {
      console.error(err);
      const message = getApiErrorMessage(err, t('slides.errors.exportFailed'));
      setError(message);
      showToast({ type: 'error', message });
    } finally {
      setExportingFormat('');
    }
  };

  return (
    <div className={`slide-studio gamma-studio theme-${themeMeta.key}`}>
      <input
        ref={importImageInputRef}
        type="file"
        className="slide-import-image-input"
        accept="image/png,image/jpeg,image/webp,image/gif"
        onChange={handleImportImageChange}
      />
      <section className="studio-header-bar card">
        <div className="studio-header-main">
          <button className="button button-secondary studio-back-button" onClick={handleBack}>
            <LuArrowLeft aria-hidden="true" />
            <span>{t('slides.back')}</span>
          </button>
          <div className="studio-title-stack">
            <span className="studio-kicker">{t('slides.eyebrow')}</span>
            <h2>{deck?.title || documentMeta?.fileName || t('slides.heroFallbackTitle')}</h2>
            <p>
              {documentMeta?.fileName || t('slides.noData')}
              <span className="studio-inline-dot" aria-hidden="true" />
              {completedSlides}/{allPreviewItems.length || desiredSlideCount} {t('slides.slideUnit')}
              <span className="studio-inline-dot" aria-hidden="true" />
              {getProgressStageLabel(activeProgress)}
            </p>
          </div>
        </div>

        <div className="studio-header-actions">
          <button className="button button-secondary studio-action-button" onClick={() => setIsInspectorOpen((current) => !current)} title={isInspectorOpen ? t('slides.hideInspector') : t('slides.showInspector')}>
            {isInspectorOpen ? <LuPanelRightClose aria-hidden="true" /> : <LuPanelRightOpen aria-hidden="true" />}
            <span>{isInspectorOpen ? t('slides.hideInspector') : t('slides.showInspector')}</span>
          </button>
          <button className="button button-secondary studio-action-button" onClick={() => setHideLowConfidence((current) => !current)} title={hideLowConfidence ? t('slides.showAllSlides') : t('slides.hideLowConfidence')}>
            {hideLowConfidence ? <LuEye aria-hidden="true" /> : <LuEyeOff aria-hidden="true" />}
            <span>{hideLowConfidence ? t('slides.showAllSlides') : t('slides.hideLowConfidence')}</span>
          </button>
          <button className="button button-secondary studio-action-button" onClick={handleOpenPresentation} disabled={!canPresent} title={t('slides.present')}>
            <LuPresentation aria-hidden="true" />
            <span>{t('slides.present')}</span>
          </button>
          <button className="button button-secondary studio-action-button" onClick={handleDownloadHtml} disabled={isExportDisabled} title={t('slides.downloadHtml')}>
            <LuDownload aria-hidden="true" />
            <span>{exportingFormat === 'html' ? t('slides.exportingHtml') : t('slides.downloadHtml')}</span>
          </button>
          <button className="button button-secondary studio-action-button" onClick={handleOpenPrint} disabled={isExportDisabled} title={t('slides.printPdf')}>
            <LuPrinter aria-hidden="true" />
            <span>{exportingFormat === 'print' ? t('slides.openingPrint') : t('slides.printPdf')}</span>
          </button>
          <button className="button button-secondary studio-action-button" onClick={handleDownloadPptx} disabled={isExportDisabled} title={t('slides.downloadPptx')}>
            <LuFileDown aria-hidden="true" />
            <span>{exportingFormat === 'pptx' ? t('slides.exportingPptx') : t('slides.downloadPptx')}</span>
          </button>
          <button className="button studio-action-button studio-primary-action" onClick={handleGenerate} disabled={!canGenerate || isGenerating}>
            <LuSparkles aria-hidden="true" />
            <span>
              {isGenerating
                ? t('slides.generating', { percent: Math.round(activeProgressPercent) })
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

      <div className={`studio-workspace${isInspectorOpen ? ' inspector-open' : ' inspector-closed'}`}>
        <aside className="studio-left-panel">
          <section className="card studio-navigator-card">
            <div className="studio-tabs" role="tablist" aria-label={t('slides.navigationTabs')}>
              <button
                type="button"
                role="tab"
                className={`studio-tab${activeLeftTab === 'outline' ? ' active' : ''}`}
                onClick={() => setActiveLeftTab('outline')}
                aria-selected={activeLeftTab === 'outline'}
              >
                <LuListTree aria-hidden="true" />
                <span>{t('slides.outlineTab')}</span>
              </button>
              <button
                type="button"
                role="tab"
                className={`studio-tab${activeLeftTab === 'source' ? ' active' : ''}`}
                onClick={() => setActiveLeftTab('source')}
                aria-selected={activeLeftTab === 'source'}
              >
                <LuFileSearch aria-hidden="true" />
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
                      const matchedItem = previewItems.find((item) => item.slideIndex === slide.slideIndex);
                      const itemBadges = matchedItem ? buildSlideBadges(matchedItem, buildSlideImageViewModel(matchedItem, t), t) : [];
                      return (
                        <button
                          key={`${slide.slideIndex}-${slide.heading}`}
                          type="button"
                          className={`studio-outline-item${isActive ? ' active' : ''}`}
                          onClick={() => {
                            if (matchedItem) {
                              handleSelectSlide(matchedItem);
                            }
                          }}
                        >
                          <span className="studio-outline-number">{slide.slideIndex}</span>
                          <span className="studio-outline-copy">
                            <strong>{slide.heading}</strong>
                            <small>{slide.goal || getSlideTypeLabel(slide.slideType)}</small>
                            {itemBadges.length > 0 && (
                              <span className="studio-grounding-badges">
                                {itemBadges.slice(0, 2).map((badge) => (
                                  <span key={badge.key} className={`studio-grounding-badge tone-${badge.tone}`}>{badge.label}</span>
                                ))}
                              </span>
                            )}
                          </span>
                          <LuChevronRight aria-hidden="true" />
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
                  {generationReadiness && (
                    <span className={`generation-readiness-badge tone-${generationReadiness.tone}`}>
                      {getReadinessLabel(generationReadiness, language)}
                    </span>
                  )}
                </div>

                {readinessMessage && (
                  <div className={`studio-source-card generation-readiness-card tone-${generationReadiness.tone}`}>
                    <span className="studio-source-label">{readinessMessage.title}</span>
                    <p>{readinessMessage.body}</p>
                  </div>
                )}

                {highSeveritySourceHints.length > 0 && (
                  <div className="studio-source-card generation-readiness-card tone-review">
                    <span className="studio-source-label">{t('slides.grounding.sourceWarningTitle')}</span>
                    <p>{t('slides.grounding.sourceWarningBody', { count: highSeveritySourceHints.length })}</p>
                    <small>{highSeveritySourceHints.slice(0, 2).map((hint) => hint.message).join(' | ')}</small>
                  </div>
                )}

                <DocumentUnderstandingPanel
                  documentId={documentId}
                  className="studio-source-card"
                  showEmpty
                  compact
                />

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
                  <div className={`studio-progress-card${isGenerating ? ' is-active' : ''}`}>
                    <div className="studio-progress-head">
                      <strong>{getProgressStageLabel(activeProgress)}</strong>
                      <span>{Math.round(activeProgressPercent)}%</span>
                    </div>
                    <div className="generation-progress-bar">
                      <div className="generation-progress-fill" style={{ width: `${Math.max(0, Math.min(100, displayedProgressPercent))}%` }}></div>
                    </div>
                    <p>{formatEta(activeProgress.estimatedRemainingSeconds)}</p>
                  </div>
                )}
              </div>
            )}
          </section>
        </aside>

        <section className="studio-canvas-column" ref={centerPanelRef}>
          <section className="card studio-canvas-toolbar">
            <div>
              <span className="studio-kicker">{t('slides.previewCanvas')}</span>
              <h3>{selectedSlide?.heading || deck?.title || t('slides.previewFallbackTitle')}</h3>
              <p>{selectedSlide?.subheading || deck?.subtitle || deckBrief.narrativeGoal}</p>
            </div>

            <div className="studio-toolbar-actions">
              <div className="studio-mode-toggle" role="group" aria-label={t('slides.canvas.modeLabel')}>
                <button
                  type="button"
                  className={`studio-zoom-button${canvasMode === 'preview' ? ' active' : ''}`}
                  onClick={() => handleSetCanvasMode('preview')}
                >
                  {t('slides.canvas.previewMode')}
                </button>
                <button
                  type="button"
                  className={`studio-zoom-button${isTextEditMode ? ' active' : ''}`}
                  onClick={() => handleSetCanvasMode('text')}
                  disabled={!selectedSlide}
                >
                  {t('slides.editSlide')}
                </button>
                <button
                  type="button"
                  className={`studio-zoom-button${isLayoutEditMode ? ' active' : ''}`}
                  onClick={() => handleSetCanvasMode('layout')}
                  disabled={!selectedSlide}
                >
                  {t('slides.canvas.layoutMode')}
                </button>
              </div>
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
              <button
                type="button"
                className="button button-secondary"
                onClick={handleImportImageClick}
                disabled={!selectedSlide || (!isLayoutEditMode && !isTextEditMode)}
                title={t('slides.canvas.importImage')}
              >
                <LuImage aria-hidden="true" />
                <span>{t('slides.canvas.importImage')}</span>
              </button>
              {selectedSlide && (
                <>
                  {(isLayoutEditMode || isTextEditMode) && (
                    <>
                      <span className={`slide-autosave-status status-${selectedAutosaveStatus || 'idle'}`}>
                        {selectedAutosaveStatus
                          ? propertyLabels.autosaveStatus[selectedAutosaveStatus]
                          : propertyLabels.autosaveStatus.saved}
                      </span>
                      <button
                        className="button"
                        onClick={handleSaveLayout}
                        disabled={isSavingLayout || !isLayoutDirty}
                      >
                        <LuCheck aria-hidden="true" />
                        <span>{isSavingLayout ? t('slides.canvas.savingLayout') : t('slides.canvas.saveLayout')}</span>
                      </button>
                    </>
                  )}
                  <button className="button button-secondary" onClick={() => handleEdit(selectedSlide)}>
                    <LuPencil aria-hidden="true" />
                    <span>{t('slides.editSlide')}</span>
                  </button>
                </>
              )}
            </div>
          </section>

          <div className="studio-canvas-body">
            {activeProgress && isGenerating && (
              <div className="studio-canvas-progress-shell">
                <div className="studio-progress-card studio-progress-card-large is-active">
                  <div className="studio-progress-head">
                    <strong>{getProgressStageLabel(activeProgress)}</strong>
                    <span>{Math.round(activeProgressPercent)}%</span>
                  </div>
                  <div className="generation-progress-bar">
                    <div className="generation-progress-fill" style={{ width: `${Math.max(0, Math.min(100, displayedProgressPercent))}%` }}></div>
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
                <article className={`studio-slide-frame slide-preview-${normalizeSlideType(selectedSlide.slideType)}${selectedSlideIsTextOnly ? ' text-only-slide' : ''} layout-edit-slide`}>
                  <div className="studio-slide-meta">
                    <span>{t('slides.slideLabel', { index: selectedSlide.slideIndex })}</span>
                    <div className="quality-toolbar">
                      <span>{getSlideTypeLabel(selectedSlide.slideType)}</span>
                      {selectedSlide.quality?.score !== undefined && selectedSlide.quality?.score !== null && (
                        <span className={`quality-chip ${selectedSlide.quality?.isLowConfidence ? 'low' : 'good'}`}>
                          {selectedSlide.quality.score}/100
                        </span>
                      )}
                      {selectedSlideBadges.map((badge) => (
                        <span key={badge.key} className={`studio-grounding-badge tone-${badge.tone}`}>{badge.label}</span>
                      ))}
                    </div>
                  </div>

                  <SlideCanvas
                    editorState={selectedEditorState}
                    imageVm={selectedImageVm}
                    labels={canvasLabels}
                    mode={isLayoutEditMode ? 'layout' : isTextEditMode ? 'text' : 'preview'}
                    scale={canvasScale}
                    selectedElementId={isLayoutEditMode ? selectedElementId : null}
                    onPatchElement={handlePatchElement}
                    onSelectElement={isLayoutEditMode ? setSelectedElementId : undefined}
                  />

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
                <h3>{allPreviewItems.length > 0 ? t('slides.slideNotFoundTitle') : t('slides.noDeckTitle')}</h3>
                <p>{allPreviewItems.length > 0 ? t('slides.slideNotFoundBody') : t('slides.noDeckBody')}</p>
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
                    <span className="studio-grounding-badges">
                      {buildSlideBadges(item, buildSlideImageViewModel(item, t), t).slice(0, 2).map((badge) => (
                        <span key={badge.key} className={`studio-grounding-badge tone-${badge.tone}`}>{badge.label}</span>
                      ))}
                    </span>
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
                <LuX aria-hidden="true" />
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
                        <LuCheck aria-hidden="true" />
                        <span>{t('slides.saveSlide')}</span>
                      </button>
                      <button className="button button-secondary" onClick={handleCancelEdit}>
                        <LuX aria-hidden="true" />
                        <span>{t('slides.cancel')}</span>
                      </button>
                    </div>
                  </div>
                ) : (
                  <div className="studio-inspector-stack">
                    {(isLayoutEditMode || isTextEditMode) && selectedEditorState && (
                      <div className="studio-inspector-block slide-background-panel">
                        <div className="studio-inspector-block-head">
                          <div>
                            <span className="studio-kicker">{propertyLabels.backgroundTitle}</span>
                            <strong>{propertyLabels.backgroundColor}</strong>
                          </div>
                          <input
                            type="color"
                            value={selectedBackgroundValue}
                            onChange={(event) => handlePatchCanvas({ background: event.target.value })}
                            aria-label={propertyLabels.backgroundColor}
                            title={propertyLabels.backgroundColor}
                          />
                        </div>
                        <div className="slide-background-swatches" role="group" aria-label={propertyLabels.backgroundPresets}>
                          {SLIDE_BACKGROUND_PRESETS.map((color) => (
                            <button
                              key={color}
                              type="button"
                              className={`slide-background-swatch${selectedBackgroundValue.toLowerCase() === color.toLowerCase() ? ' active' : ''}`}
                              style={{ background: color }}
                              onClick={() => handlePatchCanvas({ background: color })}
                              aria-label={`${propertyLabels.backgroundColor} ${color}`}
                              title={color}
                            />
                          ))}
                        </div>
                      </div>
                    )}

                    {isLayoutEditMode && (
                      <PropertiesPanel
                        element={selectedElement}
                        labels={propertyLabels}
                        onPatch={handlePatchElement}
                      />
                    )}

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

                    {selectedSlideGrounding && (
                      <div className="studio-inspector-block studio-grounding-inspector">
                        <div className="studio-inspector-block-head">
                          <strong>{t('slides.grounding.title')}</strong>
                          <span className={`studio-grounding-badge tone-${selectedSlideGrounding.groundingStatus === 'good' ? 'good' : selectedSlideGrounding.groundingStatus === 'weak' ? 'low' : 'review'}`}>
                            {selectedSlideGrounding.statusLabel}
                          </span>
                        </div>
                        <div className="studio-inspector-meta-grid">
                          <div className="studio-source-meta">
                            <span>{t('slides.grounding.rhythm')}</span>
                            <strong>{selectedSlideGrounding.rhythm || t('slides.noData')}</strong>
                          </div>
                          <div className="studio-source-meta">
                            <span>{t('slides.grounding.visualRole')}</span>
                            <strong>{selectedSlideGrounding.visualRole || t('slides.noData')}</strong>
                          </div>
                          <div className="studio-source-meta">
                            <span>{t('slides.grounding.chartStatus')}</span>
                            <strong>{selectedSlideGrounding.needsChartReview ? t('slides.grounding.chartNeedsReview') : (selectedSlideGrounding.chartIntent || t('slides.noData'))}</strong>
                          </div>
                          <div className="studio-source-meta">
                            <span>{t('slides.grounding.confidence')}</span>
                            <strong>{formatUnderstandingConfidence(selectedSlideGrounding.groundingConfidence, t('slides.noData'))}</strong>
                          </div>
                        </div>
                        {selectedSlideGrounding.selectedChunks.length > 0 && (
                          <div className="studio-grounding-chunks">
                            {selectedSlideGrounding.selectedChunks.slice(0, 4).map((chunk) => (
                              <span key={chunk.chunkId || chunk.ChunkId}>
                                {(chunk.chunkId || chunk.ChunkId)} · {(chunk.classification || chunk.Classification)}
                              </span>
                            ))}
                          </div>
                        )}
                        {selectedSlideGrounding.reviewWarnings.length > 0 && (
                          <ul className="quality-issues">
                            {selectedSlideGrounding.reviewWarnings.slice(0, 3).map((warning) => (
                              <li key={warning}>{warning}</li>
                            ))}
                          </ul>
                        )}
                        {selectedSlideGrounding.suggestedActions.length > 0 && (
                          <div className="studio-grounding-actions">
                            {selectedSlideGrounding.suggestedActions.slice(0, 4).map((action) => (
                              <button key={action} type="button" className="button button-secondary" onClick={() => handleGroundingQuickAction(action)}>
                                {t(`slides.grounding.actions.${action}`) || action}
                              </button>
                            ))}
                          </div>
                        )}
                      </div>
                    )}

                    {selectedSlideNeedsMedia && (
                    <div className="studio-inspector-block">
                      <div className="studio-inspector-block-head">
                        <strong>{t('slides.mediaPanelTitle')}</strong>
                        <button
                          className="button button-secondary"
                          type="button"
                          onClick={() => setExpandedMediaSlideId(expandedMediaSlideId === selectedSlide.id ? null : selectedSlide.id)}
                        >
                          <LuImage aria-hidden="true" />
                          <span>{expandedMediaSlideId === selectedSlide.id ? t('slides.hideMedia') : t('slides.manageMedia')}</span>
                        </button>
                      </div>
                      <p>{selectedImageVm?.helperText}</p>
                      {selectedImageVm?.attributionText && <small>{selectedImageVm.attributionText}</small>}
                    </div>
                    )}

                    {selectedSlideNeedsMedia && expandedMediaSlideId === selectedSlide.id && (
                      <div className="studio-media-manager">
                        {selectedImageVm?.needsImage && (
                          <button
                            className="button button-secondary"
                            onClick={() => handleRefreshImages(selectedSlide)}
                            disabled={mediaBusySlideId === selectedSlide.id}
                          >
                            <LuRefreshCw aria-hidden="true" />
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
                                    <small>{[candidate.licenseLabel, candidate.attributionText].filter(Boolean).join(' | ')}</small>
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
                      <LuPencil aria-hidden="true" />
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

      {isPresenting && presentationSlide && presentationEditorState && (
        <section
          className="slide-presentation-overlay"
          aria-label={t('slides.presentation.presentMode')}
          role="dialog"
          aria-modal="true"
        >
          <div className="slide-presentation-toolbar">
            <div>
              <span className="studio-kicker">{t('slides.presentation.presentMode')}</span>
              <strong>{presentationSlide.heading || deck?.title || t('slides.previewFallbackTitle')}</strong>
            </div>
            <span className="slide-presentation-counter">
              {t('slides.presentation.presentationCounter', {
                current: presentationIndex + 1,
                total: previewItems.length,
              })}
            </span>
            <button
              type="button"
              className="slide-presentation-close"
              onClick={closePresentation}
              title={t('slides.presentation.exitPresent')}
              aria-label={t('slides.presentation.exitPresent')}
            >
              <LuX aria-hidden="true" />
            </button>
          </div>

          <div className="slide-presentation-stage">
            <button
              type="button"
              className="slide-presentation-nav previous"
              onClick={() => movePresentation(-1)}
              disabled={presentationIndex === 0}
              title={t('slides.presentation.previousSlide')}
              aria-label={t('slides.presentation.previousSlide')}
            >
              <LuChevronLeft aria-hidden="true" />
            </button>

            <article className={`slide-presentation-frame studio-slide-frame slide-preview-${normalizeSlideType(presentationSlide.slideType)}${presentationSlideIsTextOnly ? ' text-only-slide' : ''} layout-edit-slide`}>
              <SlideCanvas
                editorState={presentationEditorState}
                imageVm={presentationImageVm}
                labels={canvasLabels}
                mode="preview"
                scale={presentationCanvasScale}
              />
            </article>

            <button
              type="button"
              className="slide-presentation-nav next"
              onClick={() => movePresentation(1)}
              disabled={presentationIndex >= previewItems.length - 1}
              title={t('slides.presentation.nextSlide')}
              aria-label={t('slides.presentation.nextSlide')}
            >
              <LuChevronRight aria-hidden="true" />
            </button>
          </div>
        </section>
      )}
    </div>
  );
}

export default SlideStudio;
