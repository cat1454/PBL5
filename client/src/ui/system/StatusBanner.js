import React from 'react';
import { cx } from './utils';

function StatusBanner({ actions, children, className, icon, title, tone = 'info' }) {
  return (
    <section className={cx('sys-status-banner', `sys-status-banner-${tone}`, className)} role={tone === 'danger' ? 'alert' : 'status'}>
      {icon && <span className="sys-status-banner-icon">{icon}</span>}
      <div className="sys-status-banner-body">
        {title && <strong>{title}</strong>}
        {children && <p>{children}</p>}
      </div>
      {actions && <div className="sys-status-banner-actions">{actions}</div>}
    </section>
  );
}

export default StatusBanner;
