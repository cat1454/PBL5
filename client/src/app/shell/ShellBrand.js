import React from 'react';
import { NavLink } from 'react-router-dom';
import AnimatedAiLogo from '../../components/common/AnimatedAiLogo';

function ShellBrand({ productTag, title }) {
  return (
    <NavLink to="/" className="app-shell-brand v2-brand app-brand-lockup">
      <div className="app-shell-brand-mark v2-brand-mark" style={{ overflow: 'hidden' }}>
        <AnimatedAiLogo size="small" />
      </div>
      <div className="app-shell-brand-copy v2-brand-copy">
        <strong>{title}</strong>
        <span>{productTag}</span>
      </div>
    </NavLink>
  );
}

export default ShellBrand;
