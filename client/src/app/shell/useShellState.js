import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';

export function useShellState({ location, onLogout }) {
  const navigate = useNavigate();
  const [isMainMenuOpen, setIsMainMenuOpen] = useState(false);
  const [isAccountMenuOpen, setIsAccountMenuOpen] = useState(false);
  const accountMenuRef = useRef(null);

  useEffect(() => {
    setIsMainMenuOpen(false);
    setIsAccountMenuOpen(false);
  }, [location.pathname]);

  useEffect(() => {
    if (!isAccountMenuOpen) {
      return undefined;
    }

    const handlePointerDown = (event) => {
      if (accountMenuRef.current && !accountMenuRef.current.contains(event.target)) {
        setIsAccountMenuOpen(false);
      }
    };

    document.addEventListener('mousedown', handlePointerDown);
    return () => {
      document.removeEventListener('mousedown', handlePointerDown);
    };
  }, [isAccountMenuOpen]);

  const handleHelpClick = useCallback(() => {
    navigate('/', { state: { openGuide: true, guideChip: 'howToUse' } });
  }, [navigate]);

  const handleLogout = useCallback(() => {
    setIsAccountMenuOpen(false);
    onLogout();
    navigate('/login', { replace: true });
  }, [navigate, onLogout]);

  return {
    accountMenuRef,
    closeMainMenu: () => setIsMainMenuOpen(false),
    handleHelpClick,
    handleLogout,
    isAccountMenuOpen,
    isMainMenuOpen,
    openMainMenu: () => setIsMainMenuOpen(true),
    toggleAccountMenu: () => setIsAccountMenuOpen((current) => !current),
  };
}
