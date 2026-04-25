import React, { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { gameService } from '../services/api';
import { formatTopicForDisplay } from '../services/topicDisplay';
import { useLanguage } from '../context/LanguageContext';

function QuizGame() {
  const { t } = useLanguage();
  const { documentId } = useParams();
  const navigate = useNavigate();
  const [loading, setLoading] = useState(true);
  const [allQuestions, setAllQuestions] = useState([]);
  const [currentQuestionIndex, setCurrentQuestionIndex] = useState(0);
  const [selectedAnswer, setSelectedAnswer] = useState(null);
  const [showResult, setShowResult] = useState(false);
  const [answers, setAnswers] = useState([]);
  const [finalScore, setFinalScore] = useState(null);
  const [hideLowConfidence, setHideLowConfidence] = useState(false);

  useEffect(() => {
    const loadQuiz = async () => {
      try {
        const data = await gameService.getQuizGame(documentId, 10);
        setAllQuestions(data.questions);
      } catch (err) {
        alert(t('quiz.loadError'));
        console.error(err);
        navigate('/workspaces');
      } finally {
        setLoading(false);
      }
    };

    loadQuiz();
  }, [documentId, navigate, t]);

  useEffect(() => {
    setCurrentQuestionIndex(0);
    setSelectedAnswer(null);
    setShowResult(false);
    setAnswers([]);
    setFinalScore(null);
  }, [hideLowConfidence, allQuestions]);

  const questions = hideLowConfidence
    ? allQuestions.filter((question) => !question.quality?.isLowConfidence)
    : allQuestions;

  const handleAnswerSelect = (optionKey) => {
    if (!showResult) {
      setSelectedAnswer(optionKey);
    }
  };

  const isCurrentAnswerCorrect = () => {
    const currentQuestion = questions[currentQuestionIndex];
    return currentQuestion.correctAnswer === selectedAnswer;
  };

  const handleSubmitAnswer = () => {
    if (!selectedAnswer) {
      return;
    }

    const currentQuestion = questions[currentQuestionIndex];
    const isCorrect = currentQuestion.correctAnswer === selectedAnswer;

    setAnswers([
      ...answers,
      {
        questionId: currentQuestion.id,
        selectedAnswer,
        isCorrect,
      },
    ]);

    setShowResult(true);
  };

  const handleNextQuestion = () => {
    if (currentQuestionIndex < questions.length - 1) {
      setCurrentQuestionIndex(currentQuestionIndex + 1);
      setSelectedAnswer(null);
      setShowResult(false);
    } else {
      const correctCount = answers.filter((answer) => answer.isCorrect).length + (showResult && isCurrentAnswerCorrect() ? 1 : 0);
      const score = Math.round((correctCount / questions.length) * 100);
      setFinalScore(score);
    }
  };

  const getOptionClass = (optionKey) => {
    if (!showResult) {
      return selectedAnswer === optionKey ? 'option-button selected' : 'option-button';
    }

    const currentQuestion = questions[currentQuestionIndex];
    if (optionKey === currentQuestion.correctAnswer) {
      return 'option-button correct';
    }
    if (optionKey === selectedAnswer && selectedAnswer !== currentQuestion.correctAnswer) {
      return 'option-button incorrect';
    }
    return 'option-button';
  };

  if (loading) {
    return (
      <div className="loading">
        <div className="spinner"></div>
        <p>{t('quiz.loading')}</p>
      </div>
    );
  }

  if (questions.length === 0) {
    return (
      <div className="card">
        <h2>{allQuestions.length > 0 ? t('quiz.allHiddenTitle') : t('quiz.emptyTitle')}</h2>
        <p>
          {allQuestions.length > 0
            ? t('quiz.allHiddenBody')
            : t('quiz.emptyBody')}
        </p>
        {allQuestions.length > 0 && (
          <button className="button button-secondary" onClick={() => setHideLowConfidence(false)}>
            {t('quiz.showLowConfidence')}
          </button>
        )}
        <button className="button" onClick={() => navigate('/workspaces')}>
          {t('quiz.backToWorkspaces')}
        </button>
      </div>
    );
  }

  if (finalScore !== null) {
    const correct = answers.filter((answer) => answer.isCorrect).length + (isCurrentAnswerCorrect() ? 1 : 0);

    return (
      <div className="game-container">
        <div className="card">
          <h2>{t('quiz.completed')}</h2>
          <div className="score-display">
            <h1 style={{ fontSize: '4em', color: finalScore >= 70 ? '#28a745' : '#dc3545' }}>
              {finalScore}%
            </h1>
            <p style={{ fontSize: '1.2em' }}>
              {t('quiz.scoreLine', { correct, total: questions.length })}
            </p>
          </div>
          <div style={{ display: 'flex', gap: '10px', justifyContent: 'center', marginTop: '30px' }}>
            <button className="button" onClick={() => window.location.reload()}>
              {t('quiz.retry')}
            </button>
            <button className="button" onClick={() => navigate('/workspaces')}>
              {t('quiz.backToWorkspaces')}
            </button>
          </div>
        </div>
      </div>
    );
  }

  const currentQuestion = questions[currentQuestionIndex];
  const progress = ((currentQuestionIndex + 1) / questions.length) * 100;
  const topicDisplay = formatTopicForDisplay(currentQuestion.topic);
  const quality = currentQuestion.quality || {};

  return (
    <div className="game-container">
      <div className="card">
        <div className="progress-bar">
          <div className="progress-fill" style={{ width: `${progress}%` }}></div>
        </div>

        <h3>{t('quiz.questionProgress', { current: currentQuestionIndex + 1, total: questions.length })}</h3>
        <div className="quality-toolbar">
          <button className="button button-secondary" onClick={() => setHideLowConfidence((current) => !current)}>
            {hideLowConfidence ? t('quiz.showAllQuestions') : t('quiz.hideLowConfidence')}
          </button>
          {quality.score !== undefined && quality.score !== null && (
            <span className={`quality-chip ${quality.isLowConfidence ? 'low' : 'good'}`}>
              Verifier {quality.score}/100
            </span>
          )}
        </div>
        <p className="flashcard-meta" style={{ marginTop: '-6px' }}>{topicDisplay.friendlyLabel}</p>
        {topicDisplay.mainTopic && (
          <p className="flashcard-meta" style={{ marginTop: '-2px' }}>
            {t('quiz.mainTopic')} {topicDisplay.mainTopic}
          </p>
        )}
        {topicDisplay.technicalTag && (
          <p className="flashcard-meta" style={{ marginTop: '-2px' }}>
            {t('quiz.technicalTag')} {topicDisplay.technicalTag}
          </p>
        )}

        <div className="question-card">
          <h2>{currentQuestion.questionText}</h2>

          {(quality.isLowConfidence || quality.isUnknown) && (
            <div className="alert alert-info quality-warning">
              <strong>{quality.isLowConfidence ? t('quiz.reviewNeeded') : t('quiz.noVerifier')}</strong>
              <p>
                {quality.isLowConfidence
                  ? t('quiz.lowConfidenceBody', { score: quality.score })
                  : t('quiz.noVerifierBody')}
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

          <div className="options">
            {currentQuestion.options.map((option) => (
              <button
                key={option.key}
                className={getOptionClass(option.key)}
                onClick={() => handleAnswerSelect(option.key)}
                disabled={showResult}
              >
                <strong>{option.key}.</strong> {option.text}
              </button>
            ))}
          </div>

          {showResult && (
            <div className={`alert ${isCurrentAnswerCorrect() ? 'alert-success' : 'alert-error'}`}>
              <strong>{isCurrentAnswerCorrect() ? t('quiz.correct') : t('quiz.incorrect')}</strong>
              <p>{currentQuestion.explanation}</p>
            </div>
          )}

          <div style={{ marginTop: '20px' }}>
            {!showResult ? (
              <button
                className="button"
                onClick={handleSubmitAnswer}
                disabled={!selectedAnswer}
              >
                {t('quiz.submit')}
              </button>
            ) : (
              <button className="button" onClick={handleNextQuestion}>
                {currentQuestionIndex < questions.length - 1 ? t('quiz.next') : t('quiz.finish')}
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

export default QuizGame;
