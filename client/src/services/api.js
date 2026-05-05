import axios from 'axios';

const API_BASE_URL = (process.env.REACT_APP_API_BASE_URL || '/api').replace(/\/$/, '');

const apiClient = axios.create({
  baseURL: API_BASE_URL,
});

let authToken = '';
let onUnauthorized = null;

export function setApiAuthToken(token) {
  authToken = token || '';
}

export function setApiUnauthorizedHandler(handler) {
  onUnauthorized = typeof handler === 'function' ? handler : null;
}

apiClient.interceptors.request.use((config) => {
  if (authToken) {
    config.headers = {
      ...config.headers,
      Authorization: `Bearer ${authToken}`,
    };
  }

  return config;
});

apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    const requestUrl = error?.config?.url || '';
    const isAuthRequest = requestUrl.includes('/auth/login') || requestUrl.includes('/auth/register');

    if (error?.response?.status === 401 && !isAuthRequest && onUnauthorized) {
      onUnauthorized();
    }

    return Promise.reject(error);
  }
);

export const authService = {
  register: async (payload) => {
    const response = await apiClient.post('/auth/register', payload);
    return response.data;
  },

  login: async (payload) => {
    const response = await apiClient.post('/auth/login', payload);
    return response.data;
  },

  me: async () => {
    const response = await apiClient.get('/auth/me');
    return response.data;
  },
};

export const adminService = {
  getOverview: async () => {
    const response = await apiClient.get('/admin/overview');
    return response.data;
  },
};

export const documentService = {
  uploadDocument: async (file, userId, onProgress) => {
    const formData = new FormData();
    formData.append('file', file);
    formData.append('userId', userId || '');

    const response = await apiClient.post('/documents/upload', formData, {
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
    const response = await apiClient.get(`/documents/${id}`);
    return response.data;
  },

  getDocumentProgress: async (id) => {
    const response = await apiClient.get(`/documents/${id}/progress`);
    return response.data;
  },

  getStructure: async (id) => {
    const response = await apiClient.get(`/documents/${id}/structure`);
    return response.data;
  },

  analyzeStructure: async (id) => {
    const response = await apiClient.post(`/documents/${id}/analyze-structure`);
    return response.data;
  },

  getUserDocuments: async (userId) => {
    const response = await apiClient.get(`/documents/user/${userId}`);
    return response.data;
  },

  deleteDocument: async (id) => {
    await apiClient.delete(`/documents/${id}`);
  },
};

export const folderService = {
  createFolder: async ({ name, description, userId }) => {
    const response = await apiClient.post('/folders', {
      name,
      description,
      userId,
    });
    return response.data;
  },

  getUserFolders: async (userId) => {
    const response = await apiClient.get(`/folders/user/${userId}`);
    return response.data;
  },

  getFolder: async (id) => {
    const response = await apiClient.get(`/folders/${id}`);
    return response.data;
  },

  deleteFolder: async (id) => {
    await apiClient.delete(`/folders/${id}`);
  },

  uploadSource: async (folderId, file, userId, onProgress) => {
    const formData = new FormData();
    formData.append('file', file);
    formData.append('userId', userId || '');

    const response = await apiClient.post(`/folders/${folderId}/sources/upload`, formData, {
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

  getSources: async (folderId) => {
    const response = await apiClient.get(`/folders/${folderId}/sources`);
    return response.data;
  },

  updateSourceSelection: async (folderId, sourceId, includeInFolderSlides) => {
    const response = await apiClient.put(`/folders/${folderId}/sources/${sourceId}/slide-selection`, {
      includeInFolderSlides,
    });
    return response.data;
  },
};

export const workspaceService = {
  create: async ({ name, description, userId }) => {
    const response = await apiClient.post('/workspaces', {
      name,
      description,
      userId,
    });
    return response.data;
  },

  list: async (userId) => {
    const response = await apiClient.get(`/workspaces/user/${userId}`);
    return response.data;
  },

  get: async (workspaceId) => {
    const response = await apiClient.get(`/workspaces/${workspaceId}`);
    return response.data;
  },

  getDefault: async (userId) => {
    const response = await apiClient.get(`/workspaces/default/user/${userId}`);
    return response.data;
  },

  remove: async (workspaceId) => {
    await apiClient.delete(`/workspaces/${workspaceId}`);
  },

  uploadSource: async (workspaceId, file, userId, onProgress) => {
    const formData = new FormData();
    formData.append('file', file);
    formData.append('userId', userId || '');

    const response = await apiClient.post(`/workspaces/${workspaceId}/sources/upload`, formData, {
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

  listSources: async (workspaceId) => {
    const response = await apiClient.get(`/workspaces/${workspaceId}/sources`);
    return response.data;
  },

  updateSourceSelection: async (workspaceId, sourceId, includeInWorkspaceSlides) => {
    const response = await apiClient.put(`/workspaces/${workspaceId}/sources/${sourceId}/slide-selection`, {
      includeInWorkspaceSlides,
    });
    return response.data;
  },
};

export const questionService = {
  generateQuestions: async (documentId, count = 5, questionType = null) => {
    const response = await apiClient.post('/questions/generate', {
      documentId,
      count,
      questionType,
    }, {
      timeout: 65000,
    });
    return response.data;
  },

  startGenerateQuestions: async (documentId, count = 5, questionType = null) => {
    const response = await apiClient.post('/questions/generate/start', {
      documentId,
      count,
      questionType,
    });
    return response.data;
  },

  getGenerateProgress: async (jobId) => {
    const response = await apiClient.get(`/questions/generate/progress/${jobId}`);
    return response.data;
  },

  getQuestionsByDocument: async (documentId) => {
    const response = await apiClient.get(`/questions/document/${documentId}`);
    return response.data;
  },
};

export const learningService = {
  recordAttempt: async ({ documentId, questionId, mode, selectedAnswer, isCorrect, responseTimeMs }) => {
    const response = await apiClient.post('/learning/attempts', {
      documentId,
      questionId,
      mode,
      selectedAnswer,
      isCorrect,
      responseTimeMs,
    });
    return response.data;
  },

  submitTestResult: async ({ documentId, testType = 4, startedAt, durationMs, attemptsAlreadyRecorded = false, answers }) => {
    const response = await apiClient.post('/learning/tests/submit', {
      documentId,
      testType,
      startedAt,
      durationMs,
      attemptsAlreadyRecorded,
      answers,
    });
    return response.data;
  },

  submitTest: async (payload) => learningService.submitTestResult(payload),

  getDocumentTestResults: async (documentId) => {
    const response = await apiClient.get(`/learning/tests/document/${documentId}`);
    return response.data;
  },

  getDocumentTestSummary: async (documentId) => {
    const response = await apiClient.get(`/learning/tests/summary/${documentId}`);
    return response.data;
  },

  getDocumentProgress: async (documentId) => {
    const response = await apiClient.get(`/learning/progress/document/${documentId}`);
    return response.data;
  },

  getDocumentSummary: async (documentId) => {
    const response = await apiClient.get(`/learning/progress/summary/${documentId}`);
    return response.data;
  },
};

export const gameService = {
  createGameSession: async (documentId, userId, gameType, questionCount = 10) => {
    const response = await apiClient.post('/games/sessions', {
      documentId,
      userId,
      gameType,
      questionCount,
    });
    return response.data;
  },

  getGameSession: async (sessionId) => {
    const response = await apiClient.get(`/games/sessions/${sessionId}`);
    return response.data;
  },

  startGameSession: async (sessionId) => {
    const response = await apiClient.post(`/games/sessions/${sessionId}/start`);
    return response.data;
  },

  submitGameSession: async (sessionId, answers) => {
    const response = await apiClient.post(`/games/sessions/${sessionId}/submit`, {
      answers,
    });
    return response.data;
  },

  getQuizGame: async (documentId, count = 10) => {
    const response = await apiClient.get(`/games/quiz/${documentId}?count=${count}`);
    return response.data;
  },

  getFlashcards: async (documentId) => {
    const response = await apiClient.get(`/games/flashcards/${documentId}`);
    return response.data;
  },

  getUserGameSessions: async (userId) => {
    const response = await apiClient.get(`/games/user/${userId}`);
    return response.data;
  },
};

export const slideService = {
  startGenerateSlides: async (documentId, options = 8) => {
    const payload = typeof options === 'number'
      ? { desiredSlideCount: options }
      : options;

    const response = await apiClient.post('/slides/generate/start', {
      documentId,
      desiredSlideCount: payload?.desiredSlideCount || 8,
      themeKey: payload?.themeKey,
      audience: payload?.audience,
      tone: payload?.tone,
      narrativeGoal: payload?.narrativeGoal,
      languageStyle: payload?.languageStyle,
      sourceIds: payload?.sourceIds,
      selectedSectionIds: payload?.selectedSectionIds,
      mode: payload?.mode,
      scopePolicy: payload?.scopePolicy,
    });
    return response.data;
  },

  getGenerateProgress: async (jobId) => {
    const response = await apiClient.get(`/slides/generate/progress/${jobId}`);
    return response.data;
  },

  startGenerateSlidesForFolder: async (folderId, options = 8) => {
    const payload = typeof options === 'number'
      ? { desiredSlideCount: options }
      : options;

    const response = await apiClient.post(`/slides/folders/${folderId}/generate/start`, {
      desiredSlideCount: payload?.desiredSlideCount || 8,
      themeKey: payload?.themeKey,
      audience: payload?.audience,
      tone: payload?.tone,
      narrativeGoal: payload?.narrativeGoal,
      languageStyle: payload?.languageStyle,
      sourceIds: payload?.sourceIds,
      selectedSectionIds: payload?.selectedSectionIds,
      mode: payload?.mode,
      scopePolicy: payload?.scopePolicy,
    });
    return response.data;
  },

  getDeckByDocument: async (documentId) => {
    const response = await apiClient.get(`/slides/document/${documentId}`);
    return response.status === 204 ? null : response.data;
  },

  getDeckByFolder: async (folderId) => {
    const response = await apiClient.get(`/slides/folders/${folderId}`);
    return response.status === 204 ? null : response.data;
  },

  updateSlideItem: async (deckId, itemId, payload) => {
    const response = await apiClient.put(`/slides/${deckId}/items/${itemId}`, payload);
    return response.data;
  },

  refreshSlideItemImages: async (deckId, itemId) => {
    const response = await apiClient.post(`/slides/${deckId}/items/${itemId}/images/refresh`);
    return response.data;
  },

  selectSlideItemImage: async (deckId, itemId, candidateKey) => {
    const response = await apiClient.post(`/slides/${deckId}/items/${itemId}/images/select`, {
      candidateKey,
    });
    return response.data;
  },

  getDeckHtmlUrl: (documentId) => `${API_BASE_URL}/slides/document/${documentId}/html`,
  getFolderDeckHtmlUrl: (folderId) => `${API_BASE_URL}/slides/folders/${folderId}/html`,
};

export { API_BASE_URL, apiClient };
