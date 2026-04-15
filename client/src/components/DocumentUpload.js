import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { documentService } from '../services/api';

function DocumentUpload({ onUploadSuccess }) {
  const navigate = useNavigate();
  const [file, setFile] = useState(null);
  const [uploading, setUploading] = useState(false);
  const [uploadProgress, setUploadProgress] = useState(0);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');

  const handleFileChange = (e) => {
    const selectedFile = e.target.files[0];
    if (selectedFile) {
      const allowedTypes = ['application/pdf', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 'image/png', 'image/jpeg'];
      if (allowedTypes.includes(selectedFile.type)) {
        setFile(selectedFile);
        setError('');
        setMessage('');
      } else {
        setError('Chi ho tro PDF, DOCX, PNG va JPG. Hay chon dung dinh dang de AI xu ly chinh xac hon.');
        setFile(null);
      }
    }
  };

  const handleUpload = async (e) => {
    e.preventDefault();
    
    if (!file) {
      setError('Hay chon mot tai lieu truoc khi upload.');
      return;
    }

    setUploading(true);
    setUploadProgress(0);
    setMessage('');
    setError('');

    try {
      const userId = 'demo-user';
      const result = await documentService.uploadDocument(file, userId, (progress) => {
        setUploadProgress(progress);
      });
      
      setUploadProgress(100);

      // --- DÒNG QUAN TRỌNG NHẤT ÔNG CẦN THÊM Ở ĐÂY ---
      if (onUploadSuccess) {
        onUploadSuccess(result); // Báo cho App.js biết tên file để đổi giao diện
      }
      // ----------------------------------------------

      setMessage(`Đã upload xong...`);
      setFile(null);
      e.target.reset();
      
      setTimeout(() => {
        setMessage('');
        setUploadProgress(0);
        setUploading(false);
      }, 2000);

    } catch (err) {
      setError(err.response?.data?.message || 'Error uploading file.');
      setUploading(false);
    }};

  return (
    <div className="card">
      {/* <h2>📤 Upload Document</h2>
      <p className="section-subtitle">Upload tai lieu hoc tap de AI trich xuat noi dung, chia topic va tao bo cau hoi tu dong.</p> */}

      {/* <div className="tips-panel">
        <span className="mini-topic-tag">PDF</span>
        <span className="mini-topic-tag">DOCX</span>
        <span className="mini-topic-tag">PNG</span>
        <span className="mini-topic-tag">JPG</span>
        <p>Meo: tai lieu ro chu, co cau truc theo muc/chuong se cho bo cau hoi tot hon.</p>
      </div> */}
      
      {message && <div className="alert alert-success">{message}</div>}
      {error && <div className="alert alert-error">{error}</div>}
      
      <form onSubmit={handleUpload}>
        <div className="input-group">
          <label htmlFor="file-upload">📤 Upload Document</label>
          <input
            id="file-upload"
            type="file"
            onChange={handleFileChange}
            accept=".pdf,.docx,.png,.jpg,.jpeg"
            disabled={uploading}
          />
        </div>
        
        {file && (
          <div className="file-info-card">
            <p><strong>Da chon:</strong> {file.name}</p>
            <p><strong>Dung luong:</strong> {(file.size / 1024 / 1024).toFixed(2)} MB</p>
            <p><strong>Ky vong:</strong> Upload xong nhanh, phan tich va tao cau hoi co the mat 2-3 phut.</p>
          </div>
        )}
        
        {uploading && (
          <div className="progress-container">
            <div className="progress-bar">
              <div 
                className="progress-fill" 
                style={{ width: `${uploadProgress}%` }}
              >
                <span className="progress-text">{uploadProgress}%</span>
              </div>
            </div>
            <p className="progress-status">
              {uploadProgress < 100 ? 'Dang upload tai lieu...' : 'Upload xong. Vao My Documents de theo doi qua trinh AI xu ly.'}
            </p>
          </div>
        )}

        <div className="button-row">
          <button 
            type="submit" 
            className="button" 
            disabled={!file || uploading}
          >
            {uploading ? 'Dang upload...' : 'Upload va xu ly'}
          </button>
          <button
            type="button"
            className="button button-secondary"
            onClick={() => navigate('/documents')}
            disabled={uploading}
          >
            📚 Xem My Documents
          </button>
        </div>
      </form>
    </div>
  );
}

export default DocumentUpload;
