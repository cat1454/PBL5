export const getBoundedPresentationIndex = (index, total) => {
  if (!Number.isFinite(total) || total <= 0) {
    return 0;
  }

  if (!Number.isFinite(index)) {
    return 0;
  }

  return Math.max(0, Math.min(total - 1, index));
};

export const getPresentationStartIndex = (items, selectedSlideId) => {
  if (!Array.isArray(items) || !items.length) {
    return 0;
  }

  const selectedIndex = items.findIndex((item) => item.id === selectedSlideId);
  return selectedIndex >= 0 ? selectedIndex : 0;
};

export const getNextPresentationIndex = (currentIndex, total, direction) => (
  getBoundedPresentationIndex(currentIndex + direction, total)
);

export const getPresentationCanvasScale = ({
  viewportWidth,
  viewportHeight,
  canvasWidth = 1280,
  canvasHeight = 720,
}) => {
  const safeWidth = Number.isFinite(viewportWidth) ? viewportWidth : 1280;
  const safeHeight = Number.isFinite(viewportHeight) ? viewportHeight : 720;
  const maxWidth = Math.max(320, safeWidth - 168);
  const maxHeight = Math.max(240, safeHeight - 148);
  const widthScale = maxWidth / canvasWidth;
  const heightScale = maxHeight / canvasHeight;

  return Math.max(0.35, Math.min(1.25, widthScale, heightScale));
};

export const isPresentationTextInputTarget = (target) => {
  if (!target) {
    return false;
  }

  const tagName = target.tagName?.toLowerCase();
  return target.isContentEditable || ['input', 'textarea', 'select'].includes(tagName);
};
