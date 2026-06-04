import React, { act } from 'react';
import { createRoot } from 'react-dom/client';
import { Simulate } from 'react-dom/test-utils';
import PropertiesPanel from './PropertiesPanel';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const labels = {
  title: 'Element properties',
  empty: 'Select an element',
  text: 'Text',
  fontSize: 'Font size',
  color: 'Color',
  style: 'Style',
  bold: 'Bold',
  alignLeft: 'Align left',
  alignCenter: 'Align center',
  alignRight: 'Align right',
  lock: 'Lock element',
  unlock: 'Unlock element',
  roles: {
    title: 'Title',
  },
};

const textElement = {
  id: 'title',
  type: 'text',
  role: 'title',
  x: 100,
  y: 80,
  width: 500,
  height: 100,
  text: 'Title',
  fontSize: 48,
  color: '#ffffff',
  bold: false,
  align: 'left',
};

function renderPanel(props = {}) {
  const container = document.createElement('div');
  document.body.appendChild(container);
  const root = createRoot(container);
  const onPatch = jest.fn();

  act(() => {
    root.render(
      <PropertiesPanel
        element={textElement}
        labels={labels}
        onPatch={onPatch}
        {...props}
      />
    );
  });

  return {
    container,
    onPatch,
    cleanup: () => {
      act(() => root.unmount());
      container.remove();
    },
  };
}

describe('PropertiesPanel', () => {
  it('patches text controls for text elements', () => {
    const { container, onPatch, cleanup } = renderPanel();
    const textarea = container.querySelector('textarea');
    const colorInput = container.querySelector('input[type="color"]');
    const fontSizeInput = Array.from(container.querySelectorAll('input[type="number"]'))
      .find((input) => input.value === '48');

    act(() => {
      textarea.value = 'Edited title';
      Simulate.change(textarea);
      colorInput.value = '#123abc';
      Simulate.change(colorInput);
      fontSizeInput.value = '56';
      Simulate.change(fontSizeInput);
      container.querySelector('button[aria-label="Bold"]').click();
      container.querySelector('button[aria-label="Align center"]').click();
      container.querySelector('button[aria-label="Align right"]').click();
    });

    expect(onPatch).toHaveBeenCalledWith('title', { text: 'Edited title' });
    expect(onPatch).toHaveBeenCalledWith('title', { color: '#123abc' });
    expect(onPatch).toHaveBeenCalledWith('title', { fontSize: 56 });
    expect(onPatch).toHaveBeenCalledWith('title', { bold: true });
    expect(onPatch).toHaveBeenCalledWith('title', { align: 'center' });
    expect(onPatch).toHaveBeenCalledWith('title', { align: 'right' });

    cleanup();
  });
});
