import React, { act } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import StudyHub from './StudyHub';
import { gameService, learningService } from '../services/api';

jest.mock('../services/api', () => ({
  documentService: {
    getDocument: jest.fn(),
  },
  gameService: {
    getQuizGame: jest.fn(),
    submitQuizAnswer: jest.fn(),
    getFlashcards: jest.fn(),
  },
  getApiErrorMessage: jest.fn((error, fallback) => error?.message || fallback),
  isApiJobNotFound: jest.fn(() => false),
  learningService: {
    getDocumentSummary: jest.fn(),
    recordAttempt: jest.fn(),
    startTest: jest.fn(),
    submitTestResult: jest.fn(),
  },
  questionService: {
    generateQuestions: jest.fn(),
    startGenerateQuestions: jest.fn(),
    getGenerateProgress: jest.fn(),
    getQuestionsByDocument: jest.fn(),
    getQuestionMetrics: jest.fn(),
  },
}));

jest.mock('../context/LanguageContext', () => ({
  useLanguage: () => ({
    language: 'en',
    t: (key, params = {}) => {
      const labels = {
        'quiz.loading': 'Loading questions',
        'quiz.questionProgress': `Question ${params.current} of ${params.total}`,
        'quiz.submit': 'Submit',
        'quiz.next': 'Next',
        'quiz.finish': 'Finish',
        'quiz.correct': 'Correct',
        'quiz.incorrect': 'Incorrect',
        'quiz.completed': 'Quiz completed',
        'quiz.scoreLine': `${params.correct}/${params.total} correct`,
        'quiz.retry': 'Retry',
        'quiz.hideLowConfidence': 'Hide low confidence',
        'quiz.showAllQuestions': 'Show all questions',
        'quiz.loadError': 'Could not load quiz',
        'quiz.emptyTitle': 'No questions',
        'quiz.allHiddenTitle': 'All hidden',
        'quiz.allHiddenBody': 'All hidden body',
        'quiz.showLowConfidence': 'Show low confidence',
        'quiz.reviewNeeded': 'Review needed',
        'quiz.noVerifier': 'No verifier',
        'quiz.lowConfidenceBody': 'Low confidence',
        'quiz.noVerifierBody': 'No verifier body',
        'streak.loading': 'Loading streak',
        'streak.emptyTitle': 'No streak questions',
        'streak.correctTitle': 'Streak correct',
        'streak.incorrectTitle': 'Streak incorrect',
        'streak.correctBody': `Current streak ${params.count}`,
        'streak.incorrectBody': 'Streak reset',
        'streak.currentStreak': 'Current streak',
        'streak.bestStreak': 'Best streak',
        'streak.questionCounter': 'Question counter',
        'streak.completedTitle': 'Streak completed',
        'streak.completedSubtitle': 'Streak done',
        'streak.scoreLine': `${params.correct}/${params.total} correct`,
        'streak.retry': 'Retry streak',
        'streak.bestStreakLine': `Best streak ${params.count}`,
      };

      return labels[key] || key;
    },
  }),
}));

jest.mock('../services/topicDisplay', () => ({
  formatTopicForDisplay: () => null,
}));

jest.mock('../services/progress', () => ({
  isActiveProgress: () => false,
  normalizeProgressState: (value) => value,
}));

jest.mock('../services/generationReadiness', () => ({
  getReadinessLabel: () => '',
  getReadinessMessage: () => '',
  normalizeGenerationReadiness: (value) => value,
}));

const questions = [
  {
    id: 101,
    questionText: 'First question?',
    options: [
      { key: 'A', text: 'Alpha' },
      { key: 'B', text: 'Beta' },
    ],
    topic: 'Topic',
    quality: {},
  },
  {
    id: 102,
    questionText: 'Second question?',
    options: [
      { key: 'A', text: 'Gamma' },
      { key: 'B', text: 'Delta' },
    ],
    topic: 'Topic',
    quality: {},
  },
  {
    id: 103,
    questionText: 'Third question?',
    options: [
      { key: 'A', text: 'Epsilon' },
      { key: 'B', text: 'Zeta' },
    ],
    topic: 'Topic',
    quality: {},
  },
];

function cloneQuestions(items = questions) {
  return items.map((question) => ({
    ...question,
    options: question.options.map((option) => ({ ...option })),
  }));
}

async function renderStudyHub(mode) {
  global.IS_REACT_ACT_ENVIRONMENT = true;
  const container = document.createElement('div');
  document.body.appendChild(container);
  const root = createRoot(container);

  await act(async () => {
    root.render(
      <MemoryRouter>
        <StudyHub documentId="7" forcedMode={mode} showShell={false} />
      </MemoryRouter>
    );
  });

  return {
    container,
    root,
    unmount: () => {
      act(() => root.unmount());
      container.remove();
    },
  };
}

async function waitFor(assertion) {
  let lastError;

  for (let attempt = 0; attempt < 60; attempt += 1) {
    try {
      return assertion();
    } catch (error) {
      lastError = error;
      await act(async () => {
        await new Promise((resolve) => setTimeout(resolve, 10));
      });
    }
  }

  throw lastError;
}

async function click(element) {
  await act(async () => {
    element.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await Promise.resolve();
  });
}

function buttonByText(container, text) {
  const button = Array.from(container.querySelectorAll('button'))
    .find((candidate) => candidate.textContent.trim() === text);

  if (!button) {
    throw new Error(`Button not found: ${text}`);
  }

  return button;
}

function buttonContaining(container, text) {
  const button = Array.from(container.querySelectorAll('button'))
    .find((candidate) => candidate.textContent.includes(text));

  if (!button) {
    throw new Error(`Button not found containing: ${text}`);
  }

  return button;
}

function statValue(container, label) {
  const labelNode = Array.from(container.querySelectorAll('.streak-stat-card span'))
    .find((candidate) => candidate.textContent.trim() === label);

  if (!labelNode) {
    throw new Error(`Stat not found: ${label}`);
  }

  return labelNode.parentElement.querySelector('strong').textContent.trim();
}

describe('StudyHub question modes', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    window.requestAnimationFrame = (callback) => window.setTimeout(callback, 0);
    learningService.getDocumentSummary.mockResolvedValue({ totalQuestions: 3 });
    learningService.recordAttempt.mockResolvedValue({});
  });

  it('keeps quiz result visible after revealing the answered question', async () => {
    gameService.getQuizGame.mockResolvedValue({ questions: cloneQuestions() });
    gameService.submitQuizAnswer.mockResolvedValue({
      questionId: 101,
      isCorrect: true,
      correctAnswer: 'A',
      explanation: 'Alpha is correct.',
    });

    const view = await renderStudyHub('quiz');

    try {
      await waitFor(() => expect(view.container.textContent).toContain('Question 1 of 3'));

      await click(buttonContaining(view.container, 'Alpha'));
      await click(buttonByText(view.container, 'Submit'));

      await waitFor(() => expect(view.container.textContent).toContain('Correct'));
      expect(view.container.textContent).toContain('Alpha is correct.');

      await click(buttonByText(view.container, 'Next'));

      await waitFor(() => expect(view.container.textContent).toContain('Question 2 of 3'));
    } finally {
      view.unmount();
    }
  });

  it('preserves streak counters when answer reveal mutates questions', async () => {
    gameService.getQuizGame.mockResolvedValue({ questions: cloneQuestions() });
    gameService.submitQuizAnswer.mockImplementation((documentId, questionId, selectedAnswer) => Promise.resolve({
      questionId,
      isCorrect: questionId !== 103,
      correctAnswer: questionId === 103 ? 'B' : selectedAnswer,
      explanation: `Explanation ${questionId}`,
    }));

    const view = await renderStudyHub('streak');

    try {
      await waitFor(() => expect(view.container.textContent).toContain('Question 1 of 3'));

      await click(buttonContaining(view.container, 'Alpha'));
      await click(buttonByText(view.container, 'Submit'));
      await waitFor(() => expect(statValue(view.container, 'Current streak')).toBe('1'));
      expect(statValue(view.container, 'Best streak')).toBe('1');

      await click(buttonByText(view.container, 'Next'));
      await click(buttonContaining(view.container, 'Gamma'));
      await click(buttonByText(view.container, 'Submit'));
      await waitFor(() => expect(statValue(view.container, 'Current streak')).toBe('2'));
      expect(statValue(view.container, 'Best streak')).toBe('2');

      await click(buttonByText(view.container, 'Next'));
      await click(buttonContaining(view.container, 'Epsilon'));
      await click(buttonByText(view.container, 'Submit'));
      await waitFor(() => expect(statValue(view.container, 'Current streak')).toBe('0'));
      expect(statValue(view.container, 'Best streak')).toBe('2');
    } finally {
      view.unmount();
    }
  });

  it('keeps test session state after startTest loads questions', async () => {
    const testQuestions = cloneQuestions(questions.slice(0, 2));
    learningService.startTest.mockResolvedValue({
      testSessionId: 'session-1',
      startedAt: '2026-05-24T00:00:00Z',
      questions: testQuestions,
    });
    learningService.submitTestResult.mockResolvedValue({
      score: 50,
      correctCount: 1,
      totalQuestions: 2,
      answers: [
        { questionId: 101, correctAnswer: 'A' },
        { questionId: 102, correctAnswer: 'B' },
      ],
      weakQuestions: [],
      durationMs: 2000,
      masteryScoreAfterTest: 50,
      memoryScoreAfterTest: 50,
    });

    const view = await renderStudyHub('test');

    try {
      await waitFor(() => expect(view.container.textContent).toContain('Ready for Test Mode'));

      await click(buttonByText(view.container, 'Start test'));
      await waitFor(() => expect(view.container.textContent).toContain('Question 1 of 2'));

      await click(buttonContaining(view.container, 'Alpha'));
      await click(buttonByText(view.container, 'Next'));
      await waitFor(() => expect(view.container.textContent).toContain('Question 2 of 2'));

      await click(buttonContaining(view.container, 'Delta'));
      await click(buttonByText(view.container, 'Finish'));

      await waitFor(() => expect(learningService.submitTestResult).toHaveBeenCalled());
      expect(learningService.submitTestResult).toHaveBeenCalledWith(expect.objectContaining({
        testSessionId: 'session-1',
        answers: [
          expect.objectContaining({ questionId: 101, selectedAnswer: 'A' }),
          expect.objectContaining({ questionId: 102, selectedAnswer: 'B' }),
        ],
      }));
      await waitFor(() => expect(view.container.textContent).toContain('Test complete'));
    } finally {
      view.unmount();
    }
  });
});
