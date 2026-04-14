import React from 'react';
import {
  formatEta,
  getProgressCounterLabel,
  getProgressStageLabel,
  getSubProgress,
  isActiveProgress,
} from '../services/progress';

function ProgressCard({
  title,
  progress,
  context = 'document',
  showEta = true,
  showCounters = true,
  className = '',
}) {
  if (!progress) {
    return null;
  }

  const normalizedStatus = String(progress.status || '').toLowerCase();
  const stageLabel = getProgressStageLabel(progress);
  const etaLabel = showEta ? formatEta(progress.estimatedRemainingSeconds) : null;
  const counterLabel = showCounters ? getProgressCounterLabel(progress) : null;
  const subProgress = getSubProgress(progress.current, progress.total);
  const classes = [
    'progress-card',
    `progress-card-${context}`,
    `progress-status-${normalizedStatus || 'queued'}`,
    className,
  ].filter(Boolean).join(' ');

  return (
    <section className={classes}>
      <div className="progress-card-header">
        <div>
          <span className="progress-card-kicker">{title}</span>
          <h3 className="progress-card-heading">{stageLabel}</h3>
        </div>
        <div className="progress-card-summary">
          <strong>{Math.max(0, Math.min(100, progress.percent || 0))}%</strong>
          <span>{progress.status || 'queued'}</span>
        </div>
      </div>

      {progress.message && <p className="progress-card-message">{progress.message}</p>}
      {progress.detail && <p className="progress-card-detail">{progress.detail}</p>}
      {normalizedStatus === 'failed' && progress.error && (
        <p className="progress-card-error">{progress.error}</p>
      )}

      <div className="generation-progress-bar">
        <div
          className="generation-progress-fill"
          style={{ width: `${Math.max(0, Math.min(100, progress.percent || 0))}%` }}
        ></div>
      </div>

      {subProgress !== null && (
        <div className="generation-subprogress">
          <div className="generation-subprogress-fill" style={{ width: `${subProgress}%` }}></div>
        </div>
      )}

      <div className="progress-card-meta">
        {typeof progress.stageIndex === 'number' && typeof progress.stageCount === 'number' && (
          <span>Buoc {progress.stageIndex}/{progress.stageCount}</span>
        )}
        {counterLabel && <span>{counterLabel}</span>}
        {progress.topicTag && <span>Topic: {progress.topicTag}</span>}
        {showEta && etaLabel && isActiveProgress(progress) && <span>ETA {etaLabel}</span>}
      </div>
    </section>
  );
}

export default ProgressCard;
