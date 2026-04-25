import React, { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { gameService } from '../services/api';
import { formatTopicForDisplay } from '../services/topicDisplay';
import { useLanguage } from '../context/LanguageContext';

function FlashcardGame() {
  const { t } = useLanguage();
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
        alert(t('flashcards.loadError'));
        console.error(err);
        navigate('/workspaces');
      } finally {
        setLoading(false);
      }
    };

    loadFlashcards();
  }, [documentId, navigate, t]);

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
        <p>{t('flashcards.loading')}</p>
      </div>
    );
  }

  if (flashcards.length === 0) {
    return (
      <div className="card">
        <h2>{allFlashcards.length > 0 ? t('flashcards.allHiddenTitle') : t('flashcards.emptyTitle')}</h2>
        <p>
          {allFlashcards.length > 0
            ? t('flashcards.allHiddenBody')
            : t('flashcards.emptyBody')}
        </p>
        {allFlashcards.length > 0 && (
          <button className="button button-secondary" onClick={() => setHideLowConfidence(false)}>
            {t('flashcards.showLowConfidence')}
          </button>
        )}
        <button className="button" onClick={() => navigate('/workspaces')}>
          {t('flashcards.backToWorkspaces')}
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
            <h2>{t('flashcards.title')}</h2>
            <p className="section-subtitle">{t('flashcards.subtitle')}</p>
          </div>
          <div className="quality-toolbar">
            <span className="mini-topic-tag">{topicDisplay.friendlyLabel}</span>
            <button className="button button-secondary" onClick={() => setHideLowConfidence((current) => !current)}>
              {hideLowConfidence ? t('flashcards.showAllCards') : t('flashcards.hideLowConfidence')}
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
            {t('flashcards.mainTopic')} {topicDisplay.mainTopic}
          </p>
        )}
        {topicDisplay.technicalTag && (
          <p className="flashcard-meta" style={{ marginTop: '2px' }}>
            {t('flashcards.technicalTag')} {topicDisplay.technicalTag}
          </p>
        )}

        <div className="progress-bar">
          <div className="progress-fill" style={{ width: `${progress}%` }}></div>
        </div>

        <p className="flashcard-meta">
          {t('flashcards.cardProgress', { current: currentIndex + 1, total: flashcards.length })}
        </p>

        <div className="flashcard" onClick={handleFlip}>
          <div className="flashcard-content">
            {(quality.isLowConfidence || quality.isUnknown) && (
              <div className="alert alert-info quality-warning">
                <strong>{quality.isLowConfidence ? t('flashcards.reviewNeeded') : t('flashcards.noVerifier')}</strong>
                <p>
                  {quality.isLowConfidence
                    ? t('flashcards.lowConfidenceBody', { score: quality.score })
                    : t('flashcards.noVerifierBody')}
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
                <h3 style={{ color: '#667eea' }}>{t('flashcards.question')}</h3>
                <p>{currentCard.front}</p>
                <p style={{ marginTop: '30px', color: '#999', fontSize: '0.9em' }}>
                  {t('flashcards.tapToShow')}
                </p>
              </div>
            ) : (
              <div>
                <h3 style={{ color: '#28a745' }}>{t('flashcards.answer')}</h3>
                <p><strong>{currentCard.back}</strong></p>
                {currentCard.explanation && (
                  <div className="flashcard-explanation">
                    <strong>{t('flashcards.explanation')}</strong>
                    <p>{currentCard.explanation}</p>
                  </div>
                )}
                <p style={{ marginTop: '30px', color: '#999', fontSize: '0.9em' }}>
                  {t('flashcards.tapToReturn')}
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
            {t('flashcards.previous')}
          </button>

          <button className="button" onClick={() => navigate('/workspaces')}>
            {t('flashcards.workspaces')}
          </button>

          <button
            className="button"
            onClick={handleNext}
            disabled={currentIndex === flashcards.length - 1}
          >
            {t('flashcards.next')}
          </button>
        </div>

        {currentIndex === flashcards.length - 1 && (
          <div style={{ textAlign: 'center', marginTop: '20px' }}>
            <p>{t('flashcards.reachedEnd')}</p>
            <button
              className="button"
              onClick={() => {
                setCurrentIndex(0);
                setFlipped(false);
              }}
              style={{ marginTop: '10px' }}
            >
              {t('flashcards.restart')}
            </button>
          </div>
        )}
      </div>
    </div>
  );
}

export default FlashcardGame;
