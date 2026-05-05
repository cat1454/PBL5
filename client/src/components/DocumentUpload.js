import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { documentService, getApiErrorMessage } from '../services/api';
import { useToast } from './common/ToastProvider';
import { useLanguage } from '../context/LanguageContext';

function DocumentUpload({ onUploadSuccess, variant = 'default' }) {
  const navigate = useNavigate();
  const { t } = useLanguage();
  const { showToast } = useToast();
  const [file, setFile] = useState(null);
  const [uploading, setUploading] = useState(false);
  const [uploadProgress, setUploadProgress] = useState(0);
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
      return;
    }

    setError(t('upload.errors.invalidType'));
    setFile(null);
  };

  const handleUpload = async (event) => {
    event.preventDefault();

    if (!file) {
      setError(t('upload.errors.noFile'));
      return;
    }

    setUploading(true);
    setUploadProgress(0);
    setError('');

    try {
      const result = await documentService.uploadDocument(file, (progressValue) => {
        setUploadProgress(progressValue);
      });

      setUploadProgress(100);

      if (onUploadSuccess) {
        onUploadSuccess(result);
      }

      showToast({
        type: 'success',
        message: t('upload.success'),
      });
      setFile(null);
      event.target.reset();
      setUploadProgress(0);
      setUploading(false);
    } catch (err) {
      setError(getApiErrorMessage(err, t('upload.errors.uploadFailed')));
      setUploading(false);
    }
  };

  return (
    <div className={`card document-upload-card${isMinimalDark ? ' document-upload-card-minimal-dark' : ''}`}>
      <div className="document-upload-head">
        <div>
          <span className="document-upload-kicker">{t('upload.kicker')}</span>
          <h2>{t('upload.title')}</h2>
        </div>
        <div className="document-upload-types">
          <span>PDF</span>
          <span>DOCX</span>
          <span>PNG</span>
          <span>JPG</span>
        </div>
      </div>

      <p className="section-subtitle document-upload-subtitle">
        {t('upload.subtitle')}
      </p>
      {error && <div className="alert alert-error">{error}</div>}

      <form onSubmit={handleUpload}>
        <div className={`input-group${isMinimalDark ? ' input-group-minimal-dark' : ''}`}>
          <label htmlFor="file-upload">{t('upload.inputLabel')}</label>
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
            <p><strong>{t('upload.selected')}</strong> {file.name}</p>
            <p><strong>{t('upload.size')}</strong> {(file.size / 1024 / 1024).toFixed(2)} MB</p>
            <p><strong>{t('upload.eta')}</strong> {t('upload.etaValue')}</p>
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
              {uploadProgress < 100 ? t('upload.progressUploading') : t('upload.progressDone')}
            </p>
          </div>
        )}

        <div className="button-row">
          <button
            type="submit"
            className={`button${isMinimalDark ? ' button-upload-primary' : ''}`}
            disabled={!file || uploading}
          >
            {uploading ? t('upload.submitting') : t('upload.submit')}
          </button>
          <button
            type="button"
            className={`button button-secondary${isMinimalDark ? ' button-upload-secondary' : ''}`}
            onClick={() => navigate('/workspaces')}
            disabled={uploading}
          >
            {t('upload.workspace')}
          </button>
        </div>
      </form>
    </div>
  );
}

export default DocumentUpload;
