import React, { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { gameService } from '../services/api';
import { formatTopicForDisplay } from '../services/topicDisplay';

function QuizGame() {
  const { documentId } = useParams();
  const navigate = useNavigate();
  const [loading, setLoading] = useState(true);
  const [questions, setQuestions] = useState([]);
  const [currentQuestionIndex, setCurrentQuestionIndex] = useState(0);
  const [selectedAnswer, setSelectedAnswer] = useState(null);
  const [showResult, setShowResult] = useState(false);
  const [answers, setAnswers] = useState([]);
  const [finalScore, setFinalScore] = useState(null);

  useEffect(() => {
    const loadQuiz = async () => {
      try {
        const data = await gameService.getQuizGame(documentId, 10);
        setQuestions(data.questions);
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
        <h2>No Questions Available</h2>
        <p>Please generate questions first.</p>
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

  return (
    <div className="game-container">
      <div className="card">
        <div className="progress-bar">
          <div className="progress-fill" style={{ width: `${progress}%` }}></div>
        </div>
        
        <h3>Question {currentQuestionIndex + 1} of {questions.length}</h3>
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
