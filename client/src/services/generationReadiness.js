import translations from '../i18n';

const STATUS_TONES = {
  Good: 'good',
  NeedsReview: 'review',
  LowConfidence: 'low',
  ExtractionFailed: 'failed',
};

const FALLBACK_LABELS = {
  badges: {
    Good: 'Good',
    NeedsReview: 'Needs Review',
    LowConfidence: 'Low Confidence',
    ExtractionFailed: 'Extraction Failed',
  },
  mediumTitle: 'This document needs review before auto-generation.',
  mediumBody: 'OCR/AI could read the document, but confidence is not high. Review generated content before studying or presenting from it.',
  lowTitle: 'Extraction confidence is low.',
  lowBody: 'The document may be blurry, incomplete, or poorly OCRed. Upload a clearer file or review the content manually before generating quizzes/slides.',
  failedTitle: 'Extraction may have failed.',
  failedBody: 'The system is not confident in the extracted content. Upload a clearer document or manually check the content first.',
  confirm: 'Continue auto-generation from this low-confidence document?',
};

function getLabels(language = 'vi') {
  return translations[language]?.generationReadiness
    || translations.vi?.generationReadiness
    || FALLBACK_LABELS;
}

export function normalizeGenerationReadiness(raw) {
  if (!raw || typeof raw !== 'object') {
    return null;
  }

  const status = String(raw.status || raw.Status || 'Good');
  const action = String(raw.action || raw.Action || 'Allow');
  const confidence = typeof (raw.confidence ?? raw.Confidence) === 'number'
    ? (raw.confidence ?? raw.Confidence)
    : null;
  const reasons = Array.isArray(raw.reasons || raw.Reasons)
    ? (raw.reasons || raw.Reasons).filter(Boolean)
    : [];

  return {
    status,
    action,
    confidence,
    needsReview: Boolean(raw.needsReview ?? raw.NeedsReview),
    requiresConfirmation: Boolean(raw.requiresConfirmation ?? raw.RequiresConfirmation),
    blocked: Boolean(raw.blocked ?? raw.Blocked),
    showWarning: raw.showWarning ?? raw.ShowWarning ?? true,
    reasons,
    tone: STATUS_TONES[status] || 'review',
  };
}

export function getDocumentReadiness(source) {
  return normalizeGenerationReadiness(source?.generationReadiness || source?.processingProgress?.generationReadiness);
}

export function getReadinessLabel(readiness, language = 'vi') {
  const labels = getLabels(language);
  return labels.badges?.[readiness?.status] || labels.badges?.NeedsReview || FALLBACK_LABELS.badges.NeedsReview;
}

export function getReadinessMessage(readiness, language = 'vi', options = {}) {
  const normalized = normalizeGenerationReadiness(readiness);
  const labels = getLabels(language);
  if (!normalized || normalized.status === 'Good') {
    return null;
  }

  if (!options.force && normalized.showWarning === false && !normalized.blocked && !normalized.requiresConfirmation) {
    return null;
  }

  if (normalized.status === 'ExtractionFailed') {
    return { title: labels.failedTitle, body: labels.failedBody };
  }

  if (normalized.status === 'LowConfidence') {
    return { title: labels.lowTitle, body: labels.lowBody };
  }

  return { title: labels.mediumTitle, body: labels.mediumBody };
}

export function confirmGenerationReadiness(readiness, language = 'vi') {
  const normalized = normalizeGenerationReadiness(readiness);
  if (!normalized || normalized.status === 'Good') {
    return { confirmed: false, allowed: true, message: null };
  }

  const message = getReadinessMessage(normalized, language, {
    force: normalized.blocked || normalized.requiresConfirmation,
  });

  if (normalized.blocked && !normalized.requiresConfirmation) {
    return { confirmed: false, allowed: false, message };
  }

  if (!normalized.requiresConfirmation) {
    return { confirmed: false, allowed: true, message };
  }

  const labels = getLabels(language);
  const reasonText = normalized.reasons.length > 0 ? `\n\n${normalized.reasons.slice(0, 3).join('\n')}` : '';
  const confirmationText = message
    ? `${message.title}\n\n${message.body}${reasonText}\n\n${labels.confirm}`
    : `${labels.confirm}${reasonText}`;
  const confirmed = window.confirm(confirmationText);
  return { confirmed, allowed: confirmed, message };
}
