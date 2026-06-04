export const DESIGN_WIDTH = 1280;
export const DESIGN_HEIGHT = 720;

export const SLIDE_CANVAS_DEFAULT = {
  width: DESIGN_WIDTH,
  height: DESIGN_HEIGHT,
  background: 'theme',
};

const clamp = (value, min, max) => Math.min(Math.max(value, min), max);
const scaleDesign = (value) => Number((value * 0.8).toFixed(2));
const scaleFont = (value) => Math.max(8, Math.round(value * 0.8));

export const readEditorValue = (value, camelKey, pascalKey = null, fallback = undefined) => {
  if (!value || typeof value !== 'object') {
    return fallback;
  }

  const alternateKey = pascalKey || `${camelKey.charAt(0).toUpperCase()}${camelKey.slice(1)}`;
  return value[camelKey] ?? value[alternateKey] ?? fallback;
};

export const normalizeBodyBlocks = (bodyBlocks) => {
  if (!bodyBlocks) {
    return [];
  }

  if (Array.isArray(bodyBlocks)) {
    return bodyBlocks
      .map((block) => String(block ?? '').trim())
      .filter(Boolean)
      .slice(0, 8);
  }

  if (typeof bodyBlocks === 'string') {
    return bodyBlocks
      .split(/[\r\n]+/)
      .map((block) => block.trim())
      .filter(Boolean)
      .slice(0, 8);
  }

  return [];
};

const normalizeCanvas = (canvas) => {
  const width = Number(readEditorValue(canvas, 'width', 'Width', SLIDE_CANVAS_DEFAULT.width));
  const height = Number(readEditorValue(canvas, 'height', 'Height', SLIDE_CANVAS_DEFAULT.height));

  return {
    width: Number.isFinite(width) && width > 0 ? width : SLIDE_CANVAS_DEFAULT.width,
    height: Number.isFinite(height) && height > 0 ? height : SLIDE_CANVAS_DEFAULT.height,
    background: readEditorValue(canvas, 'background', 'Background', SLIDE_CANVAS_DEFAULT.background) || SLIDE_CANVAS_DEFAULT.background,
  };
};

const normalizeAlign = (align) => {
  const value = String(align || 'left').toLowerCase();
  return ['left', 'center', 'right'].includes(value) ? value : 'left';
};

const normalizeEffectPreset = (value) => {
  const preset = String(value || 'none').trim().toLowerCase();
  return ['none', 'soft-shadow', 'neon-glow', 'glass-frame', 'paper-cut', 'duotone'].includes(preset)
    ? preset
    : 'none';
};

const readFirstEditorValue = (value, keys, fallback = undefined) => {
  if (!value || typeof value !== 'object') {
    return fallback;
  }

  for (const key of keys) {
    if (value[key] !== undefined && value[key] !== null) {
      return value[key];
    }
  }

  return fallback;
};

const normalizeImageSrc = (element) => String(readFirstEditorValue(element, [
  'src',
  'Src',
  'url',
  'Url',
  'imageUrl',
  'ImageUrl',
  'localAssetUrl',
  'LocalAssetUrl',
  'base64',
  'Base64',
], '') || '');

const normalizeElement = (element, index, canvas) => {
  const role = String(readEditorValue(element, 'role', 'Role', `element-${index + 1}`) || `element-${index + 1}`).toLowerCase();
  const type = String(readEditorValue(element, 'type', 'Type', 'text') || 'text').toLowerCase();
  const width = clamp(Number(readFirstEditorValue(element, ['width', 'Width', 'w', 'W'], 320)) || 320, 24, canvas.width);
  const height = clamp(Number(readFirstEditorValue(element, ['height', 'Height', 'h', 'H'], 120)) || 120, 24, canvas.height);
  const x = clamp(Number(readEditorValue(element, 'x', 'X', 0)) || 0, 0, Math.max(0, canvas.width - width));
  const y = clamp(Number(readEditorValue(element, 'y', 'Y', 0)) || 0, 0, Math.max(0, canvas.height - height));

  return {
    id: String(readEditorValue(element, 'id', 'Id', `${role}-${index + 1}`) || `${role}-${index + 1}`),
    type,
    role,
    x,
    y,
    width,
    height,
    zIndex: Number(readEditorValue(element, 'zIndex', 'ZIndex', (index + 1) * 10)) || (index + 1) * 10,
    locked: Boolean(readEditorValue(element, 'locked', 'Locked', false)),
    visible: readEditorValue(element, 'visible', 'Visible', true) !== false,
    src: normalizeImageSrc(element),
    text: String(readEditorValue(element, 'text', 'Text', '') ?? ''),
    fontSize: clamp(Number(readEditorValue(element, 'fontSize', 'FontSize', 24)) || 24, 8, 160),
    bold: Boolean(readEditorValue(element, 'bold', 'Bold', false)),
    color: String(readEditorValue(element, 'color', 'Color', '#FFFFFF') || '#FFFFFF'),
    align: normalizeAlign(readFirstEditorValue(element, ['align', 'Align', 'textAlign', 'TextAlign'], 'left')),
    shapeType: readFirstEditorValue(element, ['shapeType', 'ShapeType', 'shape', 'Shape'], undefined),
    fillColor: readFirstEditorValue(element, ['fillColor', 'FillColor', 'fill', 'Fill', 'backgroundColor', 'BackgroundColor'], undefined),
    borderColor: readFirstEditorValue(element, ['borderColor', 'BorderColor', 'stroke', 'Stroke', 'lineColor', 'LineColor'], undefined),
    borderWidth: readFirstEditorValue(element, ['borderWidth', 'BorderWidth', 'strokeWidth', 'StrokeWidth', 'lineWidth', 'LineWidth'], undefined),
    opacity: readFirstEditorValue(element, ['opacity', 'Opacity'], undefined),
    rotation: readFirstEditorValue(element, ['rotation', 'Rotation'], undefined),
    effectPreset: normalizeEffectPreset(readFirstEditorValue(element, ['effectPreset', 'EffectPreset', 'effect', 'Effect'], 'none')),
    importedAssetName: String(readFirstEditorValue(element, ['importedAssetName', 'ImportedAssetName', 'assetName', 'AssetName'], '') || ''),
  };
};

const textElement = ({ role, x, y, width, height, zIndex, text, fontSize, bold = false, color = '#FFFFFF', align = 'left' }) => ({
  id: role,
  type: 'text',
  role,
  x: scaleDesign(x),
  y: scaleDesign(y),
  width: scaleDesign(width),
  height: scaleDesign(height),
  zIndex,
  locked: false,
  visible: true,
  src: '',
  text: text || '',
  fontSize: scaleFont(fontSize),
  bold,
  color,
  align,
});

const imageElement = () => ({
  id: 'image',
  type: 'image',
  role: 'image',
  x: scaleDesign(980),
  y: scaleDesign(190),
  width: scaleDesign(460),
  height: scaleDesign(420),
  zIndex: 15,
  locked: false,
  visible: true,
  src: '',
  text: '',
  fontSize: 24,
  bold: false,
  color: '#FFFFFF',
  align: 'left',
});

const getLayoutVariant = (slide) => {
  const editorState = readEditorValue(slide, 'editorState', 'EditorState', {});
  const layoutVariant = readEditorValue(editorState, 'layoutVariant', 'LayoutVariant', '');
  if (layoutVariant) {
    return String(layoutVariant).toLowerCase();
  }

  const slideType = String(readEditorValue(slide, 'slideType', 'SlideType', 'standard') || 'standard').toLowerCase();
  if (slideType === 'title') {
    return 'cover';
  }
  if (slideType === 'sectiondivider') {
    return 'divider';
  }
  if (slideType === 'stat') {
    return 'stat';
  }
  return 'standard';
};

const slideNeedsImage = (slide) => {
  const imageState = readEditorValue(slide, 'imageState', 'ImageState', {});
  return readEditorValue(imageState, 'needsImage', 'NeedsImage', true) !== false;
};

const buildFallbackElements = (slide) => {
  const heading = readEditorValue(slide, 'heading', 'Heading', '') || '';
  const subheading = readEditorValue(slide, 'subheading', 'Subheading', '') || '';
  const goal = readEditorValue(slide, 'keyMessage', 'KeyMessage', readEditorValue(slide, 'goal', 'Goal', '')) || '';
  const body = normalizeBodyBlocks(readEditorValue(slide, 'bodyBlocks', 'BodyBlocks', [])).join('\n');
  const notes = readEditorValue(slide, 'speakerNotes', 'SpeakerNotes', '') || '';
  const layoutVariant = getLayoutVariant(slide);

  if (layoutVariant === 'cover') {
    return [
      textElement({ role: 'title', x: 112, y: 190, width: 1110, height: 150, zIndex: 10, text: heading, fontSize: 64, bold: true, color: '#F8FAFC' }),
      textElement({ role: 'subtitle', x: 118, y: 360, width: 920, height: 96, zIndex: 20, text: subheading, fontSize: 30, color: '#D8E5F2' }),
      textElement({ role: 'goal', x: 120, y: 510, width: 760, height: 84, zIndex: 30, text: goal, fontSize: 24, bold: true, color: '#A7F3D0' }),
    ];
  }

  if (layoutVariant === 'divider') {
    return [
      textElement({ role: 'title', x: 130, y: 300, width: 1040, height: 130, zIndex: 10, text: heading, fontSize: 56, bold: true, color: '#F8FAFC' }),
      textElement({ role: 'subtitle', x: 134, y: 455, width: 860, height: 88, zIndex: 20, text: subheading, fontSize: 28, color: '#C7D2FE' }),
      textElement({ role: 'goal', x: 136, y: 585, width: 720, height: 76, zIndex: 30, text: goal, fontSize: 22, bold: true, color: '#FDE68A' }),
    ];
  }

  const elements = [
    textElement({ role: 'title', x: 96, y: 72, width: 920, height: 96, zIndex: 10, text: heading, fontSize: 42, bold: true, color: '#EAF7FF' }),
    textElement({ role: 'subtitle', x: 98, y: 166, width: 780, height: 64, zIndex: 15, text: subheading, fontSize: 24, color: '#C8D7EA' }),
    textElement({ role: 'body', x: 96, y: layoutVariant === 'stat' ? 244 : 244, width: layoutVariant === 'stat' ? 560 : 760, height: layoutVariant === 'stat' ? 250 : 360, zIndex: 20, text: body, fontSize: layoutVariant === 'stat' ? 66 : 28, bold: layoutVariant === 'stat', color: '#DCEBFF' }),
    textElement({ role: 'goal', x: 96, y: 642, width: 780, height: 80, zIndex: 30, text: goal, fontSize: 22, bold: true, color: '#A7F3D0' }),
  ];

  if (slideNeedsImage(slide)) {
    elements.push(imageElement());
  }

  if (notes) {
    elements.push(textElement({ role: 'notes', x: 96, y: 748, width: 880, height: 70, zIndex: 40, text: notes, fontSize: 18, color: '#B6C6D8' }));
  }

  return elements;
};

export const normalizeEditorState = (slide) => {
  const source = readEditorValue(slide, 'editorState', 'EditorState', {}) || {};
  const canvas = normalizeCanvas(readEditorValue(source, 'canvas', 'Canvas', SLIDE_CANVAS_DEFAULT));
  const sourceElements = readEditorValue(source, 'elements', 'Elements', []);
  const elements = (Array.isArray(sourceElements) && sourceElements.length > 0 ? sourceElements : buildFallbackElements(slide))
    .map((element, index) => normalizeElement(element, index, canvas))
    .sort((a, b) => a.zIndex - b.zIndex);

  return {
    version: String(readEditorValue(source, 'version', 'Version', '2') || '2'),
    revision: Number(readEditorValue(source, 'revision', 'Revision', 0)) || 0,
    layoutVariant: String(readEditorValue(source, 'layoutVariant', 'LayoutVariant', getLayoutVariant(slide)) || 'standard'),
    canvas,
    elements,
    title: readEditorValue(source, 'title', 'Title', {}),
    subtitle: readEditorValue(source, 'subtitle', 'Subtitle', {}),
    goal: readEditorValue(source, 'goal', 'Goal', {}),
    body: readEditorValue(source, 'body', 'Body', {}),
    notes: readEditorValue(source, 'notes', 'Notes', {}),
  };
};

export const patchEditorElement = (editorState, elementId, patch) => {
  const canvas = normalizeCanvas(editorState.canvas);
  return bumpRevision({
    ...editorState,
    canvas,
    elements: editorState.elements.map((element) => {
      if (element.id !== elementId) {
        return element;
      }

      return normalizeElement({ ...element, ...patch }, 0, canvas);
    }),
  });
};

export const patchEditorCanvas = (editorState, patch) => bumpRevision({
  ...editorState,
  canvas: normalizeCanvas({
    ...editorState?.canvas,
    ...patch,
  }),
});

export const bumpRevision = (editorState) => ({
  ...editorState,
  revision: Number(editorState?.revision || 0) + 1,
});

export const replaceEditorState = (editorState) => bumpRevision({
  ...editorState,
  canvas: normalizeCanvas(editorState?.canvas),
  elements: (editorState?.elements || [])
    .map((element, index) => normalizeElement(element, index, normalizeCanvas(editorState?.canvas)))
    .sort((a, b) => a.zIndex - b.zIndex),
});

export const createTextElement = (editorState, text = 'New text') => {
  const canvas = normalizeCanvas(editorState?.canvas);
  const nextIndex = (editorState?.elements?.length || 0) + 1;
  return normalizeElement({
    id: `text-${Date.now()}`,
    type: 'text',
    role: `text-${nextIndex}`,
    x: 140,
    y: 140,
    width: 420,
    height: 140,
    zIndex: nextIndex * 10,
    text,
    fontSize: 28,
    color: '#FFFFFF',
  }, nextIndex, canvas);
};

export const createImageElement = (editorState, { src, name } = {}) => {
  const canvas = normalizeCanvas(editorState?.canvas);
  const nextIndex = (editorState?.elements?.length || 0) + 1;
  const width = Math.min(420, Math.max(240, Math.round(canvas.width * 0.34)));
  const height = Math.min(320, Math.max(180, Math.round(canvas.height * 0.44)));
  const x = Math.max(0, Math.round((canvas.width - width) / 2));
  const y = Math.max(0, Math.round((canvas.height - height) / 2));
  const maxZIndex = Math.max(...(editorState?.elements || []).map((element) => Number(element.zIndex) || 0), 0);

  return normalizeElement({
    id: `image-${Date.now()}`,
    type: 'image',
    role: `image-${nextIndex}`,
    x,
    y,
    width,
    height,
    zIndex: maxZIndex + 10,
    locked: false,
    visible: true,
    src: src || '',
    importedAssetName: name || '',
    effectPreset: 'soft-shadow',
  }, nextIndex, canvas);
};

export const addEditorElement = (editorState, element) => {
  const canvas = normalizeCanvas(editorState.canvas);
  return bumpRevision({
    ...editorState,
    canvas,
    elements: [...editorState.elements, normalizeElement(element, editorState.elements.length, canvas)]
      .sort((a, b) => a.zIndex - b.zIndex),
  });
};

export const deleteEditorElement = (editorState, elementId) => bumpRevision({
  ...editorState,
  elements: editorState.elements.filter((element) => element.id !== elementId),
});

export const duplicateEditorElement = (editorState, elementId) => {
  const source = findEditorElement(editorState, elementId);
  if (!source) {
    return editorState;
  }

  return addEditorElement(editorState, {
    ...source,
    id: `${source.id}-copy-${Date.now()}`,
    role: source.role?.startsWith('text-') ? `${source.role}-copy` : source.role,
    x: source.x + 32,
    y: source.y + 32,
    zIndex: Math.max(...editorState.elements.map((element) => element.zIndex), 0) + 10,
  });
};

export const reorderEditorElement = (editorState, elementId, direction) => {
  const element = findEditorElement(editorState, elementId);
  if (!element) {
    return editorState;
  }

  const delta = direction === 'backward' ? -15 : 15;
  return bumpRevision({
    ...editorState,
    elements: editorState.elements
      .map((item) => (item.id === elementId ? { ...item, zIndex: Math.max(1, item.zIndex + delta) } : item))
      .sort((a, b) => a.zIndex - b.zIndex)
      .map((item, index) => ({ ...item, zIndex: (index + 1) * 10 })),
  });
};

export const findEditorElement = (editorState, elementId) => (
  editorState?.elements?.find((element) => element.id === elementId) || null
);

const getRoleText = (editorState, role, fallback = '') => {
  const element = editorState.elements.find((item) => item.role === role);
  return String(element?.text ?? fallback ?? '').trim();
};

export const buildSlideFromEditorState = (slide, editorState) => {
  const heading = getRoleText(editorState, 'title', slide?.heading);
  const subheading = getRoleText(editorState, 'subtitle', slide?.subheading);
  const goal = getRoleText(editorState, 'goal', slide?.goal || slide?.keyMessage);
  const bodyText = getRoleText(editorState, 'body', normalizeBodyBlocks(slide?.bodyBlocks).join('\n'));
  const speakerNotes = getRoleText(editorState, 'notes', slide?.speakerNotes);

  return {
    ...slide,
    heading,
    subheading,
    goal,
    keyMessage: goal,
    bodyBlocks: normalizeBodyBlocks(bodyText),
    speakerNotes,
    editorState,
  };
};
