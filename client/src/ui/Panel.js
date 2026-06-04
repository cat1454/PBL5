import React from 'react';

function Panel({ children, className = '', tone = 'default', ...props }) {
  return (
    <section className={`v2-panel v2-panel-${tone}${className ? ` ${className}` : ''}`} {...props}>
      {children}
    </section>
  );
}

export default Panel;
