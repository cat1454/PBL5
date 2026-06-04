import React from 'react';
import { Rnd } from 'react-rnd';
import ElementRenderer from './ElementRenderer';

function SlideElement({ element, imageVm, labels, scale, selected, onCommit, onPatch, onSelect }) {
  const handleMouseDown = (event) => {
    event.stopPropagation();
    onSelect(element.id);
  };

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
      className={`slide-canvas-element${selected ? ' selected' : ''}${element.locked ? ' locked' : ''}`}
      style={{ zIndex: element.zIndex }}
    >
      <ElementRenderer element={element} imageVm={imageVm} labels={labels} />
    </Rnd>
  );
}

export default SlideElement;
