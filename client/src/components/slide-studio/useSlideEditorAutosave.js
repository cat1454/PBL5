import { useCallback, useEffect, useRef, useState } from 'react';

export default function useSlideEditorAutosave({ debounceMs = 1000, onSave }) {
  const [statusBySlideId, setStatusBySlideId] = useState({});
  const pendingRef = useRef({});
  const timerRef = useRef({});

  const saveNow = useCallback(async (slideId, editorState) => {
    if (!slideId || !editorState) {
      return null;
    }

    setStatusBySlideId((current) => ({ ...current, [slideId]: 'saving' }));
    try {
      const result = await onSave(slideId, editorState);
      delete pendingRef.current[slideId];
      setStatusBySlideId((current) => ({ ...current, [slideId]: 'saved' }));
      return result;
    } catch (error) {
      setStatusBySlideId((current) => ({ ...current, [slideId]: 'error' }));
      throw error;
    }
  }, [onSave]);

  const scheduleSave = useCallback((slideId, editorState) => {
    if (!slideId || !editorState) {
      return;
    }

    pendingRef.current[slideId] = editorState;
    setStatusBySlideId((current) => ({ ...current, [slideId]: 'dirty' }));

    if (timerRef.current[slideId]) {
      clearTimeout(timerRef.current[slideId]);
    }

    timerRef.current[slideId] = setTimeout(() => {
      const pending = pendingRef.current[slideId];
      saveNow(slideId, pending).catch(() => {});
    }, debounceMs);
  }, [debounceMs, saveNow]);

  const flushSave = useCallback((slideId, editorState) => {
    if (timerRef.current[slideId]) {
      clearTimeout(timerRef.current[slideId]);
      timerRef.current[slideId] = null;
    }

    return saveNow(slideId, editorState || pendingRef.current[slideId]);
  }, [saveNow]);

  useEffect(() => () => {
    Object.values(timerRef.current).forEach((timer) => {
      if (timer) {
        clearTimeout(timer);
      }
    });
  }, []);

  return {
    flushSave,
    scheduleSave,
    statusBySlideId,
  };
}
