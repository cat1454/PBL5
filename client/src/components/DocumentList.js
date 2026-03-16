import React, { useCallback, useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { documentService, questionService } from '../services/api';

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

function DocumentList() {
  const [documents, setDocuments] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [generating, setGenerating] = useState({});
  const [showAnalysis, setShowAnalysis] = useState(null);
  const [refreshing, setRefreshing] = useState(false);
  const [lastUpdated, setLastUpdated] = useState(null);
  const [feedback, setFeedback] = useState(null);
  const [currentTime, setCurrentTime] = useState(Date.now());
  const navigate = useNavigate();

  const loadDocuments = useCallback(async (options = {}) => {
    const { silent = false } = options;

    if (!silent) {
      setRefreshing(true);
    }

    try {
      const userId = 'demo-user'; // In real app, get from auth context
      const docs = await documentService.getUserDocuments(userId);
      setDocuments(docs);
      setLastUpdated(new Date());
    } catch (err) {
      setError('Error loading documents');
      console.error(err);
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, []);

  useEffect(() => {
    loadDocuments();
  }, [loadDocuments]);

  useEffect(() => {
    const interval = setInterval(() => {
      setCurrentTime(Date.now());
    }, 1000);

    return () => clearInterval(interval);
  }, []);

  // Auto-refresh when documents are being processed
  useEffect(() => {
    const hasProcessingDocs = documents.some(doc => doc.status >= 0 && doc.status <= 2);
    
    if (hasProcessingDocs) {
      const interval = setInterval(() => {
        loadDocuments();
      }, 3000); // Refresh every 3 seconds

      return () => clearInterval(interval);
    }
  }, [documents, loadDocuments]);

  const handleGenerateQuestions = async (documentId) => {
    setGenerating((current) => ({
      ...current,
      [documentId]: {
        running: true,
        percent: 0,
        stage: 'queued',
        message: 'Dang xep hang tao bo cau hoi...',
      },
    }));
    setFeedback({ type: 'info', text: 'Dang tao bo cau hoi moi theo tien trinh realtime.' });

    try {
      const startResult = await questionService.startGenerateQuestions(documentId, 5);
      const jobId = startResult.jobId;

      const pollStartedAt = Date.now();
      const pollTimeoutMs = 5 * 60 * 1000;
      let completed = false;

      while (!completed) {
        if (Date.now() - pollStartedAt > pollTimeoutMs) {
          throw new Error('Timeout waiting for question generation progress');
        }

        const progressState = await questionService.getGenerateProgress(jobId);

        setGenerating((current) => ({
          ...current,
          [documentId]: {
            running: progressState.status === 'queued' || progressState.status === 'running',
            percent: progressState.percent ?? 0,
            stage: progressState.stage,
            message: progressState.message,
            current: progressState.current,
            total: progressState.total,
            topicTag: progressState.topicTag,
          },
        }));

        if (progressState.status === 'completed') {
          completed = true;
          setFeedback({
            type: 'success',
            text: `Da tao xong bo cau hoi moi (${progressState.questionsGenerated || 0} cau). Ban co the vao Quiz hoac Flashcards ngay bay gio.`,
          });
          await loadDocuments({ silent: true });
          break;
        }

        if (progressState.status === 'failed') {
          throw new Error(progressState.error || 'Question generation failed');
        }

        await sleep(1200);
      }
    } catch (err) {
      setFeedback({ type: 'error', text: 'Khong tao duoc cau hoi. Vui long thu lai va kiem tra backend log/progress.' });
      console.error(err);
    } finally {
      setGenerating((current) => {
        const next = { ...current };
        delete next[documentId];
        return next;
      });
    }
  };

  const handleDelete = async (documentId) => {
    if (window.confirm('Are you sure you want to delete this document?')) {
      try {
        await documentService.deleteDocument(documentId);
        await loadDocuments();
      } catch (err) {
        alert('Error deleting document');
        console.error(err);
      }
    }
  };

  const handleViewAnalysis = (doc) => {
    setShowAnalysis(doc);
  };

  const closeAnalysisModal = () => {
    setShowAnalysis(null);
  };

  const processingCount = documents.filter((doc) => doc.status >= 0 && doc.status <= 2).length;
  const readyCount = documents.filter((doc) => doc.status === 3).length;
  const totalQuestions = documents.reduce((sum, doc) => sum + (doc.questionsCount || 0), 0);

  const formatDateTime = (value) => new Date(value).toLocaleString();

  const getStageDurationMs = (status) => {
    switch (status) {
      case 0:
        return 10000;
      case 1:
        return 25000;
      case 2:
        return 35000;
      default:
        return 0;
    }
  };

  const formatDuration = (milliseconds) => {
    const totalSeconds = Math.max(0, Math.ceil(milliseconds / 1000));
    if (totalSeconds <= 59) {
      return `${totalSeconds}s`;
    }

    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
    return `${minutes}p ${seconds}s`;
  };

  const getEstimatedTimeRemaining = (doc) => {
    if (doc.status < 0 || doc.status > 2) {
      return null;
    }

    const stageStartedAt = new Date(doc.updatedAt || doc.createdAt).getTime();
    const stageDurationMs = getStageDurationMs(doc.status);
    const remainingMs = stageDurationMs - (currentTime - stageStartedAt);

    if (remainingMs <= 0) {
      return 'Sap xong...';
    }

    return `Uoc tinh con ${formatDuration(remainingMs)}`;
  };

  const getStatusHint = (doc) => {
    if (generating[doc.id]?.running) {
      return 'AI dang doc toan bo noi dung va tao bo cau hoi moi. Ban co the cho 2-3 phut de lay ket qua tot hon.';
    }

    switch (doc.status) {
      case 0:
        return 'Tai lieu da upload xong va dang cho trich xuat noi dung.';
      case 1:
        return 'He thong dang trich xuat text va OCR neu file la anh hoac PDF scan.';
      case 2:
        return 'AI dang phan tich noi dung, chia topic va tom tat tai lieu.';
      case 3:
        return doc.questionsCount > 0
          ? 'Da san sang hoc bang quiz hoac flashcards.'
          : 'Tai lieu da xu ly xong. Ban co the tao bo cau hoi moi ngay bay gio.';
      case 4:
        return 'Xu ly that bai. Thu upload lai hoac kiem tra file dau vao.';
      default:
        return 'Dang cap nhat trang thai tai lieu.';
    }
  };

  const getStatusColor = (status) => {
    switch (status) {
      case 0: return '#ffc107'; // Uploaded
      case 1: return '#17a2b8'; // Extracting
      case 2: return '#007bff'; // Analyzing
      case 3: return '#28a745'; // Completed
      case 4: return '#dc3545'; // Failed
      default: return '#6c757d';
    }
  };

  const getStatusText = (status) => {
    switch (status) {
      case 0: return 'Uploaded';
      case 1: return 'Extracting...';
      case 2: return 'Analyzing...';
      case 3: return 'Completed';
      case 4: return 'Failed';
      default: return 'Unknown';
    }
  };

  if (loading) {
    return (
      <div className="loading">
        <div className="spinner"></div>
        <p>Loading documents...</p>
      </div>
    );
  }

  if (error) {
    return <div className="alert alert-error">{error}</div>;
  }

  return (
    <div>
      <div className="card">
        <div className="section-header">
          <div>
            <h2>📚 My Documents</h2>
            <p className="section-subtitle">Theo doi tai lieu, qua trinh AI phan tich va tao cau hoi o mot cho duy nhat.</p>
          </div>
          <div className="header-actions">
            <button
              className="button button-secondary"
              onClick={() => loadDocuments()}
              disabled={refreshing}
            >
              {refreshing ? 'Dang lam moi...' : '↻ Lam moi'}
            </button>
            {documents.some(doc => doc.status >= 0 && doc.status <= 2) && (
              <div className="live-indicator">
                <div className="spinner-small"></div>
                Tu dong cap nhat
              </div>
            )}
          </div>
        </div>

        <div className="stats-grid">
          <div className="stat-card">
            <span className="stat-value">{documents.length}</span>
            <span className="stat-label">Tong tai lieu</span>
          </div>
          <div className="stat-card">
            <span className="stat-value">{processingCount}</span>
            <span className="stat-label">Dang xu ly</span>
          </div>
          <div className="stat-card">
            <span className="stat-value">{readyCount}</span>
            <span className="stat-label">San sang hoc</span>
          </div>
          <div className="stat-card">
            <span className="stat-value">{totalQuestions}</span>
            <span className="stat-label">Tong cau hoi</span>
          </div>
        </div>

        {lastUpdated && (
          <p className="timestamp-note">Cap nhat lan cuoi: {formatDateTime(lastUpdated)}</p>
        )}

        {feedback && (
          <div className={`alert ${feedback.type === 'success' ? 'alert-success' : feedback.type === 'error' ? 'alert-error' : 'alert-info'}`}>
            {feedback.text}
          </div>
        )}

        {documents.length === 0 ? (
          <div className="empty-state">
            <h3>Chua co tai lieu nao</h3>
            <p>Upload PDF, DOCX hoac anh de AI phan tich noi dung va tao bo cau hoi tu dong.</p>
          </div>
        ) : (
          <div className="document-list">
            {documents.map((doc) => (
              (() => {
                const generationState = generating[doc.id];
                const isGenerating = !!generationState?.running;

                return (
              <div key={doc.id} className="document-item">
                <div className="document-info">
                  <div className="document-title-row">
                    <h3>{doc.fileName}</h3>
                    <span className="status-badge" style={{ backgroundColor: getStatusColor(doc.status) }}>
                      {getStatusText(doc.status)}
                    </span>
                  </div>

                  <p className="document-meta">
                    <span>{formatDateTime(doc.createdAt)}</span>
                    <span>{(doc.fileSize / 1024).toFixed(0)} KB</span>
                    <span>{doc.questionsCount || 0} cau hoi</span>
                  </p>

                  <p className="status-hint">{getStatusHint(doc)}</p>

                  {getEstimatedTimeRemaining(doc) && (
                    <p className="status-eta">⏱️ {getEstimatedTimeRemaining(doc)}</p>
                  )}

                  {doc.mainTopics && doc.mainTopics.length > 0 && (
                    <div className="inline-topics">
                      {doc.mainTopics.slice(0, 5).map((topic, index) => (
                        <span key={index} className="mini-topic-tag">{topic}</span>
                      ))}
                    </div>
                  )}

                  {isGenerating && (
                    <div className="generation-panel">
                      <div className="spinner-small"></div>
                      <div>
                        <strong>Dang tao bo cau hoi moi ({generationState.percent || 0}%)</strong>
                        <p>{generationState.message || 'He thong dang xu ly va sinh cau hoi.'}</p>
                        {typeof generationState.current === 'number' && typeof generationState.total === 'number' && (
                          <p>Tien trinh cau hoi: {generationState.current}/{generationState.total}</p>
                        )}
                        {generationState.topicTag && <p>Topic-tag hien tai: {generationState.topicTag}</p>}
                        <div className="generation-progress-bar">
                          <div
                            className="generation-progress-fill"
                            style={{ width: `${Math.max(0, Math.min(100, generationState.percent || 0))}%` }}
                          ></div>
                        </div>
                      </div>
                    </div>
                  )}
                </div>
                <div className="document-actions">
                  {doc.status === 3 && (
                    <>
                      <button
                        className="button"
                        style={{ backgroundColor: '#6366f1' }}
                        onClick={() => handleViewAnalysis(doc)}
                      >
                        📊 View Analysis
                      </button>
                      <button
                        className="button"
                        onClick={() => handleGenerateQuestions(doc.id)}
                        disabled={isGenerating}
                      >
                        {isGenerating ? `Dang tao... ${generationState.percent || 0}%` : '🎯 Tao bo cau hoi'}
                      </button>
                      <button
                        className="button"
                        onClick={() => navigate(`/quiz/${doc.id}`)}
                        disabled={!doc.questionsCount || doc.questionsCount === 0}
                        style={{ 
                          opacity: (!doc.questionsCount || doc.questionsCount === 0) ? 0.5 : 1,
                          cursor: (!doc.questionsCount || doc.questionsCount === 0) ? 'not-allowed' : 'pointer'
                        }}
                      >
                        🎮 Quiz
                      </button>
                      <button
                        className="button"
                        onClick={() => navigate(`/flashcards/${doc.id}`)}
                        disabled={!doc.questionsCount || doc.questionsCount === 0}
                        style={{ 
                          opacity: (!doc.questionsCount || doc.questionsCount === 0) ? 0.5 : 1,
                          cursor: (!doc.questionsCount || doc.questionsCount === 0) ? 'not-allowed' : 'pointer'
                        }}
                      >
                        🃏 Flashcards
                      </button>
                    </>
                  )}
                  <button
                    className="button"
                    style={{ backgroundColor: '#dc3545' }}
                    onClick={() => handleDelete(doc.id)}
                  >
                    🗑️ Delete
                  </button>
                </div>
              </div>
                );
              })()
            ))}
          </div>
        )}
      </div>

      {/* Analysis Modal */}
      {showAnalysis && (
        <div className="modal-overlay" onClick={closeAnalysisModal}>
          <div className="modal-content" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h2>📊 Phan tich noi dung: {showAnalysis.fileName}</h2>
              <button className="close-btn" onClick={closeAnalysisModal}>✕</button>
            </div>
            <div className="modal-body">
              {/* Main Topics */}
              {showAnalysis.mainTopics && showAnalysis.mainTopics.length > 0 && (
                <div className="analysis-section">
                  <h3>🎯 Chu de chinh</h3>
                  <div className="topics-list">
                    {showAnalysis.mainTopics.map((topic, index) => (
                      <span key={index} className="topic-tag">{topic}</span>
                    ))}
                  </div>
                </div>
              )}

              {/* Key Points */}
              {showAnalysis.keyPoints && showAnalysis.keyPoints.length > 0 && (
                <div className="analysis-section">
                  <h3>💡 Y chinh</h3>
                  <ul className="key-points-list">
                    {showAnalysis.keyPoints.map((point, index) => (
                      <li key={index}>{point}</li>
                    ))}
                  </ul>
                </div>
              )}

              {/* Summary */}
              {showAnalysis.summary && (
                <div className="analysis-section">
                  <h3>📝 Tom tat</h3>
                  <p className="summary-text">{showAnalysis.summary}</p>
                </div>
              )}

              {/* Language */}
              {showAnalysis.language && (
                <div className="analysis-section">
                  <h3>🌐 Ngon ngu</h3>
                  <p><strong>{showAnalysis.language}</strong></p>
                </div>
              )}

              {/* Extracted Text Preview */}
              {showAnalysis.extractedText && (
                <div className="analysis-section">
                  <h3>📄 Van ban da trich xuat</h3>
                  <div className="extracted-text-preview">
                    {showAnalysis.extractedText.substring(0, 1000)}
                    {showAnalysis.extractedText.length > 1000 && '...'}
                  </div>
                </div>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default DocumentList;
