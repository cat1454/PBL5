import React, { createContext, useCallback, useContext, useMemo, useRef, useState } from 'react';
import { LuX } from 'react-icons/lu';
import { useLanguage } from '../../context/LanguageContext';

const DEFAULT_DURATION = 3800;

const ToastContext = createContext({
  showToast: () => {},
  dismissToast: () => {},
});

function ToastItem({ toast, onClose, closeLabel }) {
  React.useEffect(() => {
    const timeoutId = window.setTimeout(() => {
      onClose(toast.id);
    }, toast.duration || DEFAULT_DURATION);

    return () => window.clearTimeout(timeoutId);
  }, [onClose, toast.duration, toast.id]);

  return (
    <article className={`app-toast app-toast-${toast.type || 'info'}`} role="status">
      <div className="app-toast-body">
        <div className="app-toast-copy">
          <p className="app-toast-message">{toast.message}</p>
          {toast.description ? <p className="app-toast-description">{toast.description}</p> : null}
        </div>
        <button
          type="button"
          className="app-toast-close"
          onClick={() => onClose(toast.id)}
          aria-label={closeLabel}
        >
          <LuX aria-hidden="true" />
        </button>
      </div>
    </article>
  );
}

export function ToastProvider({ children }) {
  const { t } = useLanguage();
  const [toasts, setToasts] = useState([]);
  const idRef = useRef(0);

  const dismissToast = useCallback((toastId) => {
    setToasts((current) => current.filter((toast) => toast.id !== toastId));
  }, []);

  const showToast = useCallback((input) => {
    if (!input?.message) {
      return;
    }

    idRef.current += 1;

    const nextToast = {
      id: `toast-${Date.now()}-${idRef.current}`,
      type: input.type || 'info',
      message: input.message,
      description: input.description || '',
      duration: input.duration || DEFAULT_DURATION,
    };

    setToasts((current) => [...current, nextToast]);
  }, []);

  const value = useMemo(() => ({
    showToast,
    dismissToast,
  }), [dismissToast, showToast]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="app-toast-stack" aria-live="polite" aria-atomic="false">
        {toasts.map((toast) => (
          <ToastItem
            key={toast.id}
            toast={toast}
            onClose={dismissToast}
            closeLabel={t('toast.close')}
          />
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast() {
  return useContext(ToastContext);
}
