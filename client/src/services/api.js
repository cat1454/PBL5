import axios from 'axios';

function resolveApiBaseUrl() {
  const defaultBaseUrl = process.env.NODE_ENV === 'production'
    ? 'https://pbl5-api.danangtoiiu.live'
    : 'http://localhost:5000';
  const configuredBaseUrl = (process.env.REACT_APP_API_BASE_URL || defaultBaseUrl).replace(/\/$/, '');
  return configuredBaseUrl.endsWith('/api') ? configuredBaseUrl : `${configuredBaseUrl}/api`;
}

const API_BASE_URL = resolveApiBaseUrl();

const apiClient = axios.create({
  baseURL: API_BASE_URL,
});

function getFilenameFromContentDisposition(disposition, fallback) {
  if (!disposition || typeof disposition !== 'string') {
    return fallback;
  }

  const utf8Match = disposition.match(/filename\*=UTF-8''([^;]+)/i);
  if (utf8Match?.[1]) {
    return decodeURIComponent(utf8Match[1].replace(/"/g, '').trim());
  }

  const filenameMatch = disposition.match(/filename="?([^";]+)"?/i);
  return filenameMatch?.[1]?.trim() || fallback;
}

function triggerBlobDownload(blob, filename) {
  const url = window.URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.setTimeout(() => window.URL.revokeObjectURL(url), 1000);
}

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

export function getApiErrorMessage(error, fallback = 'Request failed.') {
  const data = error?.response?.data;

  if (typeof data === 'string' && data.trim()) {
    return data;
  }

  if (data && typeof data === 'object') {
    const candidates = [
      data.message,
      data.error,
      data.title,
      data.detail,
      data.reason,
    ];

    for (const candidate of candidates) {
      if (typeof candidate === 'string' && candidate.trim()) {
        return candidate;
      }
    }

    if (data.errors && typeof data.errors === 'object') {
      const firstErrors = Object.values(data.errors).flat();
      const firstMessage = firstErrors.find((item) => typeof item === 'string' && item.trim());
      if (firstMessage) {
        return firstMessage;
      }
    }
  }

  return error?.message || fallback;
}

export function getApiErrorCode(error) {
  const data = error?.response?.data;
  const code = data?.code || data?.errorCode || data?.error_key;
  return typeof code === 'string' ? code.trim() : '';
}

export function isApiNotFound(error) {
  return error?.response?.status === 404;
}

export function isApiForbidden(error) {
  return error?.response?.status === 403;
}

export function isApiJobNotFound(error) {
  if (!isApiNotFound(error)) {
    return false;
  }

  const code = getApiErrorCode(error).toLowerCase();
  if (code === 'job_not_found') {
    return true;
  }

  const message = getApiErrorMessage(error, '').toLowerCase();
  return message.includes('job not found');
}

export function isSlideSchemaUnavailable(error) {
  const code = getApiErrorCode(error).toLowerCase();
  if (code === 'slide_schema_unavailable') {
    return true;
  }

  const status = error?.response?.status;
  if (status !== 500 && status !== 503) {
    return false;
  }

  const message = getApiErrorMessage(error, '').toLowerCase();

  return message.includes('slide schema')
    || message.includes('slide_decks')
    || message.includes('slide_items')
    || message.includes('image_plan')
    || message.includes('image_candidates')
    || message.includes('selected_image_key')
    || message.includes('editor_state');
}

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
  uploadDocument: async (file, onProgress) => {
    const formData = new FormData();
    formData.append('file', file);

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

  getLatestUnderstanding: async (id) => {
    const response = await apiClient.get(`/documents/${id}/understanding/latest`);
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
  createFolder: async ({ name, description }) => {
    const response = await apiClient.post('/folders', {
      name,
      description,
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

  uploadSource: async (folderId, file, onProgress) => {
    const formData = new FormData();
    formData.append('file', file);

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
  create: async ({ name, description }) => {
    const response = await apiClient.post('/workspaces', {
      name,
      description,
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

  uploadSource: async (workspaceId, file, onProgress) => {
    const formData = new FormData();
    formData.append('file', file);

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
  generateQuestions: async (documentId, count = 5, questionType = null, options = {}) => {
    const response = await apiClient.post('/questions/generate', {
      documentId,
      count,
      questionType,
      confirmLowConfidence: Boolean(options?.confirmLowConfidence),
    }, {
      timeout: 65000,
    });
    return response.data;
  },

  startGenerateQuestions: async (documentId, count = 5, questionType = null, options = {}) => {
    const response = await apiClient.post('/questions/generate/start', {
      documentId,
      count,
      questionType,
      confirmLowConfidence: Boolean(options?.confirmLowConfidence),
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

  getQuestionMetrics: async (documentId) => {
    const response = await apiClient.get(`/questions/document/${documentId}/metrics`);
    return response.data;
  },
};

export const questionStudioService = {
  startRun: async ({ documentId, targetDraftCount = 30, mode = 'balanced', questionTypes, difficulties }) => {
    const response = await apiClient.post('/question-studio/runs/start', {
      documentId,
      targetDraftCount,
      mode,
      questionTypes,
      difficulties,
    });
    return response.data;
  },

  getRun: async (runId) => {
    const response = await apiClient.get(`/question-studio/runs/${runId}`);
    return response.data;
  },

  listDrafts: async (params) => {
    const response = await apiClient.get('/question-studio/drafts', { params });
    return response.data;
  },

  updateDraft: async (draftId, payload) => {
    const response = await apiClient.put(`/question-studio/drafts/${draftId}`, payload);
    return response.data;
  },

  acceptDraft: async (draftId) => {
    const response = await apiClient.post(`/question-studio/drafts/${draftId}/accept`);
    return response.data;
  },

  rejectDraft: async (draftId) => {
    const response = await apiClient.post(`/question-studio/drafts/${draftId}/reject`);
    return response.data;
  },

  quarantineDraft: async (draftId) => {
    const response = await apiClient.post(`/question-studio/drafts/${draftId}/quarantine`);
    return response.data;
  },

  restoreDraft: async (draftId) => {
    const response = await apiClient.post(`/question-studio/drafts/${draftId}/restore`);
    return response.data;
  },

  importDrafts: async ({ documentId, draftIds }) => {
    const response = await apiClient.post('/question-studio/import', {
      documentId,
      draftIds,
    });
    return response.data;
  },
};

export const learningService = {
  recordAttempt: async ({ documentId, questionId, mode, selectedAnswer, isCorrect, confidence, responseTimeMs }) => {
    const response = await apiClient.post('/learning/attempts', {
      documentId,
      questionId,
      mode,
      selectedAnswer,
      isCorrect,
      confidence,
      responseTimeMs,
    });
    return response.data;
  },

  startTest: async ({ documentId, count = 10, testType = 4 }) => {
    const response = await apiClient.post('/learning/tests/start', {
      documentId,
      count,
      testType,
    });
    return response.data;
  },

  submitTestResult: async ({ testSessionId, durationMs, answers }) => {
    const response = await apiClient.post('/learning/tests/submit', {
      testSessionId,
      durationMs,
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

  getReviewQueue: async (documentId) => {
    const response = await apiClient.get(`/learning/review-queue/${documentId}`);
    return response.data;
  },

  getDocumentSummary: async (documentId) => {
    const response = await apiClient.get(`/learning/progress/summary/${documentId}`);
    return response.data;
  },

  exportAttemptsCsv: async (filters = {}) => {
    const response = await apiClient.get('/learning/export/attempts.csv', {
      params: filters,
      responseType: 'blob',
    });
    return response.data;
  },

  exportProgressCsv: async (filters = {}) => {
    const response = await apiClient.get('/learning/export/progress.csv', {
      params: filters,
      responseType: 'blob',
    });
    return response.data;
  },

  exportTestResultsCsv: async (filters = {}) => {
    const response = await apiClient.get('/learning/export/test-results.csv', {
      params: filters,
      responseType: 'blob',
    });
    return response.data;
  },
};

export const analyticsService = {
  getPersonalSummary: async () => {
    const response = await apiClient.get('/analytics/personal');
    return response.data;
  },

  recordEvents: async (events) => {
    const response = await apiClient.post('/analytics/events', {
      events,
    });
    return response.data;
  },
};

export const gameService = {
  createGameSession: async (documentId, gameType, questionCount = 10) => {
    const response = await apiClient.post('/games/sessions', {
      documentId,
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

  getQuizGame: async (documentId, count = 10, { includeAnswers = false } = {}) => {
    const response = await apiClient.get(`/games/quiz/${documentId}?count=${count}&includeAnswers=${includeAnswers}`);
    return response.data;
  },

  submitQuizAnswer: async (documentId, questionId, selectedAnswer) => {
    const response = await apiClient.post(`/games/quiz/${documentId}/answers`, {
      questionId,
      selectedAnswer,
    });
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
      confirmLowConfidence: Boolean(payload?.confirmLowConfidence),
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
      confirmLowConfidence: Boolean(payload?.confirmLowConfidence),
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

  exportDeckHtml: async (deckId) => {
    const response = await apiClient.get(`/slides/${deckId}/export/html`, {
      responseType: 'blob',
    });
    const filename = getFilenameFromContentDisposition(
      response.headers?.['content-disposition'],
      `slide-deck-${deckId}.html`
    );
    triggerBlobDownload(response.data, filename);
    return { filename };
  },

  exportDeckPptx: async (deckId) => {
    const response = await apiClient.get(`/slides/${deckId}/export/pptx`, {
      responseType: 'blob',
    });
    const filename = getFilenameFromContentDisposition(
      response.headers?.['content-disposition'],
      `slide-deck-${deckId}.pptx`
    );
    triggerBlobDownload(response.data, filename);
    return { filename };
  },

  getDeckPrintUrl: (deckId) => `${API_BASE_URL}/slides/${deckId}/export/print`,

  getDeckPrintHtml: async (deckId) => {
    const response = await apiClient.get(`/slides/${deckId}/export/print`, {
      responseType: 'blob',
    });
    return response.data;
  },

  getDeckHtmlUrl: (documentId) => `${API_BASE_URL}/slides/document/${documentId}/html`,
  getFolderDeckHtmlUrl: (folderId) => `${API_BASE_URL}/slides/folders/${folderId}/html`,
};

export { API_BASE_URL, apiClient };
