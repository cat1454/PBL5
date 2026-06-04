import React from 'react';

function Button({
  children,
  className = '',
  disabled = false,
  icon = null,
  onClick,
  type = 'button',
  variant = 'primary',
}) {
  return (
    <button
      type={type}
      className={`v2-button v2-button-${variant}${className ? ` ${className}` : ''}`}
      disabled={disabled}
      onClick={onClick}
    >
      {icon && <span className="v2-button-icon">{icon}</span>}
      <span>{children}</span>
    </button>
  );
}

export default Button;
