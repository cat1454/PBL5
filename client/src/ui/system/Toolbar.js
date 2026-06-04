import React from 'react';
import { cx } from './utils';

function Toolbar({ actions, children, className, title }) {
  return (
    <div className={cx('sys-toolbar', className)}>
      <div className="sys-toolbar-main">
        {title && <strong>{title}</strong>}
        {children}
      </div>
      {actions && <div className="sys-toolbar-actions">{actions}</div>}
    </div>
  );
}

export default Toolbar;
