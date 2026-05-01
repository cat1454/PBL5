import React from 'react';
import { Link } from 'react-router-dom';
import DocumentUpload from './DocumentUpload';

function HomePage() {
  return (
    <div className="home">
      <section className="card home-hero-card">
        <div className="home-hero-layout">
          <div className="home-hero-copy">
            <span className="home-kicker">Luồng chính</span>
            <h2>Tải tài liệu một lần, học và trình bày trong cùng một luồng.</h2>
            <p className="home-hero-text">
              Trang chủ đóng vai trò là điểm vào chính của hệ thống: đưa tài liệu vào pipeline AI,
              theo dõi xử lý, sau đó chuyển sang Quiz, Flashcards hoặc Xưởng slide từ bảng điều khiển.
            </p>

            <div className="home-hero-actions">
              <a href="#upload-document" className="button">Tải tài liệu lên</a>
              <Link to="/workspaces" className="button button-secondary">Mở không gian làm việc</Link>
            </div>
          </div>

          <div className="home-hero-panel">
            <h3>Tiếp theo sẽ có gì?</h3>
            <div className="hero-flow-list">
              <div className="hero-flow-item">
                <span>01</span>
                <div>
                  <strong>Tải lên</strong>
                  <p>Đưa PDF, DOCX, PNG, JPG vào hệ thống.</p>
                </div>
              </div>
              <div className="hero-flow-item">
                <span>02</span>
                <div>
                  <strong>Xử lý</strong>
                  <p>OCR, trích xuất, tóm tắt, chủ đề và tiến trình thời gian thực.</p>
                </div>
              </div>
              <div className="hero-flow-item">
                <span>03</span>
                <div>
                  <strong>Học tập</strong>
                  <p>Tạo bộ câu hỏi để học bằng Quiz và Flashcards.</p>
                </div>
              </div>
              <div className="hero-flow-item">
                <span>04</span>
                <div>
                  <strong>Trình bày</strong>
                  <p>Tạo bộ slide và mở Xưởng slide để chỉnh sửa.</p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section className="features home-features-grid">
        <article className="feature">
          <h3>Phân tích tài liệu</h3>
          <p>OCR, trích xuất, tóm tắt, chủ đề chính và ý quan trọng cho từng tài liệu.</p>
        </article>
        <article className="feature">
          <h3>Tiến trình thời gian thực</h3>
          <p>Theo dõi tiến trình tài liệu, tạo câu hỏi và tạo slide trong một luồng rõ ràng.</p>
        </article>
        <article className="feature">
          <h3>Quiz và Flashcards</h3>
          <p>Chuyển tài liệu đã xử lý thành bộ câu hỏi để ôn tập nhanh và có tính tương tác.</p>
        </article>
        <article className="feature">
          <h3>Xưởng slide</h3>
          <p>Tạo bộ slide từ tài liệu và chỉnh sửa nội dung ngay trên web.</p>
        </article>
      </section>

      <section className="home-workspace-grid">
        <div id="upload-document">
          <DocumentUpload />
        </div>

        <aside className="card home-side-panel">
          <h3>Trang chủ chỉ nên có một điểm vào chính</h3>
          <p className="section-subtitle">
            Ở đây, hành động chính là tải tài liệu lên. Sau khi tài liệu đã vào hệ thống,
            người dùng sẽ tiếp tục ở Không gian làm việc để xem tiến trình và mở các tính năng phía sau.
          </p>

          <div className="home-side-block">
            <strong>Dùng Không gian làm việc để:</strong>
            <ul className="home-side-list">
              <li>Xem tài liệu đang xử lý</li>
              <li>Tạo bộ câu hỏi mới</li>
              <li>Mở Quiz, Flashcards hoặc Xưởng slide</li>
              <li>Theo dõi tiến trình và kết quả đã tạo</li>
            </ul>
          </div>

          <Link to="/workspaces" className="button home-side-cta">Đi đến không gian làm việc</Link>
        </aside>
      </section>
    </div>
  );
}

export default HomePage;
