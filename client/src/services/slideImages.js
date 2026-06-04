const TEXT_ONLY_SLIDE_TYPES = new Set(['sectiondivider', 'quote']);

const fallbackTranslate = (_key, fallback, params) => {
  if (!params) {
    return fallback;
  }

  return Object.entries(params).reduce(
    (message, [key, value]) => message.replace(`{{${key}}}`, value),
    fallback
  );
};

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
    case 'image-plan-invalid':
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
    ?? item?.slideImage?.needsImage
    ?? item?.SlideImage?.NeedsImage;

  if (typeof explicit === 'boolean') {
    return explicit;
  }

  return !TEXT_ONLY_SLIDE_TYPES.has(slideType);
};

export const normalizeImageCandidates = (rawCandidates) => toArray(rawCandidates)
  .map((candidate, index) => {
    const rawSourceType = normalizeOptionalString(candidate?.sourceType ?? candidate?.SourceType)?.toLowerCase();
    const sourceType = rawSourceType === 'generated'
      ? 'generated'
      : rawSourceType === 'pdf-region'
        ? 'pdf-region'
        : 'web';

    return {
      key: pickFirstString(candidate?.key, candidate?.Key, `candidate-${index + 1}`),
      sourceType,
      provider: pickFirstString(
        candidate?.provider,
        candidate?.Provider,
        candidate?.domain,
        candidate?.Domain,
        sourceType === 'generated' ? 'AI Generated' : sourceType === 'pdf-region' ? 'Source PDF' : 'Web'
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
      altText: pickFirstString(candidate?.altText, candidate?.AltText, 'Illustration for slide'),
      licenseLabel: pickFirstString(candidate?.licenseLabel, candidate?.LicenseLabel),
      attributionText: pickFirstString(candidate?.attributionText, candidate?.AttributionText),
      width: Number.isFinite(candidate?.width) ? candidate.width : (Number.isFinite(candidate?.Width) ? candidate.Width : null),
      height: Number.isFinite(candidate?.height) ? candidate.height : (Number.isFinite(candidate?.Height) ? candidate.Height : null),
      score: Number.isFinite(candidate?.score) ? candidate.score : (Number.isFinite(candidate?.Score) ? candidate.Score : null),
      isSelected: Boolean(candidate?.isSelected ?? candidate?.IsSelected),
      layoutMode: pickFirstString(candidate?.layoutMode, candidate?.LayoutMode),
      pageNumber: Number.isFinite(candidate?.pageNumber) ? candidate.pageNumber : (Number.isFinite(candidate?.PageNumber) ? candidate.PageNumber : null),
      regionType: pickFirstString(candidate?.regionType, candidate?.RegionType),
      regionText: pickFirstString(candidate?.regionText, candidate?.RegionText),
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

const getImageStatusLabel = (status, t = fallbackTranslate) => {
  switch (status) {
    case 'ready':
      return t('slides.imageStates.ready', 'Media is ready');
    case 'queued':
      return t('slides.imageStates.queued', 'Waiting for media workflow');
    case 'running':
      return t('slides.imageStates.running', 'Media workflow is running');
    case 'sourcing-web':
      return t('slides.imageStates.sourcingWeb', 'Searching safe web images');
    case 'generating-fallback':
      return t('slides.imageStates.generatingFallback', 'Generating fallback image');
    case 'failed':
      return t('slides.imageStates.failed', 'Media workflow failed');
    case 'no-image-needed':
      return t('slides.imageStates.noImageNeeded', 'This slide works best as text-only');
    case 'image-plan-invalid':
      return t('slides.imageStates.imagePlanInvalid', 'Image plan skipped');
    case 'no-license-safe-image':
      return t('slides.imageStates.noLicenseSafeImage', 'No license-safe image found yet');
    case 'not-requested':
    default:
      return t('slides.imageStates.notRequested', 'No media requested yet');
  }
};

const getImageBadgeLabel = (status, selectedImage, needsImage, t = fallbackTranslate) => {
  if (!needsImage) {
    return t('slides.imageBadges.textOnly', 'Text only');
  }

  if (selectedImage?.sourceType === 'generated') {
    return t('slides.imageBadges.generated', 'AI image');
  }

  if (selectedImage?.sourceType === 'web') {
    return t('slides.imageBadges.web', 'Web image');
  }

  if (selectedImage?.sourceType === 'pdf-region') {
    return t('slides.imageBadges.pdfRegion', 'PDF image');
  }

  if (status === 'no-license-safe-image') {
    return t('slides.imageBadges.noSafeImage', 'No license-safe image yet');
  }

  if (status === 'ready') {
    return t('slides.imageBadges.ready', 'Media ready');
  }

  return t('slides.imageBadges.pending', 'Waiting for image');
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

const buildAttributionText = (selectedImage, t = fallbackTranslate) => {
  if (!selectedImage) {
    return null;
  }

  if (selectedImage.sourceType === 'generated') {
    return t('slides.imageAttribution.generated', 'Source: {{provider}}', { provider: selectedImage.provider });
  }

  if (selectedImage.sourceType === 'pdf-region') {
    return selectedImage.pageNumber
      ? t('slides.imageAttribution.pdfRegionPage', 'Source PDF, page {{page}}', { page: selectedImage.pageNumber })
      : t('slides.imageAttribution.pdfRegion', 'Source PDF');
  }

  const segments = [
    selectedImage.provider,
    selectedImage.licenseLabel,
    selectedImage.attributionText,
  ].filter(Boolean);

  return segments.length > 0 ? segments.join(' • ') : t('slides.imageAttribution.web', 'Web source');
};

const buildDefaultMessage = ({ needsImage, status, selectedImage, candidateCount, t = fallbackTranslate }) => {
  if (!needsImage) {
    return t('slides.imageMessages.textOnly', 'This slide is intentionally text-only to keep the reading rhythm clean.');
  }

  if (selectedImage) {
    return t('slides.imageMessages.hasSelected', 'Media preview is already ready for this slide.');
  }

  if (candidateCount > 0) {
    return t('slides.imageMessages.hasCandidates', '{{count}} image candidates are ready for review.', { count: candidateCount });
  }

  switch (status) {
    case 'queued':
    case 'running':
      return t('slides.imageMessages.queued', 'The media workflow will start after the slide content stabilizes.');
    case 'sourcing-web':
      return t('slides.imageMessages.sourcingWeb', 'The system is searching for clearly sourced web images.');
    case 'generating-fallback':
      return t('slides.imageMessages.generatingFallback', 'Generating a safe fallback image from the current prompt.');
    case 'no-license-safe-image':
      return t('slides.imageMessages.noSafeImage', 'No web image currently satisfies the source and licensing policy.');
    case 'failed':
      return t('slides.imageMessages.failed', 'The media workflow hit an error. Try running the image search again.');
    case 'not-requested':
    default:
      return t('slides.imageMessages.notRequested', 'No media has been requested yet. This slide is ready for image sourcing when needed.');
  }
};

export const buildSlideImageViewModel = (item, t = fallbackTranslate) => {
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
      t,
    });

  return {
    slideType,
    needsImage,
    status,
    statusLabel: getImageStatusLabel(status, t),
    badgeLabel: getImageBadgeLabel(status, selectedImage, needsImage, t),
    badgeTone: getBadgeTone(status, selectedImage, needsImage),
    selectedImage,
    candidates,
    hasCandidates: candidates.length > 0,
    candidateCount: candidates.length,
    helperText: message,
    attributionText: buildAttributionText(selectedImage, t),
  };
};
