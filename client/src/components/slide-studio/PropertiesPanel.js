import React from 'react';
import { LuAlignCenter, LuAlignLeft, LuAlignRight, LuBold, LuLock, LuLockOpen } from 'react-icons/lu';

const numberValue = (value) => (Number.isFinite(Number(value)) ? Number(value) : 0);

function PropertiesPanel({ element, labels, onPatch }) {
  if (!element) {
    return (
      <div className="studio-inspector-block slide-properties-panel">
        <span className="studio-kicker">{labels.title}</span>
        <p>{labels.empty}</p>
      </div>
    );
  }

  const patchNumber = (field) => (event) => {
    onPatch(element.id, { [field]: numberValue(event.target.value) });
  };

  return (
    <div className="studio-inspector-block slide-properties-panel">
      <div className="studio-inspector-block-head">
        <div>
          <span className="studio-kicker">{labels.title}</span>
          <strong>{labels.roles[element.role] || element.role}</strong>
        </div>
        <button
          type="button"
          className={`studio-icon-button${element.locked ? ' active' : ''}`}
          onClick={() => onPatch(element.id, { locked: !element.locked })}
          aria-label={element.locked ? labels.unlock : labels.lock}
          title={element.locked ? labels.unlock : labels.lock}
        >
          {element.locked ? <LuLock aria-hidden="true" /> : <LuLockOpen aria-hidden="true" />}
        </button>
      </div>

      {element.type === 'text' && (
        <label className="gamma-field">
          <span>{labels.text}</span>
          <textarea
            rows={5}
            value={element.text}
            onChange={(event) => onPatch(element.id, { text: event.target.value })}
          />
        </label>
      )}

      <div className="slide-property-grid">
        <label className="gamma-field">
          <span>X</span>
          <input type="number" value={Math.round(element.x)} onChange={patchNumber('x')} />
        </label>
        <label className="gamma-field">
          <span>Y</span>
          <input type="number" value={Math.round(element.y)} onChange={patchNumber('y')} />
        </label>
        <label className="gamma-field">
          <span>W</span>
          <input type="number" min="24" value={Math.round(element.width)} onChange={patchNumber('width')} />
        </label>
        <label className="gamma-field">
          <span>H</span>
          <input type="number" min="24" value={Math.round(element.height)} onChange={patchNumber('height')} />
        </label>
      </div>

      {element.type === 'text' && (
        <>
          <div className="slide-property-grid">
            <label className="gamma-field">
              <span>{labels.fontSize}</span>
              <input type="number" min="8" max="160" value={element.fontSize} onChange={patchNumber('fontSize')} />
            </label>
            <label className="gamma-field">
              <span>{labels.color}</span>
              <input type="color" value={element.color} onChange={(event) => onPatch(element.id, { color: event.target.value })} />
            </label>
          </div>

          <div className="slide-property-actions" role="group" aria-label={labels.style}>
            <button
              type="button"
              className={`studio-icon-button${element.bold ? ' active' : ''}`}
              onClick={() => onPatch(element.id, { bold: !element.bold })}
              aria-label={labels.bold}
              title={labels.bold}
            >
              <LuBold aria-hidden="true" />
            </button>
            <button
              type="button"
              className={`studio-icon-button${element.align === 'left' ? ' active' : ''}`}
              onClick={() => onPatch(element.id, { align: 'left' })}
              aria-label={labels.alignLeft}
              title={labels.alignLeft}
            >
              <LuAlignLeft aria-hidden="true" />
            </button>
            <button
              type="button"
              className={`studio-icon-button${element.align === 'center' ? ' active' : ''}`}
              onClick={() => onPatch(element.id, { align: 'center' })}
              aria-label={labels.alignCenter}
              title={labels.alignCenter}
            >
              <LuAlignCenter aria-hidden="true" />
            </button>
            <button
              type="button"
              className={`studio-icon-button${element.align === 'right' ? ' active' : ''}`}
              onClick={() => onPatch(element.id, { align: 'right' })}
              aria-label={labels.alignRight}
              title={labels.alignRight}
            >
              <LuAlignRight aria-hidden="true" />
            </button>
          </div>
        </>
      )}
    </div>
  );
}

export default PropertiesPanel;
