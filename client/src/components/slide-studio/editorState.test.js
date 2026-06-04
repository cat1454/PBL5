import {
  addEditorElement,
  createImageElement,
  duplicateEditorElement,
  normalizeEditorState,
  patchEditorCanvas,
  patchEditorElement,
} from './editorState';

describe('slide editor state normalization', () => {
  it('uses the 1280x720 design canvas by default', () => {
    const state = normalizeEditorState({
      heading: 'Deck title',
      subheading: 'Deck subtitle',
      bodyBlocks: ['Point one'],
      imageState: { needsImage: false },
    });

    expect(state.canvas).toMatchObject({
      width: 1280,
      height: 720,
    });
  });

  it('normalizes layout aliases and preserves visibility and image source fields', () => {
    const state = normalizeEditorState({
      editorState: {
        canvas: {},
        elements: [
          {
            id: 'hero',
            type: 'image',
            role: 'image',
            x: 40,
            y: 50,
            w: 300,
            h: 220,
            zIndex: 12,
            textAlign: 'center',
            visible: false,
            src: 'data:image/png;base64,abc',
            effectPreset: 'neon-glow',
            importedAssetName: 'diagram.png',
          },
        ],
      },
    });

    expect(state.elements[0]).toMatchObject({
      width: 300,
      height: 220,
      align: 'center',
      visible: false,
      src: 'data:image/png;base64,abc',
      effectPreset: 'neon-glow',
      importedAssetName: 'diagram.png',
    });
  });

  it('keeps persisted layout fields when patching text content', () => {
    const state = normalizeEditorState({
      editorState: {
        canvas: { width: 1280, height: 720 },
        elements: [
          {
            id: 'subtitle',
            role: 'subtitle',
            type: 'text',
            x: 120,
            y: 180,
            width: 480,
            height: 90,
            zIndex: 20,
            text: 'Before',
            fontSize: 28,
            color: '#ffffff',
            textAlign: 'right',
            locked: true,
            visible: true,
          },
        ],
      },
    });

    const next = patchEditorElement(state, 'subtitle', { text: 'After' });

    expect(next.elements[0]).toMatchObject({
      x: 120,
      y: 180,
      width: 480,
      height: 90,
      zIndex: 20,
      text: 'After',
      fontSize: 28,
      color: '#ffffff',
      align: 'right',
      locked: true,
      visible: true,
    });
  });

  it('preserves and patches canvas background color', () => {
    const state = normalizeEditorState({
      editorState: {
        canvas: {
          width: 1280,
          height: 720,
          background: '#123abc',
        },
        elements: [],
      },
      heading: 'Background slide',
    });

    const next = patchEditorCanvas(state, { background: '#f8fafc' });

    expect(state.canvas.background).toBe('#123abc');
    expect(next.canvas.background).toBe('#f8fafc');
    expect(next.revision).toBe(state.revision + 1);
  });

  it('creates centered imported image elements above existing layers', () => {
    const state = normalizeEditorState({
      editorState: {
        canvas: { width: 1280, height: 720 },
        elements: [
          {
            id: 'title',
            role: 'title',
            type: 'text',
            width: 400,
            height: 80,
            zIndex: 20,
            text: 'Title',
          },
        ],
      },
    });

    const imported = createImageElement(state, {
      src: 'data:image/png;base64,imported',
      name: 'imported.png',
    });
    const next = addEditorElement(state, imported);

    expect(imported).toMatchObject({
      type: 'image',
      src: 'data:image/png;base64,imported',
      importedAssetName: 'imported.png',
      effectPreset: 'soft-shadow',
      zIndex: 30,
    });
    expect(imported.x).toBeGreaterThan(0);
    expect(imported.y).toBeGreaterThan(0);
    expect(next.elements.at(-1)).toMatchObject({
      id: imported.id,
      zIndex: 30,
    });
  });

  it('preserves imported image metadata and effects when duplicating', () => {
    const state = normalizeEditorState({
      editorState: {
        canvas: { width: 1280, height: 720 },
        elements: [
          {
            id: 'imported',
            role: 'image-1',
            type: 'image',
            x: 100,
            y: 120,
            width: 300,
            height: 220,
            zIndex: 10,
            src: 'data:image/png;base64,imported',
            importedAssetName: 'imported.png',
            effectPreset: 'glass-frame',
            rotation: 3,
            opacity: 0.8,
          },
        ],
      },
    });

    const next = duplicateEditorElement(state, 'imported');
    const duplicate = next.elements.find((element) => element.id.startsWith('imported-copy-'));

    expect(duplicate).toMatchObject({
      type: 'image',
      src: 'data:image/png;base64,imported',
      importedAssetName: 'imported.png',
      effectPreset: 'glass-frame',
      rotation: 3,
      opacity: 0.8,
    });
  });
});
