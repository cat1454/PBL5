import React from 'react';
import { cx } from './utils';

function SegmentedControl({ ariaLabel, className, onChange, options, value }) {
  return (
    <div className={cx('sys-segmented-control', className)} role="group" aria-label={ariaLabel}>
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          className={option.value === value ? 'is-active' : ''}
          onClick={() => onChange(option.value)}
          aria-pressed={option.value === value}
        >
          {option.icon && <span>{option.icon}</span>}
          {option.label}
        </button>
      ))}
    </div>
  );
}

export default SegmentedControl;
