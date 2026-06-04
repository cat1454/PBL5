import React from 'react';
import { LuCopy, LuLock, LuLockOpen, LuTrash2 } from 'react-icons/lu';

function LayersPanel({ elements, labels, selectedElementId, onDelete, onDuplicate, onPatch, onReorder, onSelect }) {
  return (
    <div className="studio-inspector-block slide-layers-panel">
      <div className="studio-inspector-block-head">
        <div>
          <span className="studio-kicker">{labels.title}</span>
          <strong>{labels.count(elements.length)}</strong>
        </div>
      </div>

      <div className="slide-layer-list">
        {[...elements].sort((a, b) => b.zIndex - a.zIndex).map((element) => (
          <div key={element.id} className={`slide-layer-row${selectedElementId === element.id ? ' active' : ''}`}>
            <button type="button" className="slide-layer-name" onClick={() => onSelect(element.id)}>
              <span>{labels.roles[element.role] || element.role || element.type}</span>
              <small>{element.type}</small>
            </button>
            <div className="slide-layer-actions">
              <button type="button" className="studio-icon-button" onClick={() => onPatch(element.id, { locked: !element.locked })} title={element.locked ? labels.unlock : labels.lock}>
                {element.locked ? <LuLock aria-hidden="true" /> : <LuLockOpen aria-hidden="true" />}
              </button>
              <button type="button" className="studio-icon-button" onClick={() => onReorder(element.id, 'forward')} title={labels.forward}>
                ]
              </button>
              <button type="button" className="studio-icon-button" onClick={() => onReorder(element.id, 'backward')} title={labels.backward}>
                [
              </button>
              <button type="button" className="studio-icon-button" onClick={() => onDuplicate(element.id)} title={labels.duplicate}>
                <LuCopy aria-hidden="true" />
              </button>
              <button type="button" className="studio-icon-button" onClick={() => onDelete(element.id)} title={labels.delete}>
                <LuTrash2 aria-hidden="true" />
              </button>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

export default LayersPanel;
