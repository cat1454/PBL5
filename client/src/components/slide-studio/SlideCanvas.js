import React from 'react';
import SlideElement from './SlideElement';

const isHexColor = (value) => /^#(?:[0-9a-f]{3}|[0-9a-f]{6})$/i.test(String(value || '').trim());

const getCanvasBackgroundStyle = (background) => (
  isHexColor(background) ? { background: String(background).trim() } : {}
);

function SlideCanvas({
  editorState,
  imageVm,
  labels,
  mode = 'layout',
  remoteSelections = [],
  scale = 1,
  selectedElementId,
  onCommitElement,
  onPatchElement,
  onSelectElement,
}) {
  const canvas = editorState.canvas;
  const backgroundStyle = getCanvasBackgroundStyle(canvas.background);

  return (
    <div
      className="studio-editable-slide-viewport"
      style={{
        width: canvas.width * scale,
        height: canvas.height * scale,
      }}
    >
      <div
        className="studio-editable-slide"
        style={{
          width: canvas.width,
          height: canvas.height,
          transform: `scale(${scale})`,
          ...backgroundStyle,
        }}
        onMouseDown={() => {
          if (mode === 'layout') {
            onSelectElement?.(null);
          }
        }}
      >
        {editorState.elements.filter((element) => element.visible !== false).map((element) => (
          <React.Fragment key={element.id}>
            <SlideElement
              element={element}
              imageVm={imageVm}
              labels={labels}
              mode={mode}
              scale={scale}
              selected={selectedElementId === element.id}
              onCommit={onCommitElement || onPatchElement}
              onPatch={onPatchElement}
              onSelect={onSelectElement}
            />
            {mode === 'layout' && remoteSelections
              .filter((selection) => selection.elementId === element.id)
              .map((selection) => (
                <div
                  key={`${selection.clientId}-${element.id}`}
                  className="slide-remote-selection"
                  style={{
                    left: element.x,
                    top: element.y,
                    width: element.width,
                    height: element.height,
                    zIndex: element.zIndex + 1,
                  }}
                >
                  <span>{selection.displayName}</span>
                </div>
              ))}
          </React.Fragment>
        ))}
      </div>
    </div>
  );
}

export default SlideCanvas;
