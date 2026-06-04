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
        </div>

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
