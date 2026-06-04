import { useCallback, useState } from 'react';

export default function useSlideEditorHistory() {
  const [history, setHistory] = useState({});

  const getHistory = useCallback((slideId) => (
    history[slideId] || { past: [], future: [] }
  ), [history]);

  const pushHistory = useCallback((slideId, editorState) => {
    if (!slideId || !editorState) {
      return;
    }

    setHistory((current) => {
      const state = current[slideId] || { past: [], future: [] };
      return {
        ...current,
        [slideId]: {
          past: [...state.past.slice(-39), editorState],
          future: [],
        },
      };
    });
  }, []);

  const clearHistory = useCallback((slideId) => {
    setHistory((current) => ({
      ...current,
      [slideId]: { past: [], future: [] },
    }));
  }, []);

  const undo = useCallback((slideId, currentEditorState) => {
    const state = history[slideId] || { past: [], future: [] };
    if (!state.past.length) {
      return null;
    }

    const previous = state.past[state.past.length - 1];
    setHistory((current) => ({
      ...current,
      [slideId]: {
        past: state.past.slice(0, -1),
        future: [currentEditorState, ...state.future],
      },
    }));
    return previous;
  }, [history]);

  const redo = useCallback((slideId, currentEditorState) => {
    const state = history[slideId] || { past: [], future: [] };
    if (!state.future.length) {
      return null;
    }

    const next = state.future[0];
    setHistory((current) => ({
      ...current,
      [slideId]: {
        past: [...state.past, currentEditorState],
        future: state.future.slice(1),
      },
    }));
    return next;
  }, [history]);

  return {
    clearHistory,
    getHistory,
    pushHistory,
    redo,
    undo,
  };
}
