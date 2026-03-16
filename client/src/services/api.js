import axios from 'axios';

const API_BASE_URL = process.env.REACT_APP_API_URL || 'http://localhost:5000/api';

export const documentService = {
  uploadDocument: async (file, userId, onProgress) => {
    const formData = new FormData();
    formData.append('file', file);
    formData.append('userId', userId);

    const response = await axios.post(`${API_BASE_URL}/documents/upload`, formData, {
      headers: {
        'Content-Type': 'multipart/form-data',
      },
      onUploadProgress: (progressEvent) => {
        const percentCompleted = Math.round((progressEvent.loaded * 100) / progressEvent.total);
        if (onProgress) {
          onProgress(percentCompleted);
        }
      },
    });
    return response.data;
  },

  getDocument: async (id) => {
    const response = await axios.get(`${API_BASE_URL}/documents/${id}`);
    return response.data;
  },

  getUserDocuments: async (userId) => {
    const response = await axios.get(`${API_BASE_URL}/documents/user/${userId}`);
    return response.data;
  },

  deleteDocument: async (id) => {
    await axios.delete(`${API_BASE_URL}/documents/${id}`);
  },
};

export const questionService = {
  generateQuestions: async (documentId, count = 5, questionType = null) => {
    const response = await axios.post(`${API_BASE_URL}/questions/generate`, {
      documentId,
      count,
      questionType,
    }, {
      timeout: 65000,
    });
    return response.data;
  },

  startGenerateQuestions: async (documentId, count = 5, questionType = null) => {
    const response = await axios.post(`${API_BASE_URL}/questions/generate/start`, {
      documentId,
      count,
      questionType,
    });
    return response.data;
  },

  getGenerateProgress: async (jobId) => {
    const response = await axios.get(`${API_BASE_URL}/questions/generate/progress/${jobId}`);
    return response.data;
  },

  getQuestionsByDocument: async (documentId) => {
    const response = await axios.get(`${API_BASE_URL}/questions/document/${documentId}`);
    return response.data;
  },
};

export const gameService = {
  createGameSession: async (documentId, userId, gameType, questionCount = 10) => {
    const response = await axios.post(`${API_BASE_URL}/games/sessions`, {
      documentId,
      userId,
      gameType,
      questionCount,
    });
    return response.data;
  },

  getGameSession: async (sessionId) => {
    const response = await axios.get(`${API_BASE_URL}/games/sessions/${sessionId}`);
    return response.data;
  },

  startGameSession: async (sessionId) => {
    const response = await axios.post(`${API_BASE_URL}/games/sessions/${sessionId}/start`);
    return response.data;
  },

  submitGameSession: async (sessionId, answers) => {
    const response = await axios.post(`${API_BASE_URL}/games/sessions/${sessionId}/submit`, {
      answers,
    });
    return response.data;
  },

  getQuizGame: async (documentId, count = 10) => {
    const response = await axios.get(`${API_BASE_URL}/games/quiz/${documentId}?count=${count}`);
    return response.data;
  },

  getFlashcards: async (documentId) => {
    const response = await axios.get(`${API_BASE_URL}/games/flashcards/${documentId}`);
    return response.data;
  },

  getUserGameSessions: async (userId) => {
    const response = await axios.get(`${API_BASE_URL}/games/user/${userId}`);
    return response.data;
  },
};
