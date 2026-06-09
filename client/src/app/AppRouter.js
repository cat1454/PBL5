import React, { useMemo } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import AppShell from './AppShell';
import AuthPage from '../components/auth/AuthPage';
import ProtectedRoute from '../components/auth/ProtectedRoute';
import { useAuth } from '../context/AuthContext';
import { useLanguage } from '../context/LanguageContext';
import AuthLayout from '../layouts/AuthLayout';

function AppRouter() {
  const { currentUser, isAuthenticated, logout } = useAuth();
  const { t } = useLanguage();

  const localizedUser = useMemo(() => {
    if (!currentUser) {
      return null;
    }

    return {
      ...currentUser,
      name: currentUser.fullName,
      roleLabel: t(`app.roles.${currentUser.role}`),
      avatar: null,
    };
  }, [currentUser, t]);

  return (
    <Routes>
      <Route path="/login" element={isAuthenticated ? <Navigate to="/" replace /> : <AuthLayout><AuthPage initialTab="login" /></AuthLayout>} />
      <Route path="/register" element={isAuthenticated ? <Navigate to="/" replace /> : <AuthLayout><AuthPage initialTab="register" /></AuthLayout>} />
      <Route
        path="/*"
        element={(
          <ProtectedRoute>
            <AppShell user={localizedUser} onLogout={logout} />
          </ProtectedRoute>
        )}
      />
    </Routes>
  );
}

export default AppRouter;
