const STATUS_TONES = {
  Good: 'good',
  NeedsReview: 'review',
  LowConfidence: 'low',
  ExtractionFailed: 'failed',
};

const FIGURE_REGION_TYPES = new Set(['FigureCandidate', 'DiagramCandidate']);
const FAILED_STATUSES = new Set(['ExtractionFailed', 'Rejected', 'Failed', 'Error']);
const REVIEW_STATUSES = new Set(['NeedsReview', 'AcceptedWithWarnings', 'SummaryOnlyRecommended']);
const GOOD_STATUSES = new Set(['AutoGenerateAllowed', 'Accepted', 'Good']);

function read(value, ...keys) {
  if (!value || typeof value !== 'object') {
    return undefined;
  }

  for (const key of keys) {
    if (value[key] !== undefined) {
      return value[key];
    }
  }

  return undefined;
}

function asArray(value) {
  if (!value) {
    return [];
  }

  if (Array.isArray(value)) {
    return value.filter((item) => item !== null && item !== undefined);
  }

  if (typeof value === 'string') {
    return value.trim() ? [value.trim()] : [];
  }

  if (typeof value === 'object') {
    return Object.values(value).flatMap(asArray);
  }

  return [String(value)];
}

function asNumber(value) {
  if (typeof value === 'number' && Number.isFinite(value)) {
    return value;
  }

  if (typeof value === 'string' && value.trim()) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }

  return null;
}

function uniqueStrings(values) {
  const seen = new Set();
  return values
    .map((value) => (typeof value === 'string' ? value.trim() : String(value ?? '').trim()))
    .filter((value) => {
      if (!value || seen.has(value.toLowerCase())) {
        return false;
      }

      seen.add(value.toLowerCase());
      return true;
    });
}

function normalizeRegion(region) {
  const type = read(region, 'regionType', 'RegionType') || 'Text';
  const layoutConfidence = asNumber(read(region, 'layoutConfidence', 'LayoutConfidence'));
  const visionConfidence = asNumber(read(region, 'visionConfidence', 'VisionConfidence'));
  const confidence = layoutConfidence ?? visionConfidence;

  return {
    pageNumber: read(region, 'pageNumber', 'PageNumber') || 1,
    regionType: type,
    text: read(region, 'text', 'Text') || '',
    confidence,
    layoutConfidence,
    visionConfidence,
    needsReview: Boolean(read(region, 'needsReview', 'NeedsReview')),
    reviewTags: asArray(read(region, 'reviewTags', 'ReviewTags')),
    description: read(region, 'description', 'Description') || '',
    extractedLabels: asArray(read(region, 'extractedLabels', 'ExtractedLabels')),
    relationships: asArray(read(region, 'relationships', 'Relationships')),
    uncertaintyReason: read(region, 'uncertaintyReason', 'UncertaintyReason') || '',
  };
}

function collectRegions(result) {
  const topLevelRegions = asArray(read(result, 'regions', 'Regions')).map(normalizeRegion);
  const pageRegions = asArray(read(result, 'pages', 'Pages'))
    .flatMap((page) => asArray(read(page, 'regions', 'Regions')))
    .map(normalizeRegion);

  const seen = new Set();
  return [...topLevelRegions, ...pageRegions].filter((region) => {
    const key = [
      region.pageNumber,
      region.regionType,
      region.text.slice(0, 80),
      region.description.slice(0, 80),
    ].join('|');

    if (seen.has(key)) {
      return false;
    }

    seen.add(key);
    return true;
  });
}

function normalizePages(result) {
  return asArray(read(result, 'pages', 'Pages')).map((page) => ({
    pageNumber: read(page, 'pageNumber', 'PageNumber') || 1,
    confidence: asNumber(read(page, 'confidence', 'Confidence')),
    text: read(page, 'text', 'Text') || '',
    regionCount: asArray(read(page, 'regions', 'Regions')).length,
  }));
}

function normalizeStatus({ status, confidence, needsReview }) {
  if (FAILED_STATUSES.has(status)) {
    return 'ExtractionFailed';
  }

  if (confidence !== null && confidence < 0.65) {
    return 'LowConfidence';
  }

  if (needsReview || REVIEW_STATUSES.has(status)) {
    return 'NeedsReview';
  }

  if (GOOD_STATUSES.has(status) || (confidence !== null && confidence >= 0.82)) {
    return 'Good';
  }

  return 'NeedsReview';
}

export function formatUnderstandingConfidence(value, unknownLabel = 'Unknown') {
  const confidence = asNumber(value);
  return confidence === null ? unknownLabel : `${Math.round(confidence * 100)}%`;
}

export function normalizeDocumentUnderstanding(raw) {
  const latestRun = read(raw, 'latestRun', 'LatestRun') || raw;
  if (!latestRun || typeof latestRun !== 'object') {
    return null;
  }

  const result = read(latestRun, 'result', 'Result') || {};
  const quality = read(result, 'quality', 'Quality') || {};
  const rawStatus = read(latestRun, 'status', 'Status') || read(result, 'status', 'Status') || read(quality, 'status', 'Status') || '';
  const confidence = asNumber(
    read(latestRun, 'documentConfidence', 'DocumentConfidence')
      ?? read(result, 'confidence', 'Confidence')
      ?? read(quality, 'confidence', 'Confidence')
  );
  const needsReview = Boolean(
    read(latestRun, 'needsReview', 'NeedsReview')
      ?? read(quality, 'needsReview', 'NeedsReview')
  );
  const pages = normalizePages(result);
  const regions = collectRegions(result);
  const reviewRegions = regions.filter((region) => region.needsReview || region.uncertaintyReason || region.reviewTags.length > 0);
  const figureDescriptions = regions.filter((region) => FIGURE_REGION_TYPES.has(region.regionType) && (
    region.description || region.extractedLabels.length > 0 || region.relationships.length > 0
  ));
  const failureReasons = uniqueStrings([
    ...asArray(read(latestRun, 'failureReasons', 'FailureReasons')),
    ...asArray(read(quality, 'reasons', 'Reasons')),
    ...asArray(read(result, 'warnings', 'Warnings')),
  ]);
  const status = normalizeStatus({
    status: String(rawStatus || ''),
    confidence,
    needsReview,
  });

  return {
    id: read(latestRun, 'id', 'Id'),
    documentId: read(latestRun, 'documentId', 'DocumentId') || read(raw, 'documentId', 'DocumentId'),
    rawStatus: String(rawStatus || ''),
    status,
    tone: STATUS_TONES[status] || 'review',
    confidence,
    needsReview,
    pages,
    regions,
    reviewRegions,
    figureDescriptions,
    failureReasons,
    createdAt: read(latestRun, 'createdAt', 'CreatedAt'),
  };
}
