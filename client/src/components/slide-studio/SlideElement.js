import React from 'react';
import { Rnd } from 'react-rnd';
import ElementRenderer from './ElementRenderer';

const getEffectClass = (effectPreset) => {
  const preset = String(effectPreset || 'none').trim().toLowerCase();
  return ['soft-shadow', 'neon-glow', 'glass-frame', 'paper-cut', 'duotone'].includes(preset)
    ? ` effect-${preset}`
    : '';
};

function SlideElement({ element, imageVm, labels, mode = 'layout', scale, selected, onCommit, onPatch, onSelect }) {
  const isEditMode = mode === 'layout' || mode === 'edit';
  const effectClass = getEffectClass(element.effectPreset);
  const elementStyle = {
    zIndex: element.zIndex,
  };

  const absoluteStyle = {
    position: 'absolute',
    left: element.x,
    top: element.y,
    width: element.width,
    height: element.height,
    zIndex: element.zIndex,
  };

  const handleMouseDown = (event) => {
    event.stopPropagation();
    if (isEditMode) {
      onSelect?.(element.id);
    }
  };

  const renderer = (
    <ElementRenderer
      element={element}
      imageVm={imageVm}
      labels={labels}
      mode={isEditMode ? 'edit' : mode}
      onTextChange={(elementId, text) => onPatch?.(elementId, { text })}
      onTextCommit={(elementId, text) => onCommit?.(elementId, { text })}
    />
  );

  if (!isEditMode) {
    return (
      <div
        className={`slide-canvas-element mode-${mode}${effectClass}`}
        data-slide-element-id={element.id}
        data-effect={element.effectPreset || 'none'}
        style={absoluteStyle}
      >
        {renderer}
      </div>
    );
  }

  return (
    <Rnd
      bounds="parent"
      scale={scale}
      size={{ width: element.width, height: element.height }}
      position={{ x: element.x, y: element.y }}
      dragGrid={[8, 8]}
      resizeGrid={[8, 8]}
      disableDragging={element.locked}
      enableResizing={!element.locked}
      onMouseDown={handleMouseDown}
      onDragStop={(event, data) => {
        onCommit(element.id, { x: data.x, y: data.y });
      }}
      onDrag={(event, data) => {
        onPatch(element.id, { x: data.x, y: data.y });
      }}
      onResizeStop={(event, direction, ref, delta, position) => {
        onCommit(element.id, {
          x: position.x,
          y: position.y,
          width: Number.parseFloat(ref.style.width),
          height: Number.parseFloat(ref.style.height),
        });
      }}
      onResize={(event, direction, ref, delta, position) => {
        onPatch(element.id, {
          x: position.x,
          y: position.y,
          width: Number.parseFloat(ref.style.width),
          height: Number.parseFloat(ref.style.height),
        });
      }}
      className={`slide-canvas-element${selected ? ' selected' : ''}${element.locked ? ' locked' : ''}${effectClass}`}
      data-slide-element-id={element.id}
      data-effect={element.effectPreset || 'none'}
      style={elementStyle}
    >
      {renderer}
    </Rnd>
  );
}

export default SlideElement;
