import React from 'react';

function ElementRenderer({ element, imageVm, labels }) {
  if (element.type === 'image') {
    const selectedImage = imageVm?.selectedImage;

    return (
      <div className="slide-canvas-image">
        {selectedImage?.localAssetUrl ? (
          <img
            src={selectedImage.localAssetUrl}
            alt={selectedImage.altText || labels?.imageAlt || ''}
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
      className={`slide-canvas-text role-${element.role}`}
      style={{
        color: element.color,
        fontSize: element.fontSize,
        fontWeight: element.bold ? 800 : 500,
        textAlign: element.align,
      }}
    >
      {element.text || labels?.emptyText}
    </div>
  );
}

export default ElementRenderer;
