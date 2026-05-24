import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { BrowserRouter as Router, NavLink, Navigate, Route, Routes, useLocation, useNavigate, useParams } from 'react-router-dom';
import './App.css';
import AdminPage from './components/AdminPage';
import DocumentUpload from './components/DocumentUpload';
import FlashcardGame from './components/FlashcardGame';
import FolderProjects from './components/FolderProjects';
import FolderStudio from './components/FolderStudio';
import QuizGame from './components/QuizGame';
import QuestionStudioPage from './components/question-studio/QuestionStudioPage';
import SlideStudio from './components/SlideStudio';
import StreakGame from './components/StreakGame';
import StudyHub from './components/StudyHub';
import AdminRoute from './components/auth/AdminRoute';
import LoginPage from './components/auth/LoginPage';
import ProtectedRoute from './components/auth/ProtectedRoute';
import RegisterPage from './components/auth/RegisterPage';
import { ToastProvider } from './components/common/ToastProvider';
import { AuthProvider, useAuth } from './context/AuthContext';
import { useLanguage } from './context/LanguageContext';
import { getApiErrorMessage, workspaceService } from './services/api';

const MAX_RECENT_SOURCES = 4;
const GUIDE_CHIPS = ['howToUse', 'createQuestions', 'createSlides', 'whatNext'];
const PIPELINE_STEPS = ['upload', 'ocr', 'analysis', 'questions', 'slides'];
const CHECKLIST_STEPS = ['upload', 'analysis', 'questions', 'study', 'deck', 'preview'];

function App() {
  return (
    <Router>
      <ToastProvider>
        <AuthProvider>
          <AppRouter />
        </AuthProvider>
      </ToastProvider>
    </Router>
  );
}

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
      <Route path="/login" element={isAuthenticated ? <Navigate to="/" replace /> : <LoginPage />} />
      <Route path="/register" element={isAuthenticated ? <Navigate to="/" replace /> : <RegisterPage />} />
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

function AppShell({ user, onLogout }) {
  const location = useLocation();
  const navigate = useNavigate();
  const [isMainMenuOpen, setIsMainMenuOpen] = useState(false);
  const [isAccountMenuOpen, setIsAccountMenuOpen] = useState(false);
  const accountMenuRef = useRef(null);
  const { language, setLanguage, t } = useLanguage();
  const { currentUser } = useAuth();
  const isHybridRoute = location.pathname.startsWith('/documents') || location.pathname.startsWith('/folders') || location.pathname.startsWith('/workspaces');
  const isStudioRoute = location.pathname.startsWith('/slides/') || location.pathname.startsWith('/folders/') || location.pathname.startsWith('/workspaces/') || location.pathname.startsWith('/study/') || location.pathname.startsWith('/question-studio/');
  const isSlideStudioRoute = location.pathname.startsWith('/slides/');

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

  return (
    <div className={`App app-shell${isHybridRoute ? ' app-shell-documents' : ''}${isSlideStudioRoute ? ' app-shell-slide-route' : ''}${isMainMenuOpen ? ' is-menu-open' : ''}`}>
      <header className="App-header app-shell-header app-topbar">
        <div className="container app-shell-header-inner">
          <div className="app-shell-header-start">
            <button
              type="button"
              className="app-menu-toggle"
              onClick={() => setIsMainMenuOpen(true)}
              aria-label={t('app.menu.open')}
            >
              <span />
              <span />
              <span />
            </button>

            <NavLink to="/" className="app-shell-brand">
              <div className="app-shell-brand-mark">AI</div>
              <div className="app-shell-brand-copy">
                <strong>{t('app.brand')}</strong>
                <span>{t('app.topbar.productTag')}</span>
              </div>
            </NavLink>

            <nav className="app-topbar-nav" aria-label={t('app.menu.navigation')}>
              <NavLink to="/" end className={({ isActive }) => `app-topbar-link${isActive ? ' active' : ''}`}>
                {t('app.nav.dashboard')}
              </NavLink>
              <NavLink to="/workspaces" className={({ isActive }) => `app-topbar-link${isActive ? ' active' : ''}`}>
                {t('app.nav.workspaces')}
              </NavLink>
              {currentUser?.role === 'ADMIN' && (
                <NavLink to="/admin" className={({ isActive }) => `app-topbar-link${isActive ? ' active' : ''}`}>
                  {t('app.nav.admin')}
                </NavLink>
              )}
              <button type="button" className="app-topbar-link app-topbar-link-placeholder" onClick={handleHelpClick}>
                {t('app.nav.help')}
              </button>
            </nav>
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

            <div className={`app-shell-account${isAccountMenuOpen ? ' is-open' : ''}`} ref={accountMenuRef}>
              <button
                type="button"
                className="app-shell-account-trigger"
                onClick={() => setIsAccountMenuOpen((current) => !current)}
                aria-expanded={isAccountMenuOpen}
                aria-haspopup="menu"
              >
                <div className="app-shell-user-avatar">
                  {user.avatar ? <img src={user.avatar} alt={t('app.account.avatarAlt', { name: user.name })} /> : user.name.charAt(0)}
                </div>
                <div className="app-shell-user-meta">
                  <span className="user-name">{user.name}</span>
                  <span>{user.roleLabel}</span>
                </div>
                <span className="app-shell-account-chevron" aria-hidden="true">▾</span>
              </button>

              {isAccountMenuOpen && (
                <div className="app-account-menu" role="menu" aria-label={t('app.account.menuLabel')}>
                  <div className="app-account-summary">
                    <div className="app-shell-user-avatar">
                      {user.avatar ? <img src={user.avatar} alt={t('app.account.avatarAlt', { name: user.name })} /> : user.name.charAt(0)}
                    </div>
                    <div>
                      <strong>{user.name}</strong>
                      <p>{user.roleLabel}</p>
                    </div>
                  </div>

                  <button type="button" className="app-account-item" onClick={handleHelpClick}>
                    <span>{t('app.account.helpGuide')}</span>
                    <small>{t('app.account.helpHint')}</small>
                  </button>
                  <button type="button" className="app-account-item app-account-item-danger" onClick={handleLogout}>
                    <span>{t('app.account.logout')}</span>
                    <small>{t('app.account.logoutHint')}</small>
                  </button>
                </div>
              )}
            </div>
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
                aria-label={t('app.menu.close')}
              >
                ×
              </button>
            </div>

            <nav className="app-menu-nav" aria-label={t('app.menu.navigation')}>
              <NavLink to="/" end className={({ isActive }) => (isActive ? 'active' : '')}>
                {t('app.nav.dashboard')}
              </NavLink>
              <NavLink to="/workspaces" className={({ isActive }) => (isActive ? 'active' : '')}>
                {t('app.nav.workspaces')}
              </NavLink>
              {currentUser?.role === 'ADMIN' && (
                <NavLink to="/admin" className={({ isActive }) => (isActive ? 'active' : '')}>
                  {t('app.nav.admin')}
                </NavLink>
              )}
              <button type="button" className="app-menu-placeholder" onClick={handleHelpClick}>
                {t('app.nav.help')}
              </button>
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
              <Route path="/" element={<DashboardPage />} />
              <Route path="/documents" element={<Navigate to="/workspaces" replace />} />
              <Route path="/folders" element={<Navigate to="/workspaces" replace />} />
              <Route path="/folders/:folderId/studio" element={<LegacyWorkspaceRedirect />} />
              <Route path="/workspaces" element={<FolderProjects />} />
              <Route path="/workspaces/:workspaceId" element={<FolderStudio />} />
              <Route path="/documents-legacy" element={<Navigate to="/workspaces" replace />} />
              <Route path="/settings" element={<SettingsPage />} />
              <Route path="/admin" element={<AdminRoute><AdminPage /></AdminRoute>} />
              <Route path="/study/:documentId" element={<StudyHub />} />
              <Route path="/study/:documentId/:mode" element={<StudyHub />} />
              <Route path="/question-studio/:documentId" element={<QuestionStudioPage />} />
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

function DashboardPage() {
  const { currentUser } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const { language, t } = useLanguage();
  const {
    defaultWorkspace,
    sources,
    loading,
    error,
    reload,
  } = useWorkspaceHomeData();
  const [selectedGuideChip, setSelectedGuideChip] = useState('whatNext');

  useEffect(() => {
    if (location.state?.openGuide && GUIDE_CHIPS.includes(location.state.guideChip)) {
      setSelectedGuideChip(location.state.guideChip);
      navigate(location.pathname, { replace: true, state: null });
    }
  }, [location.pathname, location.state, navigate]);

  const dashboardVm = useMemo(
    () => buildDashboardViewModel(defaultWorkspace, sources),
    [defaultWorkspace, sources],
  );
  const recentSources = useMemo(() => sources.slice(0, MAX_RECENT_SOURCES), [sources]);

  const handleUploadSuccess = useCallback((data) => {
    const uploadState = {
      uploadNotice: {
        message: t('upload.success'),
        description: t('upload.processingStarted'),
      },
    };

    if (data?.workspaceId) {
      navigate(`/workspaces/${data.workspaceId}`, { state: uploadState });
      return;
    }

    navigate('/workspaces', { state: uploadState });
  }, [navigate, t]);

  const openWorkspaces = useCallback(() => {
    navigate('/workspaces');
  }, [navigate]);

  const openWorkspaceStudio = useCallback(() => {
    if (defaultWorkspace?.id) {
      navigate(`/workspaces/${defaultWorkspace.id}`);
      return;
    }

    navigate('/workspaces');
  }, [defaultWorkspace?.id, navigate]);

  const openStudyRoute = useCallback((mode, documentId) => {
    if (!documentId) {
      return;
    }

    navigate(`/${mode}/${documentId}`);
  }, [navigate]);

  const executeDashboardAction = useCallback((action) => {
    if (!action || action.disabled) {
      return;
    }

    switch (action.type) {
      case 'upload':
        document.getElementById('dashboard-upload-section')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
        break;
      case 'workspaces':
        openWorkspaces();
        break;
      case 'workspaceStudio':
        openWorkspaceStudio();
        break;
      case 'quiz':
      case 'flashcards':
      case 'streak':
        openStudyRoute(action.type, action.documentId);
        break;
      default:
        break;
    }
  }, [openStudyRoute, openWorkspaceStudio, openWorkspaces]);

  const guideState = useMemo(() => getGuideState(dashboardVm, t), [dashboardVm, t]);
  const guideAnswer = useMemo(
    () => getGuideAnswer(selectedGuideChip, dashboardVm, t),
    [selectedGuideChip, dashboardVm, t],
  );
  const shortcutCards = useMemo(
    () => buildShortcutCards(dashboardVm, t),
    [dashboardVm, t],
  );
  const pipelineSteps = useMemo(
    () => buildPipelineSteps(dashboardVm, t),
    [dashboardVm, t],
  );
  const checklistItems = useMemo(
    () => buildChecklistItems(dashboardVm, t),
    [dashboardVm, t],
  );
  const nextAction = useMemo(
    () => getNextBestAction(dashboardVm, t),
    [dashboardVm, t],
  );
  const dashboardRole = currentUser?.role || 'LEARNER';
  const dashboardTitle = t(`app.dashboard.titleByRole.${dashboardRole}`);
  const dashboardSubtitle = t(`app.dashboard.subtitleByRole.${dashboardRole}`);

  return (
    <div className="workspace-dashboard">
      <section className="workspace-dashboard-header card">
        <div className="workspace-dashboard-header-copy">
          <span className="workspace-dashboard-kicker">{t('app.dashboard.kicker')}</span>
          <h2>{dashboardTitle}</h2>
          <p>{dashboardSubtitle}</p>
        </div>

        <div className="workspace-dashboard-stats">
          <div className="workspace-dashboard-stat">
            <span>{t('app.dashboard.stats.workspace')}</span>
            <strong>{defaultWorkspace?.name || t('app.dashboard.defaultWorkspaceFallback')}</strong>
          </div>
          <div className="workspace-dashboard-stat">
            <span>{t('app.dashboard.stats.sourcesLabel')}</span>
            <strong>{dashboardVm.sourceCount}</strong>
          </div>
          <div className="workspace-dashboard-stat">
            <span>{t('app.dashboard.stats.completedLabel')}</span>
            <strong>{dashboardVm.completedCount}</strong>
          </div>
          <div className="workspace-dashboard-stat">
            <span>{t('app.dashboard.stats.readyLabel')}</span>
            <strong>{dashboardVm.readyQuestionBankCount}</strong>
          </div>
        </div>
      </section>

      {loading && (
        <div className="card workspace-home-state">
          <div className="spinner"></div>
          <p>{t('app.dashboard.loading')}</p>
        </div>
      )}

      {error && (
        <div className="alert alert-error workspace-home-alert">
          <div>
            <strong>{t('app.dashboard.loadErrorTitle')}</strong>
            <p>{error}</p>
          </div>
          <button type="button" className="button button-secondary" onClick={reload}>
            {t('app.dashboard.retry')}
          </button>
        </div>
      )}

      <div className="workspace-dashboard-top-grid">
        <section className="card workspace-dashboard-welcome">
          <div className="workspace-dashboard-section-head">
            <div>
              <h3>{t('app.dashboard.workspaceTitle')}</h3>
              <p>{t('app.dashboard.workspaceSubtitle')}</p>
            </div>
          </div>

          <div className="workspace-dashboard-action-row">
            <button type="button" className="button" onClick={() => executeDashboardAction({ type: 'upload' })}>
              {t('app.dashboard.actions.upload')}
            </button>
            <button type="button" className="button button-secondary" onClick={openWorkspaces}>
              {t('app.dashboard.actions.openWorkspace')}
            </button>
            <button
              type="button"
              className="button button-secondary"
              onClick={openWorkspaceStudio}
              disabled={!defaultWorkspace?.id}
            >
              {t('app.dashboard.actions.createSlides')}
            </button>
          </div>

          <div className="workspace-dashboard-mini-grid">
            <article className="workspace-dashboard-mini-card">
              <span>{t('app.dashboard.highlights.workspaceStatus')}</span>
              <strong>{dashboardVm.workspaceHasDeck ? t('app.dashboard.deck.ready') : t('app.dashboard.deck.notReady')}</strong>
              <p>{dashboardVm.workspaceHasDeck ? t('app.dashboard.highlights.workspaceStatusReady') : t('app.dashboard.highlights.workspaceStatusPending')}</p>
            </article>
            <article className="workspace-dashboard-mini-card">
              <span>{t('app.dashboard.highlights.recentSource')}</span>
              <strong>{dashboardVm.latestSource?.fileName || t('app.dashboard.emptySourceLabel')}</strong>
              <p>{dashboardVm.latestSource ? t('app.dashboard.sourceMeta.updated', { time: formatRelativeTime(dashboardVm.latestSource.updatedAt || dashboardVm.latestSource.createdAt, t, language) }) : t('app.dashboard.emptySourceHint')}</p>
            </article>
          </div>

          <div id="dashboard-upload-section" className="workspace-dashboard-upload-wrap">
            <div className="workspace-dashboard-section-head">
              <div>
                <h3>{t('app.dashboard.uploadTitle')}</h3>
                <p>{t('app.dashboard.uploadSubtitle', { workspaceName: defaultWorkspace?.name || t('app.dashboard.defaultWorkspaceFallback') })}</p>
              </div>
            </div>
            <DocumentUpload onUploadSuccess={handleUploadSuccess} />
          </div>
        </section>

        <section className="card workspace-guide-card">
          <div className="workspace-dashboard-section-head">
            <div>
              <h3>{t('app.dashboard.guide.title')}</h3>
              <p>{t('app.dashboard.guide.subtitle')}</p>
            </div>
            <span className={`workspace-guide-status workspace-guide-status-${guideState.tone}`}>
              {guideState.badge}
            </span>
          </div>

          <div className="workspace-guide-message">
            <strong>{guideState.title}</strong>
            <p>{guideState.body}</p>
          </div>

          <div className="workspace-guide-actions">
            {guideState.actions.map((action) => (
              <button
                key={action.label}
                type="button"
                className={`button${action.secondary ? ' button-secondary' : ''}`}
                onClick={() => executeDashboardAction(action)}
                disabled={action.disabled}
              >
                {action.label}
              </button>
            ))}
          </div>

          <div className="workspace-guide-chip-row">
            {GUIDE_CHIPS.map((chip) => (
              <button
                key={chip}
                type="button"
                className={`workspace-guide-chip${selectedGuideChip === chip ? ' active' : ''}`}
                onClick={() => setSelectedGuideChip(chip)}
              >
                {t(`app.dashboard.guide.chips.${chip}`)}
              </button>
            ))}
          </div>

          <div className="workspace-guide-answer">
            <strong>{guideAnswer.title}</strong>
            <p>{guideAnswer.body}</p>
            <div className="workspace-guide-actions">
              {guideAnswer.actions.map((action) => (
                <button
                  key={action.label}
                  type="button"
                  className={`button${action.secondary ? ' button-secondary' : ''}`}
                  onClick={() => executeDashboardAction(action)}
                  disabled={action.disabled}
                >
                  {action.label}
                </button>
              ))}
            </div>
          </div>
        </section>
      </div>

      <section className="workspace-dashboard-shortcuts">
        {shortcutCards.map((card) => (
          <article key={card.key} className={`card workspace-shortcut-card${card.disabled ? ' is-disabled' : ''}`}>
            <div className="workspace-shortcut-icon" aria-hidden="true">{card.icon}</div>
            <div>
              <h4>{card.title}</h4>
              <p>{card.body}</p>
            </div>
            {card.cta ? (
              <button
                type="button"
                className="workspace-shortcut-link"
                onClick={() => executeDashboardAction(card.cta)}
                disabled={card.disabled}
              >
                {card.cta.label}
              </button>
            ) : (
              <span className="workspace-shortcut-pill">{card.pill}</span>
            )}
          </article>
        ))}
      </section>

      <div className="workspace-dashboard-main-grid">
        <section className="card workspace-dashboard-recent">
          <div className="workspace-dashboard-section-head">
            <div>
              <h3>{t('app.dashboard.recentSourcesTitle')}</h3>
              <p>{t('app.dashboard.recentSourcesSubtitle')}</p>
            </div>
          </div>

          {!loading && recentSources.length === 0 && (
            <div className="workspace-home-empty">
              <h4>{t('app.dashboard.emptyTitle')}</h4>
              <p>{t('app.dashboard.emptyBody')}</p>
            </div>
          )}

          {recentSources.length > 0 && (
            <div className="workspace-dashboard-source-list">
              {recentSources.map((source) => {
                const sourceAction = getSourcePrimaryAction(source, dashboardVm, t);
                const deckHint = getWorkspaceDeckHint(dashboardVm, t);
                return (
                  <article key={source.id} className="workspace-dashboard-source-card">
                    <div className="workspace-dashboard-source-main">
                      <div className="workspace-dashboard-source-top">
                        <h4>{source.fileName}</h4>
                        <span className={`workspace-source-status status-${normalizeSourceStatus(source.status)}`}>
                          {t(`app.dashboard.status.${normalizeSourceStatus(source.status)}`)}
                        </span>
                      </div>
                      <div className="workspace-source-meta">
                        <span>{t('app.dashboard.sourceMeta.questions', { count: source.questionsCount || 0 })}</span>
                        <span>{deckHint}</span>
                        <span>{t('app.dashboard.sourceMeta.updated', { time: formatRelativeTime(source.updatedAt || source.createdAt, t, language) })}</span>
                      </div>
                      <p className="workspace-dashboard-source-note">{sourceAction.helper}</p>
                    </div>

                    <button
                      type="button"
                      className="button button-secondary"
                      onClick={() => executeDashboardAction(sourceAction.action)}
                      disabled={sourceAction.action.disabled}
                    >
                      {sourceAction.action.label}
                    </button>
                  </article>
                );
              })}
            </div>
          )}
        </section>

        <section className="card workspace-dashboard-pipeline">
          <div className="workspace-dashboard-section-head">
            <div>
              <h3>{t('app.dashboard.pipeline.title')}</h3>
              <p>{t('app.dashboard.pipeline.subtitle')}</p>
            </div>
          </div>

          <div className="workspace-pipeline-list">
            {pipelineSteps.map((step) => (
              <article key={step.key} className={`workspace-pipeline-step is-${step.state}`}>
                <div className="workspace-pipeline-marker">{step.index}</div>
                <div className="workspace-pipeline-copy">
                  <strong>{step.title}</strong>
                  <p>{step.body}</p>
                </div>
                <span className={`workspace-pipeline-state state-${step.state}`}>{step.label}</span>
              </article>
            ))}
          </div>
        </section>
      </div>

      <div className="workspace-dashboard-support-grid">
        <section className="card workspace-dashboard-next-action">
          <div className="workspace-dashboard-section-head">
            <div>
              <h3>{t('app.dashboard.nextAction.title')}</h3>
              <p>{t('app.dashboard.nextAction.subtitle')}</p>
            </div>
          </div>

          <div className="workspace-next-action-card">
            <span className="workspace-next-action-eyebrow">{nextAction.eyebrow}</span>
            <strong>{nextAction.title}</strong>
            <p>{nextAction.body}</p>
            <div className="workspace-guide-actions">
              <button
                type="button"
                className="button"
                onClick={() => executeDashboardAction(nextAction.action)}
                disabled={nextAction.action.disabled}
              >
                {nextAction.action.label}
              </button>
              {nextAction.secondaryAction && (
                <button
                  type="button"
                  className="button button-secondary"
                  onClick={() => executeDashboardAction(nextAction.secondaryAction)}
                  disabled={nextAction.secondaryAction.disabled}
                >
                  {nextAction.secondaryAction.label}
                </button>
              )}
            </div>
          </div>
        </section>

        <section className="card workspace-dashboard-checklist">
          <div className="workspace-dashboard-section-head">
            <div>
              <h3>{t('app.dashboard.checklist.title')}</h3>
              <p>{t('app.dashboard.checklist.subtitle')}</p>
            </div>
          </div>

          <div className="workspace-checklist-list">
            {checklistItems.map((item) => (
              <article key={item.key} className={`workspace-checklist-item is-${item.state}`}>
                <div className="workspace-checklist-bullet" aria-hidden="true">{item.state === 'complete' ? '✓' : item.state === 'active' ? '•' : '○'}</div>
                <div>
                  <strong>{item.title}</strong>
                  <p>{item.body}</p>
                </div>
              </article>
            ))}
          </div>
        </section>
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

function useWorkspaceHomeData() {
  const { currentUser } = useAuth();
  const { t } = useLanguage();
  const [defaultWorkspace, setDefaultWorkspace] = useState(null);
  const [sources, setSources] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const loadData = useCallback(async () => {
    if (!currentUser?.id) {
      setDefaultWorkspace(null);
      setSources([]);
      setLoading(false);
      return;
    }

    setLoading(true);
    setError('');

    try {
      const workspace = await workspaceService.getDefault(String(currentUser?.id || ''));
      const workspaceSources = workspace?.id
        ? await workspaceService.listSources(workspace.id)
        : [];

      setDefaultWorkspace(workspace || null);
      setSources(sortSourcesByRecency(Array.isArray(workspaceSources) ? workspaceSources : []));
    } catch (err) {
      console.error(err);
      setDefaultWorkspace(null);
      setSources([]);
      setError(getApiErrorMessage(err, t('app.dashboard.loadErrorBody')));
    } finally {
      setLoading(false);
    }
  }, [currentUser?.id, t]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  return {
    defaultWorkspace,
    sources,
    studyReadySource: sources.find(isStudyReadySource) || null,
    loading,
    error,
    reload: loadData,
  };
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

  if (pathname.startsWith('/question-studio/')) {
    return t('app.pageTitle.questionStudio');
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

function buildDashboardViewModel(defaultWorkspace, sources) {
  const completedSources = sources.filter(isCompletedSource);
  const studyReadySources = sources.filter(isStudyReadySource);
  const processingSource = sources.find((source) => !isCompletedSource(source) && normalizeSourceStatus(source.status) !== 'failed') || null;
  const latestSource = sources[0] || null;
  const latestCompletedSource = completedSources[0] || null;
  const workspaceDeck = defaultWorkspace?.latestDeck || null;
  const workspaceHasDeck = Boolean(workspaceDeck);
  const workspaceNeedsDeck = completedSources.length > 0 && !workspaceHasDeck;
  const workspaceDeckStale = Boolean(workspaceDeck?.isStale);

  return {
    sourceCount: sources.length,
    completedCount: completedSources.length,
    readyQuestionBankCount: studyReadySources.length,
    hasSource: sources.length > 0,
    hasCompletedSource: completedSources.length > 0,
    studyReadySource: studyReadySources[0] || null,
    processingSource,
    latestSource,
    latestCompletedSource,
    workspaceDeck,
    workspaceHasDeck,
    workspaceNeedsDeck,
    workspaceDeckStale,
    defaultWorkspace,
  };
}

function getGuideState(vm, t) {
  if (!vm.hasSource) {
    return {
      tone: 'neutral',
      badge: t('app.dashboard.guide.badges.getStarted'),
      title: t('app.dashboard.guide.states.noSource.title'),
      body: t('app.dashboard.guide.states.noSource.body'),
      actions: [
        { type: 'upload', label: t('app.dashboard.actions.upload') },
        { type: 'workspaces', label: t('app.dashboard.actions.openWorkspace'), secondary: true },
      ],
    };
  }

  if (vm.processingSource && !vm.hasCompletedSource) {
    return {
      tone: 'processing',
      badge: t('app.dashboard.guide.badges.processing'),
      title: t('app.dashboard.guide.states.processing.title'),
      body: t('app.dashboard.guide.states.processing.body'),
      actions: [
        { type: 'workspaceStudio', label: t('app.dashboard.guide.actions.viewProgress'), disabled: !vm.defaultWorkspace?.id },
      ],
    };
  }

  if (vm.latestCompletedSource && Number(vm.latestCompletedSource.questionsCount || 0) === 0) {
    return {
      tone: 'ready',
      badge: t('app.dashboard.guide.badges.analysisReady'),
      title: t('app.dashboard.guide.states.noQuestions.title'),
      body: t('app.dashboard.guide.states.noQuestions.body'),
      actions: [
        { type: 'workspaceStudio', label: t('app.dashboard.guide.actions.createQuestions'), disabled: !vm.defaultWorkspace?.id },
      ],
    };
  }

  if (vm.studyReadySource) {
    const actions = [
      { type: 'quiz', documentId: vm.studyReadySource.id, label: t('app.dashboard.guide.actions.openQuiz') },
      { type: 'flashcards', documentId: vm.studyReadySource.id, label: t('app.dashboard.guide.actions.openFlashcards'), secondary: true },
      { type: 'streak', documentId: vm.studyReadySource.id, label: t('app.dashboard.guide.actions.openStreak'), secondary: true },
      { type: 'workspaceStudio', label: t('app.dashboard.guide.actions.openStudio'), secondary: true, disabled: !vm.defaultWorkspace?.id },
    ];

    if (vm.workspaceNeedsDeck) {
      return {
        tone: 'highlight',
        badge: t('app.dashboard.guide.badges.slideReady'),
        title: t('app.dashboard.guide.states.noDeck.title'),
        body: t('app.dashboard.guide.states.noDeck.body'),
        actions: [
          { type: 'workspaceStudio', label: t('app.dashboard.guide.actions.createDeck'), disabled: !vm.defaultWorkspace?.id },
          { type: 'quiz', documentId: vm.studyReadySource.id, label: t('app.dashboard.guide.actions.openQuiz'), secondary: true },
        ],
      };
    }

    return {
      tone: 'success',
      badge: t('app.dashboard.guide.badges.studyReady'),
      title: t('app.dashboard.guide.states.studyReady.title'),
      body: t('app.dashboard.guide.states.studyReady.body'),
      actions,
    };
  }

  return {
    tone: 'neutral',
    badge: t('app.dashboard.guide.badges.workspace'),
    title: t('app.dashboard.guide.states.workspace.title'),
    body: t('app.dashboard.guide.states.workspace.body'),
    actions: [
      { type: 'workspaceStudio', label: t('app.dashboard.guide.actions.openStudio'), disabled: !vm.defaultWorkspace?.id },
    ],
  };
}

function getGuideAnswer(chip, vm, t) {
  const studyAction = vm.studyReadySource
    ? { type: 'quiz', documentId: vm.studyReadySource.id, label: t('app.dashboard.guide.actions.openQuiz') }
    : { type: 'workspaceStudio', label: t('app.dashboard.guide.actions.openStudio'), disabled: !vm.defaultWorkspace?.id };

  switch (chip) {
    case 'howToUse':
      return {
        title: t('app.dashboard.guide.answers.howToUse.title'),
        body: t('app.dashboard.guide.answers.howToUse.body'),
        actions: [
          { type: 'upload', label: t('app.dashboard.actions.upload') },
          { type: 'workspaceStudio', label: t('app.dashboard.actions.openWorkspace'), secondary: true, disabled: !vm.defaultWorkspace?.id },
        ],
      };
    case 'createQuestions':
      return {
        title: t('app.dashboard.guide.answers.createQuestions.title'),
        body: t('app.dashboard.guide.answers.createQuestions.body'),
        actions: [
          { type: 'workspaceStudio', label: t('app.dashboard.guide.actions.createQuestions'), disabled: !vm.defaultWorkspace?.id },
        ],
      };
    case 'createSlides':
      return {
        title: t('app.dashboard.guide.answers.createSlides.title'),
        body: t('app.dashboard.guide.answers.createSlides.body'),
        actions: [
          { type: 'workspaceStudio', label: t('app.dashboard.actions.createSlides'), disabled: !vm.defaultWorkspace?.id },
        ],
      };
    case 'whatNext':
    default:
      return {
        title: t('app.dashboard.guide.answers.whatNext.title'),
        body: t('app.dashboard.guide.answers.whatNext.body'),
        actions: [
          studyAction,
        ],
      };
  }
}

function buildShortcutCards(vm, t) {
  return [
    {
      key: 'recentFiles',
      icon: '01',
      title: t('app.dashboard.shortcuts.recentFiles.title'),
      body: t('app.dashboard.shortcuts.recentFiles.body', { count: vm.sourceCount }),
      pill: vm.latestSource ? vm.latestSource.fileName : t('app.dashboard.shortcuts.empty'),
    },
    {
      key: 'continueLearning',
      icon: '02',
      title: t('app.dashboard.shortcuts.continueLearning.title'),
      body: vm.studyReadySource
        ? t('app.dashboard.shortcuts.continueLearning.ready')
        : t('app.dashboard.shortcuts.continueLearning.pending'),
      cta: vm.studyReadySource
        ? { type: 'quiz', documentId: vm.studyReadySource.id, label: t('app.dashboard.shortcuts.continueLearning.cta') }
        : { type: 'workspaceStudio', label: t('app.dashboard.shortcuts.continueLearning.fallback'), disabled: !vm.defaultWorkspace?.id },
      disabled: false,
    },
    {
      key: 'activity',
      icon: '03',
      title: t('app.dashboard.shortcuts.activity.title'),
      body: vm.latestSource
        ? t('app.dashboard.shortcuts.activity.body')
        : t('app.dashboard.shortcuts.activity.empty'),
      pill: vm.latestSource ? t(`app.dashboard.status.${normalizeSourceStatus(vm.latestSource.status)}`) : t('app.dashboard.shortcuts.empty'),
    },
    {
      key: 'systemStatus',
      icon: '04',
      title: t('app.dashboard.shortcuts.systemStatus.title'),
      body: vm.processingSource
        ? t('app.dashboard.shortcuts.systemStatus.processing')
        : t('app.dashboard.shortcuts.systemStatus.ready'),
      pill: vm.processingSource ? t('app.dashboard.guide.badges.processing') : t('app.dashboard.guide.badges.workspace'),
    },
  ];
}

function buildPipelineSteps(vm, t) {
  return PIPELINE_STEPS.map((key, index) => {
    const step = getPipelineStepVm(key, vm, t);
    return {
      ...step,
      key,
      index: index + 1,
    };
  });
}

function getPipelineStepVm(key, vm, t) {
  switch (key) {
    case 'upload':
      return vm.hasSource
        ? { title: t('app.dashboard.pipeline.steps.upload.title'), body: t('app.dashboard.pipeline.steps.upload.complete'), label: t('app.dashboard.pipeline.labels.complete'), state: 'complete' }
        : { title: t('app.dashboard.pipeline.steps.upload.title'), body: t('app.dashboard.pipeline.steps.upload.pending'), label: t('app.dashboard.pipeline.labels.pending'), state: 'pending' };
    case 'ocr':
      return vm.hasCompletedSource
        ? { title: t('app.dashboard.pipeline.steps.ocr.title'), body: t('app.dashboard.pipeline.steps.ocr.complete'), label: t('app.dashboard.pipeline.labels.complete'), state: 'complete' }
        : vm.hasSource
          ? { title: t('app.dashboard.pipeline.steps.ocr.title'), body: t('app.dashboard.pipeline.steps.ocr.active'), label: t('app.dashboard.pipeline.labels.active'), state: 'active' }
          : { title: t('app.dashboard.pipeline.steps.ocr.title'), body: t('app.dashboard.pipeline.steps.ocr.pending'), label: t('app.dashboard.pipeline.labels.pending'), state: 'pending' };
    case 'analysis':
      return vm.hasCompletedSource
        ? { title: t('app.dashboard.pipeline.steps.analysis.title'), body: t('app.dashboard.pipeline.steps.analysis.complete'), label: t('app.dashboard.pipeline.labels.complete'), state: 'complete' }
        : vm.hasSource
          ? { title: t('app.dashboard.pipeline.steps.analysis.title'), body: t('app.dashboard.pipeline.steps.analysis.active'), label: t('app.dashboard.pipeline.labels.active'), state: 'active' }
          : { title: t('app.dashboard.pipeline.steps.analysis.title'), body: t('app.dashboard.pipeline.steps.analysis.pending'), label: t('app.dashboard.pipeline.labels.pending'), state: 'pending' };
    case 'questions':
      return vm.studyReadySource
        ? { title: t('app.dashboard.pipeline.steps.questions.title'), body: t('app.dashboard.pipeline.steps.questions.complete'), label: t('app.dashboard.pipeline.labels.complete'), state: 'complete' }
        : vm.hasCompletedSource
          ? { title: t('app.dashboard.pipeline.steps.questions.title'), body: t('app.dashboard.pipeline.steps.questions.active'), label: t('app.dashboard.pipeline.labels.active'), state: 'active' }
          : { title: t('app.dashboard.pipeline.steps.questions.title'), body: t('app.dashboard.pipeline.steps.questions.pending'), label: t('app.dashboard.pipeline.labels.pending'), state: 'pending' };
    case 'slides':
    default:
      return vm.workspaceHasDeck
        ? { title: t('app.dashboard.pipeline.steps.slides.title'), body: t('app.dashboard.pipeline.steps.slides.complete'), label: t('app.dashboard.pipeline.labels.complete'), state: 'complete' }
        : vm.hasCompletedSource
          ? { title: t('app.dashboard.pipeline.steps.slides.title'), body: t('app.dashboard.pipeline.steps.slides.active'), label: t('app.dashboard.pipeline.labels.active'), state: 'active' }
          : { title: t('app.dashboard.pipeline.steps.slides.title'), body: t('app.dashboard.pipeline.steps.slides.pending'), label: t('app.dashboard.pipeline.labels.pending'), state: 'pending' };
  }
}

function getNextBestAction(vm, t) {
  if (!vm.hasSource) {
    return {
      eyebrow: t('app.dashboard.nextAction.eyebrow'),
      title: t('app.dashboard.nextAction.states.noSource.title'),
      body: t('app.dashboard.nextAction.states.noSource.body'),
      action: { type: 'upload', label: t('app.dashboard.actions.upload') },
      secondaryAction: { type: 'workspaces', label: t('app.dashboard.actions.openWorkspace'), secondary: true },
    };
  }

  if (vm.processingSource && !vm.hasCompletedSource) {
    return {
      eyebrow: t('app.dashboard.nextAction.eyebrow'),
      title: t('app.dashboard.nextAction.states.processing.title'),
      body: t('app.dashboard.nextAction.states.processing.body'),
      action: { type: 'workspaceStudio', label: t('app.dashboard.guide.actions.viewProgress'), disabled: !vm.defaultWorkspace?.id },
    };
  }

  if (vm.latestCompletedSource && Number(vm.latestCompletedSource.questionsCount || 0) === 0) {
    return {
      eyebrow: t('app.dashboard.nextAction.eyebrow'),
      title: t('app.dashboard.nextAction.states.noQuestions.title'),
      body: t('app.dashboard.nextAction.states.noQuestions.body'),
      action: { type: 'workspaceStudio', label: t('app.dashboard.guide.actions.createQuestions'), disabled: !vm.defaultWorkspace?.id },
    };
  }

  if (vm.studyReadySource && !vm.workspaceHasDeck) {
    return {
      eyebrow: t('app.dashboard.nextAction.eyebrow'),
      title: t('app.dashboard.nextAction.states.noDeck.title'),
      body: t('app.dashboard.nextAction.states.noDeck.body'),
      action: { type: 'workspaceStudio', label: t('app.dashboard.guide.actions.createDeck'), disabled: !vm.defaultWorkspace?.id },
      secondaryAction: { type: 'quiz', documentId: vm.studyReadySource.id, label: t('app.dashboard.guide.actions.openQuiz') },
    };
  }

  if (vm.studyReadySource) {
    return {
      eyebrow: t('app.dashboard.nextAction.eyebrow'),
      title: t('app.dashboard.nextAction.states.studyReady.title'),
      body: t('app.dashboard.nextAction.states.studyReady.body'),
      action: { type: 'quiz', documentId: vm.studyReadySource.id, label: t('app.dashboard.guide.actions.openQuiz') },
      secondaryAction: { type: 'flashcards', documentId: vm.studyReadySource.id, label: t('app.dashboard.guide.actions.openFlashcards'), secondary: true },
    };
  }

  return {
    eyebrow: t('app.dashboard.nextAction.eyebrow'),
    title: t('app.dashboard.nextAction.states.workspace.title'),
    body: t('app.dashboard.nextAction.states.workspace.body'),
    action: { type: 'workspaceStudio', label: t('app.dashboard.guide.actions.openStudio'), disabled: !vm.defaultWorkspace?.id },
  };
}

function buildChecklistItems(vm, t) {
  return CHECKLIST_STEPS.map((key) => {
    const state = getChecklistState(key, vm);
    return {
      key,
      title: t(`app.dashboard.checklist.items.${key}.title`),
      body: t(`app.dashboard.checklist.items.${key}.${state}`),
      state,
    };
  });
}

function getChecklistState(key, vm) {
  switch (key) {
    case 'upload':
      return vm.hasSource ? 'complete' : 'pending';
    case 'analysis':
      return vm.hasCompletedSource ? 'complete' : vm.hasSource ? 'active' : 'pending';
    case 'questions':
      return vm.studyReadySource ? 'complete' : vm.hasCompletedSource ? 'active' : 'pending';
    case 'study':
      return vm.studyReadySource ? 'complete' : 'pending';
    case 'deck':
      return vm.workspaceHasDeck ? 'complete' : vm.hasCompletedSource ? 'active' : 'pending';
    case 'preview':
      return vm.workspaceHasDeck ? 'complete' : 'pending';
    default:
      return 'pending';
  }
}

function getSourcePrimaryAction(source, vm, t) {
  if (isStudyReadySource(source)) {
    return {
      action: { type: 'quiz', documentId: source.id, label: t('app.dashboard.sourceActions.continueLearning') },
      helper: t('app.dashboard.sourceHelpers.studyReady'),
    };
  }

  if (isCompletedSource(source)) {
    return {
      action: { type: 'workspaceStudio', label: t('app.dashboard.sourceActions.generateQuestions'), disabled: !vm.defaultWorkspace?.id },
      helper: t('app.dashboard.sourceHelpers.needsQuestions'),
    };
  }

  return {
    action: { type: 'workspaceStudio', label: t('app.dashboard.sourceActions.viewProgress'), disabled: !vm.defaultWorkspace?.id },
    helper: t('app.dashboard.sourceHelpers.processing'),
  };
}

function getWorkspaceDeckHint(vm, t) {
  if (!vm.workspaceHasDeck) {
    return t('app.dashboard.deck.none');
  }

  if (vm.workspaceDeckStale) {
    return t('app.dashboard.deck.stale');
  }

  return t('app.dashboard.deck.ready');
}

function sortSourcesByRecency(sources) {
  return [...sources].sort((left, right) => {
    const rightTime = new Date(right.updatedAt || right.createdAt || 0).getTime();
    const leftTime = new Date(left.updatedAt || left.createdAt || 0).getTime();
    return rightTime - leftTime;
  });
}

function isCompletedSource(source) {
  return source?.status === 3 || String(source?.status) === 'Completed';
}

function isStudyReadySource(source) {
  return isCompletedSource(source) && Number(source?.questionsCount || 0) > 0;
}

function normalizeSourceStatus(status) {
  if (status === 0 || String(status) === 'Uploaded') {
    return 'uploaded';
  }

  if (status === 1 || String(status) === 'Extracting') {
    return 'extracting';
  }

  if (status === 2 || String(status) === 'Analyzing') {
    return 'analyzing';
  }

  if (status === 3 || String(status) === 'Completed') {
    return 'completed';
  }

  if (status === 4 || String(status) === 'Failed') {
    return 'failed';
  }

  return 'unknown';
}

function formatRelativeTime(value, t, language) {
  if (!value) {
    return '-';
  }

  const diffMs = Date.now() - new Date(value).getTime();
  if (diffMs < 60_000) {
    return t('workspaces.relativeTime.justNow');
  }
  if (diffMs < 3_600_000) {
    return t('workspaces.relativeTime.minutesAgo', { count: Math.max(1, Math.floor(diffMs / 60_000)) });
  }
  if (diffMs < 86_400_000) {
    return t('workspaces.relativeTime.hoursAgo', { count: Math.max(1, Math.floor(diffMs / 3_600_000)) });
  }

  return new Date(value).toLocaleString(language === 'vi' ? 'vi-VN' : 'en-US');
}

export default App;
