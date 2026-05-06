const ACTIVE_STATUSES = new Set(['queued', 'running']);
const TERMINAL_STATUSES = new Set(['completed', 'failed']);

const normalizeString = (value, fallback = '') => (
  typeof value === 'string' ? value.trim() : fallback
);

const normalizeOptionalNumber = (value) => (
  typeof value === 'number' && Number.isFinite(value) ? value : null
);

const ENGLISH_UNIT_LABELS = {
  'câu hỏi': 'questions',
  trang: 'pages',
  'khối nội dung': 'content blocks',
};

const ENGLISH_STAGE_LABELS = {
  queued: 'Queued',
  preparing: 'Preparing',
  extracting: 'Extracting text',
  analyzing: 'Analyzing content',
  saving: 'Saving results',
  completed: 'Completed',
  failed: 'Failed',
};

export const normalizeProgressState = (raw, fallback = {}) => {
  const source = raw && typeof raw === 'object' ? raw : {};
  const base = fallback && typeof fallback === 'object' ? fallback : {};

  const progress = {
    jobId: normalizeString(source.jobId || source.JobId, normalizeString(base.jobId)),
    documentId: normalizeOptionalNumber(source.documentId ?? source.DocumentId ?? base.documentId),
    folderProjectId: normalizeOptionalNumber(source.folderProjectId ?? source.FolderProjectId ?? base.folderProjectId),
    slideDeckId: normalizeOptionalNumber(source.slideDeckId ?? source.SlideDeckId ?? base.slideDeckId),
    status: normalizeString(source.status, normalizeString(base.status, 'queued')).toLowerCase() || 'queued',
    stage: normalizeString(source.stage, normalizeString(base.stage, 'queued')),
    stageLabel: normalizeString(source.stageLabel, normalizeString(base.stageLabel)),
    message: normalizeString(source.message, normalizeString(base.message)),
    detail: normalizeString(source.detail, normalizeString(base.detail)),
    error: normalizeString(source.error, normalizeString(base.error)),
    topicTag: normalizeString(source.topicTag, normalizeString(base.topicTag)),
    unitLabel: normalizeString(source.unitLabel, normalizeString(base.unitLabel)),
    percent: normalizeOptionalNumber(source.percent ?? base.percent) ?? 0,
    current: normalizeOptionalNumber(source.current ?? base.current),
    total: normalizeOptionalNumber(source.total ?? base.total),
    stageIndex: normalizeOptionalNumber(source.stageIndex ?? base.stageIndex),
    stageCount: normalizeOptionalNumber(source.stageCount ?? base.stageCount),
    elapsedSeconds: normalizeOptionalNumber(source.elapsedSeconds ?? base.elapsedSeconds) ?? 0,
    estimatedRemainingSeconds: normalizeOptionalNumber(source.estimatedRemainingSeconds ?? base.estimatedRemainingSeconds),
    questionsGenerated: normalizeOptionalNumber(source.questionsGenerated ?? base.questionsGenerated),
    slidesGenerated: normalizeOptionalNumber(source.slidesGenerated ?? base.slidesGenerated),
  };

  return progress;
};

export const isActiveProgress = (progressOrStatus) => {
  const status = typeof progressOrStatus === 'string'
    ? progressOrStatus
    : progressOrStatus?.status;

  return ACTIVE_STATUSES.has(String(status || '').toLowerCase());
};

export const isTerminalProgress = (progressOrStatus) => {
  const status = typeof progressOrStatus === 'string'
    ? progressOrStatus
    : progressOrStatus?.status;

  return TERMINAL_STATUSES.has(String(status || '').toLowerCase());
};

export const formatEta = (seconds, options = {}) => {
  const language = options.language === 'en' ? 'en' : 'vi';

  if (typeof seconds !== 'number' || !Number.isFinite(seconds)) {
    return null;
  }

  if (seconds <= 0) {
    return language === 'vi' ? 'Sắp xong...' : 'Almost done...';
  }

  if (seconds < 60) {
    return `${seconds}s`;
  }

  const minutes = Math.floor(seconds / 60);
  const remain = seconds % 60;
  return language === 'vi'
    ? `${minutes}p ${remain}s`
    : `${minutes}m ${remain}s`;
};

export const getSubProgress = (current, total) => {
  if (typeof current !== 'number' || typeof total !== 'number' || total <= 0) {
    return null;
  }

  return Math.max(0, Math.min(100, Math.round((current / total) * 100)));
};

export const getProgressCounterLabel = (progress, options = {}) => {
  const language = options.language === 'en' ? 'en' : 'vi';

  if (typeof progress?.current !== 'number' || typeof progress?.total !== 'number' || progress.total <= 0) {
    return null;
  }

  const defaultUnit = language === 'vi' ? 'mục' : 'items';
  const rawUnitLabel = normalizeString(progress.unitLabel);
  const unitLabel = language === 'en'
    ? ENGLISH_UNIT_LABELS[rawUnitLabel.toLowerCase()] || rawUnitLabel || defaultUnit
    : rawUnitLabel || defaultUnit;

  return `${progress.current}/${progress.total} ${unitLabel}`;
};

export const getProgressStageLabel = (progress, options = {}) => {
  const language = options.language === 'en' ? 'en' : 'vi';

  if (language === 'en') {
    const stageKey = normalizeString(progress?.stage).toLowerCase();
    const statusKey = normalizeString(progress?.status).toLowerCase();
    return ENGLISH_STAGE_LABELS[stageKey]
      || ENGLISH_STAGE_LABELS[statusKey]
      || normalizeString(progress?.stageLabel)
      || normalizeString(progress?.stage)
      || normalizeString(progress?.status)
      || 'Queued';
  }

  return normalizeString(progress?.stageLabel)
    || normalizeString(progress?.stage)
    || normalizeString(progress?.status)
    || 'Đang chờ';
};
