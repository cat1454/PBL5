import axios from 'axios';

const API_BASE_URL = (process.env.REACT_APP_API_BASE_URL || '/api').replace(/\/$/, '');
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

  getDocumentProgress: async (id) => {
    const response = await axios.get(`${API_BASE_URL}/documents/${id}/progress`);
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

export const folderService = {
  createFolder: async ({ name, description, userId }) => {
    const response = await axios.post(`${API_BASE_URL}/folders`, {
      name,
      description,
      userId,
    });
    return response.data;
  },

  getUserFolders: async (userId) => {
    const response = await axios.get(`${API_BASE_URL}/folders/user/${userId}`);
    return response.data;
  },

  getFolder: async (id) => {
    const response = await axios.get(`${API_BASE_URL}/folders/${id}`);
    return response.data;
  },

  deleteFolder: async (id) => {
    await axios.delete(`${API_BASE_URL}/folders/${id}`);
  },

  uploadSource: async (folderId, file, userId, onProgress) => {
    const formData = new FormData();
    formData.append('file', file);
    formData.append('userId', userId);

    const response = await axios.post(`${API_BASE_URL}/folders/${folderId}/sources/upload`, formData, {
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
    const response = await axios.get(`${API_BASE_URL}/folders/${folderId}/sources`);
    return response.data;
  },

  updateSourceSelection: async (folderId, sourceId, includeInFolderSlides) => {
    const response = await axios.put(`${API_BASE_URL}/folders/${folderId}/sources/${sourceId}/slide-selection`, {
      includeInFolderSlides,
    });
    return response.data;
  },
};

export const workspaceService = {
  create: async ({ name, description, userId }) => {
    const response = await axios.post(`${API_BASE_URL}/workspaces`, {
      name,
      description,
      userId,
    });
    return response.data;
  },

  list: async (userId) => {
    const response = await axios.get(`${API_BASE_URL}/workspaces/user/${userId}`);
    return response.data;
  },

  get: async (workspaceId) => {
    const response = await axios.get(`${API_BASE_URL}/workspaces/${workspaceId}`);
    return response.data;
  },

  getDefault: async (userId) => {
    const response = await axios.get(`${API_BASE_URL}/workspaces/default/user/${userId}`);
    return response.data;
  },

  remove: async (workspaceId) => {
    await axios.delete(`${API_BASE_URL}/workspaces/${workspaceId}`);
  },

  uploadSource: async (workspaceId, file, userId, onProgress) => {
    const formData = new FormData();
    formData.append('file', file);
    formData.append('userId', userId);

    const response = await axios.post(`${API_BASE_URL}/workspaces/${workspaceId}/sources/upload`, formData, {
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
    const response = await axios.get(`${API_BASE_URL}/workspaces/${workspaceId}/sources`);
    return response.data;
  },

  updateSourceSelection: async (workspaceId, sourceId, includeInWorkspaceSlides) => {
    const response = await axios.put(`${API_BASE_URL}/workspaces/${workspaceId}/sources/${sourceId}/slide-selection`, {
      includeInWorkspaceSlides,
    });
    return response.data;
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

export const slideService = {
  startGenerateSlides: async (documentId, options = 8) => {
    const payload = typeof options === 'number'
      ? { desiredSlideCount: options }
      : options;

    const response = await axios.post(`${API_BASE_URL}/slides/generate/start`, {
      documentId,
      desiredSlideCount: payload?.desiredSlideCount || 8,
      themeKey: payload?.themeKey,
      audience: payload?.audience,
      tone: payload?.tone,
      narrativeGoal: payload?.narrativeGoal,
      languageStyle: payload?.languageStyle,
    });
    return response.data;
  },

  getGenerateProgress: async (jobId) => {
    const response = await axios.get(`${API_BASE_URL}/slides/generate/progress/${jobId}`);
    return response.data;
  },

  startGenerateSlidesForFolder: async (folderId, options = 8) => {
    const payload = typeof options === 'number'
      ? { desiredSlideCount: options }
      : options;

    const response = await axios.post(`${API_BASE_URL}/slides/folders/${folderId}/generate/start`, {
      desiredSlideCount: payload?.desiredSlideCount || 8,
      themeKey: payload?.themeKey,
      audience: payload?.audience,
      tone: payload?.tone,
      narrativeGoal: payload?.narrativeGoal,
      languageStyle: payload?.languageStyle,
    });
    return response.data;
  },

  getDeckByDocument: async (documentId) => {
    const response = await axios.get(`${API_BASE_URL}/slides/document/${documentId}`);
    return response.status === 204 ? null : response.data;
  },

  getDeckByFolder: async (folderId) => {
    const response = await axios.get(`${API_BASE_URL}/slides/folders/${folderId}`);
    return response.status === 204 ? null : response.data;
  },

  updateSlideItem: async (deckId, itemId, payload) => {
    const response = await axios.put(`${API_BASE_URL}/slides/${deckId}/items/${itemId}`, payload);
    return response.data;
  },

  refreshSlideItemImages: async (deckId, itemId) => {
    const response = await axios.post(`${API_BASE_URL}/slides/${deckId}/items/${itemId}/images/refresh`);
    return response.data;
  },

  selectSlideItemImage: async (deckId, itemId, candidateKey) => {
    const response = await axios.post(`${API_BASE_URL}/slides/${deckId}/items/${itemId}/images/select`, {
      candidateKey,
    });
    return response.data;
  },

  getDeckHtmlUrl: (documentId) => `${API_BASE_URL}/slides/document/${documentId}/html`,
  getFolderDeckHtmlUrl: (folderId) => `${API_BASE_URL}/slides/folders/${folderId}/html`,
};
