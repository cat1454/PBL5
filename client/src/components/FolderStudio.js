import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  LuAlignCenter,
  LuAlignLeft,
  LuAlignRight,
  LuArrowLeft,
  LuBookOpen,
  LuBold,
  LuDownload,
  LuFileDown,
  LuFilePlus2,
  LuGamepad2,
  LuHighlighter,
  LuIndentDecrease,
  LuIndentIncrease,
  LuItalic,
  LuLayers,
  LuLayoutTemplate,
  LuImage,
  LuLink2,
  LuList,
  LuListOrdered,
  LuMousePointer2,
  LuPanelRightClose,
  LuPanelRightOpen,
  LuPalette,
  LuPlus,
  LuPresentation,
  LuPrinter,
  LuRedo2,
  LuRefreshCw,
  LuSave,
  LuSparkles,
  LuStrikethrough,
  LuSubscript,
  LuSuperscript,
  LuType,
  LuUnderline,
  LuUndo2,
  LuUpload,
  LuX,
} from 'react-icons/lu';
import {
  documentService,
  getApiErrorMessage,
  isApiJobNotFound,
  isSlideSchemaUnavailable,
  slideService,
  workspaceService,
} from '../services/api';
import { buildSlideImageViewModel } from '../services/slideImages';
import { formatEta, getProgressCounterLabel, isActiveProgress, isTerminalProgress, normalizeProgressState } from '../services/progress';
import {
  confirmGenerationReadiness,
  getReadinessLabel,
  getReadinessMessage,
  getDocumentReadiness,
} from '../services/generationReadiness';
import { useAnimatedProgress } from '../hooks/useAnimatedProgress';
import { useToast } from './common/ToastProvider';
import { useLanguage } from '../context/LanguageContext';
import { useAuth } from '../context/AuthContext';
import DocumentUnderstandingPanel from './DocumentUnderstandingPanel';
import LayersPanel from './slide-studio/LayersPanel';
import PropertiesPanel from './slide-studio/PropertiesPanel';
import SlideCanvas from './slide-studio/SlideCanvas';
import SlideStudioCanvaMockup from './slide-studio/SlideStudioCanvaMockup';
import SlideThumbnail from './slide-studio/SlideThumbnail';
import {
  addEditorElement,
  buildSlideFromEditorState,
  createImageElement,
  createTextElement,
  deleteEditorElement,
  duplicateEditorElement,
  findEditorElement,
  normalizeEditorState as normalizeSlideEditorState,
  patchEditorElement,
  reorderEditorElement,
} from './slide-studio/editorState';
import useSlideEditorAutosave from './slide-studio/useSlideEditorAutosave';
import useSlideEditorHistory from './slide-studio/useSlideEditorHistory';
import useSlideEditorRealtime from './slide-studio/useSlideEditorRealtime';
import useSlideEditorShortcuts from './slide-studio/useSlideEditorShortcuts';

const DEFAULT_BRIEF = {
  desiredSlideCount: 12,
  themeKey: 'editorial-sunrise',
  audience: 'Sinh viên và người học',
  tone: 'Rõ ràng, hiện đại, dễ nhớ',
  narrativeGoal: 'Tổng hợp các ý chính để tạo một deck giảng dạy ngắn gọn, dễ đọc, dễ chỉnh sửa.',
  languageStyle: 'Tiếng Việt ngắn gọn, chuyên nghiệp, dễ đọc trên slide',
  mode: 'lecture',
  scopePolicy: 'selected-sections-only',
};

const FONT_OPTIONS = ['Lexend', 'Noto Sans', 'Trebuchet MS', 'Segoe UI', 'Palatino Linotype', 'Courier New'];
const FONT_SIZES = [14, 16, 18, 20, 24, 28, 32, 36];
const TEXT_COLOR_OPTIONS = ['#0f172a', '#4338ca', '#047857', '#b42318', '#b45309'];
const HIGHLIGHT_COLOR_OPTIONS = ['transparent', '#fef3c7', '#dbeafe', '#dcfce7', '#fee2e2'];
const LINE_HEIGHT_OPTIONS = [1.2, 1.4, 1.6, 1.8, 2];
const DEFAULT_TEXT_COLOR = '#0f172a';
const DEFAULT_HIGHLIGHT_COLOR = 'transparent';
const AUTOSAVE_DEBOUNCE_MS = 800;
const THEME_OPTIONS = [
  { value: 'editorial-sunrise', label: 'Editorial Sunrise' },
  { value: 'paper-mint', label: 'Paper Mint' },
  { value: 'cobalt-grid', label: 'Cobalt Grid' },
  { value: 'midnight-signal', label: 'Midnight Signal' },
];
const AUDIENCE_OPTIONS = [
  'Sinh viên và người học',
  'Giáo viên / người thuyết trình',
  'Người mới bắt đầu',
  'Quản lý / lãnh đạo',
];
const TONE_OPTIONS = [
  'Rõ ràng, hiện đại, dễ nhớ',
  'Học thuật nhưng dễ tiếp thu',
  'Tự tin, có nhấn mạnh',
  'Khơi gợi trí tò mò',
];
const LANGUAGE_STYLE_OPTIONS = [
  'Tiếng Việt ngắn gọn, chuyên nghiệp, dễ đọc trên slide',
  'Tiếng Việt thân thiện, dễ đọc trên web',
  'Tiếng Việt học thuật, có cấu trúc',
  'Tiếng Việt thuyết trình, nhấn ý mạnh',
];
const DECK_LENGTH_OPTIONS = [8, 12, 18];
const DECK_MODE_OPTIONS = ['lecture', 'summary', 'exam-review', 'timeline'];
const EXCLUDED_SCOPE_CLASSES = ['FRONT_MATTER', 'TABLE_OF_CONTENTS', 'REFERENCE', 'APPENDIX', 'NOISE'];
const SCOPE_TITLE_MAX_LENGTH = 90;
const SCOPE_PREVIEW_MAX_LENGTH = 500;

function buildScopedSectionId(sourceId, sectionKey) {
  return `${sourceId}::${sectionKey}`;
}

function getSelectableSections(source) {
  return Array.isArray(source?.structure)
    ? source.structure.filter((section) => !EXCLUDED_SCOPE_CLASSES.includes(section.classification))
    : [];
}

function normalizeScopeText(value) {
  return typeof value === 'string' ? value.replace(/\r\n/g, '\n').trim() : '';
}

function truncateScopeText(value, maxLength) {
  const normalized = normalizeScopeText(value).replace(/\s+/g, ' ').trim();
  if (!normalized) {
    return '';
  }

  if (normalized.length <= maxLength) {
    return normalized;
  }

  return `${normalized.slice(0, Math.max(0, maxLength - 3)).trimEnd()}...`;
}

function getScopeFallbackTitle(index, language) {
  return language === 'vi' ? `Phần nội dung ${index + 1}` : `Content section ${index + 1}`;
}

function getScopeFirstSentence(value) {
  const normalized = normalizeScopeText(value).replace(/\s+/g, ' ').trim();
  if (!normalized) {
    return '';
  }

  const sentenceMatch = normalized.match(/^(.+?[.!?])(?:\s|$)/);
  return sentenceMatch ? sentenceMatch[1].trim() : normalized;
}

function getScopeSectionTitle(section, index, language) {
  const titleCandidates = [
    section?.title,
    section?.sectionTitle,
    section?.heading,
    section?.topic,
    section?.name,
  ];
  const directTitle = titleCandidates
    .map((value) => normalizeScopeText(value))
    .find(Boolean);
  const firstLine = directTitle
    ?.split('\n')
    .map((line) => line.trim())
    .find(Boolean);

  if (firstLine) {
    return truncateScopeText(firstLine, SCOPE_TITLE_MAX_LENGTH);
  }

  const fallbackSentence = [
    section?.summary,
    section?.content,
    section?.text,
    section?.detail,
    section?.description,
    section?.preview,
  ]
    .map((value) => getScopeFirstSentence(value))
    .find(Boolean);

  if (fallbackSentence) {
    return truncateScopeText(fallbackSentence, SCOPE_TITLE_MAX_LENGTH);
  }

  return getScopeFallbackTitle(index, language);
}

function getScopeSectionPreview(section, title) {
  const previewCandidates = [
    section?.preview,
    section?.summary,
    section?.detail,
    section?.description,
    section?.content,
    section?.text,
  ];
  const normalizedTitle = normalizeScopeText(title).replace(/\s+/g, ' ').trim();
  const preview = previewCandidates
    .map((value) => normalizeScopeText(value))
    .filter((value) => value.replace(/\s+/g, ' ').trim() !== normalizedTitle)
    .find(Boolean);

  return truncateScopeText(preview, SCOPE_PREVIEW_MAX_LENGTH);
}

function normalizeEditorBlock(block = {}, fallback = {}) {
  const listStyle = block.listStyle || (block.bullet ? 'bullet' : fallback.listStyle || 'none');

  return {
    text: block.text ?? fallback.text ?? '',
    fontFamily: block.fontFamily || fallback.fontFamily || 'Lexend',
    fontSize: Number(block.fontSize || fallback.fontSize || 18),
    bold: Boolean(block.bold ?? fallback.bold),
    italic: Boolean(block.italic ?? fallback.italic),
    underline: Boolean(block.underline ?? fallback.underline),
    strike: Boolean(block.strike ?? fallback.strike),
    align: block.align || fallback.align || 'left',
    bullet: listStyle === 'bullet',
    listStyle,
    textColor: block.textColor || fallback.textColor || DEFAULT_TEXT_COLOR,
    highlightColor: block.highlightColor || fallback.highlightColor || DEFAULT_HIGHLIGHT_COLOR,
    indentLevel: Math.max(0, Math.min(4, Number(block.indentLevel ?? fallback.indentLevel ?? 0))),
    lineHeight: Number(block.lineHeight || fallback.lineHeight || 1.6),
    script: block.script || fallback.script || 'normal',
    linkUrl: block.linkUrl || fallback.linkUrl || '',
  };
}

function createFallbackEditorState(item) {
  const editorState = item?.editorState || {};
  const fallback = {
    title: { text: item?.heading || '', fontFamily: 'Lexend', fontSize: 28, bold: true, align: 'left' },
    subtitle: { text: item?.subheading || '', fontFamily: 'Lexend', fontSize: 16, align: 'left' },
    goal: { text: item?.goal || '', fontFamily: 'Lexend', fontSize: 14, bold: true, align: 'left' },
    body: {
      text: Array.isArray(item?.bodyBlocks) ? item.bodyBlocks.join('\n') : '',
      fontFamily: 'Lexend',
      fontSize: 18,
      align: 'left',
      listStyle: 'bullet',
    },
    notes: { text: item?.speakerNotes || '', fontFamily: 'Lexend', fontSize: 14, align: 'left' },
  };

  return {
    layoutVariant: editorState.layoutVariant || 'standard',
    title: normalizeEditorBlock(editorState.title, fallback.title),
    subtitle: normalizeEditorBlock(editorState.subtitle, fallback.subtitle),
    goal: normalizeEditorBlock(editorState.goal, fallback.goal),
    body: normalizeEditorBlock(editorState.body, fallback.body),
    notes: normalizeEditorBlock(editorState.notes, fallback.notes),
  };
}

function cloneDraft(draft) {
  return JSON.parse(JSON.stringify(draft));
}
function getItemBodyText(item) {
  const editorBody = item?.editorState?.body?.text || '';
  const blockBody = Array.isArray(item?.bodyBlocks) ? item.bodyBlocks.join('\n') : '';
  return editorBody || blockBody;
}

function getDraftBodyText(draft) {
  return draft?.body?.text || '';
}

function isDraftBodyEmpty(draft) {
  return !getDraftBodyText(draft).trim();
}

function itemHasGeneratedBody(item) {
  return Boolean(getItemBodyText(item).trim());
}

function getSlideSourceRevision(item) {
  return [
    item?.updatedAt || '',
    item?.status || '',
    item?.heading || '',
    item?.subheading || '',
    item?.goal || '',
    item?.keyMessage || '',
    getItemBodyText(item),
    item?.speakerNotes || '',
  ].join('::');
}

function getNewestSlideId(deckData) {
  if (!deckData?.items?.length) {
    return null;
  }

  return [...deckData.items]
    .sort((left, right) => (left.slideIndex || 0) - (right.slideIndex || 0))
    .at(-1)?.id || null;
}

function getAnimationChunkSize(currentText, targetText) {
  const remaining = Math.max(0, targetText.length - currentText.length);

  if (remaining <= 2) {
    return remaining;
  }

  if (targetText.charAt(currentText.length) === '\n') {
    return 1;
  }

  return Math.min(3, remaining);
}
function normalizeStatusLabel(status) {
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
}

function hasFiniteNumber(value) {
  return typeof value === 'number' && Number.isFinite(value);
}

function clampPercent(value) {
  if (!hasFiniteNumber(value)) {
    return null;
  }

  return Math.max(0, Math.min(100, value));
}

function formatEtaDisplay(seconds, language) {
  if (!hasFiniteNumber(seconds)) {
    return null;
  }

  if (seconds <= 0) {
    return language === 'vi' ? 'Sắp xong...' : 'Almost done...';
  }

  if (seconds < 60) {
    return `${Math.round(seconds)}s`;
  }

  const minutes = Math.floor(seconds / 60);
  const remain = Math.round(seconds % 60);
  return `${minutes}m ${remain}s`;
}

function estimateRemainingSeconds(percent, elapsedSeconds) {
  if (!hasFiniteNumber(percent) || !hasFiniteNumber(elapsedSeconds) || percent <= 0 || percent >= 100) {
    return null;
  }

  const estimatedTotalSeconds = elapsedSeconds / (percent / 100);
  return Math.max(0, Math.round(estimatedTotalSeconds - elapsedSeconds));
}

function getSourceElapsedSeconds(source, progressState) {
  if (hasFiniteNumber(progressState?.elapsedSeconds) && progressState.elapsedSeconds >= 0) {
    return progressState.elapsedSeconds;
  }

  if (!source?.createdAt) {
    return null;
  }

  const createdAtMs = new Date(source.createdAt).getTime();
  if (!Number.isFinite(createdAtMs)) {
    return null;
  }

  return Math.max(0, Math.round((Date.now() - createdAtMs) / 1000));
}

function isSourceProcessing(source) {
  const status = Number(source?.status);
  const progressStatus = String(source?.processingProgress?.status || '').toLowerCase();

  return [0, 1, 2].includes(status)
    || progressStatus === 'queued'
    || progressStatus === 'running';
}

function buildSourceProcessingViewModel(source, language, t) {
  const rawProgress = source?.processingProgress && typeof source.processingProgress === 'object'
    ? source.processingProgress
    : null;
  const progressState = rawProgress
    ? normalizeProgressState(rawProgress)
    : (isSourceProcessing(source) ? normalizeProgressState({ status: 'running' }) : null);
  const isCompleted = source?.status === 3;
  const isFailed = source?.status === 4;
  const isActive = isSourceProcessing(source);
  const stageLabel = progressState?.stageLabel || '';
  const message = progressState?.message || '';
  const stageMessage = [stageLabel, message]
    .filter(Boolean)
    .filter((value, index, values) => values.indexOf(value) === index)
    .join(' · ') || t('slides.sourceProcessing.stageFallback');
  const errorMessage = progressState?.error || progressState?.message || t('slides.sourceProcessing.failedFallback');
  const hasProgressPercent = hasFiniteNumber(rawProgress?.percent);
  const progressPercent = hasProgressPercent ? clampPercent(Number(rawProgress.percent)) : null;
  const elapsedSeconds = getSourceElapsedSeconds(source, progressState);
  const explicitEtaSeconds = hasFiniteNumber(rawProgress?.estimatedRemainingSeconds)
    ? Number(rawProgress.estimatedRemainingSeconds)
    : (hasFiniteNumber(progressState?.estimatedRemainingSeconds) ? progressState.estimatedRemainingSeconds : null);
  const estimatedEtaSeconds = explicitEtaSeconds ?? estimateRemainingSeconds(progressPercent, elapsedSeconds);
  const etaLabel = formatEtaDisplay(estimatedEtaSeconds, language) || t('slides.sourceProcessing.etaEstimating');

  return {
    progressState,
    isCompleted,
    isFailed,
    isActive,
    isPending: isActive || (!isCompleted && !isFailed),
    hasProgressPercent,
    progressPercent,
    progressWidth: hasProgressPercent ? `${progressPercent}%` : '32%',
    stageMessage,
    errorMessage,
    etaLabel,
    statusLabel: t('slides.sourceProcessing.statusLabel'),
    failedLabel: t('slides.sourceProcessing.failedLabel'),
  };
}

function SourceProcessingProgress({ vm, t, compact = false }) {
  const backendPercent = vm?.hasProgressPercent ? Math.round(vm.progressPercent) : null;
  const displayedPercent = useAnimatedProgress(vm?.hasProgressPercent ? vm.progressPercent : 0);
  const progressClasses = [
    'folder-studio-source-progress',
    compact ? 'folder-studio-source-progress-compact' : '',
    vm?.hasProgressPercent ? (vm?.isActive ? 'is-active' : '') : 'indeterminate',
  ].filter(Boolean).join(' ');

  if (!vm) {
    return null;
  }

  return (
    <div className="folder-studio-source-processing">
      <div className="folder-studio-source-processing-head">
        <strong>
          {vm.hasProgressPercent
            ? `${backendPercent}%`
            : t('slides.sourceProcessing.indeterminateLabel')}
        </strong>
        <span>{vm.stageMessage}</span>
      </div>
      <div className={progressClasses}>
        <div
          className="folder-studio-source-progress-fill"
          style={{ width: vm.hasProgressPercent ? `${displayedPercent}%` : vm.progressWidth }}
        />
      </div>
      <div className="folder-studio-source-live folder-studio-source-live-block">
        {vm.isActive && <span className="folder-studio-live-hint">{t('slides.sourceProcessing.liveHint')}</span>}
        <span>{t('slides.sourceProcessing.etaLabel')} {vm.etaLabel}</span>
      </div>
    </div>
  );
}

function applyTextStyle(block = {}) {
  const decorations = [
    block.underline ? 'underline' : '',
    block.strike ? 'line-through' : '',
  ].filter(Boolean);
  const scriptFontScale = block.script === 'normal' ? 1 : 0.82;

  return {
    fontFamily: block.fontFamily || 'Lexend',
    fontSize: `${Math.round((block.fontSize || 18) * scriptFontScale)}px`,
    fontWeight: block.bold ? 700 : 400,
    fontStyle: block.italic ? 'italic' : 'normal',
    textDecoration: decorations.length ? decorations.join(' ') : 'none',
    textAlign: block.align || 'left',
    color: block.textColor || DEFAULT_TEXT_COLOR,
    backgroundColor: block.highlightColor && block.highlightColor !== DEFAULT_HIGHLIGHT_COLOR
      ? block.highlightColor
      : 'transparent',
    paddingLeft: block.indentLevel ? `${block.indentLevel * 18}px` : undefined,
    lineHeight: block.lineHeight || 1.6,
    verticalAlign: block.script === 'superscript' ? 'super' : block.script === 'subscript' ? 'sub' : 'baseline',
  };
}

function WorkspaceToolbarButton({
  active = false,
  children,
  className = '',
  label,
  ...props
}) {
  return (
    <button
      type="button"
      className={`folder-studio-toolbar-btn${active ? ' active' : ''}${className ? ` ${className}` : ''}`}
      aria-label={label}
      title={label}
      {...props}
    >
      {children}
    </button>
  );
}

function WorkspaceColorButton({ color, label, active, disabled, onClick, icon: Icon }) {
  return (
    <WorkspaceToolbarButton
      active={active}
      className="folder-studio-toolbar-color"
      disabled={disabled}
      label={label}
      onClick={onClick}
    >
      <Icon aria-hidden="true" />
      <span
        className="folder-studio-color-swatch"
        style={{ background: color === DEFAULT_HIGHLIGHT_COLOR ? '#ffffff' : color }}
      />
    </WorkspaceToolbarButton>
  );
}

function WorkspaceLinkAffordance({ block, label }) {
  if (!block?.linkUrl) {
    return null;
  }

  return (
    <div className="folder-slide-link-affordance">
      <LuLink2 aria-hidden="true" />
      <span>{label}</span>
      <strong>{block.linkUrl}</strong>
    </div>
  );
}
function WorkspaceDeckProgressCard({ progress, language }) {
  const percent = Math.max(0, Math.min(100, Number(progress?.percent || 0)));
  const displayedPercent = useAnimatedProgress(percent);

  if (!progress) {
    return null;
  }

  const counterLabel = getProgressCounterLabel(progress, { language });
  const etaLabel = formatEta(progress.estimatedRemainingSeconds, { language }) || (language === 'vi' ? 'Đang ước tính...' : 'Estimating...');

  return (
    <div className="workspace-generate-progress-card">
      <div className="workspace-generate-progress-head">
        <div>
          <p className="workspace-generate-kicker">
            {language === 'vi' ? 'Đang tạo slide deck' : 'Generating slide deck'}
          </p>
          <h3>{progress.stageLabel || (language === 'vi' ? 'Đang xử lý' : 'Processing')}</h3>
        </div>

        <span className="workspace-generate-percent">{percent}%</span>
      </div>

      <div className="workspace-generate-progress-track">
        <div
          className="workspace-generate-progress-fill"
          style={{ width: `${displayedPercent}%` }}
        />
      </div>

      <p className="workspace-generate-message">
        {progress.message || (language === 'vi' ? 'Hệ thống đang xử lý deck.' : 'The deck is being processed.')}
      </p>

      {progress.detail && (
        <p className="workspace-generate-detail">{progress.detail}</p>
      )}

      <div className="workspace-generate-meta">
        {counterLabel && <span>{counterLabel}</span>}
        <span>ETA: {etaLabel}</span>
      </div>
    </div>
  );
}

function WorkspaceQuestionProgressCard({ progress, language }) {
  const percent = Math.max(0, Math.min(100, Number(progress?.percent || 0)));
  const displayedPercent = useAnimatedProgress(percent);

  if (!progress) {
    return null;
  }

  const counterLabel = getProgressCounterLabel(progress, { language });
  const etaLabel = formatEta(progress.estimatedRemainingSeconds, { language }) || (language === 'vi' ? 'Đang ước tính...' : 'Estimating...');

  return (
    <div className="workspace-generate-progress-card">
      <div className="workspace-generate-progress-head">
        <div>
          <p className="workspace-generate-kicker">
            {language === 'vi' ? 'Đang tạo question bank' : 'Generating question bank'}
          </p>
          <h3>{progress.stageLabel || (language === 'vi' ? 'Đang xử lý' : 'Processing')}</h3>
        </div>

        <span className="workspace-generate-percent">{percent}%</span>
      </div>

      <div className="workspace-generate-progress-track">
        <div
          className="workspace-generate-progress-fill"
          style={{ width: `${displayedPercent}%` }}
        />
      </div>

      <p className="workspace-generate-message">
        {progress.message || (language === 'vi' ? 'Hệ thống đang tạo bộ câu hỏi từ source đã chọn.' : 'The question bank is being generated from the selected source.')}
      </p>

      {progress.detail && (
        <p className="workspace-generate-detail">{progress.detail}</p>
      )}

      <div className="workspace-generate-meta">
        {counterLabel && <span>{counterLabel}</span>}
        <span>ETA: {etaLabel}</span>
      </div>
    </div>
  );
}

function FolderStudioRuntime() {
  const { t, language } = useLanguage();
  const { currentUser } = useAuth();
  const { showToast } = useToast();
  const { workspaceId } = useParams();
  const location = useLocation();
  const navigate = useNavigate();
  const fileInputRef = useRef(null);
  const canvasImageInputRef = useRef(null);
  const centerCanvasRef = useRef(null);
  const editorSurfaceRef = useRef(null);

  const [folder, setFolder] = useState(null);
  const [sources, setSources] = useState([]);
  const [deck, setDeck] = useState(null);
  const [drafts, setDrafts] = useState({});
  const [dirtyDrafts, setDirtyDrafts] = useState({});
  const [draftMeta, setDraftMeta] = useState({});
  const [history, setHistory] = useState({});
  const [selectedSlideId, setSelectedSlideId] = useState(null);
  const [activeField, setActiveField] = useState('body');
  const [selectedEditorField, setSelectedEditorField] = useState(null);
  const [insertMenuField, setInsertMenuField] = useState(null);
  const [autoSaveStatus, setAutoSaveStatus] = useState('idle');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [uploadNotice, setUploadNotice] = useState('');
  const [jobId, setJobId] = useState(null);
  const [progress, setProgress] = useState(null);
  const [generationError, setGenerationError] = useState('');
  const [questionProgress, setQuestionProgress] = useState(null);
  const [questionError, setQuestionError] = useState('');
  const [isAnalyzingStructure, setIsAnalyzingStructure] = useState(false);
  const [mediaOpen, setMediaOpen] = useState(false);
  const [mediaBusy, setMediaBusy] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [exportingFormat, setExportingFormat] = useState('');
  const [brief, setBrief] = useState(DEFAULT_BRIEF);
  const [selectedSourceId, setSelectedSourceId] = useState(null);
  const [selectedSectionIds, setSelectedSectionIds] = useState([]);
  const [expandedSectionIds, setExpandedSectionIds] = useState([]);
  const [isScopePickerOpen, setIsScopePickerOpen] = useState(false);
  const [scopeRecommendation, setScopeRecommendation] = useState(null);
  const [filterText, setFilterText] = useState('');
  const [activeTool, setActiveTool] = useState('sources');
  const [canvasMode, setCanvasMode] = useState('preview');
  const [selectedElementId, setSelectedElementId] = useState(null);
  const [copiedElement, setCopiedElement] = useState(null);
  const [canvasStageSize, setCanvasStageSize] = useState({ width: 0, height: 0 });
  const [remoteSelections, setRemoteSelections] = useState({});
  const [, setAnimatingSlides] = useState({});
  const progressRef = useRef(null);
  const latestDeckRef = useRef(null);
  const latestDraftsRef = useRef({});
  const latestDirtyDraftsRef = useRef({});
  const autoSaveTimerRef = useRef(null);
  const autoSaveInFlightRef = useRef({});
  const autoSaveQueuedRef = useRef({});
  const autoSaveStatusTimerRef = useRef(null);
  const typewriterTimersRef = useRef({});
  const typewriterStateRef = useRef({});
  const animatedRevisionRef = useRef({});
  const realtimeThrottleRef = useRef({});
  const canvasHistory = useSlideEditorHistory();
  const canvasImportImageTypes = useMemo(() => new Set(['image/png', 'image/jpeg', 'image/webp', 'image/gif']), []);
  const audienceLabels = t('slides.options.audiences');
  const toneLabels = t('slides.options.tones');
  const languageStyleLabels = t('slides.options.languageStyles');
  const getModeLabel = (mode) => {
    if (language === 'vi') {
      switch (mode) {
        case 'summary':
          return 'Tóm tắt';
        case 'exam-review':
          return 'Ôn thi';
        case 'timeline':
          return 'Timeline lịch sử';
        default:
          return 'Bài giảng';
      }
    }

    switch (mode) {
      case 'summary':
        return 'Summary';
      case 'exam-review':
        return 'Exam review';
      case 'timeline':
        return 'Historical timeline';
      default:
        return 'Lecture';
    }
  };
  const slideTitlePlaceholder = language === 'vi' ? 'Tiêu đề slide' : 'Slide title';
  const slideGoalPlaceholder = language === 'vi' ? 'Mục tiêu / take-away của slide' : 'Slide goal / take-away';
  const bodyPlaceholder = language === 'vi' ? 'Mỗi dòng tương ứng một bullet hoặc một ý chính.' : 'Each line becomes one bullet or one key point.';
  const notesPlaceholder = language === 'vi' ? 'Ghi chú thuyết trình, script, nhắc nhở...' : 'Speaker notes, script, reminders...';

  const formatRelativeTimeLabel = (value) => {
    if (!value) {
      return '-';
    }

    const diffMs = Date.now() - new Date(value).getTime();
    if (diffMs < 60_000) {
      return language === 'vi' ? 'vừa cập nhật' : 'just updated';
    }
    if (diffMs < 3_600_000) {
      const count = Math.max(1, Math.floor(diffMs / 60_000));
      return language === 'vi' ? `${count} phút trước` : `${count} minutes ago`;
    }
    if (diffMs < 86_400_000) {
      const count = Math.max(1, Math.floor(diffMs / 3_600_000));
      return language === 'vi' ? `${count} giờ trước` : `${count} hours ago`;
    }

    return new Date(value).toLocaleString();
  };

  useEffect(() => {
    const nextNotice = location.state?.uploadNotice;
    if (!nextNotice) {
      setUploadNotice('');
      return;
    }

    setUploadNotice([nextNotice.message, nextNotice.description].filter(Boolean).join(' '));
  }, [location.pathname, location.state]);

  const stopTypewriterAnimation = useCallback((slideId) => {
    if (!slideId) {
      return;
    }

    if (typewriterTimersRef.current[slideId]) {
      clearTimeout(typewriterTimersRef.current[slideId]);
      delete typewriterTimersRef.current[slideId];
    }

    if (typewriterStateRef.current[slideId]) {
      delete typewriterStateRef.current[slideId];
    }

    setAnimatingSlides((current) => {
      if (!current[slideId]) {
        return current;
      }

      const next = { ...current };
      delete next[slideId];
      return next;
    });
  }, []);

  const startTypewriterAnimation = useCallback((item, revision) => {
    if (!item?.id || !revision) {
      return;
    }

    const slideId = item.id;
    const targetDraft = createFallbackEditorState(item);
    animatedRevisionRef.current[slideId] = revision;
    stopTypewriterAnimation(slideId);

    typewriterStateRef.current[slideId] = {
      revision,
      targetDraft,
    };

    setAnimatingSlides((current) => ({
      ...current,
      [slideId]: revision,
    }));

    setDrafts((current) => {
      const base = current[slideId] || createFallbackEditorState(item);
      return {
        ...current,
        [slideId]: {
          ...cloneDraft(base),
          title: { ...base.title, text: '' },
          subtitle: { ...base.subtitle, text: '' },
          goal: { ...base.goal, text: '' },
          body: { ...base.body, text: '' },
          notes: { ...base.notes, text: '' },
        },
      };
    });

    const tick = () => {
      const activeAnimation = typewriterStateRef.current[slideId];
      if (!activeAnimation || activeAnimation.revision !== revision) {
        return;
      }

      let done = true;

      setDrafts((current) => {
        const base = current[slideId] || createFallbackEditorState(item);
        const nextDraft = cloneDraft(base);
        let changed = false;

        ['title', 'subtitle', 'goal', 'body', 'notes'].forEach((fieldKey) => {
          const targetText = activeAnimation.targetDraft[fieldKey]?.text || '';
          const currentText = nextDraft[fieldKey]?.text || '';

          if (currentText !== targetText) {
            done = false;
            nextDraft[fieldKey].text = targetText.slice(0, currentText.length + getAnimationChunkSize(currentText, targetText));
            changed = true;
          }
        });

        if (!changed) {
          return current;
        }

        return {
          ...current,
          [slideId]: nextDraft,
        };
      });

      if (done) {
        stopTypewriterAnimation(slideId);
        setDraftMeta((current) => ({
          ...current,
          [slideId]: { ...(current[slideId] || {}), sourceRevision: revision },
        }));
        return;
      }

      typewriterTimersRef.current[slideId] = setTimeout(tick, 36);
    };

    typewriterTimersRef.current[slideId] = setTimeout(tick, 48);
  }, [stopTypewriterAnimation]);

  useEffect(() => {
    progressRef.current = progress;
  }, [progress]);

  const loadWorkspace = useCallback(async ({ silent = false } = {}) => {
    if (!silent) {
      setLoading(true);
    }

    try {
      setError('');
      const [folderData, sourceData, deckData] = await Promise.all([
        workspaceService.get(workspaceId),
        workspaceService.listSources(workspaceId),
        slideService.getDeckByFolder(workspaceId),
      ]);

      setFolder(folderData);
      setSources(Array.isArray(sourceData) ? sourceData : []);
      setDeck(deckData || null);

      if (deckData?.generationProgress) {
        const nextProgress = normalizeProgressState(deckData.generationProgress, progressRef.current || {});
        setProgress(nextProgress);
        if (nextProgress.jobId) {
          setJobId(nextProgress.jobId);
        }
      } else if (!progressRef.current || isTerminalProgress(progressRef.current)) {
        setProgress(null);
        setJobId(null);
      }

      if (deckData?.outline?.brief) {
        setBrief((current) => ({
          desiredSlideCount: deckData.outline.brief.desiredSlideCount || current.desiredSlideCount,
          themeKey: deckData.outline.brief.themeKey || current.themeKey,
          audience: deckData.outline.brief.audience || current.audience,
          tone: deckData.outline.brief.tone || current.tone,
          narrativeGoal: deckData.outline.brief.narrativeGoal || current.narrativeGoal,
          languageStyle: deckData.outline.brief.languageStyle || current.languageStyle,
          mode: deckData.outline.brief.mode || current.mode,
          scopePolicy: deckData.outline.brief.scopePolicy || current.scopePolicy,
        }));
      }

      return { folderData, sourceData, deckData };
    } catch (err) {
      console.error(err);
      setError(getApiErrorMessage(err, language === 'vi' ? 'Không tải được workspace studio.' : 'Could not load the workspace studio.'));
      return null;
    } finally {
      if (!silent) {
        setLoading(false);
      }
    }
  }, [language, workspaceId]);

  useEffect(() => {
    loadWorkspace();
  }, [loadWorkspace]);

  useEffect(() => {
    if (!deck?.items?.length) {
      setSelectedSlideId(null);
      return;
    }

    setSelectedSlideId((current) => (
      deck.items.some((item) => item.id === current) ? current : deck.items[0].id
    ));
  }, [deck]);

  useEffect(() => {
    if (!deck?.items?.length) {
      return;
    }

    setDrafts((current) => {
      const next = { ...current };
      const metaPatch = {};
      let changed = false;

      deck.items.forEach((item) => {
        const revision = getSlideSourceRevision(item);
        const currentDraft = next[item.id];
        const isDirty = Boolean(dirtyDrafts[item.id]);
        const currentMeta = draftMeta[item.id];
        const isSelected = item.id === selectedSlideId;

        const shouldCreateDraft = !currentDraft;
        const shouldRefreshFromBackend = !isDirty && currentMeta?.sourceRevision !== revision;
        const shouldRepairEmptyBody = !isDirty && isDraftBodyEmpty(currentDraft) && itemHasGeneratedBody(item);
        const shouldSyncSelectedImmediately = isSelected && animatedRevisionRef.current[item.id] === revision;

        if (shouldCreateDraft || shouldRepairEmptyBody || (shouldRefreshFromBackend && (!isSelected || shouldSyncSelectedImmediately))) {
          next[item.id] = createFallbackEditorState(item);
          metaPatch[item.id] = { ...(currentMeta || {}), sourceRevision: revision };
          changed = true;
        }
      });

      if (Object.keys(metaPatch).length > 0) {
        setDraftMeta((currentMeta) => ({
          ...currentMeta,
          ...metaPatch,
        }));
      }

      return changed ? next : current;
    });
  }, [deck, dirtyDrafts, draftMeta, selectedSlideId]);

  const selectedSlide = deck?.items?.find((item) => item.id === selectedSlideId) || null;
  const selectedDraft = selectedSlide ? (drafts[selectedSlide.id] || createFallbackEditorState(selectedSlide)) : null;
  const selectedImageVm = selectedSlide ? buildSlideImageViewModel(selectedSlide) : null;
  const selectedCanvasState = selectedSlide ? normalizeSlideEditorState(selectedSlide) : null;
  const selectedCanvasElement = selectedCanvasState ? findEditorElement(selectedCanvasState, selectedElementId) : null;
  const isLayoutEditMode = canvasMode === 'layout';
  const isTextEditMode = canvasMode === 'text';
  const selectedCanvasHistory = selectedSlide ? canvasHistory.getHistory(selectedSlide.id) : { past: [], future: [] };
  const canvasRemoteSelections = selectedSlide
    ? Object.values(remoteSelections).filter((selection) => selection.slideId === selectedSlide.id)
    : [];
  const displayName = currentUser?.fullName || currentUser?.email || currentUser?.username || (language === 'vi' ? 'Nguoi dung workspace' : 'Workspace user');
  const selectedCanvasScale = useMemo(() => {
    const canvas = selectedCanvasState?.canvas;
    if (!canvas || !canvasStageSize.width || !canvasStageSize.height) {
      return 0.58;
    }

    const availableWidth = Math.max(240, canvasStageSize.width - 72);
    const availableHeight = Math.max(180, canvasStageSize.height - 132);
    const nextScale = Math.min(availableWidth / canvas.width, availableHeight / canvas.height, 0.72);
    return Number(Math.max(0.24, nextScale).toFixed(3));
  }, [canvasStageSize.height, canvasStageSize.width, selectedCanvasState]);

  const applyCanvasStateToDeck = useCallback((slideId, editorState, { remote = false } = {}) => {
    let nextSlide = null;

    setDeck((current) => {
      if (!current) {
        return current;
      }

      return {
        ...current,
        items: current.items.map((item) => {
          if (item.id !== slideId) {
            return item;
          }

          nextSlide = buildSlideFromEditorState(item, editorState);
          return nextSlide;
        }),
      };
    });

    if (nextSlide && !remote) {
      setDrafts((current) => ({
        ...current,
        [slideId]: createFallbackEditorState(nextSlide),
      }));
    }
  }, []);

  const saveCanvasEditorState = useCallback(async (slideId, editorState) => {
    const currentDeck = latestDeckRef.current;
    const slideItem = currentDeck?.items?.find((item) => item.id === slideId);
    if (!currentDeck || !slideItem || !editorState) {
      return null;
    }

    const updated = await slideService.updateSlideItem(currentDeck.id, slideId, {
      editorState,
      accentTone: slideItem.accentTone || '',
    });

    setDeck((current) => (current ? {
      ...current,
      items: current.items.map((item) => (item.id === updated.id ? updated : item)),
    } : current));
    setDrafts((current) => ({
      ...current,
      [updated.id]: createFallbackEditorState(updated),
    }));
    return updated;
  }, []);

  const canvasAutosave = useSlideEditorAutosave({
    debounceMs: 1000,
    onSave: saveCanvasEditorState,
  });

  const applyRemoteCanvasOperation = useCallback((message) => {
    const editorState = message?.payload?.editorState || message?.payload?.EditorState;
    if (!message?.slideId || !editorState) {
      return;
    }

    applyCanvasStateToDeck(message.slideId, editorState, { remote: true });
  }, [applyCanvasStateToDeck]);

  const handleRemoteSelection = useCallback((message) => {
    if (!message?.clientId) {
      return;
    }

    setRemoteSelections((current) => ({
      ...current,
      [message.clientId]: {
        clientId: message.clientId,
        displayName: message.displayName || (language === 'vi' ? 'Nguoi dung khac' : 'Remote user'),
        slideId: message.slideId,
        elementId: message.elementId,
      },
    }));
  }, [language]);

  const handleRemotePresence = useCallback((message) => {
    if (!message?.clientId || message.status !== 'offline') {
      return;
    }

    setRemoteSelections((current) => {
      const next = { ...current };
      delete next[message.clientId];
      return next;
    });
  }, []);

  const realtime = useSlideEditorRealtime({
    deckId: deck?.id,
    displayName,
    onOperation: applyRemoteCanvasOperation,
    onPresence: handleRemotePresence,
    onSelection: handleRemoteSelection,
  });
  const realtimeStatusLabel = realtime.status === 'connected'
    ? (language === 'vi' ? 'Realtime connected' : 'Realtime connected')
    : (language === 'vi' ? 'Offline - van luu REST duoc' : 'Offline - REST save still works');
  const canvasAutosaveStatus = selectedSlide ? (canvasAutosave.statusBySlideId[selectedSlide.id] || 'saved') : 'saved';
  const canvasStatusLabel = {
    dirty: language === 'vi' ? 'Chua luu' : 'Unsaved changes',
    saving: language === 'vi' ? 'Dang luu' : 'Saving',
    saved: language === 'vi' ? 'Da luu' : 'Saved',
    error: language === 'vi' ? 'Luu loi' : 'Save failed',
  }[canvasAutosaveStatus] || (language === 'vi' ? 'Da luu' : 'Saved');
  const canvasModeHint = isLayoutEditMode
    ? 'Edit layout: drag elements to move, drag corners to resize.'
    : isTextEditMode
      ? 'Edit text: update copy directly on the slide without changing layout.'
      : 'Preview mode: editor controls are hidden.';
  const handleEnterCanvasPreviewMode = useCallback(() => {
    setCanvasMode('preview');
    setSelectedElementId(null);
    setSelectedEditorField(null);
  }, []);
  const handleEnterCanvasTextMode = useCallback(() => {
    setCanvasMode('text');
    setSelectedEditorField(null);
    setSelectedElementId((current) => current || selectedCanvasState?.elements?.find((element) => element.type === 'text')?.id || null);
  }, [selectedCanvasState]);
  const handleEnterCanvasLayoutMode = useCallback(() => {
    setCanvasMode('layout');
    setSelectedEditorField(null);
    setSelectedElementId((current) => current || selectedCanvasState?.elements?.[0]?.id || null);
  }, [selectedCanvasState]);
  const canvasLabels = {
    emptyText: language === 'vi' ? 'Van ban trong' : 'Empty text',
    imageAlt: selectedSlide?.heading || (language === 'vi' ? 'Anh slide' : 'Slide image'),
    imagePlaceholderTitle: language === 'vi' ? 'Khung anh' : 'Image slot',
    imagePlaceholderBody: language === 'vi' ? 'Chon hoac lam moi anh trong panel media.' : 'Choose or refresh an image from the media panel.',
  };
  const canvasPropertyLabels = {
    title: language === 'vi' ? 'Thuoc tinh element' : 'Element properties',
    empty: language === 'vi' ? 'Chon element tren canvas de sua.' : 'Select an element on the canvas to edit it.',
    text: language === 'vi' ? 'Van ban' : 'Text',
    fontSize: language === 'vi' ? 'Co chu' : 'Font size',
    color: language === 'vi' ? 'Mau' : 'Color',
    style: language === 'vi' ? 'Kieu chu' : 'Style',
    bold: language === 'vi' ? 'In dam' : 'Bold',
    alignLeft: language === 'vi' ? 'Can trai' : 'Align left',
    alignCenter: language === 'vi' ? 'Can giua' : 'Align center',
    alignRight: language === 'vi' ? 'Can phai' : 'Align right',
    lock: language === 'vi' ? 'Khoa element' : 'Lock element',
    unlock: language === 'vi' ? 'Mo khoa element' : 'Unlock element',
    roles: {
      title: language === 'vi' ? 'Tieu de' : 'Title',
      subtitle: language === 'vi' ? 'Phu de' : 'Subtitle',
      goal: language === 'vi' ? 'Thong diep' : 'Key message',
      body: language === 'vi' ? 'Noi dung' : 'Body',
      notes: language === 'vi' ? 'Ghi chu' : 'Notes',
      image: language === 'vi' ? 'Hinh anh' : 'Image',
    },
  };
  const canvasLayerLabels = {
    ...canvasPropertyLabels,
    title: language === 'vi' ? 'Layers' : 'Layers',
    count: (count) => (language === 'vi' ? `${count} element` : `${count} elements`),
    forward: language === 'vi' ? 'Dua len tren' : 'Bring forward',
    backward: language === 'vi' ? 'Dua xuong duoi' : 'Send backward',
    duplicate: language === 'vi' ? 'Nhan doi' : 'Duplicate',
    delete: language === 'vi' ? 'Xoa' : 'Delete',
  };

  useEffect(() => {
    latestDeckRef.current = deck;
  }, [deck]);

  useEffect(() => {
    latestDraftsRef.current = drafts;
  }, [drafts]);

  useEffect(() => {
    latestDirtyDraftsRef.current = dirtyDrafts;
  }, [dirtyDrafts]);

  useEffect(() => {
    const node = editorSurfaceRef.current;
    if (!node) {
      return undefined;
    }

    const updateSize = () => {
      const rect = node.getBoundingClientRect();
      setCanvasStageSize((current) => {
        const width = Math.round(rect.width);
        const height = Math.round(rect.height);
        return current.width === width && current.height === height ? current : { width, height };
      });
    };

    updateSize();

    if (typeof ResizeObserver === 'undefined') {
      window.addEventListener('resize', updateSize);
      return () => window.removeEventListener('resize', updateSize);
    }

    const observer = new ResizeObserver(updateSize);
    observer.observe(node);
    return () => observer.disconnect();
  }, [activeTool, selectedSlideId]);

  useEffect(() => {
    if (!selectedSlide || !deck?.items?.length) {
      return undefined;
    }

    const selectedItem = deck.items.find((item) => item.id === selectedSlide.id);
    if (!selectedItem) {
      return undefined;
    }

    const revision = getSlideSourceRevision(selectedItem);
    const isDirty = Boolean(dirtyDrafts[selectedItem.id]);
    const currentMeta = draftMeta[selectedItem.id];

    if (isDirty || currentMeta?.sourceRevision === revision || animatedRevisionRef.current[selectedItem.id] === revision) {
      return undefined;
    }

    startTypewriterAnimation(selectedItem, revision);
    return undefined;
  }, [deck, dirtyDrafts, draftMeta, selectedSlide, startTypewriterAnimation]);

  useEffect(() => {
    Object.keys(typewriterStateRef.current).forEach((slideId) => {
      if (String(slideId) !== String(selectedSlideId)) {
        stopTypewriterAnimation(slideId);
      }
    });
  }, [selectedSlideId, stopTypewriterAnimation]);

  useEffect(() => {
    if (!selectedSlideId || !centerCanvasRef.current) {
      return;
    }

    centerCanvasRef.current.scrollTo({
      top: 0,
      behavior: 'smooth',
    });
  }, [selectedSlideId]);

  const handleSelectSlide = useCallback((slideId) => {
    setSelectedSlideId(slideId);
    setSelectedElementId(null);
    setSelectedEditorField(null);
    setInsertMenuField(null);
    if (centerCanvasRef.current) {
      centerCanvasRef.current.scrollTo({
        top: 0,
        behavior: 'smooth',
      });
    }
  }, []);

  useEffect(() => {
    const activeTimers = typewriterTimersRef.current;

    return () => {
      Object.keys(activeTimers).forEach((slideId) => {
        if (activeTimers[slideId]) {
          clearTimeout(activeTimers[slideId]);
        }
      });
    };
  }, []);

  useEffect(() => {
    if (!jobId || !progress || isTerminalProgress(progress)) {
      return undefined;
    }

    let cancelled = false;

    const pollProgress = async () => {
      try {
        const nextProgress = normalizeProgressState(
          await slideService.getGenerateProgress(jobId),
          progressRef.current || progress
        );

        if (cancelled) {
          return;
        }

        setProgress(nextProgress);

        if (nextProgress.status === 'completed') {
          setGenerationError('');
          const finalWorkspace = await loadWorkspace({ silent: true });
          if (cancelled) {
            return;
          }

          const newestSlideId = getNewestSlideId(finalWorkspace?.deckData);
          if (newestSlideId) {
            setSelectedSlideId(newestSlideId);
          }
          return;
        }

        if (nextProgress.status === 'failed') {
          setGenerationError(nextProgress.error || nextProgress.detail || t('slides.generationStatus.failedFallback'));
          await loadWorkspace({ silent: true });
        } else {
          setGenerationError('');
        }
      } catch (err) {
        console.error(err);
        if (cancelled) {
          return;
        }

        const refreshedWorkspace = await loadWorkspace({ silent: true });
        if (cancelled) {
          return;
        }

        if (isApiJobNotFound(err)) {
          const persistedProgress = refreshedWorkspace?.deckData?.generationProgress
            ? normalizeProgressState(refreshedWorkspace.deckData.generationProgress)
            : null;

          if (!persistedProgress || isTerminalProgress(persistedProgress)) {
            setProgress(persistedProgress);
            setJobId(persistedProgress?.jobId || null);
            setGenerationError('');
            return;
          }
        }

        setGenerationError(getApiErrorMessage(err, t('slides.generationStatus.pollFailed')));
        if (refreshedWorkspace?.deckData?.generationProgress) {
          setProgress(normalizeProgressState(refreshedWorkspace.deckData.generationProgress));
        }
      }
    };

    pollProgress();
    const interval = setInterval(pollProgress, 1500);

    return () => {
      cancelled = true;
      clearInterval(interval);
    };
  }, [jobId, loadWorkspace, progress, t]);

  const sourceProcessingPollKey = useMemo(
    () => sources
      .filter(isSourceProcessing)
      .map((source) => String(source.id))
      .sort()
      .join('|'),
    [sources]
  );

  useEffect(() => {
    if (!sourceProcessingPollKey) {
      return undefined;
    }

    let cancelled = false;
    let inFlight = false;
    const activeSourceIds = new Set(sourceProcessingPollKey.split('|').filter(Boolean));

    const pollSources = async () => {
      if (inFlight) {
        return;
      }

      inFlight = true;
      try {
        const nextSources = await workspaceService.listSources(workspaceId);
        if (cancelled) {
          return;
        }

        const normalizedSources = Array.isArray(nextSources) ? nextSources : [];
        setSources(normalizedSources);

        const terminalReached = Array.from(activeSourceIds).some((sourceId) => {
          const nextSource = normalizedSources.find((source) => String(source.id) === sourceId);
          return nextSource && !isSourceProcessing(nextSource);
        });

        if (terminalReached) {
          await loadWorkspace({ silent: true });
        }
      } catch (err) {
        console.error(err);
      } finally {
        inFlight = false;
      }
    };

    pollSources();
    const interval = setInterval(pollSources, 2000);

    return () => {
      cancelled = true;
      clearInterval(interval);
    };
  }, [loadWorkspace, sourceProcessingPollKey, workspaceId]);

  useEffect(() => {
    if (!sourceProcessingPollKey) {
      return undefined;
    }

    const handleVisibilityChange = () => {
      if (document.visibilityState === 'visible') {
        loadWorkspace({ silent: true });
      }
    };

    document.addEventListener('visibilitychange', handleVisibilityChange);

    return () => {
      document.removeEventListener('visibilitychange', handleVisibilityChange);
    };
  }, [loadWorkspace, sourceProcessingPollKey]);

  const selectedReadySources = useMemo(
    () => sources.filter((source) => source.status === 3 && (source.includeInWorkspaceSlides ?? source.includeInFolderSlides)),
    [sources]
  );
  const readySources = useMemo(
    () => sources.filter((source) => source.status === 3),
    [sources]
  );
  const sourceViewModels = useMemo(
    () => sources.map((source) => ({
      source,
      vm: buildSourceProcessingViewModel(source, language, t),
    })),
    [language, sources, t]
  );
  const selectedSource = useMemo(
    () => sources.find((source) => source.id === selectedSourceId) || null,
    [selectedSourceId, sources]
  );
  const selectableSections = useMemo(
    () => getSelectableSections(selectedSource),
    [selectedSource]
  );
  const activeProgress = progress || (deck?.generationProgress ? normalizeProgressState(deck.generationProgress) : null);

  const deckGenerationProgress = activeProgress && isActiveProgress(activeProgress)
  ? activeProgress
  : null;

  const isGeneratingDeck = Boolean(deckGenerationProgress);
  useEffect(() => {
    if (!sources.length) {
      setSelectedSourceId(null);
      return;
    }

    setSelectedSourceId((current) => {
      if (current && sources.some((source) => source.id === current)) {
        return current;
      }

      const included = readySources.find((source) => source.includeInWorkspaceSlides ?? source.includeInFolderSlides);
      return included?.id ?? readySources[0]?.id ?? null;
    });
  }, [readySources, sources]);

  useEffect(() => {
    if (!selectedSource) {
      setSelectedSectionIds([]);
      setExpandedSectionIds([]);
      return;
    }

    const validIds = new Set(getSelectableSections(selectedSource).map((section) => buildScopedSectionId(selectedSource.id, section.sectionKey)));
    setSelectedSectionIds((current) => current.filter((id) => validIds.has(id)));
    setExpandedSectionIds((current) => current.filter((id) => validIds.has(id)));
  }, [selectedSource]);

  const pushHistory = (slideId, previousDraft) => {
    setHistory((current) => {
      const previous = current[slideId] || { past: [], future: [] };
      return {
        ...current,
        [slideId]: {
          past: [...previous.past, cloneDraft(previousDraft)].slice(-30),
          future: [],
        },
      };
    });
  };

  const mutateDraft = (slideId, updater, { trackHistory = true } = {}) => {
    if (trackHistory) {
      setDirtyDrafts((current) => ({
        ...current,
        [slideId]: true,
      }));
      setAutoSaveStatus('dirty');
    }

    setDrafts((current) => {
      const base = current[slideId] || createFallbackEditorState(deck?.items?.find((item) => item.id === slideId));
      if (trackHistory) {
        pushHistory(slideId, base);
      }

      return {
        ...current,
        [slideId]: updater(cloneDraft(base)),
      };
    });
  };
  const handleFieldTextChange = (fieldKey, value) => {
    if (!selectedSlide) {
      return;
    }

    stopTypewriterAnimation(selectedSlide.id);
    mutateDraft(selectedSlide.id, (draft) => {
      draft[fieldKey].text = value;
      return draft;
    });
  };

  const handleStyleChange = (updater) => {
    if (!selectedSlide || !selectedDraft) {
      return;
    }

    mutateDraft(selectedSlide.id, (draft) => {
      draft[activeField] = updater({ ...draft[activeField] });
      return draft;
    });
  };

  const handleListStyleChange = (listStyle) => {
    handleStyleChange((block) => ({
      ...block,
      listStyle,
      bullet: listStyle === 'bullet',
    }));
  };

  const handleIndentChange = (direction) => {
    handleStyleChange((block) => ({
      ...block,
      indentLevel: Math.max(0, Math.min(4, Number(block.indentLevel || 0) + direction)),
    }));
  };

  const handleScriptChange = (script) => {
    handleStyleChange((block) => ({
      ...block,
      script: block.script === script ? 'normal' : script,
    }));
  };

  const handleLinkPrompt = () => {
    if (!selectedSlide || !selectedDraft) {
      return;
    }

    const currentUrl = selectedDraft[activeField]?.linkUrl || '';
    const label = language === 'vi'
      ? 'Nhap URL lien ket cho block dang chon. De trong de xoa lien ket.'
      : 'Enter the link URL for the active block. Leave empty to remove the link.';
    const nextUrl = window.prompt(label, currentUrl);

    if (nextUrl === null) {
      return;
    }

    handleStyleChange((block) => ({
      ...block,
      linkUrl: nextUrl.trim(),
    }));
  };

  const selectEditorField = (fieldKey) => {
    setActiveField(fieldKey);
    setSelectedEditorField(fieldKey);
    setInsertMenuField(null);
  };

  const handleInsertChoice = (choice, fieldKey) => {
    setInsertMenuField(null);

    if (choice === 'heading') {
      selectEditorField(selectedDraft?.title?.text?.trim() ? 'subtitle' : 'title');
      return;
    }

    if (choice === 'image') {
      setSelectedEditorField(null);
      setMediaOpen(true);
      return;
    }

    selectEditorField(fieldKey === 'notes' ? 'notes' : 'body');
  };

  const buildSlideItemPayload = (slideItem, draft) => ({
    heading: draft.title.text,
    subheading: draft.subtitle.text,
    goal: draft.goal.text,
    bodyBlocks: draft.body.text.split('\n').map((line) => line.trim()).filter(Boolean),
    speakerNotes: draft.notes.text,
    accentTone: slideItem.accentTone || '',
    editorState: draft,
  });

  const saveSlideDraft = useCallback(async (slideId, draftSnapshot, { manual = false } = {}) => {
    const currentDeck = latestDeckRef.current;
    const slideItem = currentDeck?.items?.find((item) => item.id === slideId);

    if (!currentDeck || !slideItem || !draftSnapshot) {
      return null;
    }

    const savedSerialized = JSON.stringify(draftSnapshot);
    const updated = await slideService.updateSlideItem(
      currentDeck.id,
      slideId,
      buildSlideItemPayload(slideItem, draftSnapshot)
    );
    const updatedRevision = getSlideSourceRevision(updated);

    setDeck((current) => (current ? {
      ...current,
      items: current.items.map((item) => (item.id === updated.id ? updated : item)),
    } : current));

    setDraftMeta((current) => ({
      ...current,
      [updated.id]: { sourceRevision: updatedRevision },
    }));

    if (manual) {
      setDrafts((current) => ({
        ...current,
        [updated.id]: createFallbackEditorState(updated),
      }));
      setDirtyDrafts((current) => ({
        ...current,
        [updated.id]: false,
      }));
      setHistory((current) => ({
        ...current,
        [updated.id]: { past: [], future: [] },
      }));
      return updated;
    }

    setDirtyDrafts((current) => {
      const latestDraft = latestDraftsRef.current[updated.id];
      const latestSerialized = latestDraft ? JSON.stringify(latestDraft) : '';

      if (latestSerialized === savedSerialized) {
        return {
          ...current,
          [updated.id]: false,
        };
      }

      autoSaveQueuedRef.current[updated.id] = true;
      return current;
    });

    return updated;
  }, []);

  const handleUndo = () => {
    if (!selectedSlide) {
      return;
    }

    setHistory((current) => {
      const state = current[selectedSlide.id];
      if (!state?.past?.length) {
        return current;
      }

      const previousDraft = state.past[state.past.length - 1];
      const currentDraft = drafts[selectedSlide.id] || createFallbackEditorState(selectedSlide);

      setDrafts((draftState) => ({
        ...draftState,
        [selectedSlide.id]: cloneDraft(previousDraft),
      }));
      setDirtyDrafts((dirtyState) => ({
        ...dirtyState,
        [selectedSlide.id]: true,
      }));
      setAutoSaveStatus('dirty');

      return {
        ...current,
        [selectedSlide.id]: {
          past: state.past.slice(0, -1),
          future: [cloneDraft(currentDraft), ...(state.future || [])].slice(0, 30),
        },
      };
    });
  };

  const handleRedo = () => {
    if (!selectedSlide) {
      return;
    }

    setHistory((current) => {
      const state = current[selectedSlide.id];
      if (!state?.future?.length) {
        return current;
      }

      const nextDraft = state.future[0];
      const currentDraft = drafts[selectedSlide.id] || createFallbackEditorState(selectedSlide);

      setDrafts((draftState) => ({
        ...draftState,
        [selectedSlide.id]: cloneDraft(nextDraft),
      }));
      setDirtyDrafts((dirtyState) => ({
        ...dirtyState,
        [selectedSlide.id]: true,
      }));
      setAutoSaveStatus('dirty');

      return {
        ...current,
        [selectedSlide.id]: {
          past: [...(state.past || []), cloneDraft(currentDraft)].slice(-30),
          future: state.future.slice(1),
        },
      };
    });
  };

  const handleSaveSlide = async () => {
    if (!deck || !selectedSlide || !selectedDraft) {
      return;
    }

    try {
      setError('');
      const manualUpdated = await saveSlideDraft(selectedSlide.id, cloneDraft(selectedDraft), { manual: true });
      setAutoSaveStatus('saved');
      showToast({
        type: 'success',
        message: language === 'vi' ? `Da luu slide ${manualUpdated?.slideIndex || selectedSlide.slideIndex}.` : `Saved slide ${manualUpdated?.slideIndex || selectedSlide.slideIndex}.`,
      });
      return;
    } catch (err) {
      console.error(err);
      setAutoSaveStatus('error');
      setError(language === 'vi' ? 'Khong luu duoc slide hien tai.' : 'Could not save the current slide.');
    }
  };

  const performAutoSave = useCallback(async (slideId) => {
    if (!slideId) {
      return;
    }

    if (autoSaveInFlightRef.current[slideId]) {
      autoSaveQueuedRef.current[slideId] = true;
      return;
    }

    const draftSnapshot = latestDraftsRef.current[slideId]
      ? cloneDraft(latestDraftsRef.current[slideId])
      : null;

    if (!draftSnapshot || !latestDirtyDraftsRef.current[slideId]) {
      return;
    }

    const savedSerialized = JSON.stringify(draftSnapshot);

    try {
      autoSaveInFlightRef.current[slideId] = true;
      setAutoSaveStatus('saving');
      await saveSlideDraft(slideId, draftSnapshot, { manual: false });
      setAutoSaveStatus('saved');

      if (autoSaveStatusTimerRef.current) {
        clearTimeout(autoSaveStatusTimerRef.current);
      }
      autoSaveStatusTimerRef.current = setTimeout(() => {
        setAutoSaveStatus('idle');
      }, 1600);
    } catch (err) {
      console.error(err);
      setAutoSaveStatus('error');
    } finally {
      autoSaveInFlightRef.current[slideId] = false;

      const latestDraft = latestDraftsRef.current[slideId];
      const latestSerialized = latestDraft ? JSON.stringify(latestDraft) : '';
      const needsTrailingSave = autoSaveQueuedRef.current[slideId]
        || (latestDirtyDraftsRef.current[slideId] && latestSerialized && latestSerialized !== savedSerialized);

      autoSaveQueuedRef.current[slideId] = false;

      if (needsTrailingSave) {
        window.setTimeout(() => performAutoSave(slideId), 0);
      }
    }
  }, [saveSlideDraft]);

  useEffect(() => {
    const dirtySlideIds = Object.keys(dirtyDrafts).filter((slideId) => dirtyDrafts[slideId] && drafts[slideId]);

    if (!dirtySlideIds.length) {
      if (autoSaveTimerRef.current) {
        clearTimeout(autoSaveTimerRef.current);
        autoSaveTimerRef.current = null;
      }
      return undefined;
    }

    const selectedKey = String(selectedSlideId || '');
    const slideIdToSave = dirtySlideIds.includes(selectedKey) ? selectedKey : dirtySlideIds[0];

    if (autoSaveTimerRef.current) {
      clearTimeout(autoSaveTimerRef.current);
    }

    autoSaveTimerRef.current = setTimeout(() => {
      performAutoSave(slideIdToSave);
    }, AUTOSAVE_DEBOUNCE_MS);

    return undefined;
  }, [dirtyDrafts, drafts, selectedSlideId, performAutoSave]);

  useEffect(() => () => {
    if (autoSaveTimerRef.current) {
      clearTimeout(autoSaveTimerRef.current);
    }
    if (autoSaveStatusTimerRef.current) {
      clearTimeout(autoSaveStatusTimerRef.current);
    }
  }, []);

  const broadcastCanvasState = useCallback((slideId, editorState, operationType = 'replaceEditorState', elementId = null) => {
    if (!deck?.id || !slideId || !editorState) {
      return;
    }

    realtime.broadcastOperation({
      slideId,
      elementId,
      operationType,
      revision: editorState.revision || 0,
      payload: { editorState },
    });
  }, [deck?.id, realtime]);

  const commitCanvasState = useCallback((slideId, nextState, operationType, elementId, { pushHistory = true, save = true } = {}) => {
    if (!slideId || !nextState) {
      return;
    }

    if (pushHistory && selectedCanvasState) {
      canvasHistory.pushHistory(slideId, selectedCanvasState);
    }

    applyCanvasStateToDeck(slideId, nextState);
    broadcastCanvasState(slideId, nextState, operationType, elementId);

    if (save) {
      canvasAutosave.scheduleSave(slideId, nextState);
    }
  }, [applyCanvasStateToDeck, broadcastCanvasState, canvasAutosave, canvasHistory, selectedCanvasState]);

  const handlePatchCanvasElement = useCallback((elementId, patch, options = {}) => {
    if (!selectedSlide || !selectedCanvasState) {
      return;
    }

    const nextState = patchEditorElement(selectedCanvasState, elementId, patch);
    applyCanvasStateToDeck(selectedSlide.id, nextState);

    const now = Date.now();
    if (now - (realtimeThrottleRef.current[elementId] || 0) > 80) {
      realtimeThrottleRef.current[elementId] = now;
      broadcastCanvasState(selectedSlide.id, nextState, 'patchElement', elementId);
    }

    if (options.commit) {
      commitCanvasState(selectedSlide.id, nextState, 'patchElement', elementId);
    }
  }, [applyCanvasStateToDeck, broadcastCanvasState, commitCanvasState, selectedCanvasState, selectedSlide]);

  const handleCommitCanvasElement = useCallback((elementId, patch) => {
    handlePatchCanvasElement(elementId, patch, { commit: true });
  }, [handlePatchCanvasElement]);

  const handleSelectCanvasElement = useCallback((elementId) => {
    setSelectedElementId(elementId);
    if (selectedSlide) {
      realtime.broadcastSelection({ slideId: selectedSlide.id, elementId });
    }
  }, [realtime, selectedSlide]);

  const handleAddCanvasText = useCallback(() => {
    if (!selectedSlide || !selectedCanvasState) {
      return;
    }

    const element = createTextElement(selectedCanvasState, language === 'vi' ? 'Van ban moi' : 'New text');
    const nextState = addEditorElement(selectedCanvasState, element);
    setCanvasMode('layout');
    setSelectedElementId(element.id);
    commitCanvasState(selectedSlide.id, nextState, 'addElement', element.id);
  }, [commitCanvasState, language, selectedCanvasState, selectedSlide]);

  const readCanvasImageFile = useCallback((file) => new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result || ''));
    reader.onerror = () => reject(reader.error || new Error('Could not read image.'));
    reader.readAsDataURL(file);
  }), []);

  const handleAddCanvasImageClick = useCallback(() => {
    if (!selectedSlide || !selectedCanvasState) {
      return;
    }

    canvasImageInputRef.current?.click();
  }, [selectedCanvasState, selectedSlide]);

  const handleAddCanvasImageChange = useCallback(async (event) => {
    const file = event.target.files?.[0];
    event.target.value = '';

    if (!file || !selectedSlide || !selectedCanvasState) {
      return;
    }

    if (!canvasImportImageTypes.has(file.type)) {
      showToast({
        type: 'error',
        message: language === 'vi' ? 'Hay chon anh PNG, JPG, WebP hoac GIF.' : 'Please choose a PNG, JPG, WebP, or GIF image.',
      });
      return;
    }

    if (file.size > 5 * 1024 * 1024) {
      showToast({
        type: 'error',
        message: language === 'vi' ? 'Anh qua lon. Hay chon anh duoi 5 MB.' : 'Image is too large. Please choose an image under 5 MB.',
      });
      return;
    }

    try {
      const src = await readCanvasImageFile(file);
      const element = createImageElement(selectedCanvasState, { src, name: file.name });
      const nextState = addEditorElement(selectedCanvasState, element);
      setCanvasMode('layout');
      setSelectedElementId(element.id);
      commitCanvasState(selectedSlide.id, nextState, 'addElement', element.id);
      showToast({
        type: 'success',
        message: language === 'vi' ? 'Da them anh vao canvas.' : 'Image added to the canvas.',
        description: file.name,
      });
    } catch (err) {
      console.error(err);
      showToast({
        type: 'error',
        message: language === 'vi' ? 'Khong the them anh nay.' : 'Could not add this image.',
      });
    }
  }, [canvasImportImageTypes, commitCanvasState, language, readCanvasImageFile, selectedCanvasState, selectedSlide, showToast]);

  const handleDeleteCanvasElement = useCallback((elementId = selectedElementId) => {
    if (!selectedSlide || !selectedCanvasState || !elementId) {
      return;
    }

    const nextState = deleteEditorElement(selectedCanvasState, elementId);
    setSelectedElementId(null);
    commitCanvasState(selectedSlide.id, nextState, 'deleteElement', elementId);
  }, [commitCanvasState, selectedCanvasState, selectedElementId, selectedSlide]);

  const handleDuplicateCanvasElement = useCallback((elementId = selectedElementId) => {
    if (!selectedSlide || !selectedCanvasState || !elementId) {
      return;
    }

    const nextState = duplicateEditorElement(selectedCanvasState, elementId);
    const duplicated = nextState.elements.find((element) => !selectedCanvasState.elements.some((existing) => existing.id === element.id));
    setSelectedElementId(duplicated?.id || elementId);
    commitCanvasState(selectedSlide.id, nextState, 'duplicateElement', duplicated?.id || elementId);
  }, [commitCanvasState, selectedCanvasState, selectedElementId, selectedSlide]);

  const handleReorderCanvasElement = useCallback((elementId = selectedElementId, direction = 'forward') => {
    if (!selectedSlide || !selectedCanvasState || !elementId) {
      return;
    }

    const nextState = reorderEditorElement(selectedCanvasState, elementId, direction);
    commitCanvasState(selectedSlide.id, nextState, direction === 'forward' ? 'bringForward' : 'sendBackward', elementId);
  }, [commitCanvasState, selectedCanvasState, selectedElementId, selectedSlide]);

  const handleCopyCanvasElement = useCallback(() => {
    if (selectedCanvasElement) {
      setCopiedElement(selectedCanvasElement);
    }
  }, [selectedCanvasElement]);

  const handlePasteCanvasElement = useCallback(() => {
    if (!selectedSlide || !selectedCanvasState || !copiedElement) {
      return;
    }

    const nextState = addEditorElement(selectedCanvasState, {
      ...copiedElement,
      id: `${copiedElement.id}-paste-${Date.now()}`,
      x: copiedElement.x + 40,
      y: copiedElement.y + 40,
      zIndex: Math.max(...selectedCanvasState.elements.map((element) => element.zIndex), 0) + 10,
    });
    const pasted = nextState.elements.find((element) => !selectedCanvasState.elements.some((existing) => existing.id === element.id));
    setSelectedElementId(pasted?.id || null);
    commitCanvasState(selectedSlide.id, nextState, 'pasteElement', pasted?.id);
  }, [commitCanvasState, copiedElement, selectedCanvasState, selectedSlide]);

  const handleCanvasUndo = useCallback(() => {
    if (!selectedSlide || !selectedCanvasState) {
      return;
    }

    const previous = canvasHistory.undo(selectedSlide.id, selectedCanvasState);
    if (previous) {
      commitCanvasState(selectedSlide.id, previous, 'undo', selectedElementId, { pushHistory: false });
    }
  }, [canvasHistory, commitCanvasState, selectedCanvasState, selectedElementId, selectedSlide]);

  const handleCanvasRedo = useCallback(() => {
    if (!selectedSlide || !selectedCanvasState) {
      return;
    }

    const next = canvasHistory.redo(selectedSlide.id, selectedCanvasState);
    if (next) {
      commitCanvasState(selectedSlide.id, next, 'redo', selectedElementId, { pushHistory: false });
    }
  }, [canvasHistory, commitCanvasState, selectedCanvasState, selectedElementId, selectedSlide]);

  const handleSaveCanvasLayout = useCallback(async () => {
    if (!selectedSlide || !selectedCanvasState) {
      return;
    }

    try {
      await canvasAutosave.flushSave(selectedSlide.id, selectedCanvasState);
      showToast({
        type: 'success',
        message: language === 'vi' ? 'Da luu layout slide.' : 'Slide layout saved.',
      });
    } catch (err) {
      console.error(err);
      setError(getApiErrorMessage(err, language === 'vi' ? 'Khong the luu layout.' : 'Could not save layout.'));
    }
  }, [canvasAutosave, language, selectedCanvasState, selectedSlide, showToast]);

  const handleMoveCanvasElement = useCallback((movement) => {
    if (!selectedCanvasElement) {
      return;
    }

    handleCommitCanvasElement(selectedCanvasElement.id, {
      x: selectedCanvasElement.x + movement.x,
      y: selectedCanvasElement.y + movement.y,
    });
  }, [handleCommitCanvasElement, selectedCanvasElement]);

  useSlideEditorShortcuts({
    active: isLayoutEditMode,
    selectedElementId,
    onBringForward: () => handleReorderCanvasElement(selectedElementId, 'forward'),
    onClearSelection: () => setSelectedElementId(null),
    onCopy: handleCopyCanvasElement,
    onDelete: () => handleDeleteCanvasElement(selectedElementId),
    onDuplicate: () => handleDuplicateCanvasElement(selectedElementId),
    onMove: handleMoveCanvasElement,
    onPaste: handlePasteCanvasElement,
    onRedo: handleCanvasRedo,
    onSave: handleSaveCanvasLayout,
    onSendBackward: () => handleReorderCanvasElement(selectedElementId, 'backward'),
    onUndo: handleCanvasUndo,
  });

  useEffect(() => {
    const handlePointerDown = (event) => {
      const target = event.target;

      if (!(target instanceof Element)) {
        return;
      }

      const keepOpenSelector = [
        '.folder-editable-block',
        '.folder-floating-toolbar',
        '.folder-inline-insert',
        '.folder-inline-insert-popover',
        '.folder-properties-panel',
      ].join(',');

      if (target.closest(keepOpenSelector)) {
        return;
      }

      setSelectedEditorField(null);
      setInsertMenuField(null);
    };

    document.addEventListener('pointerdown', handlePointerDown, true);
    return () => {
      document.removeEventListener('pointerdown', handlePointerDown, true);
    };
  }, []);

  const handleUploadClick = () => {
    fileInputRef.current?.click();
  };

  const handleSourceUpload = async (event) => {
    const files = Array.from(event.target.files || []);
    event.target.value = '';

    if (!files.length) {
      return;
    }

    try {
      setUploading(true);
      setError('');
      for (const file of files) {
        await workspaceService.uploadSource(workspaceId, file);
      }

      showToast({
        type: 'success',
        message: language === 'vi' ? 'Đã thêm nguồn vào workspace.' : 'Added the source to the workspace.',
      });
      await loadWorkspace({ silent: true });
    } catch (err) {
      console.error(err);
      setError(getApiErrorMessage(err, language === 'vi' ? 'Không upload được source cho workspace này.' : 'Could not upload sources for this workspace.'));
    } finally {
      setUploading(false);
    }
  };

  const toggleSourceSelection = async (source) => {
    const nextIncluded = !(source.includeInWorkspaceSlides ?? source.includeInFolderSlides);

    try {
      setError('');
      await workspaceService.updateSourceSelection(
        workspaceId,
        source.id,
        nextIncluded
      );
      if (nextIncluded) {
        if (source.id !== selectedSourceId) {
          setSelectedSectionIds([]);
        }
        setSelectedSourceId(source.id);
      } else if (source.id === selectedSourceId) {
        const fallbackSource = readySources.find((item) => (
          item.id !== source.id && (item.includeInWorkspaceSlides ?? item.includeInFolderSlides)
        ));
        setSelectedSourceId(fallbackSource?.id ?? null);
        setSelectedSectionIds([]);
      }
      showToast({
        type: 'success',
        message: nextIncluded
          ? (language === 'vi' ? `Đã đưa ${source.fileName} vào tập nguồn sinh slide.` : `Added ${source.fileName} to the slide source set.`)
          : (language === 'vi' ? `Đã bỏ ${source.fileName} khỏi tập nguồn sinh slide.` : `Removed ${source.fileName} from the slide source set.`),
      });
      await loadWorkspace({ silent: true });
    } catch (err) {
      console.error(err);
      setError(language === 'vi' ? 'Không cập nhật được trạng thái chọn nguồn.' : 'Could not update the source selection state.');
    }
  };
  void toggleSourceSelection;

  const handleFocusSource = (source) => {
    if (!source || !(source.includeInWorkspaceSlides ?? source.includeInFolderSlides)) {
      return;
    }

    if (source.id !== selectedSourceId) {
      setSelectedSourceId(source.id);
      setSelectedSectionIds([]);
    }
  };

  const handleAnalyzeStructure = async () => {
    if (!selectedSource || isAnalyzingStructure) {
      return;
    }

    try {
      setIsAnalyzingStructure(true);
      setError('');
      await documentService.analyzeStructure(selectedSource.id);
      await loadWorkspace({ silent: true });
      showToast({
        type: 'success',
        message: language === 'vi' ? 'Đã cập nhật cấu trúc tài liệu.' : 'Document structure updated.',
      });
    } catch (err) {
      console.error(err);
      setError(getApiErrorMessage(err, language === 'vi' ? 'Không phân tích lại được cấu trúc tài liệu này.' : 'Could not re-analyze this document structure.'));
    } finally {
      setIsAnalyzingStructure(false);
    }
  };

  const handleToggleSection = (sectionKey) => {
    if (!selectedSource) {
      return;
    }

    const scopedId = buildScopedSectionId(selectedSource.id, sectionKey);
    setSelectedSectionIds((current) => (
      current.includes(scopedId)
        ? current.filter((id) => id !== scopedId)
        : [...current, scopedId]
    ));
  };

  const handleSelectAllSections = () => {
    if (!selectedSource) {
      return;
    }

    setSelectedSectionIds(selectableSections.map((section) => buildScopedSectionId(selectedSource.id, section.sectionKey)));
  };

  const handleClearSections = () => {
    setSelectedSectionIds([]);
  };

  const handleToggleSectionPreview = (scopedId) => {
    setExpandedSectionIds((current) => (
      current.includes(scopedId)
        ? current.filter((id) => id !== scopedId)
        : [...current, scopedId]
    ));
  };

  const handleOpenScopePicker = (source) => {
    if (!source) {
      return;
    }

    if (source.id !== selectedSourceId) {
      setSelectedSourceId(source.id);
    }

    setIsScopePickerOpen(true);
  };

  const handleCloseScopePicker = () => {
    setIsScopePickerOpen(false);
    setExpandedSectionIds([]);
  };

  const handleGenerateDeck = async () => {
    if (generateDisabledReason) {
      setError(generateDisabledReason);
      return;
    }

    const readinessDecision = confirmGenerationReadiness(getDocumentReadiness(selectedSource), language);
    if (!readinessDecision.allowed) {
      return;
    }

    try {
    setError('');
    const response = await slideService.startGenerateSlidesForFolder(workspaceId, {
      ...brief,
      sourceIds: [selectedSource.id],
      selectedSectionIds,
      mode: brief.mode,
      scopePolicy: 'selected-sections-only',
      confirmLowConfidence: readinessDecision.confirmed,
    });

    setScopeRecommendation(response.scopeRecommendation || null);
    setJobId(response.jobId || response.progress?.jobId || null);
    setProgress(normalizeProgressState(response.progress, {
      jobId: response.jobId,
      status: response.status,
      stageLabel: language === 'vi' ? 'Chờ xử lý' : 'Queued',
      message: language === 'vi'
        ? 'Đã tạo job sinh slide cấp workspace'
        : 'Workspace slide generation job created',
    }));
    showToast({
      type: 'info',
      message: language === 'vi' ? 'Đã bắt đầu tạo slide deck.' : 'Started generating the slide deck.',
      description: language === 'vi'
        ? 'Tiến trình sẽ hiển thị trong progress card của workspace.'
        : 'Progress will continue in the workspace progress card.',
    });

    await loadWorkspace({ silent: true });
    } catch (err) {
    console.error(err);
    setError(isSlideSchemaUnavailable(err)
      ? t('slides.errors.schemaUnavailable')
      : (language === 'vi'
        ? 'Không bắt đầu được quá trình sinh slide cấp workspace.'
        : 'Could not start workspace slide generation.'));
    }
  };

  const handleRefreshImages = async () => {
    if (!deck || !selectedSlide) {
      return;
    }

    try {
      setMediaBusy(true);
      setError('');
      const updated = await slideService.refreshSlideItemImages(deck.id, selectedSlide.id);
      setDeck((current) => ({
        ...current,
        items: current.items.map((item) => (item.id === updated.id ? updated : item)),
      }));
      setMediaOpen(true);
      showToast({
        type: 'success',
        message: language === 'vi' ? `Đã làm mới image candidates cho slide ${updated.slideIndex}.` : `Refreshed image candidates for slide ${updated.slideIndex}.`,
      });
    } catch (err) {
      console.error(err);
      setError(language === 'vi' ? 'Không refresh được image candidates cho slide này.' : 'Could not refresh image candidates for this slide.');
    } finally {
      setMediaBusy(false);
    }
  };

  const handleGenerateQuestions = async () => {
    if (!selectedSourceDocumentId || selectedSource?.status !== 3 || isActiveProgress(questionProgress)) {
      return;
    }

    navigate(`/question-studio/${selectedSourceDocumentId}`);
  };

  const handleSelectImage = async (candidateKey) => {
    if (!deck || !selectedSlide) {
      return;
    }

    try {
      setMediaBusy(true);
      setError('');
      const updated = await slideService.selectSlideItemImage(deck.id, selectedSlide.id, candidateKey);
      setDeck((current) => ({
        ...current,
        items: current.items.map((item) => (item.id === updated.id ? updated : item)),
      }));
      showToast({
        type: 'success',
        message: language === 'vi' ? `Đã chọn ảnh cho slide ${updated.slideIndex}.` : `Selected an image for slide ${updated.slideIndex}.`,
      });
    } catch (err) {
      console.error(err);
      setError(language === 'vi' ? 'Không chọn được image candidate này.' : 'Could not select this image candidate.');
    } finally {
      setMediaBusy(false);
    }
  };

  const handleDeleteFolder = async () => {
    if (!folder || !window.confirm(language === 'vi' ? 'Xóa workspace này và toàn bộ source bên trong?' : 'Delete this workspace and all sources inside it?')) {
      return;
    }

    try {
      await workspaceService.remove(folder.id);
      navigate('/workspaces');
    } catch (err) {
      console.error(err);
      setError(getApiErrorMessage(err, language === 'vi' ? 'Không xóa được workspace.' : 'Could not delete the workspace.'));
    }
  };

  const isExportDisabled = !deck || isGeneratingDeck || Boolean(exportingFormat);

  const handleDownloadHtml = async () => {
    if (!deck || isExportDisabled) {
      return;
    }

    try {
      setExportingFormat('html');
      const result = await slideService.exportDeckHtml(deck.id);
      showToast({
        type: 'success',
        message: language === 'vi' ? 'Đã tải file HTML.' : 'HTML file downloaded.',
        description: result.filename,
      });
    } catch (err) {
      console.error(err);
      const message = getApiErrorMessage(
        err,
        language === 'vi' ? 'Không thể xuất deck.' : 'Could not export the deck.'
      );
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
      showToast({
        type: 'error',
        message: language === 'vi' ? 'Trình duyệt đã chặn tab in.' : 'The browser blocked the print tab.',
      });
      return;
    }
    printWindow.opener = null;

    try {
      setExportingFormat('print');
      const blob = await slideService.getDeckPrintHtml(deck.id);
      const url = window.URL.createObjectURL(blob);
      printWindow.location.href = url;
      window.setTimeout(() => window.URL.revokeObjectURL(url), 60000);
      showToast({
        type: 'success',
        message: language === 'vi' ? 'Đã mở chế độ In / Lưu PDF.' : 'Print / Save as PDF view opened.',
      });
    } catch (err) {
      console.error(err);
      printWindow.close();
      const message = getApiErrorMessage(
        err,
        language === 'vi' ? 'Không thể mở chế độ in.' : 'Could not open the print view.'
      );
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
      const result = await slideService.exportDeckPptx(deck.id);
      showToast({
        type: 'success',
        message: language === 'vi' ? 'Đã tải file PPTX.' : 'PPTX file downloaded.',
        description: result.filename,
      });
    } catch (err) {
      console.error(err);
      const message = getApiErrorMessage(
        err,
        language === 'vi' ? 'Không thể xuất PPTX.' : 'Could not export PPTX.'
      );
      setError(message);
      showToast({ type: 'error', message });
    } finally {
      setExportingFormat('');
    }
  };

  const slideItems = deck?.items || [];
  const normalizedFilter = filterText.trim().toLowerCase();
  const filteredSources = normalizedFilter
    ? sources.filter((source) => [source.fileName, source.summary]
      .filter(Boolean)
      .some((value) => String(value).toLowerCase().includes(normalizedFilter)))
    : sources;
  const runningSourceVm = sourceViewModels.find(({ vm }) => vm.isActive) || null;
  const topbarDeckProgress = activeProgress && isActiveProgress(activeProgress)
    ? activeProgress
    : null;
  const topbarDeckProgressPercent = clampPercent(Number(topbarDeckProgress?.percent || 0)) ?? 0;
  const topbarProgress = topbarDeckProgress || runningSourceVm?.vm.progressState || null;
  const topbarCounter = getProgressCounterLabel(topbarProgress, { language });
  const topbarEta = topbarProgress && isActiveProgress(topbarProgress)
    ? (formatEta(topbarProgress?.estimatedRemainingSeconds, { language }) || t('slides.sourceProcessing.etaEstimating'))
    : null;
  const topbarLiveSummary = topbarDeckProgress
    ? [
        t('slides.generationStatus.liveTitle'),
        `${Math.round(topbarDeckProgressPercent)}%`,
        topbarDeckProgress.stageLabel || topbarDeckProgress.message || t('slides.generatingSlides'),
      ].filter(Boolean).join(' · ')
    : runningSourceVm
      ? [
          runningSourceVm.vm.statusLabel,
          runningSourceVm.vm.hasProgressPercent ? `${Math.round(runningSourceVm.vm.progressPercent)}%` : t('slides.sourceProcessing.indeterminateLabel'),
          runningSourceVm.vm.stageMessage,
        ].filter(Boolean).join(' · ')
      : null;
  const topbarLiveClass = topbarDeckProgress
    ? 'folder-studio-live folder-studio-live-pill'
    : 'folder-studio-live folder-studio-live-pill folder-studio-live-source';
  const activeFieldState = selectedDraft?.[activeField] || null;
  const activeHistory = selectedSlide ? (history[selectedSlide.id] || { past: [], future: [] }) : { past: [], future: [] };
  const toolbarLabels = t('slides.editorToolbar');
  const autoSaveLabels = t('slides.editorAutosave');
  const activeTextColor = activeFieldState?.textColor || DEFAULT_TEXT_COLOR;
  const activeHighlightColor = activeFieldState?.highlightColor || DEFAULT_HIGHLIGHT_COLOR;
  const activeListStyle = activeFieldState?.listStyle || (activeFieldState?.bullet ? 'bullet' : 'none');
  const selectedBodyListStyle = selectedDraft?.body?.listStyle || (selectedDraft?.body?.bullet ? 'bullet' : 'none');
  const activeLineHeight = activeFieldState?.lineHeight || 1.6;
  const activeScript = activeFieldState?.script || 'normal';
  const alignOptions = [
    { key: 'left', label: toolbarLabels.alignLeft, icon: LuAlignLeft },
    { key: 'center', label: toolbarLabels.alignCenter, icon: LuAlignCenter },
    { key: 'right', label: toolbarLabels.alignRight, icon: LuAlignRight },
  ];
  const selectedImage = selectedImageVm?.selectedImage || null;
  const selectedSlideNeedsMedia = selectedImageVm?.needsImage !== false;
  const deckProgressActive = Boolean(activeProgress && isActiveProgress(activeProgress));
  const generateDisabledReason = deckProgressActive
    ? t('slides.folderGenerate.disabledGenerating')
    : !selectedSource
      ? t('slides.folderGenerate.disabledNoSource')
      : selectedSource.status !== 3
        ? t('slides.folderGenerate.disabledSourceProcessing')
        : !selectedSectionIds.length
          ? t('slides.folderGenerate.disabledNoScope')
          : '';
  const canGenerate = !generateDisabledReason;
  const selectedSourceDocumentId = selectedSource?.documentId ?? selectedSource?.DocumentId ?? selectedSource?.id ?? null;
  const selectedSourceQuestionsCount = Number(selectedSource?.questionsCount ?? selectedSource?.QuestionsCount ?? 0);
  const selectedSourceHasQuestions = selectedSourceQuestionsCount > 0;
  const canGenerateQuestions = Boolean(selectedSourceDocumentId && selectedSource?.status === 3) && !isActiveProgress(questionProgress);
  const studyHubEnabled = Boolean(selectedSourceDocumentId && selectedSource?.status === 3 && selectedSourceHasQuestions);
  const streakModeHint = !selectedSourceHasQuestions
    ? t('slides.studyActions.streakHint')
    : '';
  const questionActionTitle = selectedSourceHasQuestions
    ? (language === 'vi' ? 'Tạo lại question bank' : 'Regenerate question bank')
    : (language === 'vi' ? 'Tạo câu hỏi ôn tập' : 'Generate review questions');
  const questionActionDetail = !selectedSource
    ? (language === 'vi'
      ? 'Chọn một source Completed để tạo question bank.'
      : 'Select a completed source to generate a question bank.')
    : selectedSource.status !== 3
      ? (language === 'vi'
        ? 'Source này vẫn đang xử lý. Hoàn tất xong mới tạo được câu hỏi.'
        : 'This source is still processing. Wait until it is completed.')
      : selectedSourceHasQuestions
        ? (language === 'vi'
          ? `Tạo lại sẽ thay thế question bank hiện tại (${selectedSourceQuestionsCount} câu).`
          : `Regenerating will replace the active question bank (${selectedSourceQuestionsCount} questions).`)
        : (language === 'vi'
          ? 'Sinh quiz và flow ôn tập từ source đang chọn'
          : 'Generate quiz-ready review questions from the selected source');

  useEffect(() => {
    if (!questionProgress || isActiveProgress(questionProgress)) {
      return;
    }

    if (!selectedSourceDocumentId || Number(questionProgress.documentId) !== Number(selectedSourceDocumentId)) {
      setQuestionProgress(null);
      setQuestionError('');
    }
  }, [questionProgress, selectedSourceDocumentId]);
  const hasAnySources = sources.length > 0;
  const hasCompletedSources = readySources.length > 0;
  const previewProcessingVm = runningSourceVm?.vm || sourceViewModels.find(({ vm }) => vm.isPending || vm.isFailed)?.vm || null;
  const hasSelectedScope = selectedSectionIds.length > 0;
  const deckReady = Boolean(deck?.items?.length);
  const emptyStateCopy = !hasAnySources
    ? {
        tone: 'empty',
        title: language === 'vi' ? 'Bắt đầu bằng source đầu tiên' : 'Start with the first source',
        body: language === 'vi'
          ? 'Workspace này đang trống. Thêm PDF, DOCX, PPTX, ảnh hoặc TXT để mở luồng chọn phạm vi và tạo deck.'
          : 'This workspace is empty. Add a PDF, DOCX, PPTX, image, or TXT file to unlock scope selection and deck generation.',
        primaryLabel: language === 'vi' ? 'Thêm nguồn' : 'Add source',
        secondaryLabel: language === 'vi' ? 'Chưa thể tạo deck' : 'Deck not ready',
      }
    : hasCompletedSources && !hasSelectedScope
      ? {
          tone: 'scope',
          title: language === 'vi' ? 'Chọn phạm vi trước khi tạo deck' : 'Choose scope before generating a deck',
          body: language === 'vi'
            ? 'Đã có source hoàn tất. Mở thẻ Phạm vi nội dung, chọn các section phù hợp, rồi tạo deck từ nội dung thật.'
            : 'Completed sources are ready. Open Content scope, select the relevant sections, then generate a deck from real content.',
          primaryLabel: language === 'vi' ? 'Thêm nguồn khác' : 'Add another source',
          secondaryLabel: language === 'vi' ? 'Tạo deck' : 'Generate deck',
        }
      : {
          tone: 'ready',
          title: language === 'vi' ? 'Workspace đã sẵn sàng tạo deck' : 'Workspace is ready for deck generation',
          body: language === 'vi'
            ? 'Source và phạm vi đã sẵn sàng. Kiểm tra brief ở panel bên phải rồi tạo deck để bắt đầu chỉnh sửa.'
            : 'Source and scope are ready. Review the brief in the right panel, then generate a deck to start editing.',
          primaryLabel: language === 'vi' ? 'Thêm nguồn' : 'Add source',
          secondaryLabel: language === 'vi' ? 'Tạo deck' : 'Generate deck',
        };
  const workspaceReadinessItems = [
    {
      key: 'source',
      label: language === 'vi' ? 'Nguồn' : 'Source',
      value: hasCompletedSources
        ? (language === 'vi' ? `${readySources.length} đã hoàn tất` : `${readySources.length} completed`)
        : hasAnySources
          ? (language === 'vi' ? 'Đang xử lý' : 'Processing')
          : (language === 'vi' ? 'Chưa có' : 'Missing'),
      tone: hasCompletedSources ? 'ready' : hasAnySources ? 'working' : 'blocked',
    },
    {
      key: 'scope',
      label: language === 'vi' ? 'Phạm vi' : 'Scope',
      value: hasSelectedScope
        ? (language === 'vi' ? `${selectedSectionIds.length} phần` : `${selectedSectionIds.length} sections`)
        : (language === 'vi' ? 'Chưa chọn' : 'Not selected'),
      tone: hasSelectedScope ? 'ready' : 'blocked',
    },
    {
      key: 'deck',
      label: 'Deck',
      value: deckReady
        ? `${deck.items.length} ${language === 'vi' ? 'slide' : 'slides'}`
        : (canGenerate ? (language === 'vi' ? 'Có thể tạo' : 'Ready to create') : (language === 'vi' ? 'Chờ nguồn' : 'Waiting')),
      tone: deckReady || canGenerate ? 'ready' : 'blocked',
    },
  ];
  const qualityIssues = selectedSlide?.quality?.issues || [];
  const qualityScore = selectedSlide?.quality?.score;
  const topbarMeta = [
    language === 'vi' ? `${sources.length} nguồn` : `${sources.length} sources`,
    language === 'vi' ? `${selectedReadySources.length} source được chọn` : `${selectedReadySources.length} selected sources`,
    deck?.items?.length ? `${deck.items.length} ${language === 'vi' ? 'slide' : 'slides'}` : (language === 'vi' ? 'Chưa có deck' : 'No deck yet'),
    `${language === 'vi' ? 'Cập nhật' : 'Updated'}: ${formatRelativeTimeLabel(deck?.updatedAt || folder?.updatedAt)}`,
  ];

  const getScopedSelectionCount = (source) => {
    if (!source || source.id !== selectedSourceId) {
      return 0;
    }

    return selectedSectionIds.length;
  };

  const getScopeActionLabel = (source) => {
    const selectedCount = getScopedSelectionCount(source);
    if (source?.id === selectedSourceId && selectedCount > 0) {
      return t('slides.scopePicker.editScope');
    }

    if (source?.id === selectedSourceId) {
      return t('slides.scopePicker.chooseScope');
    }

    return t('slides.scopePicker.viewScope');
  };

  const getScopeDisabledReason = (source) => {
    if (!source) {
      return '';
    }

    if (source.status !== 3) {
      return t('slides.scopePicker.processingHint');
    }

    if (!getSelectableSections(source).length) {
      return t('slides.scopePicker.noStructureHint');
    }

    return '';
  };

  const editorInsertLabels = t('slides.editorInsert');
  const activeEditorLabel = editorInsertLabels?.fields?.[selectedEditorField]
    || (selectedEditorField || '').toUpperCase();

  const renderFloatingToolbar = (fieldKey) => {
    if (!selectedDraft || selectedEditorField !== fieldKey || activeField !== fieldKey || !activeFieldState) {
      return null;
    }

    return (
      <div className="folder-floating-toolbar" aria-label={toolbarLabels.label} onClick={(event) => event.stopPropagation()}>
        <div className="folder-floating-toolbar-selects">
          <select
            value={activeFieldState.fontFamily || 'Lexend'}
            onChange={(event) => handleStyleChange((block) => ({ ...block, fontFamily: event.target.value }))}
            aria-label={toolbarLabels.fontFamily}
            title={toolbarLabels.fontFamily}
          >
            {FONT_OPTIONS.map((option) => (
              <option key={option} value={option}>{option}</option>
            ))}
          </select>
          <select
            value={activeFieldState.fontSize || 18}
            onChange={(event) => handleStyleChange((block) => ({ ...block, fontSize: Number(event.target.value) }))}
            aria-label={toolbarLabels.fontSize}
            title={toolbarLabels.fontSize}
          >
            {FONT_SIZES.map((option) => (
              <option key={option} value={option}>{option}</option>
            ))}
          </select>
        </div>
        <WorkspaceToolbarButton active={activeFieldState.bold} label={toolbarLabels.bold} onClick={() => handleStyleChange((block) => ({ ...block, bold: !block.bold }))}>
          <LuBold aria-hidden="true" />
        </WorkspaceToolbarButton>
        <WorkspaceToolbarButton active={activeFieldState.italic} label={toolbarLabels.italic} onClick={() => handleStyleChange((block) => ({ ...block, italic: !block.italic }))}>
          <LuItalic aria-hidden="true" />
        </WorkspaceToolbarButton>
        <WorkspaceToolbarButton active={activeFieldState.underline} label={toolbarLabels.underline} onClick={() => handleStyleChange((block) => ({ ...block, underline: !block.underline }))}>
          <LuUnderline aria-hidden="true" />
        </WorkspaceToolbarButton>
        <div className="folder-floating-colors" role="group" aria-label={toolbarLabels.textColor}>
          {TEXT_COLOR_OPTIONS.map((color) => (
            <button
              type="button"
              key={color}
              className={`folder-floating-color${activeTextColor === color ? ' active' : ''}`}
              style={{ '--text-swatch': color }}
              onClick={() => handleStyleChange((block) => ({ ...block, textColor: color }))}
              aria-label={`${toolbarLabels.textColor}: ${color}`}
              title={`${toolbarLabels.textColor}: ${color}`}
            />
          ))}
        </div>
      </div>
    );
  };

  const renderInlineInsertMenu = (fieldKey) => {
    if (!selectedDraft) {
      return null;
    }

    const isOpen = insertMenuField === fieldKey;
    const options = [
      { key: 'text', label: editorInsertLabels.text, icon: LuType },
      { key: 'heading', label: editorInsertLabels.heading, icon: LuBold },
      { key: 'image', label: editorInsertLabels.image, icon: LuImage },
    ];

    return (
      <div className="folder-inline-insert" onClick={(event) => event.stopPropagation()}>
        <button
          type="button"
          className="folder-inline-insert-button"
          onClick={() => setInsertMenuField(isOpen ? null : fieldKey)}
          aria-label={editorInsertLabels.open}
          title={editorInsertLabels.open}
        >
          <LuPlus aria-hidden="true" />
        </button>
        {isOpen && (
          <div className="folder-inline-insert-popover" role="menu">
            {options.map(({ key, label, icon: Icon }) => (
              <button
                key={key}
                type="button"
                role="menuitem"
                onClick={() => handleInsertChoice(key, fieldKey)}
              >
                <Icon aria-hidden="true" />
                <span>{label}</span>
              </button>
            ))}
          </div>
        )}
      </div>
    );
  };

  const renderAdvancedPropertiesPanel = () => {
    if (!selectedEditorField || !selectedDraft || !activeFieldState) {
      return null;
    }

    return (
      <div className="folder-properties-panel">
        <div className="folder-properties-head">
          <div>
            <div className="folder-studio-panel-title">{editorInsertLabels.propertiesTitle}</div>
            <strong>{activeEditorLabel}</strong>
          </div>
          <button
            type="button"
            className="folder-studio-mini-btn"
            onClick={() => {
              setSelectedEditorField(null);
              setInsertMenuField(null);
            }}
            aria-label={editorInsertLabels.closeProperties}
            title={editorInsertLabels.closeProperties}
          >
            <LuX aria-hidden="true" />
          </button>
        </div>

        <div className="folder-studio-action-section">
          <div className="folder-studio-section-label">{toolbarLabels.emphasisGroup}</div>
          <div className="folder-properties-select-grid">
            <label className="folder-studio-form-row">
              <span>{toolbarLabels.fontFamily}</span>
              <select value={activeFieldState.fontFamily || 'Lexend'} onChange={(event) => handleStyleChange((block) => ({ ...block, fontFamily: event.target.value }))}>
                {FONT_OPTIONS.map((option) => (
                  <option key={option} value={option}>{option}</option>
                ))}
              </select>
            </label>
            <label className="folder-studio-form-row">
              <span>{toolbarLabels.fontSize}</span>
              <select value={activeFieldState.fontSize || 18} onChange={(event) => handleStyleChange((block) => ({ ...block, fontSize: Number(event.target.value) }))}>
                {FONT_SIZES.map((option) => (
                  <option key={option} value={option}>{option}</option>
                ))}
              </select>
            </label>
          </div>
          <div className="folder-properties-control-row">
            <WorkspaceToolbarButton active={activeFieldState.bold} label={toolbarLabels.bold} onClick={() => handleStyleChange((block) => ({ ...block, bold: !block.bold }))}>
              <LuBold aria-hidden="true" />
            </WorkspaceToolbarButton>
            <WorkspaceToolbarButton active={activeFieldState.italic} label={toolbarLabels.italic} onClick={() => handleStyleChange((block) => ({ ...block, italic: !block.italic }))}>
              <LuItalic aria-hidden="true" />
            </WorkspaceToolbarButton>
            <WorkspaceToolbarButton active={activeFieldState.underline} label={toolbarLabels.underline} onClick={() => handleStyleChange((block) => ({ ...block, underline: !block.underline }))}>
              <LuUnderline aria-hidden="true" />
            </WorkspaceToolbarButton>
          </div>
          <div className="folder-properties-text-colors" role="group" aria-label={toolbarLabels.textColor}>
            {TEXT_COLOR_OPTIONS.map((color) => (
              <button
                type="button"
                key={color}
                className={`folder-floating-color${activeTextColor === color ? ' active' : ''}`}
                style={{ '--text-swatch': color }}
                onClick={() => handleStyleChange((block) => ({ ...block, textColor: color }))}
                aria-label={`${toolbarLabels.textColor}: ${color}`}
                title={`${toolbarLabels.textColor}: ${color}`}
              />
            ))}
          </div>
        </div>

        <div className="folder-studio-action-section">
          <div className="folder-studio-section-label">{toolbarLabels.alignGroup}</div>
          <div className="folder-properties-control-row">
            {alignOptions.map(({ key, label, icon: Icon }) => (
              <WorkspaceToolbarButton key={key} active={activeFieldState.align === key} label={label} onClick={() => handleStyleChange((block) => ({ ...block, align: key }))}>
                <Icon aria-hidden="true" />
              </WorkspaceToolbarButton>
            ))}
          </div>
        </div>

        <div className="folder-studio-action-section">
          <label className="folder-studio-form-row">
            <span>{toolbarLabels.lineHeight}</span>
            <select value={activeLineHeight} onChange={(event) => handleStyleChange((block) => ({ ...block, lineHeight: Number(event.target.value) }))}>
              {LINE_HEIGHT_OPTIONS.map((option) => (
                <option key={option} value={option}>{option}x</option>
              ))}
            </select>
          </label>
          <div className="folder-properties-control-row">
            <WorkspaceToolbarButton active={activeFieldState.strike} label={toolbarLabels.strike} onClick={() => handleStyleChange((block) => ({ ...block, strike: !block.strike }))}>
              <LuStrikethrough aria-hidden="true" />
            </WorkspaceToolbarButton>
            <WorkspaceToolbarButton active={activeScript === 'superscript'} label={toolbarLabels.superscript} onClick={() => handleScriptChange('superscript')}>
              <LuSuperscript aria-hidden="true" />
            </WorkspaceToolbarButton>
            <WorkspaceToolbarButton active={activeScript === 'subscript'} label={toolbarLabels.subscript} onClick={() => handleScriptChange('subscript')}>
              <LuSubscript aria-hidden="true" />
            </WorkspaceToolbarButton>
            <WorkspaceToolbarButton active={Boolean(activeFieldState.linkUrl)} label={toolbarLabels.link} onClick={handleLinkPrompt}>
              <LuLink2 aria-hidden="true" />
            </WorkspaceToolbarButton>
          </div>
        </div>

        <div className="folder-studio-action-section">
          <div className="folder-studio-section-label">{toolbarLabels.listGroup}</div>
          <div className="folder-properties-control-row">
            <WorkspaceToolbarButton active={activeListStyle === 'bullet'} disabled={activeField !== 'body'} label={toolbarLabels.bulletList} onClick={() => handleListStyleChange(activeListStyle === 'bullet' ? 'none' : 'bullet')}>
              <LuList aria-hidden="true" />
            </WorkspaceToolbarButton>
            <WorkspaceToolbarButton active={activeListStyle === 'numbered'} disabled={activeField !== 'body'} label={toolbarLabels.numberedList} onClick={() => handleListStyleChange(activeListStyle === 'numbered' ? 'none' : 'numbered')}>
              <LuListOrdered aria-hidden="true" />
            </WorkspaceToolbarButton>
            <WorkspaceToolbarButton label={toolbarLabels.outdent} onClick={() => handleIndentChange(-1)}>
              <LuIndentDecrease aria-hidden="true" />
            </WorkspaceToolbarButton>
            <WorkspaceToolbarButton label={toolbarLabels.indent} onClick={() => handleIndentChange(1)}>
              <LuIndentIncrease aria-hidden="true" />
            </WorkspaceToolbarButton>
          </div>
        </div>

        <div className="folder-studio-action-section">
          <div className="folder-studio-section-label">{toolbarLabels.colorGroup}</div>
          <div className="folder-properties-color-grid">
            {HIGHLIGHT_COLOR_OPTIONS.map((color) => (
              <WorkspaceColorButton
                key={color}
                active={activeHighlightColor === color}
                color={color}
                icon={LuHighlighter}
                label={color === DEFAULT_HIGHLIGHT_COLOR ? toolbarLabels.clearHighlight : `${toolbarLabels.highlight}: ${color}`}
                onClick={() => handleStyleChange((block) => ({ ...block, highlightColor: color }))}
              />
            ))}
          </div>
        </div>

        <div className="folder-studio-action-section">
          <div className="folder-studio-section-label">{toolbarLabels.historyGroup}</div>
          <div className="folder-properties-control-row">
            <WorkspaceToolbarButton disabled={!activeHistory.past.length} label={toolbarLabels.undo} onClick={handleUndo}>
              <LuUndo2 aria-hidden="true" />
            </WorkspaceToolbarButton>
            <WorkspaceToolbarButton disabled={!activeHistory.future.length} label={toolbarLabels.redo} onClick={handleRedo}>
              <LuRedo2 aria-hidden="true" />
            </WorkspaceToolbarButton>
            <WorkspaceToolbarButton disabled={!selectedSlide || !selectedSlideNeedsMedia} label={mediaOpen ? toolbarLabels.hideMedia : toolbarLabels.openMedia} onClick={() => setMediaOpen((current) => !current)}>
              {mediaOpen ? <LuPanelRightClose aria-hidden="true" /> : <LuPanelRightOpen aria-hidden="true" />}
            </WorkspaceToolbarButton>
          </div>
          <button type="button" className="folder-studio-action" onClick={handleSaveSlide} disabled={!selectedDraft}>
            <span className="folder-studio-action-icon"><LuSave aria-hidden="true" /></span>
            <span className="folder-studio-action-copy">
              <strong>{language === 'vi' ? 'Luu slide hien tai' : 'Save current slide'}</strong>
              <span>{language === 'vi' ? 'Luu cac thay doi cua block dang chon vao deck.' : 'Save the selected block changes into the deck.'}</span>
            </span>
          </button>
        </div>
      </div>
    );
  };

  const getSourceScopeStatusLabel = (sourceVm, source) => {
    if (sourceVm.isActive) {
      return sourceVm.statusLabel;
    }

    if (sourceVm.isFailed) {
      return sourceVm.failedLabel;
    }

    return normalizeStatusLabel(source.status);
  };

  if (loading) {
    return (
      <div className="loading">
        <div className="spinner"></div>
        <p>{language === 'vi' ? 'Đang tải workspace studio...' : 'Loading workspace studio...'}</p>
      </div>
    );
  }

  if (!folder) {
    return (
      <div className="card folder-studio-missing">
        <h2>{language === 'vi' ? 'Không tìm thấy workspace' : 'Workspace not found'}</h2>
        <p>{language === 'vi' ? 'Workspace này có thể đã bị xóa hoặc chưa được khởi tạo.' : 'This workspace may have been deleted or not initialized yet.'}</p>
        <button type="button" className="button" onClick={() => navigate('/workspaces')}>
          {t('slides.back')}
        </button>
      </div>
    );
  }

  return (
    <div className="folder-studio-page">
      <input
        ref={fileInputRef}
        type="file"
        multiple
        hidden
        onChange={handleSourceUpload}
        accept=".pdf,.doc,.docx,.ppt,.pptx,.png,.jpg,.jpeg,.webp,.txt"
      />
      <input
        ref={canvasImageInputRef}
        type="file"
        hidden
        onChange={handleAddCanvasImageChange}
        accept="image/png,image/jpeg,image/webp,image/gif"
      />

      {uploadNotice && <div className="alert alert-info">{uploadNotice}</div>}
      {error && <div className="alert alert-error">{error}</div>}
      {generationError && <div className="alert alert-error">{generationError}</div>}
      <section className={`folder-studio-shell tool-${activeTool || 'none'}`}>
        <div className="folder-studio-topbar">
          <button
            type="button"
            className="folder-studio-mini-btn"
            onClick={() => navigate('/workspaces')}
            aria-label={language === 'vi' ? 'Quay lại Workspaces' : 'Back to workspaces'}
          >
            <LuArrowLeft aria-hidden="true" />
          </button>

          <div className="folder-studio-topbar-copy">
            <strong>{folder.name}</strong>
            <div className="folder-studio-topbar-meta">
              {topbarMeta.map((item) => (
                <span key={item}>{item}</span>
              ))}
              {topbarLiveSummary && (
                <span className={topbarLiveClass}>
                  {topbarLiveSummary}
                  {topbarCounter ? ` | ${topbarCounter}` : ''}
                  {topbarEta ? ` | ETA ${topbarEta}` : ''}
                </span>
              )}
            </div>
          </div>

          <div className="folder-studio-topbar-modes" role="group" aria-label={language === 'vi' ? 'Che do studio' : 'Studio modes'}>
            <button
              type="button"
              className={`folder-studio-mode-chip${canvasMode === 'preview' ? ' active' : ''}`}
              disabled={!selectedSlide}
              onClick={handleEnterCanvasPreviewMode}
            >
              <LuMousePointer2 aria-hidden="true" />
              <span>Preview</span>
            </button>
            <button
              type="button"
              className={`folder-studio-mode-chip${isTextEditMode ? ' active' : ''}`}
              disabled={!selectedSlide}
              onClick={handleEnterCanvasTextMode}
            >
              <LuType aria-hidden="true" />
              <span>{language === 'vi' ? 'Sua text' : 'Edit text'}</span>
            </button>
            <button
              type="button"
              className={`folder-studio-mode-chip${isLayoutEditMode ? ' active' : ''}`}
              disabled={!selectedSlide}
              onClick={handleEnterCanvasLayoutMode}
            >
              <LuPanelRightOpen aria-hidden="true" />
              <span>{language === 'vi' ? 'Sua layout' : 'Edit layout'}</span>
            </button>
          </div>

          <div className="folder-studio-topbar-actions">
            <button
              type="button"
              className="folder-studio-mini-btn"
              onClick={() => loadWorkspace()}
              disabled={uploading}
            >
              <LuRefreshCw aria-hidden="true" />
              <span>{language === 'vi' ? 'Làm mới' : 'Refresh'}</span>
            </button>
            <button
              type="button"
              className="folder-studio-mini-btn"
              onClick={handleUploadClick}
              disabled={uploading}
            >
              <LuFilePlus2 aria-hidden="true" />
              <span>{uploading ? (language === 'vi' ? 'Đang thêm...' : 'Adding...') : (language === 'vi' ? 'Thêm nguồn' : 'Add source')}</span>
            </button>
            <button
              type="button"
              className="folder-studio-mini-primary folder-studio-topbar-primary"
              onClick={handleGenerateDeck}
              disabled={!canGenerate}
              title={generateDisabledReason || undefined}
            >
              <LuSparkles aria-hidden="true" />
              <span>{language === 'vi' ? 'Tạo deck' : 'Generate deck'}</span>
            </button>
            <button
              type="button"
              className="folder-studio-mini-btn folder-studio-panel-toggle"
              onClick={() => setActiveTool((current) => (current === 'actions' ? null : 'actions'))}
              aria-label={activeTool === 'actions'
                ? (language === 'vi' ? 'An actions drawer' : 'Hide actions drawer')
                : (language === 'vi' ? 'Mo actions drawer' : 'Show actions drawer')}
            >
              {activeTool === 'actions' ? <LuPanelRightClose aria-hidden="true" /> : <LuPanelRightOpen aria-hidden="true" />}
              {language === 'vi' ? 'Actions' : 'Actions'}
            </button>
          </div>
        </div>

        <nav className="folder-studio-rail" aria-label={language === 'vi' ? 'Cong cu studio' : 'Studio tools'}>
          {[
            { key: 'slides', label: language === 'vi' ? 'Slides' : 'Slides', icon: LuLayoutTemplate },
            { key: 'sources', label: language === 'vi' ? 'Sources' : 'Sources', icon: LuBookOpen },
            { key: 'text', label: language === 'vi' ? 'Text' : 'Text', icon: LuType },
            { key: 'elements', label: language === 'vi' ? 'Layers' : 'Layers', icon: LuLayers },
            { key: 'uploads', label: language === 'vi' ? 'Uploads' : 'Uploads', icon: LuUpload },
            { key: 'actions', label: language === 'vi' ? 'Actions' : 'Actions', icon: LuSparkles },
          ].map(({ key, label, icon: Icon }) => (
            <button
              key={key}
              type="button"
              className={activeTool === key ? 'active' : ''}
              onClick={() => setActiveTool((current) => (current === key ? null : key))}
              aria-label={label}
              title={label}
            >
              <Icon aria-hidden="true" />
              <span>{label}</span>
            </button>
          ))}
        </nav>

        <div className={`folder-studio-main drawer-${activeTool || 'closed'}`}>
          <aside className={`folder-studio-sidebar tab-${activeTool === 'slides' ? 'slides' : 'sources'}`}>
            <div className="folder-studio-panel-title">{language === 'vi' ? 'Điều hướng nội dung' : 'Content navigation'}</div>

            <div className="folder-studio-sidebar-tabs">
              <button
                type="button"
                className={`folder-studio-sidebar-tab${activeTool === 'slides' ? ' active' : ''}`}
                onClick={() => setActiveTool('slides')}
              >
                {language === 'vi' ? 'Cấu trúc slide' : 'Slides'}
              </button>
              <button
                type="button"
                className={`folder-studio-sidebar-tab${activeTool !== 'slides' ? ' active' : ''}`}
                onClick={() => setActiveTool('sources')}
              >
                {language === 'vi' ? 'Nguồn tài liệu' : 'Sources'}
              </button>
            </div>
            <div className="folder-studio-source-pane">
            <div className="folder-studio-panel-title">{language === 'vi' ? 'Nguồn & phạm vi' : 'Sources & scope'}</div>

            <div className="folder-studio-filter">
              <label className="sr-only" htmlFor="folder-studio-source-search">
                {language === 'vi' ? 'Tìm nguồn tài liệu' : 'Search document sources'}
              </label>
              <input
                id="folder-studio-source-search"
                type="text"
                value={filterText}
                onChange={(event) => setFilterText(event.target.value)}
                placeholder={language === 'vi' ? 'Tìm trong tên file hoặc summary' : 'Search by file name or summary'}
              />
              <button
                type="button"
                className="folder-studio-mini-btn"
                onClick={() => setFilterText('')}
                aria-label={language === 'vi' ? 'Xóa tìm kiếm' : 'Clear search'}
              >
                <LuX aria-hidden="true" />
              </button>
            </div>

            <div className="folder-studio-sidebar-cta">
              <button type="button" className="folder-studio-side-button" onClick={handleUploadClick} disabled={uploading}>
                <LuFilePlus2 aria-hidden="true" />
                <span>{language === 'vi' ? 'Thêm source vào workspace' : 'Add source to workspace'}</span>
              </button>
            </div>

            <div className="folder-studio-section-label">{language === 'vi' ? 'Nguồn tài liệu' : 'Document sources'}</div>
            <div className="folder-studio-source-list">
              {filteredSources.length === 0 && (
                <div className="folder-studio-empty-sidebar">
                  {!hasAnySources
                    ? (language === 'vi' ? 'Chưa có source nào. Thêm source để bắt đầu.' : 'No sources yet. Add a source to start.')
                    : (language === 'vi' ? 'Không có source khớp bộ lọc.' : 'No sources match this filter.')}
                </div>
              )}

              {filteredSources.map((source) => {
                const isSelected = source.id === selectedSourceId;
                const isIncluded = Boolean(source.includeInWorkspaceSlides ?? source.includeInFolderSlides);
                const sourceVm = buildSourceProcessingViewModel(source, language, t);
                const isReady = sourceVm.isCompleted;
                const tone = String(source.fileType || '').includes('pdf')
                  ? 'pdf'
                  : String(source.fileType || '').includes('doc')
                    ? 'doc'
                    : String(source.fileType || '').includes('image')
                      ? 'image'
                      : 'file';
                const showLive = sourceVm.isActive;
                const progressState = sourceVm.progressState;
                const readiness = getDocumentReadiness(source);

                return (
                  <div key={source.id} className={`folder-studio-source-item${isSelected ? ' selected' : ''}`}>
                    <label
                      className={`folder-studio-source-check${isIncluded ? ' checked' : ''}${!isReady ? ' disabled' : ''}`}
                      htmlFor={`workspace-source-${source.id}`}
                      onClick={(event) => event.stopPropagation()}
                    >
                      <input
                        id={`workspace-source-${source.id}`}
                        type="checkbox"
                        checked={isIncluded}
                        disabled={!isReady}
                        onChange={() => toggleSourceSelection(source)}
                        aria-label={language === 'vi'
                          ? `Chọn ${source.fileName} vào tập nguồn sinh slide`
                          : `Select ${source.fileName} for slide generation`}
                      />
                      <span aria-hidden="true" />
                    </label>
                    <div className={`folder-studio-source-icon tone-${tone}`}>
                      {String(source.fileType || '').slice(0, 3).toUpperCase()}
                    </div>
                    <div
                      role="button"
                      tabIndex={isIncluded ? 0 : -1}
                      className="folder-studio-source-copy"
                      onClick={() => handleFocusSource(source)}
                      onKeyDown={(event) => {
                        if (event.key === 'Enter' || event.key === ' ') {
                          event.preventDefault();
                          handleFocusSource(source);
                        }
                      }}
                      aria-disabled={!isIncluded}
                    >
                      <p title={source.fileName}>{source.fileName}</p>
                      <div className="folder-studio-source-meta">
                        <span className={`folder-studio-source-badge tone-${isReady ? 'completed' : showLive ? 'active' : sourceVm.isFailed ? 'failed' : 'uploaded'}`}>
                          {showLive
                            ? sourceVm.statusLabel
                            : sourceVm.isFailed
                              ? sourceVm.failedLabel
                              : normalizeStatusLabel(source.status)}
                        </span>
                        {readiness && (
                          <span className={`generation-readiness-badge tone-${readiness.tone}`}>
                            {getReadinessLabel(readiness, language)}
                          </span>
                        )}
                      </div>
                      {showLive && (
                        <>
                          <SourceProcessingProgress vm={sourceVm} t={t} compact />
                          <div className="folder-studio-source-processing is-legacy-hidden">
                            <div className="folder-studio-source-processing-head">
                              {sourceVm.hasProgressPercent ? <strong>{Math.round(sourceVm.progressPercent)}%</strong> : <strong>{t('slides.sourceProcessing.indeterminateLabel')}</strong>}
                              <span>{sourceVm.stageMessage}</span>
                            </div>
                            <div className={`folder-studio-source-progress${sourceVm.hasProgressPercent ? '' : ' indeterminate'}`}>
                              <div className="folder-studio-source-progress-fill" style={{ width: sourceVm.progressWidth }} />
                            </div>
                            <div className="folder-studio-source-live folder-studio-source-live-block">
                              <span>{sourceVm.stageMessage}</span>
                              <span>{t('slides.sourceProcessing.etaLabel')} {sourceVm.etaLabel}</span>
                            </div>
                          </div>
                        <div className="folder-studio-source-live">
                          {progressState.stageLabel || progressState.message || (language === 'vi' ? 'Đang xử lý' : 'Processing')}
                          {progressState.estimatedRemainingSeconds ? ` | ${formatEta(progressState.estimatedRemainingSeconds, { language })}` : ''}
                        </div>
                        </>
                      )}
                      {sourceVm.isFailed && (
                        <div className="folder-studio-source-error">
                          {sourceVm.errorMessage}
                        </div>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>

            <div className="folder-studio-section-label">{language === 'vi' ? 'Phạm vi nội dung' : 'Content scope'}</div>
            <div className="folder-studio-scope-list">
              {!filteredSources.length && (
                <div className="folder-studio-empty-sidebar">
                  {t('slides.scopePicker.noSources')}
                </div>
              )}

              {filteredSources.map((source) => {
                const sourceVm = buildSourceProcessingViewModel(source, language, t);
                const availableSections = getSelectableSections(source);
                const selectedCount = getScopedSelectionCount(source);
                const disabledReason = getScopeDisabledReason(source);
                const isScopeActionDisabled = Boolean(disabledReason);
                const isActiveScopeSource = source.id === selectedSourceId;
                const sourceStatusLabel = getSourceScopeStatusLabel(sourceVm, source);
                const readiness = getDocumentReadiness(source);
                const readinessMessage = getReadinessMessage(readiness, language);

                return (
                  <div
                    key={`scope-${source.id}`}
                    className={`folder-studio-scope-card workspace-scope-summary-card${isActiveScopeSource ? ' is-active' : ''}`}
                  >
                    <div className="folder-studio-scope-head">
                      <strong title={source.fileName}>{source.fileName}</strong>
                      <div className="folder-studio-scope-meta-grid">
                        <span>{t('slides.scopePicker.availableCount', { count: availableSections.length })}</span>
                        <span>{t('slides.scopePicker.selectedButton', { count: selectedCount })}</span>
                      </div>
                    </div>

                    <div className="workspace-source-status-row">
                      <span className={`folder-studio-source-badge tone-${sourceVm.isCompleted ? 'completed' : sourceVm.isActive ? 'active' : sourceVm.isFailed ? 'failed' : 'uploaded'}`}>
                        {sourceStatusLabel}
                      </span>
                      {isActiveScopeSource && (
                        <span className="workspace-source-selection-note">
                          {t('slides.scopePicker.currentSource')}
                        </span>
                      )}
                      {readiness && (
                        <span className={`generation-readiness-badge tone-${readiness.tone}`}>
                          {getReadinessLabel(readiness, language)}
                        </span>
                      )}
                    </div>

                    {readinessMessage && (
                      <div className={`folder-studio-scope-hint generation-readiness-card tone-${readiness.tone}`}>
                        <strong>{readinessMessage.title}</strong>
                        <span>{readinessMessage.body}</span>
                      </div>
                    )}

                    {sourceVm.isActive && (
                      <div className="folder-studio-source-live">
                        {sourceVm.stageMessage}
                        {sourceVm.etaLabel ? ` | ${t('slides.sourceProcessing.etaLabel')} ${sourceVm.etaLabel}` : ''}
                      </div>
                    )}

                    {sourceVm.isFailed && (
                      <div className="folder-studio-source-error">
                        {sourceVm.errorMessage}
                      </div>
                    )}

                    {disabledReason && (
                      <div className="folder-studio-scope-hint">{disabledReason}</div>
                    )}

                    <div className="workspace-source-action-group">
                      <button
                        type="button"
                        className="source-scope-button"
                        onClick={() => handleOpenScopePicker(source)}
                        disabled={isScopeActionDisabled}
                      >
                        {getScopeActionLabel(source)}
                      </button>
                      {isActiveScopeSource && (
                        <button
                          type="button"
                          className="source-secondary-button"
                          onClick={handleClearSections}
                          disabled={!selectedCount}
                        >
                          {t('slides.scopePicker.clearScope')}
                        </button>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
            </div>

            <div className="folder-studio-slide-pane">
            <div className="folder-studio-section-label">{language === 'vi' ? 'Cấu trúc slide' : 'Slide structure'}</div>
            <div className="folder-studio-flow-list">
              {!slideItems.length && (
                <div className="folder-studio-empty-sidebar">
                  {hasSelectedScope
                    ? (language === 'vi' ? 'Phạm vi đã chọn. Tạo deck để mở trình chỉnh sửa slide.' : 'Scope is selected. Generate a deck to open the slide editor.')
                    : (language === 'vi' ? 'Chưa có deck. Chọn source Completed và phạm vi nội dung trước.' : 'No deck yet. Select a completed source and content scope first.')}
                </div>
              )}

              {slideItems.map((item) => (
                <button
                  key={item.id}
                  type="button"
                  className={`folder-studio-flow-item${item.id === selectedSlideId ? ' active' : ''}`}
                  onClick={() => handleSelectSlide(item.id)}
                >
                  <div className="folder-studio-flow-thumb">
                    <span className="w-80"></span>
                    <span className="w-60"></span>
                    <span className="w-42"></span>
                  </div>
                  <div className="folder-studio-flow-copy">
                    <p>{t('slides.slideLabel', { index: item.slideIndex })}: {item.heading || 'Untitled'}</p>
                    <span>{item.goal || item.subheading || item.slideType}</span>
                  </div>
                  <span className={`folder-studio-flow-state${item.status === 'Ready' ? ' ready' : item.status === 'Generating' ? ' working' : ''}`}>
                    {item.status}
                  </span>
                </button>
              ))}
            </div>
            </div>
          </aside>

          <section className="folder-studio-center">
            <div className="folder-studio-toolbar folder-studio-toolbar-polished is-static-editor-toolbar" aria-label={toolbarLabels.label}>
              <div className="folder-studio-toolbar-group workspace-canvas-mode-group" role="group" aria-label={language === 'vi' ? 'Che do canvas slide' : 'Slide canvas mode'}>
                <WorkspaceToolbarButton active={canvasMode === 'preview'} disabled={!selectedSlide} label={language === 'vi' ? 'Preview Mode' : 'Preview Mode'} onClick={handleEnterCanvasPreviewMode}>
                  <LuPanelRightClose aria-hidden="true" />
                </WorkspaceToolbarButton>
                <WorkspaceToolbarButton active={isTextEditMode} disabled={!selectedSlide} label={language === 'vi' ? 'Edit Text Mode' : 'Edit Text Mode'} onClick={handleEnterCanvasTextMode}>
                  <LuType aria-hidden="true" />
                </WorkspaceToolbarButton>
                <WorkspaceToolbarButton active={isLayoutEditMode} disabled={!selectedSlide} label={language === 'vi' ? 'Edit Layout Mode' : 'Edit Layout Mode'} onClick={handleEnterCanvasLayoutMode}>
                  <LuPanelRightOpen aria-hidden="true" />
                </WorkspaceToolbarButton>
                <WorkspaceToolbarButton disabled={!isLayoutEditMode || !selectedCanvasHistory.past.length} label={language === 'vi' ? 'Hoan tac layout' : 'Undo layout'} onClick={handleCanvasUndo}>
                  <LuUndo2 aria-hidden="true" />
                </WorkspaceToolbarButton>
                <WorkspaceToolbarButton disabled={!isLayoutEditMode || !selectedCanvasHistory.future.length} label={language === 'vi' ? 'Lam lai layout' : 'Redo layout'} onClick={handleCanvasRedo}>
                  <LuRedo2 aria-hidden="true" />
                </WorkspaceToolbarButton>
                <WorkspaceToolbarButton disabled={!isLayoutEditMode} label={language === 'vi' ? 'Them text' : 'Add text'} onClick={handleAddCanvasText}>
                  <LuPlus aria-hidden="true" />
                </WorkspaceToolbarButton>
                <WorkspaceToolbarButton disabled={!isLayoutEditMode || !selectedCanvasState} label={language === 'vi' ? 'Luu layout' : 'Save layout'} onClick={handleSaveCanvasLayout}>
                  <LuSave aria-hidden="true" />
                </WorkspaceToolbarButton>
              </div>

              <div className="folder-studio-toolbar-group is-select-group">
                <select
                  value={activeFieldState?.fontFamily || 'Lexend'}
                  onChange={(event) => handleStyleChange((block) => ({ ...block, fontFamily: event.target.value }))}
                  disabled={!selectedDraft}
                  aria-label={toolbarLabels.fontFamily}
                  title={toolbarLabels.fontFamily}
                >
                  {FONT_OPTIONS.map((option) => (
                    <option key={option} value={option}>{option}</option>
                  ))}
                </select>

                <select
                  value={activeFieldState?.fontSize || 18}
                  onChange={(event) => handleStyleChange((block) => ({ ...block, fontSize: Number(event.target.value) }))}
                  disabled={!selectedDraft}
                  aria-label={toolbarLabels.fontSize}
                  title={toolbarLabels.fontSize}
                >
                  {FONT_SIZES.map((option) => (
                    <option key={option} value={option}>{option}</option>
                  ))}
                </select>
              </div>

              <div className="folder-studio-toolbar-group" role="group" aria-label={toolbarLabels.emphasisGroup}>
                <WorkspaceToolbarButton active={activeFieldState?.bold} disabled={!selectedDraft} label={toolbarLabels.bold} onClick={() => handleStyleChange((block) => ({ ...block, bold: !block.bold }))}>
                  <LuBold aria-hidden="true" />
                </WorkspaceToolbarButton>
                <WorkspaceToolbarButton active={activeFieldState?.italic} disabled={!selectedDraft} label={toolbarLabels.italic} onClick={() => handleStyleChange((block) => ({ ...block, italic: !block.italic }))}>
                  <LuItalic aria-hidden="true" />
                </WorkspaceToolbarButton>
                <WorkspaceToolbarButton active={activeFieldState?.underline} disabled={!selectedDraft} label={toolbarLabels.underline} onClick={() => handleStyleChange((block) => ({ ...block, underline: !block.underline }))}>
                  <LuUnderline aria-hidden="true" />
                </WorkspaceToolbarButton>
                <WorkspaceToolbarButton active={activeFieldState?.strike} disabled={!selectedDraft} label={toolbarLabels.strike} onClick={() => handleStyleChange((block) => ({ ...block, strike: !block.strike }))}>
                  <LuStrikethrough aria-hidden="true" />
                </WorkspaceToolbarButton>
              </div>

              <div className="folder-studio-toolbar-group is-color-group" role="group" aria-label={toolbarLabels.colorGroup}>
                {TEXT_COLOR_OPTIONS.map((color) => (
                  <WorkspaceColorButton
                    key={color}
                    active={activeTextColor === color}
                    color={color}
                    disabled={!selectedDraft}
                    icon={LuPalette}
                    label={`${toolbarLabels.textColor}: ${color}`}
                    onClick={() => handleStyleChange((block) => ({ ...block, textColor: color }))}
                  />
                ))}
                {HIGHLIGHT_COLOR_OPTIONS.map((color) => (
                  <WorkspaceColorButton
                    key={color}
                    active={activeHighlightColor === color}
                    color={color}
                    disabled={!selectedDraft}
                    icon={LuHighlighter}
                    label={color === DEFAULT_HIGHLIGHT_COLOR ? toolbarLabels.clearHighlight : `${toolbarLabels.highlight}: ${color}`}
                    onClick={() => handleStyleChange((block) => ({ ...block, highlightColor: color }))}
                  />
                ))}
              </div>

              <div className="folder-studio-toolbar-group" role="group" aria-label={toolbarLabels.alignGroup}>
                {alignOptions.map(({ key, label, icon: Icon }) => (
                  <WorkspaceToolbarButton
                    key={key}
                    active={activeFieldState?.align === key}
                    disabled={!selectedDraft}
                    label={label}
                    onClick={() => handleStyleChange((block) => ({ ...block, align: key }))}
                  >
                    <Icon aria-hidden="true" />
                  </WorkspaceToolbarButton>
                ))}
              </div>

              <div className="folder-studio-toolbar-group" role="group" aria-label={toolbarLabels.listGroup}>
                <WorkspaceToolbarButton
                  active={activeListStyle === 'bullet'}
                  disabled={!selectedDraft || activeField !== 'body'}
                  label={toolbarLabels.bulletList}
                  onClick={() => handleListStyleChange(activeListStyle === 'bullet' ? 'none' : 'bullet')}
                >
                  <LuList aria-hidden="true" />
                </WorkspaceToolbarButton>
                <WorkspaceToolbarButton
                  active={activeListStyle === 'numbered'}
                  disabled={!selectedDraft || activeField !== 'body'}
                  label={toolbarLabels.numberedList}
                  onClick={() => handleListStyleChange(activeListStyle === 'numbered' ? 'none' : 'numbered')}
                >
                  <LuListOrdered aria-hidden="true" />
                </WorkspaceToolbarButton>
                <WorkspaceToolbarButton disabled={!selectedDraft} label={toolbarLabels.outdent} onClick={() => handleIndentChange(-1)}>
                  <LuIndentDecrease aria-hidden="true" />
                </WorkspaceToolbarButton>
                <WorkspaceToolbarButton disabled={!selectedDraft} label={toolbarLabels.indent} onClick={() => handleIndentChange(1)}>
                  <LuIndentIncrease aria-hidden="true" />
                </WorkspaceToolbarButton>
              </div>

              <div className="folder-studio-toolbar-group is-select-group" role="group" aria-label={toolbarLabels.spacingGroup}>
                <select
                  value={activeLineHeight}
                  onChange={(event) => handleStyleChange((block) => ({ ...block, lineHeight: Number(event.target.value) }))}
                  disabled={!selectedDraft}
                  aria-label={toolbarLabels.lineHeight}
                  title={toolbarLabels.lineHeight}
                >
                  {LINE_HEIGHT_OPTIONS.map((option) => (
                    <option key={option} value={option}>{option}x</option>
                  ))}
                </select>
                <WorkspaceToolbarButton active={activeScript === 'superscript'} disabled={!selectedDraft} label={toolbarLabels.superscript} onClick={() => handleScriptChange('superscript')}>
                  <LuSuperscript aria-hidden="true" />
                </WorkspaceToolbarButton>
                <WorkspaceToolbarButton active={activeScript === 'subscript'} disabled={!selectedDraft} label={toolbarLabels.subscript} onClick={() => handleScriptChange('subscript')}>
                  <LuSubscript aria-hidden="true" />
                </WorkspaceToolbarButton>
                <WorkspaceToolbarButton active={Boolean(activeFieldState?.linkUrl)} disabled={!selectedDraft} label={toolbarLabels.link} onClick={handleLinkPrompt}>
                  <LuLink2 aria-hidden="true" />
                </WorkspaceToolbarButton>
              </div>

              <div className="folder-studio-toolbar-group" role="group" aria-label={toolbarLabels.historyGroup}>
                <WorkspaceToolbarButton disabled={!activeHistory.past.length} label={toolbarLabels.undo} onClick={handleUndo}>
                  <LuUndo2 aria-hidden="true" />
                </WorkspaceToolbarButton>
                <WorkspaceToolbarButton disabled={!activeHistory.future.length} label={toolbarLabels.redo} onClick={handleRedo}>
                  <LuRedo2 aria-hidden="true" />
                </WorkspaceToolbarButton>
                <WorkspaceToolbarButton disabled={!selectedSlide || !selectedSlideNeedsMedia} label={mediaOpen ? toolbarLabels.hideMedia : toolbarLabels.openMedia} onClick={() => setMediaOpen((current) => !current)}>
                  {mediaOpen ? <LuPanelRightClose aria-hidden="true" /> : <LuPanelRightOpen aria-hidden="true" />}
                </WorkspaceToolbarButton>
              </div>
            </div>

            <div className="folder-studio-toolbar is-legacy-hidden" aria-hidden="true">
              <select
                value={activeFieldState?.fontFamily || 'Lexend'}
                onChange={(event) => handleStyleChange((block) => ({ ...block, fontFamily: event.target.value }))}
                disabled={!selectedDraft}
              >
                {FONT_OPTIONS.map((option) => (
                  <option key={option} value={option}>{option}</option>
                ))}
              </select>

              <select
                value={activeFieldState?.fontSize || 18}
                onChange={(event) => handleStyleChange((block) => ({ ...block, fontSize: Number(event.target.value) }))}
                disabled={!selectedDraft}
              >
                {FONT_SIZES.map((option) => (
                  <option key={option} value={option}>{option}</option>
                ))}
              </select>

              <div className="folder-studio-toolbar-sep"></div>
              <button type="button" className={`folder-studio-toolbar-btn${activeFieldState?.bold ? ' active' : ''}`} onClick={() => handleStyleChange((block) => ({ ...block, bold: !block.bold }))} disabled={!selectedDraft} aria-label={language === 'vi' ? 'In đậm' : 'Bold'} title={language === 'vi' ? 'In đậm' : 'Bold'}><strong>B</strong></button>
              <button type="button" className={`folder-studio-toolbar-btn${activeFieldState?.italic ? ' active' : ''}`} onClick={() => handleStyleChange((block) => ({ ...block, italic: !block.italic }))} disabled={!selectedDraft} aria-label={language === 'vi' ? 'In nghiêng' : 'Italic'} title={language === 'vi' ? 'In nghiêng' : 'Italic'}><em>I</em></button>
              <button type="button" className={`folder-studio-toolbar-btn${activeFieldState?.underline ? ' active' : ''}`} onClick={() => handleStyleChange((block) => ({ ...block, underline: !block.underline }))} disabled={!selectedDraft} aria-label={language === 'vi' ? 'Gạch chân' : 'Underline'} title={language === 'vi' ? 'Gạch chân' : 'Underline'}><span style={{ textDecoration: 'underline' }}>U</span></button>
              <div className="folder-studio-toolbar-sep"></div>
              {[
                { key: 'left', label: language === 'vi' ? 'Trái' : 'Left' },
                { key: 'center', label: language === 'vi' ? 'Giữa' : 'Center' },
                { key: 'right', label: language === 'vi' ? 'Phải' : 'Right' },
              ].map(({ key, label }) => (
                <button
                  key={key}
                  type="button"
                  className={`folder-studio-toolbar-btn folder-studio-toolbar-btn-word${activeFieldState?.align === key ? ' active' : ''}`}
                  onClick={() => handleStyleChange((block) => ({ ...block, align: key }))}
                  disabled={!selectedDraft}
                  aria-label={label}
                  title={label}
                >
                  {label}
                </button>
              ))}
              <button
                type="button"
                className={`folder-studio-toolbar-btn folder-studio-toolbar-btn-word${activeFieldState?.bullet ? ' active' : ''}`}
                onClick={() => handleStyleChange((block) => ({ ...block, bullet: !block.bullet }))}
                disabled={!selectedDraft || activeField !== 'body'}
              >
                Bullet
              </button>
              <div className="folder-studio-toolbar-sep"></div>
              <button type="button" className="folder-studio-toolbar-btn" onClick={handleUndo} disabled={!activeHistory.past.length}>{language === 'vi' ? 'Hoàn tác' : 'Undo'}</button>
              <button type="button" className="folder-studio-toolbar-btn" onClick={handleRedo} disabled={!activeHistory.future.length}>{language === 'vi' ? 'Làm lại' : 'Redo'}</button>
              <button type="button" className="folder-studio-toolbar-btn" onClick={() => setMediaOpen((current) => !current)} disabled={!selectedSlide || !selectedSlideNeedsMedia}>
                {mediaOpen ? (language === 'vi' ? 'Ẩn media' : 'Hide media') : (language === 'vi' ? 'Mở media' : 'Open media')}
              </button>
            </div>

            <div className="folder-studio-canvas folder-studio-editor-surface" ref={(node) => {
              centerCanvasRef.current = node;
              editorSurfaceRef.current = node;
            }}>
              {selectedDraft && autoSaveStatus !== 'idle' && (
                <div className={`folder-autosave-status tone-${autoSaveStatus}`} role={autoSaveStatus === 'error' ? 'alert' : 'status'}>
                  {autoSaveLabels[autoSaveStatus]}
                </div>
              )}
              {!selectedSlide || !selectedDraft ? (
                isGeneratingDeck ? (
                  <WorkspaceDeckProgressCard
                    progress={deckGenerationProgress}
                    language={language}
                  />
                ) : (hasAnySources && !hasCompletedSources && previewProcessingVm) ? (
                  <div className="folder-studio-empty folder-studio-empty-processing">
                    <h3>{t('slides.sourceProcessing.emptyTitle')}</h3>

                    <p>{t('slides.sourceProcessing.emptyBody')}</p>

                    <div className="folder-studio-empty-processing-panel">
                      <SourceProcessingProgress vm={previewProcessingVm} t={t} />
                      <div className="folder-studio-empty-processing-legacy is-legacy-hidden">
                      <div className="folder-studio-source-meta">
                        <span className="folder-studio-source-badge tone-active">{previewProcessingVm.statusLabel}</span>
                        <span>
                          {previewProcessingVm.hasProgressPercent
                            ? `${Math.round(previewProcessingVm.progressPercent)}%`
                            : t('slides.sourceProcessing.indeterminateLabel')}
                        </span>
                      </div>
                      <div className={`folder-studio-source-progress folder-studio-source-progress-large${previewProcessingVm.hasProgressPercent ? '' : ' indeterminate'}`}>
                        <div className="folder-studio-source-progress-fill" style={{ width: previewProcessingVm.progressWidth }} />
                      </div>
                      <div className="folder-studio-source-live folder-studio-source-live-block">
                        <span>{previewProcessingVm.stageMessage}</span>
                        <span>{t('slides.sourceProcessing.etaLabel')} {previewProcessingVm.etaLabel}</span>
                      </div>
                      {previewProcessingVm.isFailed && (
                        <div className="folder-studio-source-error">
                          {previewProcessingVm.errorMessage}
                        </div>
                      )}
                      </div>
                    </div>

                    <div className="folder-studio-empty-actions">
                      <button
                        type="button"
                        className="button button-primary"
                        onClick={handleUploadClick}
                      >
                        {language === 'vi' ? 'Thêm nguồn' : 'Add source'}
                      </button>

                      <button
                        type="button"
                        className="button"
                        onClick={handleGenerateDeck}
                        disabled
                        title={generateDisabledReason || undefined}
                      >
                        {language === 'vi' ? 'Tạo deck' : 'Generate deck'}
                      </button>
                      {generateDisabledReason && (
                        <div className="folder-studio-generate-hint">{generateDisabledReason}</div>
                      )}
                    </div>
                  </div>
                ) : (
                  <div className={`folder-studio-empty tone-${emptyStateCopy.tone}`}>
                    <h3>
                      {emptyStateCopy.title}
                    </h3>

                    <p>
                      {emptyStateCopy.body}
                    </p>

                    <div className="folder-studio-readiness-row" aria-label={language === 'vi' ? 'Trạng thái workspace' : 'Workspace readiness'}>
                      {workspaceReadinessItems.map((item) => (
                        <span key={item.key} className={`folder-studio-readiness-chip tone-${item.tone}`}>
                          <strong>{item.label}</strong>
                          <span>{item.value}</span>
                        </span>
                      ))}
                    </div>

                    <div className="folder-studio-empty-actions">
                      <button
                        type="button"
                        className="button button-primary"
                        onClick={handleUploadClick}
                      >
                        {emptyStateCopy.primaryLabel}
                      </button>

                      <button
                        type="button"
                        className="button"
                        onClick={handleGenerateDeck}
                        disabled={!canGenerate}
                        title={generateDisabledReason || undefined}
                      >
                        {emptyStateCopy.secondaryLabel}
                      </button>
                    </div>
                    {generateDisabledReason && (
                      <div className="folder-studio-generate-hint">{generateDisabledReason}</div>
                    )}
                  </div>
                ) 
              ) : (
                <>
                  <div className="workspace-canvas-status-row">
                    <span className={`workspace-canvas-status tone-${canvasAutosaveStatus}`}>{canvasStatusLabel}</span>
                    <span className={`workspace-canvas-status tone-${realtime.status}`}>{realtimeStatusLabel}</span>
                    {canvasRemoteSelections.length > 0 && (
                      <span className="workspace-canvas-status tone-presence">
                        {canvasRemoteSelections.map((selection) => selection.displayName).join(', ')}
                      </span>
                    )}
                    <div className="workspace-canvas-mode-controls" role="group" aria-label="Workspace canvas mode controls">
                      <button
                        type="button"
                        className={`workspace-canvas-mode-button${canvasMode === 'preview' ? ' active' : ''}`}
                        aria-label="Preview"
                        title="Preview"
                        disabled={!selectedSlide}
                        onClick={handleEnterCanvasPreviewMode}
                      >
                        <LuPanelRightClose aria-hidden="true" />
                        <span>Preview</span>
                      </button>
                      <button
                        type="button"
                        className={`workspace-canvas-mode-button${isTextEditMode ? ' active' : ''}`}
                        aria-label="Edit text"
                        title="Edit text"
                        disabled={!selectedSlide}
                        onClick={handleEnterCanvasTextMode}
                      >
                        <LuType aria-hidden="true" />
                        <span>Edit text</span>
                      </button>
                      <button
                        type="button"
                        className={`workspace-canvas-mode-button${isLayoutEditMode ? ' active' : ''}`}
                        aria-label="Edit layout"
                        title="Edit layout"
                        disabled={!selectedSlide}
                        onClick={handleEnterCanvasLayoutMode}
                      >
                        <LuPanelRightOpen aria-hidden="true" />
                        <span>Edit layout</span>
                      </button>
                      <button
                        type="button"
                        className="workspace-canvas-mode-button"
                        aria-label={language === 'vi' ? 'Them text' : 'Add text'}
                        title={language === 'vi' ? 'Them text' : 'Add text'}
                        disabled={!selectedSlide}
                        onClick={handleAddCanvasText}
                      >
                        <LuPlus aria-hidden="true" />
                        <span>{language === 'vi' ? 'Them text' : 'Add text'}</span>
                      </button>
                      <button
                        type="button"
                        className="workspace-canvas-mode-button"
                        aria-label={language === 'vi' ? 'Them anh' : 'Add image'}
                        title={language === 'vi' ? 'Them anh' : 'Add image'}
                        disabled={!selectedSlide}
                        onClick={handleAddCanvasImageClick}
                      >
                        <LuImage aria-hidden="true" />
                        <span>{language === 'vi' ? 'Them anh' : 'Add image'}</span>
                      </button>
                      <button
                        type="button"
                        className="workspace-canvas-mode-button"
                        aria-label={language === 'vi' ? 'Thuyet trinh' : 'Present'}
                        title={language === 'vi' ? 'Thuyet trinh' : 'Present'}
                        disabled={!selectedSourceDocumentId || !deckReady}
                        onClick={() => navigate(`/slides/${selectedSourceDocumentId}`)}
                      >
                        <LuPresentation aria-hidden="true" />
                        <span>{language === 'vi' ? 'Thuyet trinh' : 'Present'}</span>
                      </button>
                      <button
                        type="button"
                        className="workspace-canvas-mode-button"
                        aria-label="Undo"
                        title="Undo"
                        disabled={!isLayoutEditMode || !selectedCanvasHistory.past.length}
                        onClick={handleCanvasUndo}
                      >
                        <LuUndo2 aria-hidden="true" />
                        <span>Undo</span>
                      </button>
                      <button
                        type="button"
                        className="workspace-canvas-mode-button"
                        aria-label="Redo"
                        title="Redo"
                        disabled={!isLayoutEditMode || !selectedCanvasHistory.future.length}
                        onClick={handleCanvasRedo}
                      >
                        <LuRedo2 aria-hidden="true" />
                        <span>Redo</span>
                      </button>
                      <button
                        type="button"
                        className="workspace-canvas-mode-button"
                        aria-label="Save layout"
                        title="Save layout"
                        disabled={!isLayoutEditMode || !selectedCanvasState}
                        onClick={handleSaveCanvasLayout}
                      >
                        <LuSave aria-hidden="true" />
                        <span>Save layout</span>
                      </button>
                    </div>
                    <span className="workspace-canvas-mode-hint">{canvasModeHint}</span>
                  </div>

                  <article className="folder-slide-card workspace-slide-canvas-card">
                    <SlideCanvas
                      editorState={selectedCanvasState}
                      imageVm={selectedImageVm}
                      labels={canvasLabels}
                      mode={isLayoutEditMode ? 'layout' : isTextEditMode ? 'text' : 'preview'}
                      remoteSelections={isLayoutEditMode ? canvasRemoteSelections : []}
                      scale={selectedCanvasScale}
                      selectedElementId={isLayoutEditMode ? selectedElementId : null}
                      onCommitElement={handleCommitCanvasElement}
                      onPatchElement={isTextEditMode ? handleCommitCanvasElement : handlePatchCanvasElement}
                      onSelectElement={isLayoutEditMode ? handleSelectCanvasElement : undefined}
                    />
                  </article>
                  {false && (
                  <article className="folder-slide-card">
                    <div className={`folder-slide-layout${selectedSlideNeedsMedia ? '' : ' text-only-layout'}`}>
                      <div className="folder-slide-copy">
                        <div className={`folder-editable-block${selectedEditorField === 'title' ? ' active' : ''}`} onClick={() => selectEditorField('title')}>
                          {renderFloatingToolbar('title')}
                          {renderInlineInsertMenu('title')}
                          <textarea
                            rows={2}
                            value={selectedDraft.title.text}
                            onFocus={() => selectEditorField('title')}
                            onChange={(event) => handleFieldTextChange('title', event.target.value)}
                            className="folder-slide-title-input"
                            style={applyTextStyle(selectedDraft.title)}
                            placeholder={slideTitlePlaceholder}
                          />
                          <WorkspaceLinkAffordance block={selectedDraft.title} label={toolbarLabels.linkedBlock} />
                        </div>

                        <div className={`folder-editable-block${selectedEditorField === 'subtitle' ? ' active' : ''}`} onClick={() => selectEditorField('subtitle')}>
                          {renderFloatingToolbar('subtitle')}
                          {renderInlineInsertMenu('subtitle')}
                          <textarea
                            rows={2}
                            value={selectedDraft.subtitle.text}
                            onFocus={() => selectEditorField('subtitle')}
                            onChange={(event) => handleFieldTextChange('subtitle', event.target.value)}
                            className="folder-slide-subtitle-input"
                            style={applyTextStyle(selectedDraft.subtitle)}
                            placeholder="Subheading / context"
                          />
                          <WorkspaceLinkAffordance block={selectedDraft.subtitle} label={toolbarLabels.linkedBlock} />
                        </div>

                        <div className={`folder-editable-block tone-soft${selectedEditorField === 'goal' ? ' active' : ''}`} onClick={() => selectEditorField('goal')}>
                          {renderFloatingToolbar('goal')}
                          {renderInlineInsertMenu('goal')}
                          <textarea
                            rows={2}
                            value={selectedDraft.goal.text}
                            onFocus={() => selectEditorField('goal')}
                            onChange={(event) => handleFieldTextChange('goal', event.target.value)}
                            className="folder-slide-goal-input"
                            style={applyTextStyle(selectedDraft.goal)}
                            placeholder={slideGoalPlaceholder}
                          />
                          <WorkspaceLinkAffordance block={selectedDraft.goal} label={toolbarLabels.linkedBlock} />
                        </div>

                        <div className={`folder-editable-block${selectedEditorField === 'body' ? ' active' : ''}`} onClick={() => selectEditorField('body')}>
                          {renderFloatingToolbar('body')}
                          {renderInlineInsertMenu('body')}
                          <textarea
                            rows={8}
                            value={selectedDraft.body.text}
                            onFocus={() => selectEditorField('body')}
                            onChange={(event) => handleFieldTextChange('body', event.target.value)}
                            className="folder-slide-body-input"
                            style={applyTextStyle(selectedDraft.body)}
                            placeholder={bodyPlaceholder}
                          />
                          <WorkspaceLinkAffordance block={selectedDraft.body} label={toolbarLabels.linkedBlock} />
                          <small>
                            {selectedBodyListStyle === 'numbered'
                              ? toolbarLabels.numberedModeHint
                              : selectedBodyListStyle === 'bullet'
                                ? toolbarLabels.bulletModeHint
                                : toolbarLabels.textModeHint}
                          </small>
                          <small className="is-legacy-hidden">
                            {selectedDraft.body.bullet
                              ? (language === 'vi' ? 'Bullet mode đang bật: mỗi dòng sẽ được lưu thành 1 body block.' : 'Bullet mode is on: each line will be saved as one body block.')
                              : (language === 'vi' ? 'Đang ở text mode: nội dung vẫn được lưu theo từng dòng.' : 'Text mode is on: content is still saved line by line.')}
                          </small>
                        </div>
                      </div>

                      {selectedSlideNeedsMedia && (
                      <div className="folder-slide-visual">
                        {selectedImage?.localAssetUrl ? (
                          <img src={selectedImage.localAssetUrl} alt={selectedImage.altText || 'Selected media'} />
                        ) : (
                          <div className="folder-slide-visual-placeholder">
                            <span>{selectedImageVm?.badgeLabel || 'Media pending'}</span>
                            <strong>{selectedImageVm?.statusLabel || (language === 'vi' ? 'Chưa có preview' : 'No preview yet')}</strong>
                            <p>{selectedImageVm?.helperText || (language === 'vi' ? 'Media pipeline sẽ nối sau khi nội dung ổn định.' : 'The media pipeline can continue once the content is stable.')}</p>
                          </div>
                        )}
                        <div className="folder-slide-visual-meta">
                          <span>{selectedImageVm?.badgeLabel || (language === 'vi' ? 'Chưa có media' : 'No media')}</span>
                          <strong>{selectedImage?.provider || 'Folder visual slot'}</strong>
                          <small>{selectedImageVm?.attributionText || (language === 'vi' ? 'Có thể refresh để lấy image candidates mới.' : 'You can refresh to get new image candidates.')}</small>
                        </div>
                      </div>
                      )}
                    </div>

                    <div className="folder-slide-hint">
                      <span>AI</span>
                      <p>
                        {qualityIssues[0]
                          || selectedImageVm?.helperText
                          || (language === 'vi' ? 'Deck workspace này đang cho phép sửa title, subtitle, body, notes và chọn image tương ứng.' : 'This workspace deck currently supports editing title, subtitle, body, notes, and the selected image.')}
                        {typeof qualityScore === 'number' ? (language === 'vi' ? ` Điểm verifier hiện tại: ${qualityScore}.` : ` Current verifier score: ${qualityScore}.`) : ''}
                      </p>
                    </div>
                  </article>
                  )}

                  <div className="folder-studio-panels">
                    <section className="folder-studio-panel-card">
                      <div className="folder-studio-panel-card-head">
                        <strong>{language === 'vi' ? 'Speaker notes' : 'Speaker notes'}</strong>
                        <span>{t('slides.slideLabel', { index: selectedSlide.slideIndex })}</span>
                      </div>
                      <div className={`folder-editable-block folder-editable-notes${selectedEditorField === 'notes' ? ' active' : ''}`} onClick={() => selectEditorField('notes')}>
                        {renderFloatingToolbar('notes')}
                        {renderInlineInsertMenu('notes')}
                        <textarea
                          rows={5}
                          value={selectedDraft.notes.text}
                          onFocus={() => selectEditorField('notes')}
                          onChange={(event) => handleFieldTextChange('notes', event.target.value)}
                          className={`folder-slide-notes-input${selectedEditorField === 'notes' ? ' active' : ''}`}
                          style={applyTextStyle(selectedDraft.notes)}
                          placeholder={notesPlaceholder}
                        />
                        <WorkspaceLinkAffordance block={selectedDraft.notes} label={toolbarLabels.linkedBlock} />
                      </div>
                    </section>

                    {selectedSlideNeedsMedia && (
                    <section className="folder-studio-panel-card">
                      <div className="folder-studio-panel-card-head">
                        <strong>{language === 'vi' ? 'Dải media' : 'Media strip'}</strong>
                        <div className="folder-studio-inline-actions">
                          <button type="button" className="folder-studio-mini-btn" onClick={handleRefreshImages} disabled={mediaBusy}>
                            {mediaBusy ? (language === 'vi' ? 'Đang refresh...' : 'Refreshing...') : (language === 'vi' ? 'Làm mới ảnh' : 'Refresh images')}
                          </button>
                          <button type="button" className="folder-studio-mini-btn" onClick={() => setMediaOpen((current) => !current)}>
                            {mediaOpen ? (language === 'vi' ? 'Thu gọn' : 'Collapse') : (language === 'vi' ? 'Mở' : 'Open')}
                          </button>
                        </div>
                      </div>

                      {mediaOpen ? (
                        <>
                          <p className="folder-studio-media-copy">
                            {selectedImageVm?.helperText || (language === 'vi' ? 'Chưa có image payload cho slide này.' : 'No image payload for this slide yet.')}
                          </p>
                          <div className="folder-studio-media-grid">
                            {selectedImageVm?.candidates?.length ? selectedImageVm.candidates.map((candidate) => (
                              <button
                                type="button"
                                key={candidate.key}
                                className={`folder-studio-media-card${candidate.isSelected ? ' active' : ''}`}
                                onClick={() => handleSelectImage(candidate.key)}
                                disabled={mediaBusy}
                              >
                                {candidate.thumbnailUrl ? (
                                  <img src={candidate.thumbnailUrl} alt={candidate.altText || 'Media candidate'} />
                                ) : (
                                  <div className="folder-studio-media-fallback">No preview</div>
                                )}
                                <div className="folder-studio-media-meta">
                                  <strong>{candidate.provider || candidate.sourceType}</strong>
                                  <span>{candidate.licenseLabel || candidate.sourceType}</span>
                                </div>
                              </button>
                            )) : (
                              <div className="folder-studio-empty-sidebar">
                                {language === 'vi' ? 'Chưa có candidate nào. Bấm "Làm mới ảnh" để tạo / tải lại media candidates.' : 'No candidates yet. Click "Refresh images" to generate or reload media candidates.'}
                              </div>
                            )}
                          </div>
                        </>
                      ) : (
                        <p className="folder-studio-media-copy">
                          {language === 'vi' ? 'Media strip đang thu gọn. Mở ra để đổi image, chọn candidate hoặc refresh media workflow.' : 'The media strip is collapsed. Open it to swap images, pick a candidate, or refresh the media workflow.'}
                        </p>
                      )}
                    </section>
                    )}
                  </div>
                </>
              )}
            </div>
          </section>

          {((isLayoutEditMode && selectedCanvasElement) || selectedEditorField) && (
            <aside className="folder-studio-floating-inspector">
              {isLayoutEditMode && selectedCanvasElement && (
                <PropertiesPanel
                  element={selectedCanvasElement}
                  labels={canvasPropertyLabels}
                  onPatch={(elementId, patch) => handleCommitCanvasElement(elementId, patch)}
                />
              )}
              {selectedEditorField && renderAdvancedPropertiesPanel()}
            </aside>
          )}

          <aside className={`folder-studio-rpanel drawer-${activeTool || 'closed'}`}>
            {activeTool === 'text' && (
              <div className="folder-studio-action-section folder-studio-text-drawer-section">
                <div className="folder-studio-section-label">{language === 'vi' ? 'Text' : 'Text'}</div>
                <button
                  type="button"
                  className="folder-studio-action tone-primary"
                  onClick={handleAddCanvasText}
                  disabled={!selectedSlide}
                >
                  <span className="folder-studio-action-icon"><LuType aria-hidden="true" /></span>
                  <span className="folder-studio-action-copy">
                    <strong>{language === 'vi' ? 'Them text vao canvas' : 'Add text to canvas'}</strong>
                    <span>{language === 'vi' ? 'Tao text element moi va chuyen sang edit layout.' : 'Create a new text element and switch to layout editing.'}</span>
                  </span>
                  <span className="folder-studio-action-badge">Text</span>
                </button>
                <div className="folder-studio-scope-hint">
                  {language === 'vi'
                    ? 'Edit text truc tiep tren slide: chon Edit text roi click vao text element.'
                    : 'Edit text inline on the slide: choose Edit text, then click a text element.'}
                </div>
              </div>
            )}
            {isLayoutEditMode && selectedCanvasState && (
              <>
                <LayersPanel
                  elements={selectedCanvasState.elements}
                  labels={canvasLayerLabels}
                  selectedElementId={selectedElementId}
                  onDelete={handleDeleteCanvasElement}
                  onDuplicate={handleDuplicateCanvasElement}
                  onPatch={handleCommitCanvasElement}
                  onReorder={handleReorderCanvasElement}
                  onSelect={handleSelectCanvasElement}
                />
              </>
            )}
            <div className="folder-studio-panel-title">{language === 'vi' ? 'Studio / Hành động' : 'Studio / Actions'}</div>

            <div className="folder-studio-action-section">
              <div className="folder-studio-section-label">{language === 'vi' ? 'Tạo mới' : 'Create'}</div>
              <button type="button" className="folder-studio-action tone-primary" onClick={handleGenerateDeck} disabled={!canGenerate} title={generateDisabledReason || undefined}>
                <span className="folder-studio-action-icon"><LuSparkles aria-hidden="true" /></span>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Tạo deck từ nội dung' : 'Generate content deck'}</strong>
                  <span>{generateDisabledReason || (language === 'vi' ? `${selectedReadySources.length} source ready đang được chọn cho workspace` : `${selectedReadySources.length} ready sources selected for this workspace`)}</span>
                </span>
                <span className="folder-studio-action-badge">{canGenerate ? 'AI' : 'WAIT'}</span>
              </button>
              {generateDisabledReason && (
                <div className="folder-studio-generate-hint">{generateDisabledReason}</div>
              )}
              <button
                type="button"
                className="folder-studio-action"
                onClick={handleGenerateQuestions}
                disabled={!canGenerateQuestions}
              >
                <span className="folder-studio-action-icon"><LuBookOpen aria-hidden="true" /></span>
                <span className="folder-studio-action-copy">
                  <strong>{questionActionTitle}</strong>
                  <span>{questionActionDetail}</span>
                </span>
                <span className="folder-studio-action-badge">
                  {isActiveProgress(questionProgress)
                    ? `${Math.round(questionProgress?.percent || 0)}%`
                    : (selectedSourceHasQuestions ? 'Ready' : 'AI')}
                </span>
              </button>
              {questionError && (
                <div className="folder-studio-scope-hint">{questionError}</div>
              )}
              {questionProgress && (
                <WorkspaceQuestionProgressCard progress={questionProgress} language={language} />
              )}
              <button
                type="button"
                className="folder-studio-action"
                onClick={() => navigate(`/study/${selectedSourceDocumentId}`)}
                disabled={!studyHubEnabled}
              >
                <span className="folder-studio-action-icon"><LuBookOpen aria-hidden="true" /></span>
                <span className="folder-studio-action-copy">
                  <strong>{t('slides.studyActions.openStudyHubTitle')}</strong>
                  <span>{t('slides.studyActions.openStudyHubSubtitle')}</span>
                </span>
                <span className="folder-studio-action-badge">HUB</span>
              </button>
              <button
                type="button"
                className="folder-studio-action"
                onClick={() => navigate(`/study/${selectedSourceDocumentId}/streak`)}
                disabled={!studyHubEnabled}
              >
                <span className="folder-studio-action-icon"><LuGamepad2 aria-hidden="true" /></span>
                <span className="folder-studio-action-copy">
                  <strong>{t('slides.studyActions.streakTitle')}</strong>
                  <span>{t('slides.studyActions.streakSubtitle')}</span>
                </span>
                <span className="folder-studio-action-badge">GAME</span>
              </button>
              {streakModeHint && (
                <div className="folder-studio-scope-hint">{streakModeHint}</div>
              )}
            </div>

            <div className="folder-studio-action-section">
              <div className="folder-studio-section-label">{t('slides.deckBrief')}</div>
              <div className="folder-studio-source-live">
                {selectedSource
                  ? (language === 'vi'
                    ? `Nguồn chính: ${selectedSource.fileName} | Đã chọn ${selectedSectionIds.length} phần`
                    : `Primary source: ${selectedSource.fileName} | ${selectedSectionIds.length} sections selected`)
                  : (language === 'vi'
                    ? 'Bước 1-2: chọn tài liệu chính và phạm vi chapter/section trước khi tạo deck.'
                    : 'Steps 1-2: choose the primary source and chapter/section scope before generating the deck.')}
              </div>
              {selectedSourceDocumentId && (
                <DocumentUnderstandingPanel
                  documentId={selectedSourceDocumentId}
                  showEmpty
                  compact
                />
              )}
              {scopeRecommendation && (
                <div className="folder-studio-source-live">
                  {language === 'vi'
                    ? `Gợi ý phạm vi: nên dùng khoảng ${scopeRecommendation.suggestedSlideCount} slide để giữ mạch chương rõ hơn.`
                    : `Scope suggestion: use about ${scopeRecommendation.suggestedSlideCount} slides to preserve the chapter flow more clearly.`}
                </div>
              )}
              <label className="folder-studio-form-row">
                <span>{t('slides.desiredSlides')}</span>
                <select
                  value={brief.desiredSlideCount}
                  onChange={(event) => setBrief((current) => ({ ...current, desiredSlideCount: Number(event.target.value) || 12 }))}
                >
                  {DECK_LENGTH_OPTIONS.map((count) => (
                    <option key={count} value={count}>
                      {language === 'vi'
                        ? (count === 8 ? 'Ngắn - 8 slide' : count === 12 ? 'Vừa - 12 slide' : 'Đầy đủ - 18 slide')
                        : (count === 8 ? 'Short - 8 slides' : count === 12 ? 'Medium - 12 slides' : 'Full - 18 slides')}
                    </option>
                  ))}
                </select>
              </label>
              <label className="folder-studio-form-row">
                <span>Theme</span>
                <select value={brief.themeKey} onChange={(event) => setBrief((current) => ({ ...current, themeKey: event.target.value }))}>
                  {THEME_OPTIONS.map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                  ))}
                </select>
              </label>
              <label className="folder-studio-form-row">
                <span>{t('slides.audience')}</span>
                <select value={brief.audience} onChange={(event) => setBrief((current) => ({ ...current, audience: event.target.value }))}>
                  {AUDIENCE_OPTIONS.map((option, index) => (
                    <option key={option} value={option}>{audienceLabels[index] || option}</option>
                  ))}
                </select>
              </label>
              <label className="folder-studio-form-row">
                <span>{t('slides.tone')}</span>
                <select value={brief.tone} onChange={(event) => setBrief((current) => ({ ...current, tone: event.target.value }))}>
                  {TONE_OPTIONS.map((option, index) => (
                    <option key={option} value={option}>{toneLabels[index] || option}</option>
                  ))}
                </select>
              </label>
              <label className="folder-studio-form-row">
                <span>{language === 'vi' ? 'Kiểu deck' : 'Deck mode'}</span>
                <select value={brief.mode} onChange={(event) => setBrief((current) => ({ ...current, mode: event.target.value }))}>
                  {DECK_MODE_OPTIONS.map((option) => (
                    <option key={option} value={option}>{getModeLabel(option)}</option>
                  ))}
                </select>
              </label>
              <label className="folder-studio-form-row">
                <span>{t('slides.languageStyle')}</span>
                <select value={brief.languageStyle} onChange={(event) => setBrief((current) => ({ ...current, languageStyle: event.target.value }))}>
                  {LANGUAGE_STYLE_OPTIONS.map((option, index) => (
                    <option key={option} value={option}>{languageStyleLabels[index] || option}</option>
                  ))}
                </select>
              </label>
              <label className="folder-studio-form-row">
                <span>{t('slides.narrativeGoal')}</span>
                <textarea
                  rows={4}
                  value={brief.narrativeGoal}
                  onChange={(event) => setBrief((current) => ({ ...current, narrativeGoal: event.target.value }))}
                  placeholder={language === 'vi' ? 'Mục tiêu cấu trúc và nhấn mạnh cho deck workspace này' : 'Structure and emphasis goal for this workspace deck'}
                />
              </label>
            </div>

            <div className="folder-studio-action-section">
              <div className="folder-studio-section-label">{language === 'vi' ? 'Trạng thái demo' : 'Demo readiness'}</div>
              <div className="folder-studio-readiness-panel">
                {workspaceReadinessItems.map((item) => (
                  <div key={item.key} className={`folder-studio-readiness-item tone-${item.tone}`}>
                    <span>{item.label}</span>
                    <strong>{item.value}</strong>
                  </div>
                ))}
              </div>
            </div>

            <div className="folder-studio-action-section">
              <div className="folder-studio-section-label">{language === 'vi' ? 'Xuất bản & Chia sẻ' : 'Publish & Share'}</div>
              <button type="button" className="folder-studio-action" onClick={handleSaveSlide} disabled={!selectedDraft}>
                <span className="folder-studio-action-icon"><LuSave aria-hidden="true" /></span>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Lưu slide hiện tại' : 'Save current slide'}</strong>
                  <span>{language === 'vi' ? 'Lưu title, body, notes và trạng thái editor vào deck.' : 'Save title, body, notes, and editor state into the deck.'}</span>
                </span>
                <span className="folder-studio-action-badge">Save</span>
              </button>
              <button
                type="button"
                className="folder-studio-action"
                onClick={handleDownloadHtml}
                disabled={isExportDisabled}
              >
                <span className="folder-studio-action-icon"><LuDownload aria-hidden="true" /></span>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Tải HTML' : 'Download HTML'}</strong>
                  <span>{language === 'vi' ? 'Tải deck thành file .html độc lập.' : 'Download the deck as a standalone .html file.'}</span>
                </span>
                <span className="folder-studio-action-badge">{exportingFormat === 'html' ? '...' : 'HTML'}</span>
              </button>
              <button
                type="button"
                className="folder-studio-action"
                onClick={handleOpenPrint}
                disabled={isExportDisabled}
              >
                <span className="folder-studio-action-icon"><LuPrinter aria-hidden="true" /></span>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'In / Lưu PDF' : 'Print / Save as PDF'}</strong>
                  <span>{language === 'vi' ? 'Mở bản in thân thiện để lưu PDF từ browser.' : 'Open the print-friendly view for browser PDF saving.'}</span>
                </span>
                <span className="folder-studio-action-badge">{exportingFormat === 'print' ? '...' : 'PDF'}</span>
              </button>
              <button
                type="button"
                className="folder-studio-action"
                onClick={handleDownloadPptx}
                disabled={isExportDisabled}
              >
                <span className="folder-studio-action-icon"><LuFileDown aria-hidden="true" /></span>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Tải PPTX' : 'Download PPTX'}</strong>
                  <span>{language === 'vi' ? 'Tải file PowerPoint có title, body và notes cơ bản.' : 'Download a basic PowerPoint file with title, body, and notes.'}</span>
                </span>
                <span className="folder-studio-action-badge">{exportingFormat === 'pptx' ? '...' : 'PPTX'}</span>
              </button>
            </div>

            <div className="folder-studio-action-section">
              <div className="folder-studio-section-label">{language === 'vi' ? 'Quản trị' : 'Admin'}</div>
              <button type="button" className="folder-studio-action" onClick={() => loadWorkspace()}>
                <span className="folder-studio-action-icon"><LuRefreshCw aria-hidden="true" /></span>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Làm mới dữ liệu' : 'Refresh data'}</strong>
                  <span>{language === 'vi' ? 'Tải lại sources, deck, progress và metadata của workspace' : 'Reload sources, deck, progress, and workspace metadata'}</span>
                </span>
                <span className="folder-studio-action-badge">Sync</span>
              </button>
              <button type="button" className="folder-studio-action tone-danger" onClick={handleDeleteFolder}>
                <span className="folder-studio-action-icon"><LuX aria-hidden="true" /></span>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Xóa workspace' : 'Delete workspace'}</strong>
                  <span>{language === 'vi' ? 'Thao tác này sẽ xóa workspace và các source bên trong' : 'This action will delete the workspace and all sources inside it'}</span>
                </span>
                <span className="folder-studio-action-badge">Delete</span>
              </button>
            </div>
          </aside>
        </div>
        <footer className="folder-studio-filmstrip" aria-label={language === 'vi' ? 'Danh sach slide' : 'Slide thumbnails'}>
          {slideItems.map((item) => (
            <button
              key={item.id}
              type="button"
              className={`folder-studio-filmstrip-item${item.id === selectedSlideId ? ' active' : ''}`}
              onClick={() => handleSelectSlide(item.id)}
            >
              <SlideThumbnail
                editorState={normalizeSlideEditorState(item)}
                imageVm={buildSlideImageViewModel(item)}
                labels={canvasLabels}
              />
              <strong>{item.heading || 'Untitled'}</strong>
            </button>
          ))}
          {!slideItems.length && (
            <div className="folder-studio-filmstrip-empty">
              {hasSelectedScope
                ? (language === 'vi' ? 'Tao deck de co thumbnail slide.' : 'Generate a deck to show slide thumbnails.')
                : (language === 'vi' ? 'Chon source va scope truoc.' : 'Select a source and scope first.')}
            </div>
          )}
        </footer>
      </section>

      {isScopePickerOpen && selectedSource && (
        <div className="scope-picker-backdrop" onClick={handleCloseScopePicker}>
          <div className="scope-picker-modal" onClick={(event) => event.stopPropagation()}>
            <div className="scope-picker-header">
              <div>
                <p className="scope-picker-kicker">{t('slides.scopePicker.kicker')}</p>
                <h3>{selectedSource.fileName}</h3>
                <span>
                  {selectedSource.status !== 3
                    ? t('slides.scopePicker.processingShort')
                    : `${t('slides.scopePicker.availableCount', { count: selectableSections.length })} | ${t('slides.scopePicker.selectedButton', { count: selectedSectionIds.length })}`}
                </span>
              </div>
              <button type="button" className="scope-picker-close" onClick={handleCloseScopePicker}>
                {t('slides.scopePicker.close')}
              </button>
            </div>

            <div className="scope-picker-actions">
              <button type="button" onClick={handleSelectAllSections} disabled={!selectableSections.length}>
                {t('slides.scopePicker.wholeDocument')}
              </button>
              <button type="button" onClick={handleClearSections} disabled={!selectedSectionIds.length}>
                {t('slides.scopePicker.clear')}
              </button>
              <button type="button" onClick={handleAnalyzeStructure} disabled={isAnalyzingStructure}>
                {isAnalyzingStructure ? (language === 'vi' ? 'Đang phân tích...' : 'Analyzing...') : t('slides.scopePicker.analyzeAgain')}
              </button>
            </div>

            {getScopeDisabledReason(selectedSource) ? (
              <div className="folder-studio-scope-hint">{getScopeDisabledReason(selectedSource)}</div>
            ) : (
              <div className="scope-picker-list">
                {selectableSections.map((section, index) => {
                  const scopedId = buildScopedSectionId(selectedSource.id, section.sectionKey);
                  const isSelected = selectedSectionIds.includes(scopedId);
                  const isExpanded = expandedSectionIds.includes(scopedId);
                  const title = getScopeSectionTitle(section, index, language);
                  const preview = getScopeSectionPreview(section, title);
                  const previewContent = preview || (language === 'vi'
                    ? 'Không có nội dung xem trước.'
                    : 'No preview content available.');

                  return (
                    <div
                      key={scopedId}
                      className={'scope-section-card' + (isSelected ? ' is-selected' : '') + (isExpanded ? ' is-expanded' : '')}
                    >
                      <div className="scope-section-row">
                        <div className="scope-section-check">
                          <input
                            id={scopedId}
                            type="checkbox"
                            checked={isSelected}
                            onChange={() => handleToggleSection(section.sectionKey)}
                            aria-label={title}
                          />
                        </div>
                        <div className="scope-section-title-wrap">
                          <strong className="scope-section-title">{title}</strong>
                        </div>
                        <div className="scope-section-actions">
                          <button
                            type="button"
                            className="scope-section-preview-button"
                            onClick={() => handleToggleSectionPreview(scopedId)}
                            aria-expanded={isExpanded}
                            aria-controls={`${scopedId}-preview`}
                          >
                            {isExpanded ? t('slides.scopePicker.collapse') : t('slides.scopePicker.preview')}
                          </button>
                        </div>
                      </div>
                      {isExpanded && (
                        <div id={`${scopedId}-preview`} className="scope-section-preview">
                          <div className="scope-section-preview-content">
                            <div className="scope-section-detail">{previewContent}</div>
                          </div>
                        </div>
                      )}
                    </div>
                  );
                })}

                {!selectableSections.length && (
                  <div className="folder-studio-empty-sidebar">
                    {t('slides.scopePicker.noStructureHint')}
                  </div>
                )}
              </div>
            )}

            <div className="scope-picker-footer">
              <span>
                {t('slides.scopePicker.selectedForSource', { count: selectedSectionIds.length })}
              </span>
              <button type="button" className="scope-picker-apply" onClick={handleCloseScopePicker}>
                {t('slides.scopePicker.apply')}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function FolderStudio() {
  const { workspaceId } = useParams();
  const [searchParams] = useSearchParams();
  const showStudioMockup = searchParams.get('studioMockup') === '1';

  if (showStudioMockup) {
    return <SlideStudioCanvaMockup workspaceName={`Workspace ${workspaceId || 'demo'}`} />;
  }

  return <FolderStudioRuntime />;
}

export default FolderStudio;
