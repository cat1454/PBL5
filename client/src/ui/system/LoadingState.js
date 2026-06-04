import React from 'react';
import { cx } from './utils';

function LoadingState({ className, label }) {
  return (
    <div className={cx('sys-loading-state', className)} role="status" aria-live="polite">
      <span className="sys-spinner" aria-hidden="true" />
      {label && <span>{label}</span>}
    </div>
  );
}

export default LoadingState;
