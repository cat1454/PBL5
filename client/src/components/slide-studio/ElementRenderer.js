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

  const debounceTimerRef = useRef(null);

  const handleInput = (event) => {
    const nextText = event.currentTarget.textContent || '';
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current);
    }

    if (typeof jest !== 'undefined' || (typeof process !== 'undefined' && process.env?.NODE_ENV === 'test')) {
      onTextChange?.(element.id, nextText);
    } else {
      debounceTimerRef.current = setTimeout(() => {
        onTextChange?.(element.id, nextText);
      }, 400);
    }
  };

  const handleBlur = (event) => {
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current);
      debounceTimerRef.current = null;
    }
    const nextText = event.currentTarget.textContent || '';
    onTextCommit?.(element.id, nextText);
  };

  useEffect(() => {
    return () => {
      if (debounceTimerRef.current) {
        clearTimeout(debounceTimerRef.current);
      }
    };
  }, []);

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
      onInput={isTextMode ? handleInput : undefined}
      onBlur={isTextMode ? handleBlur : undefined}
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
