import React, { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { authService, setApiAuthToken, setApiUnauthorizedHandler } from '../services/api';

const TOKEN_STORAGE_KEY = 'elearn-auth-token';
const DEFAULT_ROLE = 'LEARNER';

const AuthContext = createContext({
  currentUser: null,
  token: '',
  isAuthenticated: false,
  isInitializing: true,
  login: async () => {},
  register: async () => {},
  logout: () => {},
  refreshMe: async () => {},
});

function getStoredToken() {
  if (typeof window === 'undefined') {
    return '';
  }

  return window.localStorage.getItem(TOKEN_STORAGE_KEY) || '';
}

export function normalizeRole(role) {
  if (typeof role !== 'string') {
    return DEFAULT_ROLE;
  }

  const normalized = role.trim().toUpperCase();
  return normalized || DEFAULT_ROLE;
}

function normalizeAuthUser(user) {
  if (!user || typeof user !== 'object') {
    return null;
  }

  return {
    ...user,
    role: normalizeRole(user.role),
  };
}

export function AuthProvider({ children }) {
  const [token, setToken] = useState(getStoredToken);
  const [currentUser, setCurrentUser] = useState(null);
  const [isInitializing, setIsInitializing] = useState(true);

  const clearSession = useCallback(() => {
    setToken('');
    setCurrentUser(null);
    setApiAuthToken('');
    if (typeof window !== 'undefined') {
      window.localStorage.removeItem(TOKEN_STORAGE_KEY);
    }
  }, []);

  const persistSession = useCallback((nextToken, user) => {
    const normalizedUser = normalizeAuthUser(user);
    setToken(nextToken);
    setCurrentUser(normalizedUser);
    setApiAuthToken(nextToken);
    if (typeof window !== 'undefined') {
      window.localStorage.setItem(TOKEN_STORAGE_KEY, nextToken);
    }
  }, []);

  const refreshMe = useCallback(async () => {
    const user = normalizeAuthUser(await authService.me());
    setCurrentUser(user);
    return user;
  }, []);

  useEffect(() => {
    setApiAuthToken(token);
  }, [token]);

  useEffect(() => {
    setApiUnauthorizedHandler(() => {
      clearSession();
    });

    return () => {
      setApiUnauthorizedHandler(null);
    };
  }, [clearSession]);

  useEffect(() => {
    let isMounted = true;

    const bootstrap = async () => {
      if (!token) {
        setIsInitializing(false);
        return;
      }

      try {
        setApiAuthToken(token);
        const user = normalizeAuthUser(await authService.me());
        if (isMounted) {
          setCurrentUser(user);
        }
      } catch (error) {
        if (isMounted) {
          clearSession();
        }
      } finally {
        if (isMounted) {
          setIsInitializing(false);
        }
      }
    };

    bootstrap();

    return () => {
      isMounted = false;
    };
  }, [clearSession, token]);

  const login = useCallback(async (payload) => {
    const result = await authService.login(payload);
    persistSession(result.token, result.user);
    return result.user;
  }, [persistSession]);

  const register = useCallback(async (payload) => {
    const result = await authService.register(payload);
    persistSession(result.token, result.user);
    return result.user;
  }, [persistSession]);

  const logout = useCallback(() => {
    clearSession();
  }, [clearSession]);

  const value = useMemo(() => ({
    currentUser,
    token,
    isAuthenticated: Boolean(token && currentUser),
    isInitializing,
    login,
    register,
    logout,
    refreshMe,
  }), [currentUser, isInitializing, login, logout, refreshMe, register, token]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  return useContext(AuthContext);
}
