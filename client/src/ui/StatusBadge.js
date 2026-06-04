import React from 'react';

function StatusBadge({ label, tone = 'neutral' }) {
  return <span className={`v2-status-badge is-${tone}`}>{label}</span>;
}

export default StatusBadge;
