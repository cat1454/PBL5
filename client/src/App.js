import React, { useState } from 'react';
import { BrowserRouter as Router, NavLink, Route, Routes, useLocation } from 'react-router-dom';
import './App.css';
import AnalysisContent from './components/AnalysisContent';
import DocumentList from './components/DocumentList';
import DocumentUpload from './components/DocumentUpload';
import FlashcardGame from './components/FlashcardGame';
import FolderProjects from './components/FolderProjects';
import FolderStudio from './components/FolderStudio';
import QuizGame from './components/QuizGame';
import SlideStudio from './components/SlideStudio';

function App() {
  const [currentUser] = useState({
    name: 'Tran Hong Thao',
    role: 'Teaching workspace',
    avatar: null,
  });

  return (
    <Router>
      <AppShell user={currentUser} />
    </Router>
  );
}

function AppShell({ user }) {
  const location = useLocation();
  const [currentFile, setCurrentFile] = useState(null);
  const isHybridRoute = location.pathname.startsWith('/documents') || location.pathname.startsWith('/folders');
  const isStudioRoute = location.pathname.startsWith('/slides/') || location.pathname.startsWith('/folders/');

  return (
    <div className={`App${isHybridRoute ? ' app-shell-documents' : ''}`}>
      <header className="App-header app-shell-header">
        <div className="container app-shell-header-inner">
          <div className="app-shell-brand">
            <div className="app-shell-brand-mark">AI</div>
            <div className="app-shell-brand-copy">
              <strong>AI Teaching</strong>
            </div>
          </div>

          <div className="app-shell-user">
            <div className="app-shell-user-meta">
              <span className="user-name">{user.name}</span>
              <span>{user.role}</span>
            </div>
            <div className="app-shell-user-avatar">
              {user.avatar ? <img src={user.avatar} alt="avatar" /> : user.name.charAt(0)}
            </div>
            <button type="button" className="button button-secondary">Logout</button>
          </div>
        </div>
      </header>

      <div className="app-shell-body">
        <aside className="app-sidebar">
          <div className="app-sidebar-inner">
            <nav className="app-sidebar-nav">
              <NavLink to="/" end className={({ isActive }) => `app-sidebar-link${isActive ? ' active' : ''}`}>
                <span className="sidebar-emoji">Dashboard</span>
              </NavLink>

              <NavLink to="/documents" className={({ isActive }) => `app-sidebar-link${isActive ? ' active' : ''}`}>
                <span className="sidebar-emoji">Documents</span>
              </NavLink>

              <NavLink to="/folders" className={({ isActive }) => `app-sidebar-link${isActive ? ' active' : ''}`}>
                <span className="sidebar-emoji">Folders</span>
              </NavLink>

              <NavLink to="/settings" className={({ isActive }) => `app-sidebar-link${isActive ? ' active' : ''}`}>
                <span className="sidebar-emoji">Settings</span>
              </NavLink>
            </nav>
          </div>
        </aside>

        <main className="app-shell-content">
          <div className="container app-shell-content-inner">
            {!isStudioRoute && (
              <div className="app-page-header">
                <h1>{getPageTitle(location.pathname)}</h1>
              </div>
            )}

            <Routes>
              <Route path="/" element={<DashboardPage currentFile={currentFile} setCurrentFile={setCurrentFile} />} />
              <Route path="/documents" element={<DocumentList />} />
              <Route path="/folders" element={<FolderProjects />} />
              <Route path="/folders/:folderId/studio" element={<FolderStudio />} />
              <Route path="/settings" element={<SettingsPage />} />
              <Route path="/quiz/:documentId" element={<QuizGame />} />
              <Route path="/flashcards/:documentId" element={<FlashcardGame />} />
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

  if (!currentFile) {
    return (
      <div className="workspace-clean-start">
        <div className="workspace-clean-start-orb" aria-hidden="true"></div>
        <section className="upload-minimal-box">
          <div className="workspace-clean-start-copy">
            <span className="workspace-clean-start-kicker">Workspace init</span>
            <h2>Upload tai lieu de bat dau mot workspace tap trung va toi gian.</h2>
            <p>
              Khung nay duoc toi uu de nguoi dung vao thang hanh dong chinh:
              dua document vao pipeline OCR, AI analysis, quiz, flashcards va slide.
            </p>
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
        DASHBOARD <span className="file-name">[{currentFile.fileName}]</span>
      </div>

      <div className="workspace-toolbar">
        <button className={activeTab === 'file' ? 'active' : ''} onClick={() => setActiveTab('file')}>View Analysis</button>
        <button className={activeTab === 'slide' ? 'active' : ''} onClick={() => setActiveTab('slide')}>Slide</button>
        <button className={activeTab === 'quiz' ? 'active' : ''} onClick={() => setActiveTab('quiz')}>Quiz</button>
        <button className={activeTab === 'flash' ? 'active' : ''} onClick={() => setActiveTab('flash')}>Flashcard</button>
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
  return (
    <div className="card">
      <h2>Settings</h2>
      <p className="section-subtitle">Placeholder cho cau hinh tai khoan va giao dien. Chua co logic backend rieng.</p>
    </div>
  );
}

function getPageTitle(pathname) {
  if (pathname.startsWith('/documents')) {
    return 'My Documents';
  }

  if (pathname.startsWith('/folders/')) {
    return 'Folder Studio';
  }

  if (pathname.startsWith('/folders')) {
    return 'Folder Projects';
  }

  if (pathname.startsWith('/settings')) {
    return 'Settings';
  }

  if (pathname.startsWith('/quiz/')) {
    return 'Quiz';
  }

  if (pathname.startsWith('/flashcards/')) {
    return 'Flashcards';
  }

  if (pathname.startsWith('/slides/')) {
    return 'Slide Studio';
  }

  return 'Dashboard';
}

export default App;
