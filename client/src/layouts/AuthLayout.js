import React from 'react';

function AuthLayout({ children }) {
  return (
    <div className="auth-layout app-auth-layout">
      {children}
    </div>
  );
}

export default AuthLayout;
