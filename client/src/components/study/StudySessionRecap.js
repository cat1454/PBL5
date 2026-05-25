import React from 'react';

export default function StudySessionRecap({
  title,
  subtitle,
  scorePercent,
  scoreTone,
  scoreLine,
  inlineMeta,
  metrics,
  testMetrics,
  weakQuestions,
  actions,
}) {
  return (
    <div className="study-summary-wrap">
      <div>
        <h2>{title}</h2>
        <p className="section-subtitle">{subtitle}</p>
        <div className="score-display">
          <h1 style={{ fontSize: '4em', color: scoreTone }}>
            {scorePercent}%
          </h1>
          <p style={{ fontSize: '1.1em' }}>{scoreLine}</p>
          {inlineMeta && <p className="study-summary-inline-meta">{inlineMeta}</p>}
          {testMetrics}
        </div>
      </div>
      {metrics && <div className="study-progress-summary-grid">{metrics}</div>}
      {weakQuestions}
      {actions && <div className="study-action-row">{actions}</div>}
    </div>
  );
}
