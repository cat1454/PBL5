import React from 'react';

export default function StreakSummary({ currentStreak, bestStreak, currentQuestionIndex, total, progress, streakBump, t, copy }) {
  return (
    <div className="streak-summary-wrap">
      <div className="streak-stats-row">
        <div className={`streak-stat-card streak-stat-primary${streakBump ? ' is-bumping' : ''}`}>
          <span>{t('streak.currentStreak')}</span>
          <strong>{currentStreak}</strong>
        </div>
        <div className="streak-stat-card">
          <span>{t('streak.bestStreak')}</span>
          <strong>{bestStreak}</strong>
        </div>
        <div className="streak-stat-card">
          <span>{t('streak.questionCounter')}</span>
          <strong>{currentQuestionIndex + 1}/{total}</strong>
        </div>
      </div>
      <div className="streak-progress" aria-label={copy.progressAria(Math.round(progress))}>
        <div className="streak-progress-fill" style={{ width: `${progress}%` }} />
      </div>
    </div>
  );
}
