import React, { useState } from 'react';
import { BrowserRouter as Router, Routes, Route, NavLink, useLocation } from 'react-router-dom';
import './App.css';
import DocumentUpload from './components/DocumentUpload';
import DocumentList from './components/DocumentList';
import QuizGame from './components/QuizGame';
import FlashcardGame from './components/FlashcardGame';
import SlideStudio from './components/SlideStudio';
import AnalysisContent from './components/AnalysisContent';


function App() {
  const [currentUser, setCurrentUser] = useState({
    name: "Tran Hong Thao",
    role: "Teaching workspace",
    avatar: null
  });

  return (
    <Router>
      {/* Truyền dữ liệu user vào AppShell */}
      <AppShell user={currentUser} />
    </Router>
  );
}

function AppShell({ user }) {
  const location = useLocation();
  const [currentFile, setCurrentFile] = useState(null);
  return (
    <div className="App">
      <header className="App-header app-shell-header">
        <div className="container app-shell-header-inner">
          <div className="app-shell-brand">
            <div className="app-shell-brand-mark">AI</div>
            <div className="app-shell-brand-copy">
              <strong>AI Teaching</strong>
              {/* <span>{getPageSubtitle(location.pathname)}</span> */}
            </div>
          </div>

          <div className="app-shell-user">
            <div className="app-shell-user-meta">
              <span className="user-name">{user.name}</span>
              <span>Teaching workspace</span>
            </div>
            <div className="app-shell-user-avatar">
              {user.avatar ? <img src={user.avatar} alt="avt" /> : user.name.charAt(0)}
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
                <span className="sidebar-emoji">📊</span> Dashboard
              </NavLink>

              <NavLink to="/documents" className={({ isActive }) => `app-sidebar-link${isActive ? ' active' : ''}`}>
                <span className="sidebar-emoji">📁</span> My Documents
              </NavLink>

              <NavLink to="/settings" className={({ isActive }) => `app-sidebar-link${isActive ? ' active' : ''}`}>
                <span className="sidebar-emoji">⚙️</span> Settings
              </NavLink>
            </nav>
          </div>
        </aside>

        <main className="app-shell-content">
          <div className="container app-shell-content-inner">
            <div className="app-page-header">
              <h1>{getPageTitle(location.pathname)}</h1>

            </div>

            <Routes>
              <Route
                path="/"
                element={<DashboardPage currentFile={currentFile} setCurrentFile={setCurrentFile} />}
              />

              <Route path="/documents" element={<DocumentList />} />
              <Route path="/settings" element={<SettingsPage />} />
              {/* <Route path="/" element={<DashboardPage />} /> */}
              <Route path="/documents" element={<DocumentList />} />
              <Route path="/settings" element={<SettingsPage />} />
              <Route path="/quiz/:documentId" element={<QuizGame />} />
              <Route path="/flashcards/:documentId" element={<FlashcardGame />} />
              <Route path="/slides/:documentId" element={<SlideStudio />} />
            </Routes>
          </div>
        </main>
      </div>

      {/* <footer className="App-footer">
        <div className="container">
          <p>&copy; 2026 AI Teaching Assistant - Transform documents into interactive learning experiences</p>
        </div>
      </footer> */}
    </div>
  );
}

function DashboardPage({ currentFile, setCurrentFile }) {
  const [activeTab, setActiveTab] = useState('file');

  // GIAI ĐOẠN 1: CHƯA CÓ FILE - Chỉ hiện đúng cái khung chọn tệp
  if (!currentFile) {
    return (
      <div className="workspace-clean-start">
        <div className="upload-minimal-box">
          <DocumentUpload onUploadSuccess={(data) => setCurrentFile(data)} />
        </div>
      </div>
    );
  }

  // GIAI ĐOẠN 2: ĐÃ CÓ FILE - Hiện Workspace 3 tầng như ông muốn
  return (
    <div className="workspace-container">
      {/* Tầng 1: Đường dẫn (Breadcrumb) - Thay cho cái chữ Dashboard cũ */}
      <div className="workspace-breadcrumb">
        DASHBOARD <span className="file-name">[{currentFile.fileName}]</span>
      </div>

      {/* Tầng 2: Thanh Tab điều hướng tính năng */}
      <div className="workspace-toolbar">
        <button className={activeTab === 'file' ? 'active' : ''} onClick={() => setActiveTab('file')}>🔍 View Analysis</button>
        <button className={activeTab === 'slide' ? 'active' : ''} onClick={() => setActiveTab('slide')}>🎦 Slide</button>
        <button className={activeTab === 'quiz' ? 'active' : ''} onClick={() => setActiveTab('quiz')}>📝 Quiz</button>
        <button className={activeTab === 'flash' ? 'active' : ''} onClick={() => setActiveTab('flash')}>🃏 Flashcard</button>
      </div>

      {/* Tầng 3: Frame nội dung chính */}
      <div className="workspace-main-frame">

        {/* SỬA CHỖ NÀY: Gọi AnalysisContent để nó hiện bảng tím thay vì mấy dòng chữ test */}
        {activeTab === 'file' && <AnalysisContent data={currentFile} />}

        {/* 2. Các tab Game giữ nguyên để truyền ID vào */}
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

function getPageSubtitle(pathname) {
  if (pathname.startsWith('/documents')) {
    return 'Track documents and generated content';
  }

  if (pathname.startsWith('/settings')) {
    return 'Workspace settings';
  }

  if (pathname.startsWith('/quiz/')) {
    return 'Quiz practice';
  }

  if (pathname.startsWith('/flashcards/')) {
    return 'Flashcard review';
  }

  if (pathname.startsWith('/slides/')) {
    return 'AI slide workspace';
  }

  return 'Upload your document and create teaching content with AI';
}

function getPageDescription(pathname) {
  if (pathname.startsWith('/documents')) {
    return 'View uploaded files, progress, and actions for each document.';
  }

  if (pathname.startsWith('/settings')) {
    return 'Manage workspace preferences and account placeholders.';
  }

  if (pathname.startsWith('/quiz/')) {
    return 'Practice with AI-generated quiz questions from your document.';
  }

  if (pathname.startsWith('/flashcards/')) {
    return 'Review core ideas and explanations with flashcards.';
  }

  if (pathname.startsWith('/slides/')) {
    return 'Generate, preview, and edit presentation slides from your content.';
  }

  return 'Upload your document and create teaching content with AI.';
}

export default App;