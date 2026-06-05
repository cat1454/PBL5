import React, { useEffect, useRef } from 'react';

const pickImageSource = (element, imageVm) => {
  const selectedImage = imageVm?.selectedImage;
  return element?.src
    || element?.url
    || element?.imageUrl
    || element?.localAssetUrl
    || element?.base64
    || selectedImage?.localAssetUrl
    || selectedImage?.imageUrl
    || selectedImage?.url
    || selectedImage?.thumbnailUrl
    || '';
};

function ElementRenderer({ element, imageVm, labels, mode = 'layout', onTextChange, onTextCommit }) {
  const isTextMode = mode === 'text' || mode === 'edit' || mode === 'layout';
  const textRef = useRef(null);
  const text = element.text || '';
  const isCleanMode = mode === 'preview' || mode === 'clean';
  const displayText = text || (isCleanMode ? '' : labels?.emptyText);

  useEffect(() => {
    if (element.type === 'image') {
      return;
    }

    const node = textRef.current;
    if (!node || node.textContent === displayText) {
      return;
    }

    if (isTextMode && document.activeElement === node) {
      return;
    }

    node.textContent = displayText;
  }, [displayText, element.type, isTextMode]);

  if (element.type === 'image') {
    const selectedImage = imageVm?.selectedImage;
    const imageSource = pickImageSource(element, imageVm);

    return (
      <div className="slide-canvas-image">
        {imageSource ? (
          <img
            src={imageSource}
            alt={selectedImage?.altText || labels?.imageAlt || ''}
          />
        ) : (
          <div className="slide-canvas-image-placeholder">
            <strong>{imageVm?.badgeLabel || labels?.imagePlaceholderTitle}</strong>
            <span>{imageVm?.statusLabel || labels?.imagePlaceholderBody}</span>
          </div>
        )}
      </div>
    );
  }

  return (
    <div
      ref={textRef}
      className={`slide-canvas-text role-${element.role}`}
      contentEditable={isTextMode}
      spellCheck={false}
      suppressContentEditableWarning
      onInput={isTextMode ? (event) => onTextChange?.(element.id, event.currentTarget.textContent || '') : undefined}
      onBlur={isTextMode ? (event) => onTextCommit?.(element.id, event.currentTarget.textContent || '') : undefined}
      style={{
        color: element.color,
        fontFamily: element.fontFamily,
        fontSize: element.fontSize,
        fontWeight: element.bold ? 800 : 500,
        textAlign: element.align,
      }}
    >
      {displayText}
    </div>
  );
}

export default ElementRenderer;
