import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { documentService } from '../services/api';

function DocumentUpload({ onUploadSuccess, variant = 'default' }) {
  const navigate = useNavigate();
  const [file, setFile] = useState(null);
  const [uploading, setUploading] = useState(false);
  const [uploadProgress, setUploadProgress] = useState(0);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const isMinimalDark = variant === 'minimal-dark';

  const handleFileChange = (event) => {
    const selectedFile = event.target.files[0];
    if (!selectedFile) {
      return;
    }

    const allowedTypes = [
      'application/pdf',
      'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      'image/png',
      'image/jpeg',
    ];

    if (allowedTypes.includes(selectedFile.type)) {
      setFile(selectedFile);
      setError('');
      setMessage('');
      return;
    }

    setError('Chi ho tro PDF, DOCX, PNG va JPG. Hay chon dung dinh dang de AI xu ly chinh xac hon.');
    setFile(null);
  };

  const handleUpload = async (event) => {
    event.preventDefault();

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

      if (onUploadSuccess) {
        onUploadSuccess(result);
      }

      setMessage('Da upload xong.');
      setFile(null);
      event.target.reset();

      setTimeout(() => {
        setMessage('');
        setUploadProgress(0);
        setUploading(false);
      }, 2000);
    } catch (err) {
      setError(err.response?.data?.message || 'Error uploading file.');
      setUploading(false);
    }
  };

  return (
    <div className={`card document-upload-card${isMinimalDark ? ' document-upload-card-minimal-dark' : ''}`}>
      <div className="document-upload-head">
        <div>
          <span className="document-upload-kicker">Primary action</span>
          <h2>Upload document</h2>
        </div>
        <div className="document-upload-types">
          <span>PDF</span>
          <span>DOCX</span>
          <span>PNG</span>
          <span>JPG</span>
        </div>
      </div>

      <p className="section-subtitle document-upload-subtitle">
        Dua tai lieu vao he thong de AI trich xuat noi dung, tom tat va mo cac che do hoc tap tu dong.
      </p>

      {message && <div className="alert alert-success">{message}</div>}
      {error && <div className="alert alert-error">{error}</div>}

      <form onSubmit={handleUpload}>
        <div className={`input-group${isMinimalDark ? ' input-group-minimal-dark' : ''}`}>
          <label htmlFor="file-upload">Chon tai lieu nguon</label>
          <input
            id="file-upload"
            type="file"
            onChange={handleFileChange}
            accept=".pdf,.docx,.png,.jpg,.jpeg"
            disabled={uploading}
          />
        </div>

        {file && (
          <div className={`file-info-card${isMinimalDark ? ' file-info-card-dark' : ''}`}>
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
            className={`button${isMinimalDark ? ' button-upload-primary' : ''}`}
            disabled={!file || uploading}
          >
            {uploading ? 'Dang upload...' : 'Upload va xu ly'}
          </button>
          <button
            type="button"
            className={`button button-secondary${isMinimalDark ? ' button-upload-secondary' : ''}`}
            onClick={() => navigate('/documents')}
            disabled={uploading}
          >
            Xem My Documents
          </button>
        </div>
      </form>
    </div>
  );
}

export default DocumentUpload;
