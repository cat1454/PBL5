import React from 'react';
import { NavLink } from 'react-router-dom';
import { ShellIcon } from './icons';

function ShellBrand({ productTag, title }) {
  return (
    <NavLink to="/" className="app-shell-brand v2-brand app-brand-lockup">
      <div className="app-shell-brand-mark v2-brand-mark"><ShellIcon name="brand" /></div>
      <div className="app-shell-brand-copy v2-brand-copy">
        <strong>{title}</strong>
        <span>{productTag}</span>
      </div>
    </NavLink>
  );
}

export default ShellBrand;
