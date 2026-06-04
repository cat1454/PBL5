import React from 'react';

function LanguageSwitch({ language, setLanguage, label }) {
  return (
    <div className="language-toggle v2-language-toggle app-language-switch" aria-label={label}>
      <button
        type="button"
        className={`language-toggle-button${language === 'vi' ? ' active' : ''}`}
        onClick={() => setLanguage('vi')}
      >
        VI
      </button>
      <button
        type="button"
        className={`language-toggle-button${language === 'en' ? ' active' : ''}`}
        onClick={() => setLanguage('en')}
      >
        EN
      </button>
    </div>
  );
}

export default LanguageSwitch;
