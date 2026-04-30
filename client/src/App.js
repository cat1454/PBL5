import React, { useEffect, useMemo, useState } from 'react';
import { BrowserRouter as Router, NavLink, Navigate, Route, Routes, useLocation, useParams } from 'react-router-dom';
import './App.css';
import AnalysisContent from './components/AnalysisContent';
import DocumentList from './components/DocumentList';
import DocumentUpload from './components/DocumentUpload';
import FlashcardGame from './components/FlashcardGame';
import FolderProjects from './components/FolderProjects';
import FolderStudio from './components/FolderStudio';
import QuizGame from './components/QuizGame';
import SlideStudio from './components/SlideStudio';
import StreakGame from './components/StreakGame';
import StudyHub from './components/StudyHub';
import { useLanguage } from './context/LanguageContext';

function App() {
  const { t } = useLanguage();
  const [currentUser] = useState({
    name: 'Tran Hong Thao',
    role: t('app.userRole'),
    avatar: null,
  });

  const localizedUser = useMemo(() => ({
    ...currentUser,
    role: t('app.userRole'),
  }), [currentUser, t]);

  return (
    <Router>
      <AppShell user={localizedUser} />
    </Router>
  );
}

function AppShell({ user }) {
  const location = useLocation();
  const [currentFile, setCurrentFile] = useState(null);
  const [isMainMenuOpen, setIsMainMenuOpen] = useState(false);
  const { language, setLanguage, t } = useLanguage();
  const isHybridRoute = location.pathname.startsWith('/documents') || location.pathname.startsWith('/folders') || location.pathname.startsWith('/workspaces');
  const isStudioRoute = location.pathname.startsWith('/slides/') || location.pathname.startsWith('/folders/') || location.pathname.startsWith('/workspaces/') || location.pathname.startsWith('/study/');
  const isSlideStudioRoute = location.pathname.startsWith('/slides/');

  useEffect(() => {
    setIsMainMenuOpen(false);
  }, [location.pathname]);

  const openMenuLabel = language === 'vi' ? 'Mở menu điều hướng' : 'Open navigation menu';
  const closeMenuLabel = language === 'vi' ? 'Đóng menu' : 'Close menu';
  const navigationMenuLabel = language === 'vi' ? 'Menu điều hướng chính' : 'Main navigation menu';

  return (
    <div className={`App app-shell${isHybridRoute ? ' app-shell-documents' : ''}${isSlideStudioRoute ? ' app-shell-slide-route' : ''}${isMainMenuOpen ? ' is-menu-open' : ''}`}>
      <header className="App-header app-shell-header app-topbar">
        <div className="container app-shell-header-inner">
          <div className="app-shell-header-start">
            <button
              type="button"
              className="app-menu-toggle"
              onClick={() => setIsMainMenuOpen(true)}
              aria-label={openMenuLabel}
            >
              <span />
              <span />
              <span />
            </button>

            <div className="app-shell-brand">
              <div className="app-shell-brand-mark">AI</div>
              <div className="app-shell-brand-copy">
                <strong>{t('app.brand')}</strong>
              </div>
            </div>
          </div>

          <div className="app-shell-user">
            <div className="language-toggle" aria-label={t('app.languageToggle.label')}>
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

            <div className="app-shell-user-meta">
              <span className="user-name">{user.name}</span>
              <span>{user.role}</span>
            </div>
            <div className="app-shell-user-avatar">
              {user.avatar ? <img src={user.avatar} alt="avatar" /> : user.name.charAt(0)}
            </div>
            <button type="button" className="button button-secondary">{t('app.logout')}</button>
          </div>
        </div>
      </header>

      {isMainMenuOpen && (
        <div
          className="app-menu-backdrop"
          onClick={() => setIsMainMenuOpen(false)}
        >
          <aside
            className="app-menu-drawer"
            onClick={(event) => event.stopPropagation()}
          >
            <div className="app-menu-header">
              <div className="app-menu-logo">AI</div>
              <strong>{t('app.brand')}</strong>

              <button
                type="button"
                className="app-menu-close"
                onClick={() => setIsMainMenuOpen(false)}
                aria-label={closeMenuLabel}
              >
                ×
              </button>
            </div>

            <nav className="app-menu-nav" aria-label={navigationMenuLabel}>
              <NavLink to="/" end className={({ isActive }) => (isActive ? 'active' : '')}>
                {t('app.nav.dashboard')}
              </NavLink>
              <NavLink to="/workspaces" className={({ isActive }) => (isActive ? 'active' : '')}>
                {t('app.nav.workspaces')}
              </NavLink>
              <NavLink to="/settings" className={({ isActive }) => (isActive ? 'active' : '')}>
                {t('app.nav.settings')}
              </NavLink>
            </nav>
          </aside>
        </div>
      )}

      <div className="app-shell-body">
        <main className="app-main app-shell-content">
          <div className="app-shell-content-inner">
            {!isStudioRoute && (
              <div className="app-page-header">
                <h1>{getPageTitle(location.pathname, t)}</h1>
              </div>
            )}

            <Routes>
              <Route path="/" element={<DashboardPage currentFile={currentFile} setCurrentFile={setCurrentFile} />} />
              <Route path="/documents" element={<Navigate to="/workspaces" replace />} />
              <Route path="/folders" element={<Navigate to="/workspaces" replace />} />
              <Route path="/folders/:folderId/studio" element={<LegacyWorkspaceRedirect />} />
              <Route path="/workspaces" element={<FolderProjects />} />
              <Route path="/workspaces/:workspaceId" element={<FolderStudio />} />
              <Route path="/documents-legacy" element={<DocumentList />} />
              <Route path="/settings" element={<SettingsPage />} />
              <Route path="/study/:documentId" element={<StudyHub />} />
              <Route path="/study/:documentId/:mode" element={<StudyHub />} />
              <Route path="/quiz/:documentId" element={<QuizGame />} />
              <Route path="/flashcards/:documentId" element={<FlashcardGame />} />
              <Route path="/streak/:documentId" element={<StreakGame />} />
              <Route path="/slides/:documentId" element={<SlideStudio />} />
            </Routes>
          </div>
        </main>
      </div>
    </div>
  );
}

function DashboardPage({ currentFile, setCurrentFile }) {
  const [activeTab, setActiveTab] = useState('file');
  const { t } = useLanguage();

  if (!currentFile) {
    return (
      <div className="workspace-clean-start">
        <div className="workspace-clean-start-orb" aria-hidden="true"></div>
        <section className="upload-minimal-box">
          <div className="workspace-clean-start-copy">
            <span className="workspace-clean-start-kicker">{t('app.dashboard.kicker')}</span>
            <h2>{t('app.dashboard.title')}</h2>
            <p>{t('app.dashboard.subtitle')}</p>
          </div>
          <DocumentUpload
            variant="minimal-dark"
            onUploadSuccess={(data) => setCurrentFile(data)}
          />
        </section>
      </div>
    );
  }

  return (
    <div className="workspace-container">
      <div className="workspace-breadcrumb">
        {t('app.dashboard.breadcrumb')} <span className="file-name">[{currentFile.fileName}]</span>
      </div>

      <div className="workspace-toolbar">
        <button className={activeTab === 'file' ? 'active' : ''} onClick={() => setActiveTab('file')}>{t('app.dashboard.tabs.analysis')}</button>
        <button className={activeTab === 'slide' ? 'active' : ''} onClick={() => setActiveTab('slide')}>{t('app.dashboard.tabs.slides')}</button>
        <button className={activeTab === 'quiz' ? 'active' : ''} onClick={() => setActiveTab('quiz')}>{t('app.dashboard.tabs.quiz')}</button>
        <button className={activeTab === 'flash' ? 'active' : ''} onClick={() => setActiveTab('flash')}>{t('app.dashboard.tabs.flashcards')}</button>
      </div>

      <div className="workspace-main-frame">
        {activeTab === 'file' && <AnalysisContent data={currentFile} />}
        {activeTab === 'slide' && <SlideStudio documentId={currentFile.id} />}
        {activeTab === 'quiz' && <QuizGame documentId={currentFile.id} />}
        {activeTab === 'flash' && <FlashcardGame documentId={currentFile.id} />}
      </div>
    </div>
  );
}

function SettingsPage() {
  const { t } = useLanguage();

  return (
    <div className="card">
      <h2>{t('app.settings.title')}</h2>
      <p className="section-subtitle">{t('app.settings.subtitle')}</p>
    </div>
  );
}

function LegacyWorkspaceRedirect() {
  const { folderId } = useParams();
  return <Navigate to={`/workspaces/${folderId}`} replace />;
}

function getPageTitle(pathname, t) {
  if (pathname.startsWith('/workspaces/')) {
    return t('app.pageTitle.workspaceStudio');
  }

  if (pathname.startsWith('/documents') || pathname.startsWith('/folders') || pathname.startsWith('/workspaces')) {
    return t('app.pageTitle.workspaces');
  }

  if (pathname.startsWith('/settings')) {
    return t('app.pageTitle.settings');
  }

  if (pathname.startsWith('/quiz/')) {
    return t('app.pageTitle.quiz');
  }

  if (pathname.startsWith('/study/')) {
    return t('app.pageTitle.studyHub');
  }

  if (pathname.startsWith('/flashcards/')) {
    return t('app.pageTitle.flashcards');
  }

  if (pathname.startsWith('/streak/')) {
    return t('app.pageTitle.streak');
  }

  if (pathname.startsWith('/slides/')) {
    return t('app.pageTitle.slides');
  }

  return t('app.pageTitle.dashboard');
}

export default App;
