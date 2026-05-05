import React, { createContext, useContext, useEffect, useMemo, useState } from 'react';
import translations from '../i18n';

const STORAGE_KEY = 'elearn-language';
const DEFAULT_LANGUAGE = 'vi';

const LanguageContext = createContext({
  language: DEFAULT_LANGUAGE,
  setLanguage: () => {},
  toggleLanguage: () => {},
  t: (key, vars) => key,
});

function getInitialLanguage() {
  if (typeof window === 'undefined') {
    return DEFAULT_LANGUAGE;
  }

  const saved = window.localStorage.getItem(STORAGE_KEY);
  return saved === 'en' ? 'en' : DEFAULT_LANGUAGE;
}

function resolveTranslation(language, key) {
  return key.split('.').reduce((current, segment) => current?.[segment], translations[language]);
}

function interpolate(template, vars = {}) {
  if (typeof template !== 'string') {
    return template;
  }

  return template.replace(/\{\{(.*?)\}\}/g, (_, rawKey) => {
    const value = vars[rawKey.trim()];
    return value === undefined || value === null ? '' : String(value);
  });
}

export function LanguageProvider({ children }) {
  const [language, setLanguage] = useState(getInitialLanguage);

  useEffect(() => {
    window.localStorage.setItem(STORAGE_KEY, language);
  }, [language]);

  const value = useMemo(() => ({
    language,
    setLanguage,
    toggleLanguage: () => setLanguage((current) => (current === 'vi' ? 'en' : 'vi')),
    t: (key, vars) => {
      const translated = resolveTranslation(language, key);
      if (translated === undefined) {
        return key;
      }

      return interpolate(translated, vars);
    },
  }), [language]);

  return <LanguageContext.Provider value={value}>{children}</LanguageContext.Provider>;
}

export function useLanguage() {
  return useContext(LanguageContext);
}

export { DEFAULT_LANGUAGE };
