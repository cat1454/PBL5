import React from 'react';
import { BrowserRouter as Router, Routes, Route, Link } from 'react-router-dom';
import './App.css';
import DocumentUpload from './components/DocumentUpload';
import DocumentList from './components/DocumentList';
import QuizGame from './components/QuizGame';
import FlashcardGame from './components/FlashcardGame';

function App() {
  return (
    <Router>
      <div className="App">
        <header className="App-header">
          <div className="container">
            <h1>🎮 E-Learning Game Platform</h1>
            <nav className="nav">
              <Link to="/">Home</Link>
              <Link to="/documents">My Documents</Link>
            </nav>
          </div>
        </header>
        
        <main className="container">
          <Routes>
            <Route path="/" element={<Home />} />
            <Route path="/documents" element={<DocumentList />} />
            <Route path="/quiz/:documentId" element={<QuizGame />} />
            <Route path="/flashcards/:documentId" element={<FlashcardGame />} />
          </Routes>
        </main>

        <footer className="App-footer">
          <div className="container">
            <p>&copy; 2026 E-Learning Game Platform - Transform documents into interactive learning experiences</p>
          </div>
        </footer>
      </div>
    </Router>
  );
}

function Home() {
  return (
    <div className="home">
      <div className="card">
        <h2>Welcome to E-Learning Game Platform!</h2>
        <p>
          Transform your educational documents (PDF, DOCX, Images) into interactive learning games
          using AI-powered content analysis.
        </p>
        <div className="features">
          <div className="feature">
            <h3>📄 Upload Documents</h3>
            <p>Support for PDF (text & scanned), DOCX, PNG, JPG files</p>
          </div>
          <div className="feature">
            <h3>🤖 AI Analysis</h3>
            <p>Automatic content extraction and analysis using Local LLM</p>
          </div>
          <div className="feature">
            <h3>🎯 Generate Questions</h3>
            <p>Auto-generate quiz questions from your content</p>
          </div>
          <div className="feature">
            <h3>🎮 Play Games</h3>
            <p>Quiz, Flashcards, and Test games for better learning</p>
          </div>
        </div>
      </div>

      <DocumentUpload />
    </div>
  );
}

export default App;
