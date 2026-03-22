import React, { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { gameService } from '../services/api';
import { formatTopicForDisplay } from '../services/topicDisplay';

function QuizGame() {
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
        alert('Error loading quiz. Please generate questions first.');
        console.error(err);
        navigate('/documents');
      } finally {
        setLoading(false);
      }
    };

    loadQuiz();
  }, [documentId, navigate]);

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

  const handleSubmitAnswer = () => {
    if (!selectedAnswer) return;

    const currentQuestion = questions[currentQuestionIndex];
    const isCorrect = currentQuestion.correctAnswer === selectedAnswer;

    setAnswers([
      ...answers,
      {
        questionId: currentQuestion.id,
        selectedAnswer: selectedAnswer,
        isCorrect: isCorrect,
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
      // Quiz completed, calculate final score
      const correctCount = answers.filter((a) => a.isCorrect).length + (showResult && isCurrentAnswerCorrect() ? 1 : 0);
      const score = Math.round((correctCount / questions.length) * 100);
      setFinalScore(score);
    }
  };

  const isCurrentAnswerCorrect = () => {
    const currentQuestion = questions[currentQuestionIndex];
    return currentQuestion.correctAnswer === selectedAnswer;
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
        <p>Loading quiz...</p>
      </div>
    );
  }

  if (questions.length === 0) {
    return (
      <div className="card">
        <h2>{allQuestions.length > 0 ? 'Tat ca cau hoi hien dang bi an' : 'No Questions Available'}</h2>
        <p>
          {allQuestions.length > 0
            ? 'Tat het bo loc an low-confidence de xem lai toan bo cau hoi.'
            : 'Please generate questions first.'}
        </p>
        {allQuestions.length > 0 && (
          <button className="button button-secondary" onClick={() => setHideLowConfidence(false)}>
            Hien lai cau hoi diem thap
          </button>
        )}
        <button className="button" onClick={() => navigate('/documents')}>
          Back to Documents
        </button>
      </div>
    );
  }

  if (finalScore !== null) {
    return (
      <div className="game-container">
        <div className="card">
          <h2>🎉 Quiz Completed!</h2>
          <div className="score-display">
            <h1 style={{ fontSize: '4em', color: finalScore >= 70 ? '#28a745' : '#dc3545' }}>
              {finalScore}%
            </h1>
            <p style={{ fontSize: '1.2em' }}>
              You got {answers.filter((a) => a.isCorrect).length + (isCurrentAnswerCorrect() ? 1 : 0)} out of {questions.length} questions correct!
            </p>
          </div>
          <div style={{ display: 'flex', gap: '10px', justifyContent: 'center', marginTop: '30px' }}>
            <button className="button" onClick={() => window.location.reload()}>
              🔄 Retry Quiz
            </button>
            <button className="button" onClick={() => navigate('/documents')}>
              📚 Back to Documents
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
        
        <h3>Question {currentQuestionIndex + 1} of {questions.length}</h3>
        <div className="quality-toolbar">
          <button className="button button-secondary" onClick={() => setHideLowConfidence((current) => !current)}>
            {hideLowConfidence ? 'Hien tat ca cau hoi' : 'An cau hoi diem thap'}
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
            Chủ đề chính: {topicDisplay.mainTopic}
          </p>
        )}
        {topicDisplay.technicalTag && (
          <p className="flashcard-meta" style={{ marginTop: '-2px' }}>
            Tag kỹ thuật: {topicDisplay.technicalTag}
          </p>
        )}
        
        <div className="question-card">
          <h2>{currentQuestion.questionText}</h2>

          {(quality.isLowConfidence || quality.isUnknown) && (
            <div className="alert alert-info quality-warning">
              <strong>{quality.isLowConfidence ? 'Can review' : 'Chua co verifier score'}</strong>
              <p>
                {quality.isLowConfidence
                  ? `Cau hoi nay co diem verifier ${quality.score}/100. Nen doc ky explanation truoc khi hoc.`
                  : 'Cau hoi nay da duoc chinh sua thu cong hoac chua qua verifier moi.'}
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
              <strong>{isCurrentAnswerCorrect() ? '✅ Correct!' : '❌ Incorrect'}</strong>
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
                Submit Answer
              </button>
            ) : (
              <button className="button" onClick={handleNextQuestion}>
                {currentQuestionIndex < questions.length - 1 ? 'Next Question →' : 'Finish Quiz'}
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

export default QuizGame;
