import React, { useEffect, useMemo, useState } from 'react';
import { documentService } from '../services/api';
import {
  formatUnderstandingConfidence,
  normalizeDocumentUnderstanding,
} from '../services/documentUnderstanding';
import { useLanguage } from '../context/LanguageContext';

function shortText(value, maxLength = 180) {
  const normalized = typeof value === 'string' ? value.replace(/\s+/g, ' ').trim() : '';
  if (!normalized || normalized.length <= maxLength) {
    return normalized;
  }

  return `${normalized.slice(0, Math.max(0, maxLength - 3)).trimEnd()}...`;
}

function DetailList({ items, emptyLabel, renderItem }) {
  if (!items.length) {
    return <p className="document-understanding-empty">{emptyLabel}</p>;
  }

  return (
    <div className="document-understanding-list">
      {items.map(renderItem)}
    </div>
  );
}

function DocumentUnderstandingPanel({
  documentId,
  initialData = null,
  className = '',
  showEmpty = false,
  defaultOpen = false,
  compact = false,
}) {
  const { t } = useLanguage();
  const [rawData, setRawData] = useState(initialData);
  const [loading, setLoading] = useState(Boolean(documentId) && !initialData);
  const [error, setError] = useState('');

  useEffect(() => {
    let cancelled = false;

    const loadUnderstanding = async () => {
      if (!documentId) {
        setRawData(null);
        setLoading(false);
        return;
      }

      setLoading(true);
      setError('');

      try {
        const data = await documentService.getLatestUnderstanding(documentId);
        if (!cancelled) {
          setRawData(data);
        }
      } catch (err) {
        if (!cancelled) {
          setError(t('documentUnderstanding.loadError'));
          setRawData(null);
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    };

    loadUnderstanding();

    return () => {
      cancelled = true;
    };
  }, [documentId, t]);

  const understanding = useMemo(() => normalizeDocumentUnderstanding(rawData), [rawData]);

  if (!documentId) {
    return null;
  }

  if (loading) {
    return (
      <div className={`document-understanding-panel is-loading${compact ? ' compact' : ''} ${className}`.trim()}>
        <span className="document-understanding-kicker">{t('documentUnderstanding.title')}</span>
        <p>{t('documentUnderstanding.loading')}</p>
      </div>
    );
  }

  if (!understanding) {
    if (!showEmpty && !error) {
      return null;
    }

    return (
      <div className={`document-understanding-panel is-empty${compact ? ' compact' : ''} ${className}`.trim()}>
        <div className="document-understanding-head">
          <div>
            <span className="document-understanding-kicker">{t('documentUnderstanding.title')}</span>
            <strong>{error || t('documentUnderstanding.noDataTitle')}</strong>
          </div>
        </div>
        <p>{error || t('documentUnderstanding.noDataBody')}</p>
      </div>
    );
  }

  const badgeLabel = t(`documentUnderstanding.badges.${understanding.status}`);
  const confidenceLabel = formatUnderstandingConfidence(understanding.confidence, t('documentUnderstanding.unknown'));
  const pageCount = understanding.pages.length;
  const reviewCount = understanding.reviewRegions.length;
  const figureCount = understanding.figureDescriptions.length;
  const presentation = understanding.presentation;
  const presentationConfidenceLabel = presentation
    ? formatUnderstandingConfidence(presentation.extractionConfidence, t('documentUnderstanding.unknown'))
    : null;
  const reviewHintCount = presentation?.reviewHintCount || presentation?.uxReviewHints?.length || 0;

  return (
    <details
      className={`document-understanding-panel tone-${understanding.tone}${compact ? ' compact' : ''} ${className}`.trim()}
      open={defaultOpen}
    >
      <summary className="document-understanding-summary">
        <div>
          <span className="document-understanding-kicker">{t('documentUnderstanding.title')}</span>
          <strong>{t('documentUnderstanding.summaryTitle')}</strong>
        </div>
        <span className={`generation-readiness-badge tone-${understanding.tone}`}>{badgeLabel}</span>
      </summary>

      <div className="document-understanding-body">
        <div className="document-understanding-stats">
          <div>
            <span>{t('documentUnderstanding.confidence')}</span>
            <strong>{confidenceLabel}</strong>
          </div>
          <div>
            <span>{t('documentUnderstanding.pages')}</span>
            <strong>{pageCount || t('documentUnderstanding.unknown')}</strong>
          </div>
          <div>
            <span>{t('documentUnderstanding.regions')}</span>
            <strong>{understanding.regions.length}</strong>
          </div>
          <div>
            <span>{t('documentUnderstanding.reviewRegions')}</span>
            <strong>{reviewCount}</strong>
          </div>
          {presentation && (
            <>
              <div>
                <span>{t('documentUnderstanding.presentationSections')}</span>
                <strong>{presentation.sectionCount}</strong>
              </div>
              <div>
                <span>{t('documentUnderstanding.visualCandidates')}</span>
                <strong>{presentation.visualCount}</strong>
              </div>
              <div>
                <span>{t('documentUnderstanding.chartReviews')}</span>
                <strong>{presentation.chartReviewCount}</strong>
              </div>
              <div>
                <span>{t('documentUnderstanding.reviewHints')}</span>
                <strong>{reviewHintCount}</strong>
              </div>
              <div>
                <span>{t('documentUnderstanding.denseSections')}</span>
                <strong>{presentation.denseSectionCount || 0}</strong>
              </div>
              <div>
                <span>{t('documentUnderstanding.extractionConfidence')}</span>
                <strong>{presentationConfidenceLabel}</strong>
              </div>
            </>
          )}
        </div>

        {presentation?.warnings?.length > 0 && (
          <section className="document-understanding-section">
            <h4>{t('documentUnderstanding.presentationWarnings')}</h4>
            <DetailList
              items={presentation.warnings.slice(0, 4)}
              emptyLabel={t('documentUnderstanding.noPresentationWarnings')}
              renderItem={(warning, index) => (
                <p key={`presentation-warning-${index}`} className="document-understanding-reason">{warning}</p>
              )}
            />
          </section>
        )}

        {presentation && (
          <section className="document-understanding-section">
            <h4>{t('documentUnderstanding.presentationOverview')}</h4>
            <div className="document-understanding-list">
              <article className="document-understanding-region">
                <div className="document-understanding-region-head">
                  <strong>{t('documentUnderstanding.audienceProfile')}</strong>
                  <span>{presentation.audienceProfile?.readingDifficulty || t('documentUnderstanding.unknown')}</span>
                </div>
                <p>{presentation.presentationFlow?.suggestedOpening || presentation.sourceSummary}</p>
                {presentation.audienceProfile?.jargonTerms?.length > 0 && (
                  <small>{t('documentUnderstanding.jargonTerms')}: {presentation.audienceProfile.jargonTerms.slice(0, 8).join(', ')}</small>
                )}
              </article>
            </div>
          </section>
        )}

        {presentation && (
          <section className="document-understanding-section">
            <h4>{t('documentUnderstanding.uxReviewHints')}</h4>
            <DetailList
              items={presentation.uxReviewHints.slice(0, 6)}
              emptyLabel={t('documentUnderstanding.noUxReviewHints')}
              renderItem={(hint, index) => (
                <article key={`ux-hint-${hint.hintType}-${index}`} className={`document-understanding-region hint-${hint.severity}`}>
                  <div className="document-understanding-region-head">
                    <strong>{hint.hintType}</strong>
                    <span>{hint.severity}</span>
                  </div>
                  <p>{hint.message}</p>
                  {hint.suggestedAction && <small>{hint.suggestedAction}</small>}
                  {(hint.pageNumber || hint.sectionId) && (
                    <small>{[hint.pageNumber ? t('documentUnderstanding.pageLabel', { page: hint.pageNumber }) : '', hint.sectionId].filter(Boolean).join(' | ')}</small>
                  )}
                </article>
              )}
            />
          </section>
        )}

        {presentation && (
          <section className="document-understanding-section">
            <h4>{t('documentUnderstanding.sourceGrounding')}</h4>
            <DetailList
              items={presentation.sourceGrounding.slice(0, 6)}
              emptyLabel={t('documentUnderstanding.noSourceGrounding')}
              renderItem={(grounding, index) => (
                <article key={`grounding-${grounding.sectionId}-${index}`} className="document-understanding-region">
                  <div className="document-understanding-region-head">
                    <strong>{grounding.sectionId || t('documentUnderstanding.unknown')}</strong>
                    <span>{formatUnderstandingConfidence(grounding.confidence, t('documentUnderstanding.unknown'))}</span>
                  </div>
                  {grounding.pageNumbers.length > 0 && <p>{t('documentUnderstanding.pages')}: {grounding.pageNumbers.join(', ')}</p>}
                  {grounding.chunkIds.length > 0 && <small>{t('documentUnderstanding.chunks')}: {grounding.chunkIds.slice(0, 5).join(', ')}</small>}
                  {grounding.evidenceExcerpt && <small>{shortText(grounding.evidenceExcerpt, 220)}</small>}
                  {grounding.missingEvidenceWarnings.length > 0 && <small>{grounding.missingEvidenceWarnings.join(' | ')}</small>}
                </article>
              )}
            />
          </section>
        )}

        <section className="document-understanding-section">
          <h4>{t('documentUnderstanding.pageQuality')}</h4>
          <DetailList
            items={understanding.pages.slice(0, 6)}
            emptyLabel={t('documentUnderstanding.noPages')}
            renderItem={(page) => (
              <article key={`page-${page.pageNumber}`} className="document-understanding-row">
                <div>
                  <strong>{t('documentUnderstanding.pageLabel', { page: page.pageNumber })}</strong>
                  <span>{page.regionCount} {t('documentUnderstanding.regionUnit')}</span>
                </div>
                <span>{formatUnderstandingConfidence(page.confidence, t('documentUnderstanding.unknown'))}</span>
              </article>
            )}
          />
        </section>

        <section className="document-understanding-section">
          <h4>{t('documentUnderstanding.regionsToReview')}</h4>
          <DetailList
            items={understanding.reviewRegions.slice(0, 6)}
            emptyLabel={t('documentUnderstanding.noReviewRegions')}
            renderItem={(region, index) => (
              <article key={`review-${region.pageNumber}-${region.regionType}-${index}`} className="document-understanding-region">
                <div className="document-understanding-region-head">
                  <strong>{t('documentUnderstanding.regionLabel', { page: region.pageNumber, type: region.regionType })}</strong>
                  <span>{formatUnderstandingConfidence(region.confidence, t('documentUnderstanding.unknown'))}</span>
                </div>
                {region.reviewTags.length > 0 && (
                  <p>{region.reviewTags.join(', ')}</p>
                )}
                {region.uncertaintyReason && <p>{region.uncertaintyReason}</p>}
                {region.text && <small>{shortText(region.text)}</small>}
              </article>
            )}
          />
        </section>

        <section className="document-understanding-section">
          <h4>{t('documentUnderstanding.figures')}</h4>
          <DetailList
            items={understanding.figureDescriptions.slice(0, 4)}
            emptyLabel={t('documentUnderstanding.noFigures')}
            renderItem={(region, index) => (
              <article key={`figure-${region.pageNumber}-${region.regionType}-${index}`} className="document-understanding-region">
                <div className="document-understanding-region-head">
                  <strong>{t('documentUnderstanding.regionLabel', { page: region.pageNumber, type: region.regionType })}</strong>
                  <span>{formatUnderstandingConfidence(region.visionConfidence, t('documentUnderstanding.unknown'))}</span>
                </div>
                {region.description && <p>{region.description}</p>}
                {region.extractedLabels.length > 0 && <small>{t('documentUnderstanding.labels')}: {region.extractedLabels.join(', ')}</small>}
                {region.relationships.length > 0 && <small>{t('documentUnderstanding.relationships')}: {region.relationships.join(', ')}</small>}
              </article>
            )}
          />
        </section>

        <section className="document-understanding-section">
          <h4>{t('documentUnderstanding.failureReasons')}</h4>
          <DetailList
            items={understanding.failureReasons.slice(0, 8)}
            emptyLabel={t('documentUnderstanding.noFailureReasons')}
            renderItem={(reason, index) => (
              <p key={`reason-${index}`} className="document-understanding-reason">{reason}</p>
            )}
          />
        </section>

        {figureCount + reviewCount > 10 && (
          <p className="document-understanding-empty">{t('documentUnderstanding.truncated')}</p>
        )}
      </div>
    </details>
  );
}

export default DocumentUnderstandingPanel;
