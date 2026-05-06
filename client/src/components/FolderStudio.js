import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import {
  documentService,
  getApiErrorMessage,
  isApiJobNotFound,
  isSlideSchemaUnavailable,
  questionService,
  slideService,
  workspaceService,
} from '../services/api';
import { buildSlideImageViewModel } from '../services/slideImages';
import { formatEta, getProgressCounterLabel, isActiveProgress, isTerminalProgress, normalizeProgressState } from '../services/progress';
import { useAnimatedProgress } from '../hooks/useAnimatedProgress';
import { useToast } from './common/ToastProvider';
import { useLanguage } from '../context/LanguageContext';

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

const FONT_OPTIONS = ['Georgia', 'Trebuchet MS', 'Segoe UI', 'Palatino Linotype', 'Courier New'];
const FONT_SIZES = [14, 16, 18, 20, 24, 28, 32, 36];
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

function createFallbackEditorState(item) {
  return item?.editorState || {
    layoutVariant: 'standard',
    title: { text: item?.heading || '', fontFamily: 'Georgia', fontSize: 28, bold: true, italic: false, underline: false, align: 'left', bullet: false },
    subtitle: { text: item?.subheading || '', fontFamily: 'Segoe UI', fontSize: 16, bold: false, italic: false, underline: false, align: 'left', bullet: false },
    goal: { text: item?.goal || '', fontFamily: 'Segoe UI', fontSize: 14, bold: true, italic: false, underline: false, align: 'left', bullet: false },
    body: { text: Array.isArray(item?.bodyBlocks) ? item.bodyBlocks.join('\n') : '', fontFamily: 'Segoe UI', fontSize: 18, bold: false, italic: false, underline: false, align: 'left', bullet: true },
    notes: { text: item?.speakerNotes || '', fontFamily: 'Segoe UI', fontSize: 14, bold: false, italic: false, underline: false, align: 'left', bullet: false },
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
  return {
    fontFamily: block.fontFamily || 'Segoe UI',
    fontSize: `${block.fontSize || 18}px`,
    fontWeight: block.bold ? 700 : 400,
    fontStyle: block.italic ? 'italic' : 'normal',
    textDecoration: block.underline ? 'underline' : 'none',
    textAlign: block.align || 'left',
  };
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

function getQuestionCountFromCollection(payload) {
  if (Array.isArray(payload)) {
    return payload.length;
  }

  if (Array.isArray(payload?.questions)) {
    return payload.questions.length;
  }

  return 0;
}

function FolderStudio() {
  const { t, language } = useLanguage();
  const { showToast } = useToast();
  const { workspaceId } = useParams();
  const location = useLocation();
  const navigate = useNavigate();
  const fileInputRef = useRef(null);

  const [folder, setFolder] = useState(null);
  const [sources, setSources] = useState([]);
  const [deck, setDeck] = useState(null);
  const [drafts, setDrafts] = useState({});
  const [dirtyDrafts, setDirtyDrafts] = useState({});
  const [draftMeta, setDraftMeta] = useState({});
  const [history, setHistory] = useState({});
  const [selectedSlideId, setSelectedSlideId] = useState(null);
  const [activeField, setActiveField] = useState('body');
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
  const [brief, setBrief] = useState(DEFAULT_BRIEF);
  const [selectedSourceId, setSelectedSourceId] = useState(null);
  const [selectedSectionIds, setSelectedSectionIds] = useState([]);
  const [expandedSectionIds, setExpandedSectionIds] = useState([]);
  const [isScopePickerOpen, setIsScopePickerOpen] = useState(false);
  const [scopeRecommendation, setScopeRecommendation] = useState(null);
  const [filterText, setFilterText] = useState('');
  const [activeSidebarTab, setActiveSidebarTab] = useState('slides');
  const [isActionPanelOpen, setIsActionPanelOpen] = useState(false);
  const [, setAnimatingSlides] = useState({});
  const progressRef = useRef(null);
  const typewriterTimersRef = useRef({});
  const typewriterStateRef = useRef({});
  const animatedRevisionRef = useRef({});
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

  const selectedSlide = deck?.items?.find((item) => item.id === selectedSlideId) || deck?.items?.[0] || null;
  const selectedDraft = selectedSlide ? (drafts[selectedSlide.id] || createFallbackEditorState(selectedSlide)) : null;
  const selectedImageVm = selectedSlide ? buildSlideImageViewModel(selectedSlide) : null;

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
      const updated = await slideService.updateSlideItem(deck.id, selectedSlide.id, {
        heading: selectedDraft.title.text,
        subheading: selectedDraft.subtitle.text,
        goal: selectedDraft.goal.text,
        bodyBlocks: selectedDraft.body.text.split('\n').map((line) => line.trim()).filter(Boolean),
        speakerNotes: selectedDraft.notes.text,
        accentTone: selectedSlide.accentTone || '',
        editorState: selectedDraft,
      });

      setDeck((current) => ({
        ...current,
        items: current.items.map((item) => (item.id === updated.id ? updated : item)),
      }));
      setDrafts((current) => ({
        ...current,
        [updated.id]: createFallbackEditorState(updated),
      }));
      setDirtyDrafts((current) => ({
        ...current,
        [updated.id]: false,
      }));

      setDraftMeta((current) => ({
        ...current,
        [updated.id]: { sourceRevision: getSlideSourceRevision(updated) },
      }));
      setHistory((current) => ({
        ...current,
        [updated.id]: { past: [], future: [] },
      }));
      showToast({
        type: 'success',
        message: language === 'vi' ? `Đã lưu slide ${updated.slideIndex}.` : `Saved slide ${updated.slideIndex}.`,
      });
    } catch (err) {
      console.error(err);
      setError(language === 'vi' ? 'Không lưu được slide hiện tại.' : 'Could not save the current slide.');
    }
  };

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

    try {
    setError('');
    const response = await slideService.startGenerateSlidesForFolder(workspaceId, {
      ...brief,
      sourceIds: [selectedSource.id],
      selectedSectionIds,
      mode: brief.mode,
      scopePolicy: 'selected-sections-only',
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

    setQuestionError('');
    setQuestionProgress(normalizeProgressState({
      status: 'queued',
      stage: 'queued',
      stageLabel: language === 'vi' ? 'Chờ xử lý' : 'Queued',
      message: language === 'vi'
        ? 'Đã tạo job sinh câu hỏi cho source đã chọn.'
        : 'Created a question generation job for the selected source.',
      percent: 0,
      documentId: selectedSourceDocumentId,
    }, { documentId: selectedSourceDocumentId }));

    showToast({
      type: 'info',
      message: language === 'vi' ? 'Đã bắt đầu tạo bộ câu hỏi.' : 'Started generating the question bank.',
      description: language === 'vi'
        ? 'Tiến trình sẽ hiển thị ngay trong action panel.'
        : 'Progress will continue in the action panel.',
    });

    try {
      const startResult = await questionService.startGenerateQuestions(selectedSourceDocumentId, 5);
      const nextJobId = startResult?.jobId;
      if (!nextJobId) {
        throw new Error(language === 'vi'
          ? 'Không tạo được mã tiến trình cho question bank.'
          : 'Could not create a progress job for the question bank.');
      }

      const timeoutAt = Date.now() + (5 * 60 * 1000);

      while (Date.now() < timeoutAt) {
        let nextProgress;

        try {
          nextProgress = normalizeProgressState(
            await questionService.getGenerateProgress(nextJobId),
            { documentId: selectedSourceDocumentId, jobId: nextJobId }
          );
        } catch (progressError) {
          if (!isApiJobNotFound(progressError)) {
            throw progressError;
          }

          const workspaceSnapshot = await loadWorkspace({ silent: true });
          const refreshedSource = (workspaceSnapshot?.sourceData || []).find((source) => Number(source.id) === Number(selectedSourceDocumentId));
          const persistedCount = Number(refreshedSource?.questionsCount ?? refreshedSource?.QuestionsCount ?? 0);
          const fallbackQuestions = persistedCount > 0
            ? null
            : await questionService.getQuestionsByDocument(selectedSourceDocumentId);
          const recoveredCount = persistedCount > 0 ? persistedCount : getQuestionCountFromCollection(fallbackQuestions);

          if (recoveredCount > 0) {
            setQuestionProgress(normalizeProgressState(questionProgress, {
              documentId: selectedSourceDocumentId,
              jobId: nextJobId,
              status: 'completed',
              percent: 100,
              questionsGenerated: recoveredCount,
              message: language === 'vi'
                ? 'Khôi phục question bank sau khi mất tiến trình.'
                : 'Recovered the question bank after progress tracking was lost.',
            }));
            setQuestionError('');
            showToast({
              type: 'success',
              message: language === 'vi'
                ? `Đã khôi phục question bank (${recoveredCount} câu).`
                : `Recovered question bank (${recoveredCount} questions).`,
            });
            navigate(`/study/${selectedSourceDocumentId}`);
            return;
          }

          throw new Error(language === 'vi'
            ? 'Mất tiến trình tạo câu hỏi. Hãy thử lại.'
            : 'Question generation progress was lost. Please try again.');
        }

        setQuestionProgress(nextProgress);

        if (nextProgress.status === 'completed') {
          const latestQuestions = await questionService.getQuestionsByDocument(selectedSourceDocumentId);
          const generatedCount = getQuestionCountFromCollection(latestQuestions) || nextProgress.questionsGenerated || 0;
          await loadWorkspace({ silent: true });
          showToast({
            type: 'success',
            message: language === 'vi'
              ? `Đã tạo xong bộ câu hỏi (${generatedCount} câu).`
              : `Question bank ready (${generatedCount} questions).`,
          });
          navigate(`/study/${selectedSourceDocumentId}`);
          return;
        }

        if (nextProgress.status === 'failed') {
          throw new Error(nextProgress.error || nextProgress.detail || nextProgress.message || 'Question generation failed');
        }

        await new Promise((resolve) => setTimeout(resolve, 1200));
      }

      throw new Error(language === 'vi'
        ? 'Hết thời gian chờ tiến trình tạo câu hỏi.'
        : 'Timed out while waiting for question generation progress.');
    } catch (err) {
      console.error(err);
      const nextError = getApiErrorMessage(err, language === 'vi'
        ? 'Không thể tạo question bank lúc này.'
        : 'Could not generate the question bank right now.');
      setQuestionError(nextError);
      setQuestionProgress((current) => (
        current?.status === 'completed'
          ? current
          : normalizeProgressState(current, {
            documentId: selectedSourceDocumentId,
            status: 'failed',
            error: nextError,
            message: nextError,
          })
      ));
      showToast({
        type: 'error',
        message: language === 'vi' ? 'Không tạo được câu hỏi.' : 'Could not generate questions.',
        description: nextError,
      });
    } finally {
      setQuestionProgress((current) => (
        current?.status === 'completed' || current?.status === 'failed' ? current : null
      ));
    }
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

  const notifySoon = (label) => {
    setError('');
    showToast({
      type: 'info',
      message: language === 'vi'
        ? `${label} đã được đặt sẵn trong UI.`
        : `${label} is already scaffolded in the UI.`,
      description: language === 'vi'
        ? 'Mình sẽ nối backend flow ở phase tiếp theo.'
        : 'The backend flow can be wired in the next phase.',
    });
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
  const selectedImage = selectedImageVm?.selectedImage || null;
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

      {uploadNotice && <div className="alert alert-info">{uploadNotice}</div>}
      {error && <div className="alert alert-error">{error}</div>}
      {generationError && <div className="alert alert-error">{generationError}</div>}
      <section className="folder-studio-shell">
        <div className="folder-studio-topbar">
          <button type="button" className="folder-studio-mini-btn" onClick={() => navigate('/workspaces')}>
            &lt;
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

          <div className="folder-studio-topbar-actions">
            <button type="button" className="folder-studio-mini-btn" onClick={() => setIsActionPanelOpen((current) => !current)}>
              {isActionPanelOpen ? (language === 'vi' ? 'Ẩn điều khiển' : 'Hide controls') : (language === 'vi' ? 'Mở điều khiển' : 'Show controls')}
            </button>
            <button type="button" className="folder-studio-mini-btn" onClick={() => loadWorkspace()} disabled={uploading}>
              {language === 'vi' ? 'Làm mới' : 'Refresh'}
            </button>
            <button type="button" className="folder-studio-mini-btn" onClick={handleUploadClick} disabled={uploading}>
              {uploading ? (language === 'vi' ? 'Đang thêm...' : 'Adding...') : (language === 'vi' ? 'Thêm nguồn' : 'Add source')}
            </button>
            <div className="folder-studio-avatar">GV</div>
            <a
              className={`folder-studio-mini-primary${!deck ? ' is-disabled' : ''}`}
              href={deck ? slideService.getFolderDeckHtmlUrl(workspaceId) : undefined}
              target={deck ? '_blank' : undefined}
              rel={deck ? 'noreferrer' : undefined}
              onClick={(event) => {
                if (!deck) {
                  event.preventDefault();
                }
              }}
            >
              HTML / PDF
            </a>
          </div>
        </div>

        <div className={`folder-studio-main${isActionPanelOpen ? ' action-open' : ' action-closed'}`}>
          <aside className={`folder-studio-sidebar tab-${activeSidebarTab}`}>
            <div className="folder-studio-panel-title">{language === 'vi' ? 'Điều hướng nội dung' : 'Content navigation'}</div>

            <div className="folder-studio-sidebar-tabs">
              <button
                type="button"
                className={`folder-studio-sidebar-tab${activeSidebarTab === 'slides' ? ' active' : ''}`}
                onClick={() => setActiveSidebarTab('slides')}
              >
                {language === 'vi' ? 'Cấu trúc slide' : 'Slides'}
              </button>
              <button
                type="button"
                className={`folder-studio-sidebar-tab${activeSidebarTab === 'sources' ? ' active' : ''}`}
                onClick={() => setActiveSidebarTab('sources')}
              >
                {language === 'vi' ? 'Nguồn tài liệu' : 'Sources'}
              </button>
            </div>
            <div className="folder-studio-panel-title">{language === 'vi' ? 'Nguồn / Slides' : 'Sources / Slides'}</div>

            <div className="folder-studio-filter">
              <input
                type="text"
                value={filterText}
                onChange={(event) => setFilterText(event.target.value)}
                placeholder={language === 'vi' ? 'Tìm trong tên file hoặc summary' : 'Search by file name or summary'}
              />
              <button type="button" className="folder-studio-mini-btn" onClick={() => setFilterText('')}>
                x
              </button>
            </div>

            <div className="folder-studio-sidebar-cta">
              <button type="button" className="folder-studio-side-button" onClick={handleUploadClick} disabled={uploading}>
                {language === 'vi' ? '+ Thêm source vào workspace' : '+ Add source to workspace'}
              </button>
            </div>

            <div className="folder-studio-section-label">{language === 'vi' ? 'Nguồn tài liệu' : 'Document sources'}</div>
            <div className="folder-studio-source-list">
              {filteredSources.length === 0 && (
                <div className="folder-studio-empty-sidebar">
                  {language === 'vi' ? 'Chưa có source nào trong workspace này.' : 'No sources in this workspace yet.'}
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

            <div className="folder-studio-section-label">{language === 'vi' ? 'Cấu trúc slide' : 'Slide structure'}</div>
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
                    </div>

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

            <div className="folder-studio-flow-list">
              {!slideItems.length && (
                <div className="folder-studio-empty-sidebar">
                  {language === 'vi' ? 'Chưa có deck. Chọn source xong rồi bấm "Tạo slide mới từ nội dung".' : 'No deck yet. Select sources and click "Generate slides from content".'}
                </div>
              )}

              {slideItems.map((item) => (
                <button
                  key={item.id}
                  type="button"
                  className={`folder-studio-flow-item${item.id === selectedSlideId ? ' active' : ''}`}
                  onClick={() => setSelectedSlideId(item.id)}
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
          </aside>

          <section className="folder-studio-center">
            <div className="folder-studio-toolbar">
              <select
                value={activeFieldState?.fontFamily || 'Segoe UI'}
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
              <button type="button" className={`folder-studio-toolbar-btn${activeFieldState?.bold ? ' active' : ''}`} onClick={() => handleStyleChange((block) => ({ ...block, bold: !block.bold }))} disabled={!selectedDraft}>B</button>
              <button type="button" className={`folder-studio-toolbar-btn${activeFieldState?.italic ? ' active' : ''}`} onClick={() => handleStyleChange((block) => ({ ...block, italic: !block.italic }))} disabled={!selectedDraft}>I</button>
              <button type="button" className={`folder-studio-toolbar-btn${activeFieldState?.underline ? ' active' : ''}`} onClick={() => handleStyleChange((block) => ({ ...block, underline: !block.underline }))} disabled={!selectedDraft}>U</button>
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
              <button type="button" className="folder-studio-toolbar-btn" onClick={() => setMediaOpen((current) => !current)} disabled={!selectedSlide}>
                {mediaOpen ? (language === 'vi' ? 'Ẩn media' : 'Hide media') : (language === 'vi' ? 'Mở media' : 'Open media')}
              </button>
            </div>

            <div className="folder-studio-canvas">
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
                  <div className="folder-studio-empty">
                    <h3>
                      {language === 'vi'
                        ? 'Workspace studio sẵn sàng'
                        : 'Workspace studio is ready'}
                    </h3>

                    <p>
                      {language === 'vi'
                        ? 'Upload nhiều source vào workspace, chọn các source đã Completed, sau đó sinh deck để bắt đầu chỉnh sửa.'
                        : 'Upload sources, select completed sources, then generate a deck to start editing.'}
                    </p>

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
                        disabled={!canGenerate}
                        title={generateDisabledReason || undefined}
                      >
                        {language === 'vi' ? 'Tạo deck' : 'Generate deck'}
                      </button>
                    </div>
                  </div>
                ) 
              ) : (
                <>
                  <article className="folder-slide-card">
                    <div className="folder-slide-layout">
                      <div className="folder-slide-copy">
                        <div className={`folder-editable-block${activeField === 'title' ? ' active' : ''}`} onClick={() => setActiveField('title')}>
                          <textarea
                            rows={2}
                            value={selectedDraft.title.text}
                            onFocus={() => setActiveField('title')}
                            onChange={(event) => handleFieldTextChange('title', event.target.value)}
                            className="folder-slide-title-input"
                            style={applyTextStyle(selectedDraft.title)}
                            placeholder={slideTitlePlaceholder}
                          />
                        </div>

                        <div className={`folder-editable-block${activeField === 'subtitle' ? ' active' : ''}`} onClick={() => setActiveField('subtitle')}>
                          <textarea
                            rows={2}
                            value={selectedDraft.subtitle.text}
                            onFocus={() => setActiveField('subtitle')}
                            onChange={(event) => handleFieldTextChange('subtitle', event.target.value)}
                            className="folder-slide-subtitle-input"
                            style={applyTextStyle(selectedDraft.subtitle)}
                            placeholder="Subheading / context"
                          />
                        </div>

                        <div className={`folder-editable-block tone-soft${activeField === 'goal' ? ' active' : ''}`} onClick={() => setActiveField('goal')}>
                          <textarea
                            rows={2}
                            value={selectedDraft.goal.text}
                            onFocus={() => setActiveField('goal')}
                            onChange={(event) => handleFieldTextChange('goal', event.target.value)}
                            className="folder-slide-goal-input"
                            style={applyTextStyle(selectedDraft.goal)}
                            placeholder={slideGoalPlaceholder}
                          />
                        </div>

                        <div className={`folder-editable-block${activeField === 'body' ? ' active' : ''}`} onClick={() => setActiveField('body')}>
                          <textarea
                            rows={8}
                            value={selectedDraft.body.text}
                            onFocus={() => setActiveField('body')}
                            onChange={(event) => handleFieldTextChange('body', event.target.value)}
                            className="folder-slide-body-input"
                            style={applyTextStyle(selectedDraft.body)}
                            placeholder={bodyPlaceholder}
                          />
                          <small>
                            {selectedDraft.body.bullet
                              ? (language === 'vi' ? 'Bullet mode đang bật: mỗi dòng sẽ được lưu thành 1 body block.' : 'Bullet mode is on: each line will be saved as one body block.')
                              : (language === 'vi' ? 'Đang ở text mode: nội dung vẫn được lưu theo từng dòng.' : 'Text mode is on: content is still saved line by line.')}
                          </small>
                        </div>
                      </div>

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

                  <div className="folder-studio-panels">
                    <section className="folder-studio-panel-card">
                      <div className="folder-studio-panel-card-head">
                        <strong>{language === 'vi' ? 'Speaker notes' : 'Speaker notes'}</strong>
                        <span>{t('slides.slideLabel', { index: selectedSlide.slideIndex })}</span>
                      </div>
                      <textarea
                        rows={5}
                        value={selectedDraft.notes.text}
                        onFocus={() => setActiveField('notes')}
                        onChange={(event) => handleFieldTextChange('notes', event.target.value)}
                        className={`folder-slide-notes-input${activeField === 'notes' ? ' active' : ''}`}
                        style={applyTextStyle(selectedDraft.notes)}
                        placeholder={notesPlaceholder}
                      />
                    </section>

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
                  </div>
                </>
              )}
            </div>
          </section>

          <aside className={`folder-studio-rpanel${isActionPanelOpen ? ' open' : ''}`}>
            <div className="folder-studio-panel-title">{language === 'vi' ? 'Studio / Hành động' : 'Studio / Actions'}</div>

            <div className="folder-studio-action-section">
              <div className="folder-studio-section-label">{language === 'vi' ? 'Tạo mới' : 'Create'}</div>
              <button type="button" className="folder-studio-action tone-primary" onClick={handleGenerateDeck} disabled={!canGenerate} title={generateDisabledReason || undefined}>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Tạo slide mới từ nội dung' : 'Generate slides from content'}</strong>
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
              <div className="folder-studio-section-label">{language === 'vi' ? 'Phân tích & Tóm tắt' : 'Analysis & Summary'}</div>
              <button type="button" className="folder-studio-action folder-studio-action-placeholder" onClick={() => notifySoon(language === 'vi' ? 'Tóm tắt nội dung' : 'Summarize content')}>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Tóm tắt nội dung' : 'Summarize content'}</strong>
                  <span>{language === 'vi' ? 'Tổng hợp summary cấp workspace từ các source đã chọn' : 'Build a workspace-level summary from selected sources'}</span>
                </span>
                <span className="folder-studio-action-badge">Soon</span>
              </button>
              <button type="button" className="folder-studio-action folder-studio-action-placeholder" onClick={() => notifySoon(language === 'vi' ? 'Phân tích ý chính' : 'Analyze key ideas')}>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Phân tích ý chính' : 'Analyze key ideas'}</strong>
                  <span>{language === 'vi' ? 'Đặt sẵn cho luồng concept extraction cấp workspace' : 'Scaffold for workspace-level concept extraction'}</span>
                </span>
                <span className="folder-studio-action-badge">Soon</span>
              </button>
              <button type="button" className="folder-studio-action folder-studio-action-placeholder" onClick={() => notifySoon(language === 'vi' ? 'Xây dựng sơ đồ tư duy' : 'Build a mind map')}>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Xây dựng sơ đồ tư duy' : 'Build a mind map'}</strong>
                  <span>{language === 'vi' ? 'Nối vào luồng mindmap trong phase tiếp theo' : 'Reserved for the next-phase mindmap flow'}</span>
                </span>
                <span className="folder-studio-action-badge">Soon</span>
              </button>
            </div>

            <div className="folder-studio-action-section">
              <div className="folder-studio-section-label">{language === 'vi' ? 'Xuất bản & Chia sẻ' : 'Publish & Share'}</div>
              <button type="button" className="folder-studio-action" onClick={handleSaveSlide} disabled={!selectedDraft}>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Lưu slide hiện tại' : 'Save current slide'}</strong>
                  <span>{language === 'vi' ? 'Persist editorState, body blocks và notes vào deck' : 'Persist editorState, body blocks, and notes into the deck'}</span>
                </span>
                <span className="folder-studio-action-badge">Save</span>
              </button>
              <a
                className={`folder-studio-action${!deck ? ' is-disabled' : ''}`}
                href={deck ? slideService.getFolderDeckHtmlUrl(workspaceId) : undefined}
                target={deck ? '_blank' : undefined}
                rel={deck ? 'noreferrer' : undefined}
                onClick={(event) => {
                  if (!deck) {
                    event.preventDefault();
                  }
                }}
              >
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Tải xuống HTML / PDF' : 'Download HTML / PDF'}</strong>
                  <span>{language === 'vi' ? 'Xuất deck cấp workspace để preview hoặc in PDF từ browser' : 'Export the workspace deck for preview or browser-based PDF printing'}</span>
                </span>
                <span className="folder-studio-action-badge">Export</span>
              </a>
              <button type="button" className="folder-studio-action folder-studio-action-placeholder" onClick={() => notifySoon(language === 'vi' ? 'Xuất PowerPoint' : 'Export PowerPoint')}>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Xuất PowerPoint' : 'Export PowerPoint'}</strong>
                  <span>{language === 'vi' ? 'Cho phase xuất file pptx sau này' : 'Reserved for a future PPTX export phase'}</span>
                </span>
                <span className="folder-studio-action-badge">Soon</span>
              </button>
              <button type="button" className="folder-studio-action folder-studio-action-placeholder" onClick={() => notifySoon(language === 'vi' ? 'Chia sẻ liên kết' : 'Share link')}>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Chia sẻ liên kết' : 'Share link'}</strong>
                  <span>{language === 'vi' ? 'Đặt sẵn cho shareable review link' : 'Scaffold for a shareable review link'}</span>
                </span>
                <span className="folder-studio-action-badge">Soon</span>
              </button>
            </div>

            <div className="folder-studio-action-section">
              <div className="folder-studio-section-label">{language === 'vi' ? 'Quản trị' : 'Admin'}</div>
              <button type="button" className="folder-studio-action" onClick={() => loadWorkspace()}>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Làm mới dữ liệu' : 'Refresh data'}</strong>
                  <span>{language === 'vi' ? 'Tải lại sources, deck, progress và metadata của workspace' : 'Reload sources, deck, progress, and workspace metadata'}</span>
                </span>
                <span className="folder-studio-action-badge">Sync</span>
              </button>
              <button type="button" className="folder-studio-action tone-danger" onClick={handleDeleteFolder}>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Xóa workspace' : 'Delete workspace'}</strong>
                  <span>{language === 'vi' ? 'Thao tác này sẽ xóa workspace và các source bên trong' : 'This action will delete the workspace and all sources inside it'}</span>
                </span>
                <span className="folder-studio-action-badge">Delete</span>
              </button>
            </div>
          </aside>
        </div>
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
                    : `${t('slides.scopePicker.availableCount', { count: selectableSections.length })} • ${t('slides.scopePicker.selectedButton', { count: selectedSectionIds.length })}`}
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

export default FolderStudio;
