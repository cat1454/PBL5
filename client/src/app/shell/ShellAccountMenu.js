import React from 'react';
import { LuChevronDown, LuCircleHelp, LuLogOut } from 'react-icons/lu';

function ShellAccountMenu({
  isOpen,
  menuRef,
  onHelp,
  onLogout,
  onToggle,
  t,
  user,
}) {
  return (
    <div className={`app-shell-account v2-account app-account-control${isOpen ? ' is-open' : ''}`} ref={menuRef}>
      <button
        type="button"
        className="app-shell-account-trigger v2-account-trigger"
        onClick={onToggle}
        aria-expanded={isOpen}
        aria-haspopup="menu"
      >
        <UserAvatar user={user} t={t} />
        <div className="app-shell-user-meta v2-account-meta">
          <span className="user-name">{user.name}</span>
          <span>{user.roleLabel}</span>
        </div>
        <LuChevronDown className="app-shell-account-chevron" aria-hidden="true" />
      </button>

      {isOpen && (
        <div className="app-account-menu v2-account-menu" role="menu" aria-label={t('app.account.menuLabel')}>
          <div className="app-account-summary">
            <UserAvatar user={user} t={t} />
            <div>
              <strong>{user.name}</strong>
              <p>{user.roleLabel}</p>
            </div>
          </div>

          <button type="button" className="app-account-item" onClick={onHelp} role="menuitem">
            <span><LuCircleHelp aria-hidden="true" /> {t('app.account.helpGuide')}</span>
            <small>{t('app.account.helpHint')}</small>
          </button>
          <button type="button" className="app-account-item app-account-item-danger" onClick={onLogout} role="menuitem">
            <span><LuLogOut aria-hidden="true" /> {t('app.account.logout')}</span>
            <small>{t('app.account.logoutHint')}</small>
          </button>
        </div>
      )}
    </div>
  );
}

function UserAvatar({ user, t }) {
  return (
    <div className="app-shell-user-avatar v2-avatar">
      {user.avatar ? <img src={user.avatar} alt={t('app.account.avatarAlt', { name: user.name })} /> : user.name.charAt(0)}
    </div>
  );
}

export default ShellAccountMenu;
