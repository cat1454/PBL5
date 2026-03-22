import React, { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { gameService } from '../services/api';
import { formatTopicForDisplay } from '../services/topicDisplay';

function FlashcardGame() {
  const { documentId } = useParams();
  const navigate = useNavigate();
  const [loading, setLoading] = useState(true);
  const [allFlashcards, setAllFlashcards] = useState([]);
  const [currentIndex, setCurrentIndex] = useState(0);
  const [flipped, setFlipped] = useState(false);
  const [hideLowConfidence, setHideLowConfidence] = useState(false);

  useEffect(() => {
    const loadFlashcards = async () => {
      try {
        const data = await gameService.getFlashcards(documentId);
        setAllFlashcards(data.flashcards);
      } catch (err) {
        alert('Error loading flashcards. Please generate questions first.');
        console.error(err);
        navigate('/documents');
      } finally {
        setLoading(false);
      }
    };

    loadFlashcards();
  }, [documentId, navigate]);

  useEffect(() => {
    setCurrentIndex(0);
    setFlipped(false);
  }, [hideLowConfidence, allFlashcards]);

  const flashcards = hideLowConfidence
    ? allFlashcards.filter((card) => !card.quality?.isLowConfidence)
    : allFlashcards;

  const handleFlip = () => {
    setFlipped(!flipped);
  };

  const handleNext = () => {
    if (currentIndex < flashcards.length - 1) {
      setCurrentIndex(currentIndex + 1);
      setFlipped(false);
    }
  };

  const handlePrevious = () => {
    if (currentIndex > 0) {
      setCurrentIndex(currentIndex - 1);
      setFlipped(false);
    }
  };

  if (loading) {
    return (
      <div className="loading">
        <div className="spinner"></div>
        <p>Dang tai flashcards...</p>
      </div>
    );
  }

  if (flashcards.length === 0) {
    return (
      <div className="card">
        <h2>{allFlashcards.length > 0 ? 'Tat ca flashcard hien dang bi an' : 'Chua co flashcards'}</h2>
        <p>
          {allFlashcards.length > 0
            ? 'Tat bo loc an low-confidence de xem lai tat ca flashcard.'
            : 'Hay tao cau hoi truoc khi mo flashcards.'}
        </p>
        {allFlashcards.length > 0 && (
          <button className="button button-secondary" onClick={() => setHideLowConfidence(false)}>
            Hien lai flashcard diem thap
          </button>
        )}
        <button className="button" onClick={() => navigate('/documents')}>
          Quay lai Documents
        </button>
      </div>
    );
  }

  const currentCard = flashcards[currentIndex];
  const progress = ((currentIndex + 1) / flashcards.length) * 100;
  const topicDisplay = formatTopicForDisplay(currentCard.topic);
  const quality = currentCard.quality || {};

  return (
    <div className="game-container">
      <div className="card">
        <div className="section-header compact">
          <div>
            <h2>🃏 Flashcards</h2>
            <p className="section-subtitle">Cham vao the de lat mat sau va on lai dap an dung.</p>
          </div>
          <div className="quality-toolbar">
            <span className="mini-topic-tag">{topicDisplay.friendlyLabel}</span>
            <button className="button button-secondary" onClick={() => setHideLowConfidence((current) => !current)}>
              {hideLowConfidence ? 'Hien tat ca the' : 'An the diem thap'}
            </button>
            {quality.score !== undefined && quality.score !== null && (
              <span className={`quality-chip ${quality.isLowConfidence ? 'low' : 'good'}`}>
                Verifier {quality.score}/100
              </span>
            )}
          </div>
        </div>
        {topicDisplay.mainTopic && (
          <p className="flashcard-meta" style={{ marginTop: '6px' }}>
            Chủ đề chính: {topicDisplay.mainTopic}
          </p>
        )}
        {topicDisplay.technicalTag && (
          <p className="flashcard-meta" style={{ marginTop: '2px' }}>
            Tag kỹ thuật: {topicDisplay.technicalTag}
          </p>
        )}
        
        <div className="progress-bar">
          <div className="progress-fill" style={{ width: `${progress}%` }}></div>
        </div>
        
        <p className="flashcard-meta">
          The {currentIndex + 1} / {flashcards.length}
        </p>

        <div className="flashcard" onClick={handleFlip}>
          <div className="flashcard-content">
            {(quality.isLowConfidence || quality.isUnknown) && (
              <div className="alert alert-info quality-warning">
                <strong>{quality.isLowConfidence ? 'Can review' : 'Chua co verifier score'}</strong>
                <p>
                  {quality.isLowConfidence
                    ? `The nay co diem verifier ${quality.score}/100. Nen kiem tra lai dap an va explanation.`
                    : 'The nay da duoc chinh sua thu cong hoac chua duoc verifier lai.'}
                </p>
                {Array.isArray(quality.issues) && quality.issues.length > 0 && (
                  <ul className="quality-issues">
                    {quality.issues.slice(0, 2).map((issue) => (
                      <li key={issue}>{issue}</li>
                    ))}
                  </ul>
                )}
              </div>
            )}
            {!flipped ? (
              <div>
                <h3 style={{ color: '#667eea' }}>Question:</h3>
                <p>{currentCard.front}</p>
                <p style={{ marginTop: '30px', color: '#999', fontSize: '0.9em' }}>
                  Bam vao the de hien dap an
                </p>
              </div>
            ) : (
              <div>
                <h3 style={{ color: '#28a745' }}>Dap an:</h3>
                <p><strong>{currentCard.back}</strong></p>
                {currentCard.explanation && (
                  <div className="flashcard-explanation">
                    <strong>Giai thich:</strong>
                    <p>{currentCard.explanation}</p>
                  </div>
                )}
                <p style={{ marginTop: '30px', color: '#999', fontSize: '0.9em' }}>
                  Bam vao the de quay lai cau hoi
                </p>
              </div>
            )}
          </div>
        </div>

        <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: '30px' }}>
          <button
            className="button"
            onClick={handlePrevious}
            disabled={currentIndex === 0}
          >
            ← Truoc
          </button>
          
          <button className="button" onClick={() => navigate('/documents')}>
            📚 Documents
          </button>
          
          <button
            className="button"
            onClick={handleNext}
            disabled={currentIndex === flashcards.length - 1}
          >
            Tiep →
          </button>
        </div>

        {currentIndex === flashcards.length - 1 && (
          <div style={{ textAlign: 'center', marginTop: '20px' }}>
            <p>🎉 Ban da xem the cuoi cung.</p>
            <button
              className="button"
              onClick={() => {
                setCurrentIndex(0);
                setFlipped(false);
              }}
              style={{ marginTop: '10px' }}
            >
              🔄 Hoc lai tu dau
            </button>
          </div>
        )}
      </div>
    </div>
  );
}

export default FlashcardGame;
