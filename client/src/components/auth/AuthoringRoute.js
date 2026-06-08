import React from 'react';
import { Navigate } from 'react-router-dom';
import { canAuthorLearningMaterial, useAuth } from '../../context/AuthContext';

function AuthoringRoute({ children }) {
  const { currentUser } = useAuth();

  if (!canAuthorLearningMaterial(currentUser)) {
    return <Navigate to="/classrooms/joined" replace />;
  }

  return children;
}

export default AuthoringRoute;
