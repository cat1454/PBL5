import React from 'react';
import IconButton from './IconButton';
import { cx } from './utils';

function ModalSheet({ children, className, closeLabel, footer, onClose, title }) {
  return (
    <div className="sys-modal-backdrop" role="presentation">
      <section className={cx('sys-modal-sheet', className)} role="dialog" aria-modal="true" aria-label={title}>
        <header className="sys-modal-header">
          <strong>{title}</strong>
          {onClose && <IconButton aria-label={closeLabel} onClick={onClose} icon={<span aria-hidden="true">x</span>} />}
        </header>
        <div className="sys-modal-body">{children}</div>
        {footer && <footer className="sys-modal-footer">{footer}</footer>}
      </section>
    </div>
  );
}

export default ModalSheet;
