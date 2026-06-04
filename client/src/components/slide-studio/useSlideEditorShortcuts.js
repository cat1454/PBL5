import { useEffect } from 'react';

const isTextInputTarget = (target) => {
  if (!target) {
    return false;
  }

  const tagName = target.tagName?.toLowerCase();
  return target.isContentEditable || ['input', 'textarea', 'select'].includes(tagName);
};

export default function useSlideEditorShortcuts({
  active,
  selectedElementId,
  onBringForward,
  onClearSelection,
  onCopy,
  onDelete,
  onDuplicate,
  onMove,
  onPaste,
  onRedo,
  onSave,
  onSendBackward,
  onUndo,
}) {
  useEffect(() => {
    if (!active) {
      return undefined;
    }

    const handleKeyDown = (event) => {
      if (isTextInputTarget(event.target)) {
        return;
      }

      const key = event.key.toLowerCase();
      const isCtrl = event.ctrlKey || event.metaKey;
      const step = event.shiftKey ? 10 : 1;

      if (isCtrl && key === 's') {
        event.preventDefault();
        onSave();
        return;
      }

      if (isCtrl && key === 'z') {
        event.preventDefault();
        if (event.shiftKey) {
          onRedo();
        } else {
          onUndo();
        }
        return;
      }

      if (isCtrl && key === 'y') {
        event.preventDefault();
        onRedo();
        return;
      }

      if (isCtrl && key === 'd') {
        event.preventDefault();
        onDuplicate();
        return;
      }

      if (isCtrl && key === 'c') {
        event.preventDefault();
        onCopy();
        return;
      }

      if (isCtrl && key === 'v') {
        event.preventDefault();
        onPaste();
        return;
      }

      if (isCtrl && event.key === ']') {
        event.preventDefault();
        onBringForward();
        return;
      }

      if (isCtrl && event.key === '[') {
        event.preventDefault();
        onSendBackward();
        return;
      }

      if (event.key === 'Escape') {
        event.preventDefault();
        onClearSelection();
        return;
      }

      if ((event.key === 'Delete' || event.key === 'Backspace') && selectedElementId) {
        event.preventDefault();
        onDelete();
        return;
      }

      const movement = {
        ArrowLeft: { x: -step, y: 0 },
        ArrowRight: { x: step, y: 0 },
        ArrowUp: { x: 0, y: -step },
        ArrowDown: { x: 0, y: step },
      }[event.key];

      if (movement && selectedElementId) {
        event.preventDefault();
        onMove(movement);
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [
    active,
    selectedElementId,
    onBringForward,
    onClearSelection,
    onCopy,
    onDelete,
    onDuplicate,
    onMove,
    onPaste,
    onRedo,
    onSave,
    onSendBackward,
    onUndo,
  ]);
}
