import React from 'react';
import { LuMenu } from 'react-icons/lu';
import { getShellClassNames } from '../app/routes';
import LanguageSwitch from '../app/shell/LanguageSwitch';
import ShellAccountMenu from '../app/shell/ShellAccountMenu';
import ShellBrand from '../app/shell/ShellBrand';
import ShellMenuDrawer from '../app/shell/ShellMenuDrawer';
import ShellNavigation from '../app/shell/ShellNavigation';
import ContentFrame from './ContentFrame';

function AppFrame({
  accountMenuRef,
  children,
  currentUser,
  displayUser,
  isAccountMenuOpen,
  isMainMenuOpen,
  language,
  location,
  onCloseMainMenu,
  onHelp,
  onLogout,
  onOpenMainMenu,
  onSetLanguage,
  onToggleAccountMenu,
  routeMeta,
  t,
}) {
  return (
    <div className={getShellClassNames(routeMeta, isMainMenuOpen)}>
      <header className="App-header app-shell-header app-topbar v2-topbar app-frame-header">
        <div className="container app-shell-header-inner v2-topbar-inner app-frame-header-inner">
          <div className="app-shell-header-start v2-topbar-start">
            <button
              type="button"
              className="app-menu-toggle v2-icon-button"
              onClick={onOpenMainMenu}
              aria-label={t('app.menu.open')}
            >
              <LuMenu aria-hidden="true" />
            </button>

            <ShellBrand title={t('app.brand')} productTag={t('app.topbar.productTag')} />
            <ShellNavigation currentUser={currentUser} onHelp={onHelp} t={t} />
          </div>

          <div className="app-shell-user v2-topbar-user">
            <LanguageSwitch language={language} setLanguage={onSetLanguage} label={t('app.languageToggle.label')} />
            <ShellAccountMenu
              isOpen={isAccountMenuOpen}
              menuRef={accountMenuRef}
              onHelp={onHelp}
              onLogout={onLogout}
              onToggle={onToggleAccountMenu}
              t={t}
              user={displayUser}
            />
          </div>
        </div>
      </header>

      <ShellMenuDrawer
        currentUser={currentUser}
        isOpen={isMainMenuOpen}
        onClose={onCloseMainMenu}
        onHelp={onHelp}
        t={t}
      />

      <ContentFrame pathname={location.pathname} routeMeta={routeMeta} t={t}>
        {children}
      </ContentFrame>
    </div>
  );
}

export default AppFrame;
