import React from 'react';
import SlideElement from './SlideElement';

function SlideCanvas({
  editorState,
  imageVm,
  labels,
  remoteSelections = [],
  scale = 1,
  selectedElementId,
  onCommitElement,
  onPatchElement,
  onSelectElement,
}) {
  const canvas = editorState.canvas;

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
        }}
        onMouseDown={() => onSelectElement(null)}
      >
        {editorState.elements.map((element) => (
          <React.Fragment key={element.id}>
            <SlideElement
              element={element}
              imageVm={imageVm}
              labels={labels}
              scale={scale}
              selected={selectedElementId === element.id}
              onCommit={onCommitElement || onPatchElement}
              onPatch={onPatchElement}
              onSelect={onSelectElement}
            />
            {remoteSelections
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
