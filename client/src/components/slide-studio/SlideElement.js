import React from 'react';
import { Rnd } from 'react-rnd';
import ElementRenderer from './ElementRenderer';

const getEffectClass = (effectPreset) => {
  const preset = String(effectPreset || 'none').trim().toLowerCase();
  return ['soft-shadow', 'neon-glow', 'glass-frame', 'paper-cut', 'duotone'].includes(preset)
    ? ` effect-${preset}`
    : '';
};

function SlideElement({
  element,
  imageVm,
  labels,
  mode = 'layout',
  scale,
  selected,
  onCommit,
  onPatch,
  onSelect,
  snapGrid = null,
}) {
  const isEditMode = mode === 'layout' || mode === 'edit';
  const effectClass = getEffectClass(element.effectPreset);
  const elementStyle = {
    zIndex: element.zIndex,
  };

  const activeDragRef = React.useRef(false);
  const transientPosRef = React.useRef({ x: element.x, y: element.y });
  const transientSizeRef = React.useRef({ width: element.width, height: element.height });

  if (!activeDragRef.current) {
    transientPosRef.current = { x: element.x, y: element.y };
    transientSizeRef.current = { width: element.width, height: element.height };
  }

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
      size={activeDragRef.current ? transientSizeRef.current : { width: element.width, height: element.height }}
      position={activeDragRef.current ? transientPosRef.current : { x: element.x, y: element.y }}
      dragGrid={snapGrid || undefined}
      resizeGrid={snapGrid || undefined}
      disableDragging={element.locked}
      enableResizing={!element.locked}
      onMouseDown={handleMouseDown}
      onDragStart={() => {
        activeDragRef.current = true;
        transientPosRef.current = { x: element.x, y: element.y };
      }}
      onDrag={(event, data) => {
        transientPosRef.current = { x: data.x, y: data.y };
      }}
      onDragStop={(event, data) => {
        activeDragRef.current = false;
        onCommit(element.id, { x: data.x, y: data.y });
      }}
      onResizeStart={() => {
        activeDragRef.current = true;
        transientPosRef.current = { x: element.x, y: element.y };
        transientSizeRef.current = { width: element.width, height: element.height };
      }}
      onResize={(event, direction, ref, delta, position) => {
        transientPosRef.current = { x: position.x, y: position.y };
        transientSizeRef.current = {
          width: Number.parseFloat(ref.style.width),
          height: Number.parseFloat(ref.style.height),
        };
      }}
      onResizeStop={(event, direction, ref, delta, position) => {
        activeDragRef.current = false;
        onCommit(element.id, {
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
