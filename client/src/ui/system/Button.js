import React from 'react';
import { cx } from './utils';

function Button({
  children,
  className,
  disabled = false,
  icon,
  loading = false,
  size = 'md',
  tone = 'primary',
  variant = 'solid',
  ...props
}) {
  return (
    <button
      type="button"
      className={cx('sys-button', `sys-button-${variant}`, `sys-button-${tone}`, `sys-button-${size}`, className)}
      disabled={disabled || loading}
      {...props}
    >
      {loading && <span className="sys-spinner" aria-hidden="true" />}
      {!loading && icon && <span className="sys-button-icon">{icon}</span>}
      <span>{children}</span>
    </button>
  );
}

export default Button;
