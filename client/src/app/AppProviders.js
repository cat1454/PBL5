import React from 'react';
import { BrowserRouter as Router } from 'react-router-dom';
import { AuthProvider } from '../context/AuthContext';
import { LanguageProvider } from '../context/LanguageContext';
import { ToastProvider } from '../components/common/ToastProvider';

function AppProviders({ children }) {
  return (
    <LanguageProvider>
      <Router>
        <ToastProvider>
          <AuthProvider>
            {children}
          </AuthProvider>
        </ToastProvider>
      </Router>
    </LanguageProvider>
  );
}

export default AppProviders;
