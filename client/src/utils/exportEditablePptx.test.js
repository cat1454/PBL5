const mockAddText = jest.fn();
const mockAddImage = jest.fn();
const mockAddShape = jest.fn();
const mockBackgroundFill = jest.fn();
const mockWriteFile = jest.fn(() => Promise.resolve());
const mockAddSlide = jest.fn(() => ({
  addText: mockAddText,
  addImage: mockAddImage,
  addShape: mockAddShape,
  background: { fill: mockBackgroundFill },
}));

const { buildEditablePptx, exportEditablePptx } = require('./exportEditablePptx');

const mockPptxFactory = () => ({
  addSlide: mockAddSlide,
  writeFile: mockWriteFile,
  shapes: {
    RECTANGLE: 'RECTANGLE',
    ROUNDED_RECTANGLE: 'ROUNDED_RECTANGLE',
    OVAL: 'OVAL',
    LINE: 'LINE',
  },
});

const deck = {
  title: 'Editable Deck',
  items: [
    {
      id: 'slide-1',
      heading: 'Fallback title',
      editorState: {
        canvas: { width: 1280, height: 720 },
        elements: [
          {
            id: 'later',
            type: 'text',
            role: 'body',
            x: 640,
            y: 360,
            width: 320,
            height: 120,
            zIndex: 30,
            text: 'Later text',
            fontSize: 32,
            color: '#abc',
            align: 'center',
            visible: true,
          },
          {
            id: 'hidden',
            type: 'text',
            role: 'hidden',
            x: 0,
            y: 0,
            width: 100,
            height: 100,
            zIndex: 5,
            text: 'Hidden text',
            visible: false,
          },
          {
            id: 'placeholder',
            type: 'text',
            role: 'placeholder',
            x: 0,
            y: 0,
            width: 100,
            height: 100,
            zIndex: 10,
            text: 'Empty text',
            visible: true,
          },
          {
            id: 'first',
            type: 'text',
            role: 'title',
            x: 0,
            y: 0,
            width: 1280,
            height: 120,
            zIndex: 20,
            text: 'First text',
            fontSize: 64,
            color: '#ffffff',
            bold: true,
            align: 'left',
            visible: true,
          },
          {
            id: 'image',
            type: 'image',
            role: 'image',
            x: 960,
            y: 180,
            width: 320,
            height: 240,
            zIndex: 40,
            src: 'data:image/png;base64,abc',
            effectPreset: 'neon-glow',
            importedAssetName: 'diagram.png',
            visible: true,
          },
          {
            id: 'shape',
            type: 'rectangle',
            role: 'shape',
            x: 120,
            y: 500,
            width: 240,
            height: 80,
            zIndex: 50,
            fillColor: '#123456',
            borderColor: '#654321',
            borderWidth: 2,
            visible: true,
          },
        ],
      },
    },
  ],
};

describe('editable PPTX export', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockAddSlide.mockImplementation(() => ({
      addText: mockAddText,
      addImage: mockAddImage,
      addShape: mockAddShape,
      background: { fill: mockBackgroundFill },
    }));
  });

  it('exports editable text, image, and shape objects in z-index order', async () => {
    const result = await buildEditablePptx({ deck, pptxFactory: mockPptxFactory });

    expect(result.filename).toBe('Editable Deck.pptx');
    expect(mockBackgroundFill).toHaveBeenCalledWith('111827');
    expect(mockAddText).toHaveBeenCalledTimes(2);
    expect(mockAddText.mock.calls.map(([text]) => text)).toEqual(['First text', 'Later text']);
    expect(mockAddText.mock.calls[0][1]).toMatchObject({
      x: 0,
      y: 0,
      w: 13.333,
      fontSize: 48,
      color: 'FFFFFF',
      bold: true,
      align: 'left',
    });
    expect(mockAddText.mock.calls[1][1]).toMatchObject({
      x: expect.closeTo(6.6665, 3),
      y: 3.75,
      w: expect.closeTo(3.33325, 3),
      h: 1.25,
      fontSize: 24,
      color: 'AABBCC',
      align: 'center',
    });
    expect(mockAddImage).toHaveBeenCalledWith(expect.objectContaining({
      data: 'data:image/png;base64,abc',
      x: expect.closeTo(9.99975, 3),
      y: 1.875,
      w: expect.closeTo(3.33325, 3),
      h: 2.5,
    }));
    expect(mockAddShape).toHaveBeenCalledWith('RECTANGLE', expect.objectContaining({
      fill: { color: '123456' },
      line: { color: '654321', width: 2 },
    }));
  });

  it('writes the built deck to a PowerPoint file', async () => {
    const result = await exportEditablePptx({ deck, pptxFactory: mockPptxFactory });

    expect(mockWriteFile).toHaveBeenCalledWith({ fileName: 'Editable Deck.pptx' });
    expect(result).toEqual({ filename: 'Editable Deck.pptx', skippedImages: 0 });
  });

  it('embeds selected PDF-region candidates as editable image objects', async () => {
    const pdfDeck = {
      title: 'PDF Regions',
      items: [
        {
          id: 'slide-pdf',
          imageCandidates: [
            {
              key: 'pdf-region-1',
              sourceType: 'pdf-region',
              provider: 'Source PDF',
              localAssetUrl: 'data:image/png;base64,pdfregion',
              isSelected: true,
            },
          ],
          selectedImageKey: 'pdf-region-1',
          editorState: {
            canvas: { width: 1280, height: 720 },
            elements: [
              {
                id: 'image',
                type: 'image',
                role: 'image',
                x: 0,
                y: 0,
                width: 1280,
                height: 720,
                zIndex: 10,
                visible: true,
              },
            ],
          },
        },
      ],
    };

    await buildEditablePptx({ deck: pdfDeck, pptxFactory: mockPptxFactory });

    expect(mockAddImage).toHaveBeenCalledWith(expect.objectContaining({
      data: 'data:image/png;base64,pdfregion',
      x: 0,
      y: 0,
      w: 13.333,
      h: 7.5,
    }));
  });

  it('exports imported data URL images while ignoring unsupported effect decoration', async () => {
    const importedDeck = {
      title: 'Imported Images',
      items: [
        {
          id: 'slide-imported',
          editorState: {
            canvas: { width: 1280, height: 720 },
            elements: [
              {
                id: 'imported',
                type: 'image',
                role: 'image-2',
                x: 320,
                y: 160,
                width: 400,
                height: 260,
                zIndex: 10,
                src: 'data:image/png;base64,imported',
                effectPreset: 'glass-frame',
                importedAssetName: 'local.png',
                visible: true,
              },
            ],
          },
        },
      ],
    };

    const result = await buildEditablePptx({ deck: importedDeck, pptxFactory: mockPptxFactory });

    expect(result.skippedImages).toBe(0);
    expect(mockAddImage).toHaveBeenCalledWith(expect.objectContaining({
      data: 'data:image/png;base64,imported',
      x: expect.closeTo(3.333, 3),
      y: expect.closeTo(1.667, 3),
      w: expect.closeTo(4.167, 3),
      h: expect.closeTo(2.708, 3),
    }));
  });
});
