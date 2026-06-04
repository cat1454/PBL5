import React from 'react';

function EmptyState({ action, body, icon, title }) {
  return (
    <div className="v2-empty-state">
      {icon && <div className="v2-empty-icon">{icon}</div>}
      <div>
        <strong>{title}</strong>
        <p>{body}</p>
      </div>
      {action && <div className="v2-empty-action">{action}</div>}
    </div>
  );
}

export default EmptyState;
