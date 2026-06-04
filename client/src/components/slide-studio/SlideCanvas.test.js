import React, { act } from 'react';
import { createRoot } from 'react-dom/client';
import SlideCanvas from './SlideCanvas';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

jest.mock('react-rnd', () => ({
  Rnd: ({
    children,
    className,
    disableDragging,
    dragGrid,
    enableResizing,
    onDrag,
    onDragStop,
    onMouseDown,
    onResize,
    onResizeStop,
    position,
    resizeGrid,
    scale,
    size,
    style,
    ...rest
  }) => (
    <div
      className={className}
      onMouseDown={onMouseDown}
      {...rest}
      style={{
        position: 'absolute',
        left: position?.x,
        top: position?.y,
        width: size?.width,
        height: size?.height,
        ...style,
      }}
    >
      {children}
    </div>
  ),
}));

const editorState = {
  canvas: { width: 1280, height: 720, background: 'theme' },
  elements: [
    {
      id: 'title',
      type: 'text',
      role: 'title',
      x: 100,
      y: 80,
      width: 500,
      height: 100,
      zIndex: 10,
      text: 'Title',
      fontSize: 48,
      color: '#ffffff',
      align: 'left',
      visible: true,
    },
    {
      id: 'empty',
      type: 'text',
      role: 'body',
      x: 120,
      y: 220,
      width: 360,
      height: 80,
      zIndex: 20,
      text: '',
      fontSize: 24,
      color: '#ffffff',
      align: 'center',
      visible: true,
    },
    {
      id: 'image',
      type: 'image',
      role: 'image',
      x: 700,
      y: 120,
      width: 320,
      height: 260,
      zIndex: 30,
      src: 'data:image/png;base64,abc',
      effectPreset: 'neon-glow',
      visible: true,
    },
  ],
};

function renderCanvas(props = {}) {
  const container = document.createElement('div');
  document.body.appendChild(container);
  const root = createRoot(container);

  act(() => {
    root.render(
      <SlideCanvas
        editorState={editorState}
        labels={{ emptyText: 'Empty text', imageAlt: 'Slide image' }}
        scale={1}
        onPatchElement={jest.fn()}
        onSelectElement={jest.fn()}
        {...props}
      />
    );
  });

  return {
    container,
    cleanup: () => {
      act(() => root.unmount());
      container.remove();
    },
  };
}

describe('SlideCanvas renderer modes', () => {
  it('renders preview without form controls or empty text placeholders', () => {
    const { container, cleanup } = renderCanvas({ mode: 'preview' });

    expect(container.querySelector('textarea')).toBeNull();
    expect(container.querySelector('input')).toBeNull();
    expect(container.textContent).toContain('Title');
    expect(container.textContent).not.toContain('Empty text');

    cleanup();
  });

  it('keeps preview and layout coordinates identical', () => {
    const preview = renderCanvas({ mode: 'preview' });
    const previewTitle = preview.container.querySelector('[data-slide-element-id="title"]');

    expect(previewTitle.style.left).toBe('100px');
    expect(previewTitle.style.top).toBe('80px');
    expect(previewTitle.style.width).toBe('500px');
    expect(previewTitle.style.height).toBe('100px');
    expect(previewTitle.style.zIndex).toBe('10');

    preview.cleanup();

    const layout = renderCanvas({ mode: 'layout', selectedElementId: 'title' });
    const layoutTitle = layout.container.querySelector('[data-slide-element-id="title"]');

    expect(layoutTitle.style.left).toBe('100px');
    expect(layoutTitle.style.top).toBe('80px');
    expect(layoutTitle.style.width).toBe('500px');
    expect(layoutTitle.style.height).toBe('100px');
    expect(layoutTitle.style.zIndex).toBe('10');
    expect(layoutTitle.className).toContain('selected');

    layout.cleanup();
  });

  it('renders image elements in preview and layout from element src', () => {
    const preview = renderCanvas({ mode: 'preview' });
    expect(preview.container.querySelector('img')?.getAttribute('src')).toBe('data:image/png;base64,abc');
    expect(preview.container.querySelector('[data-slide-element-id="image"]')?.className).toContain('effect-neon-glow');
    preview.cleanup();

    const layout = renderCanvas({ mode: 'layout' });
    expect(layout.container.querySelector('img')?.getAttribute('src')).toBe('data:image/png;base64,abc');
    expect(layout.container.querySelector('[data-slide-element-id="image"]')?.className).toContain('effect-neon-glow');
    layout.cleanup();
  });

  it('renders custom canvas background colors and leaves theme backgrounds to CSS', () => {
    const custom = renderCanvas({
      editorState: {
        ...editorState,
        canvas: { ...editorState.canvas, background: '#123abc' },
      },
    });
    expect(custom.container.querySelector('.studio-editable-slide').style.background).toBe('rgb(18, 58, 188)');
    custom.cleanup();

    const themed = renderCanvas();
    expect(themed.container.querySelector('.studio-editable-slide').style.background).toBe('');
    themed.cleanup();
  });

  it('allows text mode edits without changing layout fields', () => {
    const onPatchElement = jest.fn();
    const { container, cleanup } = renderCanvas({ mode: 'text', onPatchElement });
    const editableTitle = container.querySelector('[contenteditable="true"]');

    act(() => {
      editableTitle.textContent = 'Edited title';
      editableTitle.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: 'Edited title' }));
    });

    expect(onPatchElement).toHaveBeenCalledWith('title', { text: 'Edited title' });
    expect(onPatchElement).not.toHaveBeenCalledWith('title', expect.objectContaining({
      x: expect.anything(),
    }));

    cleanup();
  });
});
