import React from 'react';
import { cx } from './utils';

function Metric({ className, icon, label, tone = 'neutral', value }) {
  return (
    <div className={cx('sys-metric', `sys-metric-${tone}`, className)}>
      {icon && <span className="sys-metric-icon">{icon}</span>}
      <div>
        <strong>{value}</strong>
        <span>{label}</span>
      </div>
    </div>
  );
}

export default Metric;
