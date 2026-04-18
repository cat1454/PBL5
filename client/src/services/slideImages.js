const TEXT_ONLY_SLIDE_TYPES = new Set(['sectiondivider', 'quote']);

const normalizeOptionalString = (value) => {
  if (typeof value !== 'string') {
    return null;
  }

  const trimmed = value.trim();
  return trimmed ? trimmed : null;
};

const pickFirstString = (...values) => {
  for (const value of values) {
    const normalized = normalizeOptionalString(value);
    if (normalized) {
      return normalized;
    }
  }

  return null;
};

const toArray = (value) => (Array.isArray(value) ? value : []);

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

  const normalized = normalizeOptionalString(slideType);
  return normalized
    ? normalized.toLowerCase().replace(/[\s_-]+/g, '')
    : 'content';
};

const normalizeImageStatus = (status) => {
  const normalized = normalizeOptionalString(status);
  if (!normalized) {
    return null;
  }

  switch (normalized.toLowerCase().replace(/[\s_]+/g, '-')) {
    case 'queued':
    case 'running':
    case 'ready':
    case 'failed':
    case 'not-requested':
    case 'sourcing-web':
    case 'generating-fallback':
    case 'no-image-needed':
    case 'no-license-safe-image':
      return normalized.toLowerCase().replace(/[\s_]+/g, '-');
    default:
      return null;
  }
};

const resolveRawImageState = (item) => item?.imageState
  || item?.ImageState
  || item?.image?.state
  || item?.Image?.State
  || item?.slideImage?.state
  || null;

const resolveRawCandidates = (item) => item?.imageCandidates
  || item?.ImageCandidates
  || item?.image?.candidates
  || item?.Image?.Candidates
  || item?.slideImage?.candidates
  || [];

const resolveRawSelectedImage = (item) => item?.selectedImage
  || item?.SelectedImage
  || item?.image?.selectedImage
  || item?.Image?.SelectedImage
  || item?.slideImage?.selectedImage
  || null;

const resolveSelectedImageKey = (item) => pickFirstString(
  item?.selectedImageKey,
  item?.SelectedImageKey,
  item?.image?.selectedImageKey,
  item?.Image?.SelectedImageKey,
  item?.slideImage?.selectedImageKey
);

const deriveNeedsImage = (item, slideType) => {
  const explicit = item?.imageState?.needsImage
    ?? item?.ImageState?.NeedsImage
    ?? item?.image?.needsImage
    ?? item?.Image?.NeedsImage
    ?? item?.slideImage?.needsImage;

  if (typeof explicit === 'boolean') {
    return explicit;
  }

  return !TEXT_ONLY_SLIDE_TYPES.has(slideType);
};

export const normalizeImageCandidates = (rawCandidates) => toArray(rawCandidates)
  .map((candidate, index) => {
    const sourceType = normalizeOptionalString(candidate?.sourceType ?? candidate?.SourceType)?.toLowerCase() === 'generated'
      ? 'generated'
      : 'web';

    return {
      key: pickFirstString(candidate?.key, candidate?.Key, `candidate-${index + 1}`),
      sourceType,
      provider: pickFirstString(
        candidate?.provider,
        candidate?.Provider,
        candidate?.domain,
        candidate?.Domain,
        sourceType === 'generated' ? 'AI Generated' : 'Web'
      ),
      originUrl: pickFirstString(candidate?.originUrl, candidate?.OriginUrl, candidate?.sourceUrl, candidate?.SourceUrl),
      localAssetUrl: pickFirstString(
        candidate?.localAssetUrl,
        candidate?.LocalAssetUrl,
        candidate?.assetUrl,
        candidate?.AssetUrl,
        candidate?.imageUrl,
        candidate?.ImageUrl,
        candidate?.url,
        candidate?.Url
      ),
      thumbnailUrl: pickFirstString(
        candidate?.thumbnailUrl,
        candidate?.ThumbnailUrl,
        candidate?.localAssetUrl,
        candidate?.LocalAssetUrl,
        candidate?.assetUrl,
        candidate?.AssetUrl,
        candidate?.imageUrl,
        candidate?.ImageUrl,
        candidate?.url,
        candidate?.Url
      ),
      altText: pickFirstString(candidate?.altText, candidate?.AltText, 'Slide image candidate'),
      licenseLabel: pickFirstString(candidate?.licenseLabel, candidate?.LicenseLabel),
      attributionText: pickFirstString(candidate?.attributionText, candidate?.AttributionText),
      width: Number.isFinite(candidate?.width) ? candidate.width : (Number.isFinite(candidate?.Width) ? candidate.Width : null),
      height: Number.isFinite(candidate?.height) ? candidate.height : (Number.isFinite(candidate?.Height) ? candidate.Height : null),
      score: Number.isFinite(candidate?.score) ? candidate.score : (Number.isFinite(candidate?.Score) ? candidate.Score : null),
      isSelected: Boolean(candidate?.isSelected ?? candidate?.IsSelected),
      layoutMode: pickFirstString(candidate?.layoutMode, candidate?.LayoutMode),
    };
  })
  .filter((candidate) => candidate.localAssetUrl || candidate.originUrl);

export const normalizeSelectedImage = (item) => {
  const rawSelected = resolveRawSelectedImage(item);
  const candidates = normalizeImageCandidates(resolveRawCandidates(item));

  if (rawSelected && typeof rawSelected === 'object' && !Array.isArray(rawSelected)) {
    const normalized = normalizeImageCandidates([rawSelected])[0];
    if (normalized) {
      return normalized;
    }
  }

  const selectedKey = resolveSelectedImageKey(item);
  if (selectedKey) {
    const match = candidates.find((candidate) => candidate.key === selectedKey);
    if (match) {
      return match;
    }
  }

  return candidates.find((candidate) => candidate.isSelected) || null;
};

const getImageStatusLabel = (status) => {
  switch (status) {
    case 'ready':
      return 'Da co media preview';
    case 'queued':
      return 'Dang cho image workflow';
    case 'running':
      return 'Dang xu ly image workflow';
    case 'sourcing-web':
      return 'Dang tim anh web';
    case 'generating-fallback':
      return 'Dang tao anh fallback';
    case 'failed':
      return 'Image workflow that bai';
    case 'no-image-needed':
      return 'Slide uu tien text-only';
    case 'no-license-safe-image':
      return 'Chua co anh web an toan';
    case 'not-requested':
    default:
      return 'Chua co du lieu anh';
  }
};

const getImageBadgeLabel = (status, selectedImage, needsImage) => {
  if (!needsImage) {
    return 'Text-only';
  }

  if (selectedImage?.sourceType === 'generated') {
    return 'AI Generated';
  }

  if (selectedImage?.sourceType === 'web') {
    return 'Web';
  }

  if (status === 'no-license-safe-image') {
    return 'No license-safe image';
  }

  if (status === 'ready') {
    return 'Media ready';
  }

  return 'Image pending';
};

const getBadgeTone = (status, selectedImage, needsImage) => {
  if (!needsImage) {
    return 'muted';
  }

  if (selectedImage?.sourceType === 'generated') {
    return 'generated';
  }

  if (selectedImage?.sourceType === 'web') {
    return 'web';
  }

  if (status === 'failed' || status === 'no-license-safe-image') {
    return 'warning';
  }

  return 'pending';
};

const buildAttributionText = (selectedImage) => {
  if (!selectedImage) {
    return null;
  }

  if (selectedImage.sourceType === 'generated') {
    return `Nguon: ${selectedImage.provider}`;
  }

  const segments = [
    selectedImage.provider,
    selectedImage.licenseLabel,
    selectedImage.attributionText,
  ].filter(Boolean);

  return segments.length > 0 ? segments.join(' · ') : 'Nguon web';
};

const buildDefaultMessage = ({ needsImage, status, selectedImage, candidateCount }) => {
  if (!needsImage) {
    return 'Slide nay duoc de xuat giu text-only de giu nhip doc.';
  }

  if (selectedImage) {
    return 'Da co media preview cho slide nay.';
  }

  if (candidateCount > 0) {
    return `Da co ${candidateCount} image candidate cho slide nay.`;
  }

  switch (status) {
    case 'queued':
    case 'running':
      return 'Image workflow se bat dau sau khi noi dung slide on dinh.';
    case 'sourcing-web':
      return 'He thong dang tim anh web co nguon ro rang.';
    case 'generating-fallback':
      return 'Dang tao anh fallback tu prompt da redacted.';
    case 'no-license-safe-image':
      return 'Chua tim thay anh web dap ung chinh sach nguon/licensing.';
    case 'failed':
      return 'Image workflow gap loi. Can thu lai o pha backend tiep theo.';
    case 'not-requested':
    default:
      return 'Chua co payload image. UI dang san sang cho phase backend tiep theo.';
  }
};

export const buildSlideImageViewModel = (item) => {
  const slideType = normalizeSlideType(item?.slideType ?? item?.SlideType);
  const candidates = normalizeImageCandidates(resolveRawCandidates(item));
  const selectedImage = normalizeSelectedImage(item);
  const needsImage = deriveNeedsImage(item, slideType);
  const rawState = resolveRawImageState(item);
  const rawStatus = normalizeImageStatus(rawState?.status ?? rawState?.Status);
  const itemStatus = normalizeOptionalString(item?.status ?? item?.Status)?.toLowerCase();

  const status = rawStatus
    || (!needsImage
      ? 'no-image-needed'
      : (selectedImage || candidates.length > 0)
        ? 'ready'
        : (itemStatus === 'pending' || itemStatus === 'generating')
          ? 'queued'
          : 'not-requested');

  const message = pickFirstString(rawState?.message, rawState?.Message)
    || buildDefaultMessage({
      needsImage,
      status,
      selectedImage,
      candidateCount: candidates.length,
    });

  return {
    slideType,
    needsImage,
    status,
    statusLabel: getImageStatusLabel(status),
    badgeLabel: getImageBadgeLabel(status, selectedImage, needsImage),
    badgeTone: getBadgeTone(status, selectedImage, needsImage),
    selectedImage,
    candidates,
    hasCandidates: candidates.length > 0,
    candidateCount: candidates.length,
    helperText: message,
    attributionText: buildAttributionText(selectedImage),
  };
};
