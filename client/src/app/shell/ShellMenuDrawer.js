import React from 'react';
import { LuX } from 'react-icons/lu';
import ShellBrand from './ShellBrand';
import ShellNavigation from './ShellNavigation';

function ShellMenuDrawer({ currentUser, isOpen, onClose, onHelp, t }) {
  if (!isOpen) {
    return null;
  }

  return (
    <div
      className="app-menu-backdrop v2-menu-backdrop app-drawer-backdrop"
      onClick={onClose}
    >
      <aside
        className="app-menu-drawer v2-menu-drawer app-drawer-panel"
        onClick={(event) => event.stopPropagation()}
      >
        <div className="app-menu-header">
          <ShellBrand title={t('app.brand')} productTag={t('app.topbar.productTag')} />

          <button
            type="button"
            className="app-menu-close v2-icon-button"
            onClick={onClose}
            aria-label={t('app.menu.close')}
          >
            <LuX aria-hidden="true" />
          </button>
        </div>

        <ShellNavigation currentUser={currentUser} onHelp={onHelp} t={t} variant="drawer" />
      </aside>
    </div>
  );
}

export default ShellMenuDrawer;
