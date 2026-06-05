import React from 'react';
import { Navigate, useParams } from 'react-router-dom';
import AdminPage from '../components/AdminPage';
import FlashcardGame from '../components/FlashcardGame';
import FolderProjects from '../components/FolderProjects';
import FolderStudio from '../components/FolderStudio';
import PersonalAnalyticsDashboard from '../components/PersonalAnalyticsDashboard';
import QuestionStudioPage from '../components/question-studio/QuestionStudioPage';
import QuizGame from '../components/QuizGame';
import StreakGame from '../components/StreakGame';
import StudyHub from '../components/StudyHub';
import AdminRoute from '../components/auth/AdminRoute';
import DashboardPage from '../features/dashboard/DashboardPage';
import { useLanguage } from '../context/LanguageContext';

export const NAV_ITEMS = [
  {
    id: 'dashboard',
    to: '/',
    end: true,
    icon: 'home',
    labelKey: 'app.nav.dashboard',
  },
  {
    id: 'workspaces',
    to: '/workspaces',
    icon: 'workspaces',
    labelKey: 'app.nav.workspaces',
  },
  {
    id: 'analytics',
    to: '/analytics',
    icon: 'analytics',
    labelKey: 'app.nav.analytics',
  },
  {
    id: 'admin',
    to: '/admin',
    icon: 'admin',
    labelKey: 'app.nav.admin',
    role: 'ADMIN',
  },
];

export const HELP_NAV_ITEM = {
  id: 'help',
  icon: 'help',
  labelKey: 'app.nav.help',
};

export const PROTECTED_ROUTES = [
  {
    id: 'dashboard',
    path: '/',
    element: <DashboardPage />,
    titleKey: 'app.pageTitle.dashboard',
    frame: 'dashboard',
    surface: 'console',
    showPageHeader: false,
  },
  {
    id: 'documentsRedirect',
    path: '/documents',
    element: <Navigate to="/workspaces" replace />,
    titleKey: 'app.pageTitle.workspaces',
    frame: 'redirect',
    surface: 'workspace',
  },
  {
    id: 'foldersRedirect',
    path: '/folders',
    element: <Navigate to="/workspaces" replace />,
    titleKey: 'app.pageTitle.workspaces',
    frame: 'redirect',
    surface: 'workspace',
  },
  {
    id: 'legacyWorkspaceRedirect',
    path: '/folders/:folderId/studio',
    element: <LegacyWorkspaceRedirect />,
    titleKey: 'app.pageTitle.workspaceStudio',
    frame: 'redirect',
    surface: 'workspace',
  },
  {
    id: 'workspaces',
    path: '/workspaces',
    element: <FolderProjects />,
    titleKey: 'app.pageTitle.workspaces',
    frame: 'standard',
    surface: 'workspace',
  },
  {
    id: 'workspaceStudio',
    path: '/workspaces/:workspaceId',
    element: <FolderStudio />,
    titleKey: 'app.pageTitle.workspaceStudio',
    frame: 'studio',
    surface: 'workspace',
    showPageHeader: false,
  },
  {
    id: 'analytics',
    path: '/analytics',
    element: <PersonalAnalyticsDashboard />,
    titleKey: 'app.pageTitle.analytics',
    frame: 'standard',
    surface: 'analytics',
  },
  {
    id: 'documentsLegacyRedirect',
    path: '/documents-legacy',
    element: <Navigate to="/workspaces" replace />,
    titleKey: 'app.pageTitle.workspaces',
    frame: 'redirect',
    surface: 'workspace',
  },
  {
    id: 'settings',
    path: '/settings',
    element: <SettingsPage />,
    titleKey: 'app.pageTitle.settings',
    frame: 'standard',
    surface: 'settings',
  },
  {
    id: 'admin',
    path: '/admin',
    element: <AdminRoute><AdminPage /></AdminRoute>,
    titleKey: 'app.pageTitle.admin',
    frame: 'standard',
    surface: 'admin',
  },
  {
    id: 'study',
    path: '/study/:documentId',
    element: <StudyHub />,
    titleKey: 'app.pageTitle.studyHub',
    frame: 'studio',
    surface: 'study',
    showPageHeader: false,
  },
  {
    id: 'studyMode',
    path: '/study/:documentId/:mode',
    element: <StudyHub />,
    titleKey: 'app.pageTitle.studyHub',
    frame: 'studio',
    surface: 'study',
    showPageHeader: false,
  },
  {
    id: 'questionStudio',
    path: '/question-studio/:documentId',
    element: <QuestionStudioPage />,
    titleKey: 'app.pageTitle.questionStudio',
    frame: 'studio',
    surface: 'question-studio',
    showPageHeader: false,
  },
  {
    id: 'quiz',
    path: '/quiz/:documentId',
    element: <QuizGame />,
    titleKey: 'app.pageTitle.quiz',
    frame: 'standard',
    surface: 'study',
  },
  {
    id: 'flashcards',
    path: '/flashcards/:documentId',
    element: <FlashcardGame />,
    titleKey: 'app.pageTitle.flashcards',
    frame: 'standard',
    surface: 'study',
  },
  {
    id: 'streak',
    path: '/streak/:documentId',
    element: <StreakGame />,
    titleKey: 'app.pageTitle.streak',
    frame: 'standard',
    surface: 'study',
  },
];

const PATH_MATCHERS = [
  { test: (pathname) => pathname === '/', id: 'dashboard' },
  { test: (pathname) => pathname === '/folders' || pathname === '/documents' || pathname === '/documents-legacy', id: 'workspaces' },
  { test: (pathname) => pathname.startsWith('/folders/') && pathname.endsWith('/studio'), id: 'workspaceStudio' },
  { test: (pathname) => pathname === '/workspaces', id: 'workspaces' },
  { test: (pathname) => pathname.startsWith('/workspaces/'), id: 'workspaceStudio' },
  { test: (pathname) => pathname.startsWith('/analytics'), id: 'analytics' },
  { test: (pathname) => pathname.startsWith('/settings'), id: 'settings' },
  { test: (pathname) => pathname.startsWith('/admin'), id: 'admin' },
  { test: (pathname) => pathname.startsWith('/quiz/'), id: 'quiz' },
  { test: (pathname) => pathname.startsWith('/study/'), id: 'study' },
  { test: (pathname) => pathname.startsWith('/question-studio/'), id: 'questionStudio' },
  { test: (pathname) => pathname.startsWith('/flashcards/'), id: 'flashcards' },
  { test: (pathname) => pathname.startsWith('/streak/'), id: 'streak' },
];

export function getProtectedRoutes() {
  return PROTECTED_ROUTES;
}

export function getRouteMeta(pathname) {
  const match = PATH_MATCHERS.find((candidate) => candidate.test(pathname));
  return PROTECTED_ROUTES.find((route) => route.id === match?.id) || PROTECTED_ROUTES[0];
}

export function getShellClassNames(routeMeta, isMainMenuOpen) {
  return [
    'App',
    'app-shell',
    'v2-shell',
    'app-frame',
    `app-frame-${routeMeta.frame}`,
    `app-surface-${routeMeta.surface}`,
    routeMeta.surface === 'workspace' ? 'app-shell-documents' : '',
    isMainMenuOpen ? 'is-menu-open' : '',
  ].filter(Boolean).join(' ');
}

export function shouldShowPageHeader(routeMeta, pathname) {
  return pathname !== '/' && routeMeta.frame !== 'studio' && routeMeta.showPageHeader !== false;
}

function LegacyWorkspaceRedirect() {
  const { folderId } = useParams();
  return <Navigate to={`/workspaces/${folderId}`} replace />;
}

function SettingsPage() {
  const { t } = useLanguage();

  return (
    <section className="card app-settings-card">
      <h2>{t('app.settings.title')}</h2>
      <p className="section-subtitle">{t('app.settings.subtitle')}</p>
    </section>
  );
}
