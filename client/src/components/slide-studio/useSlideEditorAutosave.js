import { useCallback, useEffect, useRef, useState } from 'react';

export default function useSlideEditorAutosave({ debounceMs = 1000, onSave }) {
  const [statusBySlideId, setStatusBySlideId] = useState({});
  const pendingRef = useRef({});
  const timerRef = useRef({});
  const onSaveRef = useRef(onSave);
  const scheduledRevisionRef = useRef({});
  const savedRevisionRef = useRef({});

  useEffect(() => {
    onSaveRef.current = onSave;
  }, [onSave]);

  const saveNow = useCallback(async (slideId, editorState) => {
    if (!slideId || !editorState) {
      return null;
    }

    const revision = editorState.revision || 0;

    setStatusBySlideId((current) => {
      if ((scheduledRevisionRef.current[slideId] || 0) > revision) {
        return current;
      }
      return { ...current, [slideId]: 'saving' };
    });

    try {
      const result = await onSave(slideId, editorState);

      if (pendingRef.current[slideId]?.revision === revision) {
        delete pendingRef.current[slideId];
      }

      savedRevisionRef.current[slideId] = Math.max(savedRevisionRef.current[slideId] || 0, revision);

      setStatusBySlideId((current) => {
        if ((scheduledRevisionRef.current[slideId] || 0) > revision) {
          return current;
        }
        return { ...current, [slideId]: 'saved' };
      });
      return result;
    } catch (error) {
      setStatusBySlideId((current) => {
        if ((scheduledRevisionRef.current[slideId] || 0) > revision) {
          return current;
        }
        return { ...current, [slideId]: 'error' };
      });
      throw error;
    }
  }, [onSave]);

  const scheduleSave = useCallback((slideId, editorState) => {
    if (!slideId || !editorState) {
      return;
    }

    const revision = editorState.revision || 0;
    scheduledRevisionRef.current[slideId] = Math.max(scheduledRevisionRef.current[slideId] || 0, revision);

    pendingRef.current[slideId] = editorState;
    setStatusBySlideId((current) => ({ ...current, [slideId]: 'dirty' }));

    if (timerRef.current[slideId]) {
      clearTimeout(timerRef.current[slideId]);
    }

    timerRef.current[slideId] = setTimeout(() => {
      const pending = pendingRef.current[slideId];
      if (pending) {
        saveNow(slideId, pending).catch(() => {});
      }
    }, debounceMs);
  }, [debounceMs, saveNow]);

  const flushSave = useCallback((slideId, editorState) => {
    if (timerRef.current[slideId]) {
      clearTimeout(timerRef.current[slideId]);
      timerRef.current[slideId] = null;
    }

    const targetState = editorState || pendingRef.current[slideId];
    if (targetState) {
      const revision = targetState.revision || 0;
      scheduledRevisionRef.current[slideId] = Math.max(scheduledRevisionRef.current[slideId] || 0, revision);
    }

    return saveNow(slideId, targetState);
  }, [saveNow]);

  useEffect(() => () => {
    Object.entries(timerRef.current).forEach(([slideId, timer]) => {
      if (timer) {
        clearTimeout(timer);
      }

      const pending = pendingRef.current[slideId];
      if (pending) {
        onSaveRef.current?.(Number(slideId), pending).catch(() => {});
      }
    });
  }, []);

  return {
    flushSave,
    scheduleSave,
    statusBySlideId,
  };
}
