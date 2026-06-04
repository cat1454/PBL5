import React from 'react';
import { cx } from './utils';

function Panel({ as: Component = 'section', children, className, tone = 'default', ...props }) {
  return (
    <Component className={cx('sys-panel', `sys-panel-${tone}`, className)} {...props}>
      {children}
    </Component>
  );
}

export default Panel;
