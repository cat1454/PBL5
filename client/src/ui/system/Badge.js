import React from 'react';
import { cx } from './utils';

function Badge({ children, className, tone = 'neutral', variant = 'soft', ...props }) {
  return (
    <span className={cx('sys-badge', `sys-badge-${variant}`, `sys-badge-${tone}`, className)} {...props}>
      {children}
    </span>
  );
}

export default Badge;
