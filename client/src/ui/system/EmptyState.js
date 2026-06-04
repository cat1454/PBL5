import React from 'react';
import { cx } from './utils';

function EmptyState({ action, body, className, icon, title }) {
  return (
    <section className={cx('sys-empty-state', className)}>
      {icon && <span className="sys-empty-state-icon">{icon}</span>}
      <div>
        <strong>{title}</strong>
        {body && <p>{body}</p>}
      </div>
      {action && <div className="sys-empty-state-action">{action}</div>}
    </section>
  );
}

export default EmptyState;
