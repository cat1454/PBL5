import React from 'react';
import { NavLink } from 'react-router-dom';
import { HELP_NAV_ITEM, NAV_ITEMS } from '../routes';
import { ShellIcon } from './icons';
import { canAuthorLearningMaterial } from '../../context/AuthContext';

function ShellNavigation({ currentUser, onHelp, t, variant = 'topbar' }) {
  const items = NAV_ITEMS.filter((item) => {
    if (item.role === 'ADMIN' && currentUser?.role !== 'ADMIN') return false;
    if (item.id === 'workspaces' && !canAuthorLearningMaterial(currentUser)) return false;
    return true;
  });
  const isDrawer = variant === 'drawer';
  const navClassName = isDrawer ? 'app-menu-nav app-shell-drawer-nav' : 'app-topbar-nav v2-nav app-shell-topnav';
  const ariaLabel = t('app.menu.navigation');

  return (
    <nav className={navClassName} aria-label={ariaLabel}>
      {items.map((item) => (
        <ShellNavLink key={item.id} item={item} label={t(item.labelKey)} variant={variant} />
      ))}
      <button
        type="button"
        className={isDrawer ? 'app-menu-placeholder app-shell-nav-button' : 'app-topbar-link app-topbar-link-placeholder v2-nav-link app-shell-nav-button'}
        onClick={onHelp}
      >
        <ShellIcon name={HELP_NAV_ITEM.icon} />
        {t(HELP_NAV_ITEM.labelKey)}
      </button>
    </nav>
  );
}

function ShellNavLink({ item, label, variant }) {
  if (variant === 'drawer') {
    return (
      <NavLink to={item.to} end={item.end} className={({ isActive }) => (isActive ? 'active' : '')}>
        <ShellIcon name={item.icon} />
        {label}
      </NavLink>
    );
  }

  return (
    <NavLink to={item.to} end={item.end} className={({ isActive }) => `app-topbar-link v2-nav-link${isActive ? ' active' : ''}`}>
      <ShellIcon name={item.icon} />
      {label}
    </NavLink>
  );
}

export default ShellNavigation;
