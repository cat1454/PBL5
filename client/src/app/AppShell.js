import React from 'react';
import { Route, Routes, useLocation } from 'react-router-dom';
import { getProtectedRoutes, getRouteMeta } from './routes';
import { useShellState } from './shell/useShellState';
import { useAuth } from '../context/AuthContext';
import { useLanguage } from '../context/LanguageContext';
import AppFrame from '../layouts/AppFrame';
import StudioFrame from '../layouts/StudioFrame';

function AppShell({ user, onLogout }) {
  const location = useLocation();
  const { currentUser } = useAuth();
  const { language, setLanguage, t } = useLanguage();
  const routeMeta = getRouteMeta(location.pathname);
  const shellState = useShellState({ location, onLogout });
  const displayUser = user || {
    name: t('app.account.unknownUser'),
    roleLabel: t('app.roles.LEARNER'),
    avatar: null,
  };

  return (
    <AppFrame
      accountMenuRef={shellState.accountMenuRef}
      currentUser={currentUser}
      displayUser={displayUser}
      isAccountMenuOpen={shellState.isAccountMenuOpen}
      isMainMenuOpen={shellState.isMainMenuOpen}
      language={language}
      location={location}
      onCloseMainMenu={shellState.closeMainMenu}
      onHelp={shellState.handleHelpClick}
      onLogout={shellState.handleLogout}
      onOpenMainMenu={shellState.openMainMenu}
      onSetLanguage={setLanguage}
      onToggleAccountMenu={shellState.toggleAccountMenu}
      routeMeta={routeMeta}
      t={t}
    >
      <Routes>
        {getProtectedRoutes().map((route) => (
          <Route
            key={route.id}
            path={route.path}
            element={route.frame === 'studio' ? <StudioFrame>{route.element}</StudioFrame> : route.element}
          />
        ))}
      </Routes>
    </AppFrame>
  );
}

export default AppShell;
