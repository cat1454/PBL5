import React from 'react';
import { cx } from './utils';

function IconButton({
  'aria-label': ariaLabel,
  className,
  disabled = false,
  icon,
  loading = false,
  size = 'md',
  tone = 'neutral',
  variant = 'ghost',
  ...props
}) {
  return (
    <button
      type="button"
      className={cx('sys-icon-button', `sys-icon-button-${variant}`, `sys-icon-button-${tone}`, `sys-icon-button-${size}`, className)}
      disabled={disabled || loading}
      aria-label={ariaLabel}
      {...props}
    >
      {loading ? <span className="sys-spinner" aria-hidden="true" /> : icon}
    </button>
  );
}

export default IconButton;
