import React from 'react';
import { Link } from 'react-router-dom';
import DocumentUpload from './DocumentUpload';

function HomePage() {
  return (
    <div className="home">
      <section className="card home-hero-card">
        <div className="home-hero-layout">
          <div className="home-hero-copy">
            <span className="home-kicker">Main Workflow</span>
            <h2>Upload tai lieu mot lan, hoc va trinh bay tren cung mot luong.</h2>
            <p className="home-hero-text">
              Trang chu nen dong vai tro diem vao chinh cua he thong: dua document vao AI pipeline,
              theo doi xu ly, sau do chuyen sang Quiz, Flashcards, hoac Slide Studio tu dashboard.
            </p>

            <div className="home-hero-actions">
              <a href="#upload-document" className="button">Upload Document</a>
              <Link to="/documents" className="button button-secondary">Open My Documents</Link>
            </div>
          </div>

          <div className="home-hero-panel">
            <h3>What happens next?</h3>
            <div className="hero-flow-list">
              <div className="hero-flow-item">
                <span>01</span>
                <div>
                  <strong>Upload</strong>
                  <p>PDF, DOCX, PNG, JPG vao he thong.</p>
                </div>
              </div>
              <div className="hero-flow-item">
                <span>02</span>
                <div>
                  <strong>Process</strong>
                  <p>OCR, extraction, summary, topics, va progress realtime.</p>
                </div>
              </div>
              <div className="hero-flow-item">
                <span>03</span>
                <div>
                  <strong>Study</strong>
                  <p>Tao question set de hoc bang Quiz va Flashcards.</p>
                </div>
              </div>
              <div className="hero-flow-item">
                <span>04</span>
                <div>
                  <strong>Present</strong>
                  <p>Tao slide deck va mo Slide Studio de chinh sua.</p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section className="features home-features-grid">
        <article className="feature">
          <h3>Document Analysis</h3>
          <p>OCR, extraction, summary, main topics, va key points cho tung tai lieu.</p>
        </article>
        <article className="feature">
          <h3>Realtime Progress</h3>
          <p>Theo doi document progress, question generation, va slide generation trong mot luong ro rang.</p>
        </article>
        <article className="feature">
          <h3>Quiz and Flashcards</h3>
          <p>Chuyen document da xu ly thanh bo cau hoi de on tap nhanh va co tinh tuong tac.</p>
        </article>
        <article className="feature">
          <h3>Slide Studio</h3>
          <p>Tao slide deck tu tai lieu va chinh sua noi dung ngay tren web.</p>
        </article>
      </section>

      <section className="home-workspace-grid">
        <div id="upload-document">
          <DocumentUpload />
        </div>

        <aside className="card home-side-panel">
          <h3>Trang chu chi nen co mot diem vao chinh</h3>
          <p className="section-subtitle">
            O day, hanh dong chinh la upload document. Sau khi tai lieu da vao he thong,
            nguoi dung se tiep tuc o My Documents de xem progress va mo cac tinh nang phia sau.
          </p>

          <div className="home-side-block">
            <strong>Dung My Documents de:</strong>
            <ul className="home-side-list">
              <li>Xem document dang xu ly</li>
              <li>Tao bo cau hoi moi</li>
              <li>Mo Quiz, Flashcards, hoac Slide Studio</li>
              <li>Theo doi progress va ket qua da tao</li>
            </ul>
          </div>

          <Link to="/documents" className="button home-side-cta">Go to My Documents</Link>
        </aside>
      </section>
    </div>
  );
}

export default HomePage;
