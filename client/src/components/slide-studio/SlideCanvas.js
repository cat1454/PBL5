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
  onAddImage,
  onAddText,
  remoteSelections = [],
  scale = 1,
  selectedElementId,
  onCommitElement,
  onDeleteElement,
  onDuplicateElement,
  onPatchElement,
  onPatchCanvas,
  onReplaceImage,
  onSelectElement,
}) {
  const canvas = editorState.canvas;
  const backgroundStyle = getCanvasBackgroundStyle(canvas.background);
  const isEditMode = mode === 'layout' || mode === 'edit';
  const selectedElement = editorState.elements.find((element) => element.id === selectedElementId) || null;
  const toolbarLeft = selectedElement
    ? Math.min(Math.max(selectedElement.x, 12), Math.max(12, canvas.width - 430))
    : 24;
  const toolbarTop = selectedElement
    ? Math.max(12, selectedElement.y - 54)
    : 24;
  const backgroundColors = ['#0f172a', '#1f6f8b', '#fff7ed', '#f8fafc'];

  const patchSelectedElement = (patch) => {
    if (!selectedElement) {
      return;
    }

    onCommitElement?.(selectedElement.id, patch);
  };

  const renderTextToolbar = () => (
    <div
      className="slide-mini-toolbar"
      style={{ left: toolbarLeft, top: toolbarTop }}
      onMouseDown={(event) => event.stopPropagation()}
    >
      <select
        aria-label={labels?.font || 'Font'}
        value={selectedElement.fontFamily || 'Lexend'}
        onChange={(event) => patchSelectedElement({ fontFamily: event.target.value })}
      >
        {['Lexend', 'Noto Sans', 'Segoe UI', 'Georgia'].map((font) => (
          <option key={font} value={font}>{font}</option>
        ))}
      </select>
      <input
        aria-label={labels?.fontSize || 'Size'}
        type="number"
        min="8"
        max="160"
        value={selectedElement.fontSize}
        onChange={(event) => patchSelectedElement({ fontSize: Number(event.target.value) || selectedElement.fontSize })}
      />
      <button
        type="button"
        className={selectedElement.bold ? 'active' : ''}
        onClick={() => patchSelectedElement({ bold: !selectedElement.bold })}
      >
        {labels?.bold || 'Bold'}
      </button>
      <input
        aria-label={labels?.color || 'Color'}
        type="color"
        value={selectedElement.color || '#ffffff'}
        onChange={(event) => patchSelectedElement({ color: event.target.value })}
      />
      {['left', 'center', 'right'].map((align) => (
        <button
          key={align}
          type="button"
          className={selectedElement.align === align ? 'active' : ''}
          onClick={() => patchSelectedElement({ align })}
        >
          {align}
        </button>
      ))}
      <button type="button" onClick={() => onDuplicateElement?.(selectedElement.id)}>
        {labels?.duplicate || 'Duplicate'}
      </button>
      <button type="button" className="danger" onClick={() => onDeleteElement?.(selectedElement.id)}>
        {labels?.delete || 'Delete'}
      </button>
    </div>
  );

  const renderImageToolbar = () => (
    <div
      className="slide-mini-toolbar"
      style={{ left: toolbarLeft, top: toolbarTop }}
      onMouseDown={(event) => event.stopPropagation()}
    >
      <button type="button" onClick={() => onReplaceImage?.(selectedElement.id)}>
        {labels?.replace || 'Replace'}
      </button>
      <button type="button" onClick={() => patchSelectedElement({ effectPreset: 'glass-frame' })}>
        {labels?.crop || 'Crop'}
      </button>
      <button type="button" onClick={() => patchSelectedElement({ effectPreset: 'soft-shadow' })}>
        {labels?.fit || 'Fit'}
      </button>
      <button type="button" onClick={() => onDuplicateElement?.(selectedElement.id)}>
        {labels?.duplicate || 'Duplicate'}
      </button>
      <button type="button" className="danger" onClick={() => onDeleteElement?.(selectedElement.id)}>
        {labels?.delete || 'Delete'}
      </button>
    </div>
  );

  const renderBackgroundToolbar = () => (
    <div
      className="slide-mini-toolbar slide-background-toolbar"
      style={{ left: toolbarLeft, top: toolbarTop }}
      onMouseDown={(event) => event.stopPropagation()}
    >
      <span>{labels?.background || 'Background'}</span>
      {backgroundColors.map((color) => (
        <button
          key={color}
          type="button"
          className="slide-color-swatch"
          style={{ '--swatch-color': color }}
          aria-label={`${labels?.background || 'Background'} ${color}`}
          onClick={() => onPatchCanvas?.({ background: color })}
        />
      ))}
      <button type="button" onClick={() => onPatchCanvas?.({ background: 'theme' })}>
        {labels?.theme || 'Theme'}
      </button>
      <button type="button" onClick={onAddText}>{labels?.addText || 'Add text'}</button>
      <button type="button" onClick={onAddImage}>{labels?.addImage || 'Add image'}</button>
    </div>
  );

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
          if (isEditMode) {
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
        {isEditMode && selectedElement?.type === 'text' && renderTextToolbar()}
        {isEditMode && selectedElement?.type === 'image' && renderImageToolbar()}
        {isEditMode && selectedElement && selectedElement.type !== 'text' && selectedElement.type !== 'image' && renderTextToolbar()}
        {isEditMode && !selectedElement && renderBackgroundToolbar()}
      </div>
    </div>
  );
}

export default SlideCanvas;
