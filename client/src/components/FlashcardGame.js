import React, { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { gameService } from '../services/api';
import { formatTopicForDisplay } from '../services/topicDisplay';

function FlashcardGame() {
  const { documentId } = useParams();
  const navigate = useNavigate();
  const [loading, setLoading] = useState(true);
  const [flashcards, setFlashcards] = useState([]);
  const [currentIndex, setCurrentIndex] = useState(0);
  const [flipped, setFlipped] = useState(false);

  useEffect(() => {
    const loadFlashcards = async () => {
      try {
        const data = await gameService.getFlashcards(documentId);
        setFlashcards(data.flashcards);
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
        <h2>Chua co flashcards</h2>
        <p>Hay tao cau hoi truoc khi mo flashcards.</p>
        <button className="button" onClick={() => navigate('/documents')}>
          Quay lai Documents
        </button>
      </div>
    );
  }

  const currentCard = flashcards[currentIndex];
  const progress = ((currentIndex + 1) / flashcards.length) * 100;
  const topicDisplay = formatTopicForDisplay(currentCard.topic);

  return (
    <div className="game-container">
      <div className="card">
        <div className="section-header compact">
          <div>
            <h2>🃏 Flashcards</h2>
            <p className="section-subtitle">Cham vao the de lat mat sau va on lai dap an dung.</p>
          </div>
          <span className="mini-topic-tag">{topicDisplay.friendlyLabel}</span>
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
