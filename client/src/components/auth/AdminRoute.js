import React from 'react';
import { Navigate } from 'react-router-dom';
import { normalizeRole, useAuth } from '../../context/AuthContext';

function AdminRoute({ children }) {
  const { currentUser } = useAuth();

  if (normalizeRole(currentUser?.role) !== 'ADMIN') {
    return <Navigate to="/" replace />;
  }

  return children;
}

export default AdminRoute;
