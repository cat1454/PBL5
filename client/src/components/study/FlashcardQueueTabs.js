import React from 'react';

export default function FlashcardQueueTabs({ queueKeys, queueViewModel, activeQueue, onSelectQueue, ariaLabel, labels }) {
  return (
    <div className="flashcard-queue-tabs" role="tablist" aria-label={ariaLabel}>
      {queueKeys.map((queueKey) => (
        <button
          key={queueKey}
          type="button"
          className={activeQueue === queueKey ? 'active' : ''}
          onClick={() => onSelectQueue(queueKey)}
        >
          <span>{labels[queueKey]}</span>
          <strong>{queueViewModel[queueKey].cards.length}</strong>
        </button>
      ))}
    </div>
  );
}
