import PptxGenJS from 'pptxgenjs';
import { buildSlideImageViewModel } from '../services/slideImages';
import { normalizeEditorState } from '../components/slide-studio/editorState';
import {
  normalizePptxColor,
  pxToInH,
  pxToInW,
  pxToInX,
  pxToInY,
  pxToPt,
} from './pptxCoordinate';

const DEFAULT_BACKGROUND = '111827';
const DEFAULT_FONT = 'Arial';
const PLACEHOLDER_TEXT = new Set(['empty text']);

const sanitizeFilename = (value) => {
  const name = String(value || 'slide-deck')
    .split('')
    .map((char) => (char.charCodeAt(0) < 32 || /[<>:"/\\|?*]/.test(char) ? ' ' : char))
    .join('')
    .replace(/\s+/g, ' ')
    .trim();
  return name || 'slide-deck';
};

const readFirstString = (...values) => {
  for (const value of values) {
    if (typeof value === 'string' && value.trim()) {
      return value.trim();
    }
  }
  return '';
};

const isDataUri = (value) => /^data:/i.test(String(value || ''));

const blobToDataUri = (blob) => new Promise((resolve, reject) => {
  const reader = new FileReader();
  reader.onload = () => resolve(String(reader.result || ''));
  reader.onerror = () => reject(reader.error || new Error('Could not read image.'));
  reader.readAsDataURL(blob);
});

const fetchImageAsDataUri = async (src) => {
  if (!src || isDataUri(src)) {
    return src || '';
  }

  const response = await fetch(src);
  if (!response.ok) {
    throw new Error(`Image request failed with ${response.status}.`);
  }
  return blobToDataUri(await response.blob());
};

const resolveImageSrc = (element, slide, t) => {
  const selectedImage = buildSlideImageViewModel(slide, t)?.selectedImage;
  return readFirstString(
    element.src,
    element.url,
    element.imageUrl,
    element.localAssetUrl,
    element.base64,
    selectedImage?.localAssetUrl,
    selectedImage?.imageUrl,
    selectedImage?.url,
    selectedImage?.thumbnailUrl
  );
};

const getElementBox = (element) => ({
  x: pxToInX(element.x),
  y: pxToInY(element.y),
  w: pxToInW(element.width ?? element.w),
  h: pxToInH(element.height ?? element.h),
});

const shouldSkipText = (text, emptyTextLabel) => {
  const normalized = String(text || '').trim();
  if (!normalized) {
    return true;
  }

  const placeholders = new Set(PLACEHOLDER_TEXT);
  if (emptyTextLabel) {
    placeholders.add(String(emptyTextLabel).trim().toLowerCase());
  }
  return placeholders.has(normalized.toLowerCase());
};

const toBold = (element) => {
  if (typeof element.bold === 'boolean') {
    return element.bold;
  }

  const weight = element.fontWeight;
  return weight === 'bold' || Number(weight) >= 600;
};

const addTextElement = (slide, element, emptyTextLabel) => {
  const text = String(element.text || '');
  if (shouldSkipText(text, emptyTextLabel)) {
    return;
  }

  slide.addText(text, {
    ...getElementBox(element),
    margin: 0.06,
    fit: 'shrink',
    fontFace: element.fontFace || DEFAULT_FONT,
    fontSize: pxToPt(element.fontSize || 24),
    color: normalizePptxColor(element.color, 'FFFFFF'),
    bold: toBold(element),
    align: element.align || element.textAlign || 'left',
    valign: element.verticalAlign || element.valign || 'top',
    breakLine: false,
  });
};

const getShapeType = (pptx, element) => {
  const type = String(element.shapeType || element.shape || element.type || '').toLowerCase();
  switch (type) {
    case 'rectangle':
    case 'rect':
      return pptx.shapes.RECTANGLE;
    case 'roundedrectangle':
    case 'rounded-rectangle':
    case 'roundrect':
      return pptx.shapes.ROUNDED_RECTANGLE;
    case 'circle':
    case 'ellipse':
    case 'oval':
      return pptx.shapes.OVAL;
    case 'line':
      return pptx.shapes.LINE;
    default:
      return null;
  }
};

const addShapeElement = (pptx, slide, element) => {
  const shape = getShapeType(pptx, element);
  if (!shape) {
    return;
  }

  const fillColor = element.fillColor || element.fill || element.backgroundColor || 'FFFFFF';
  const borderColor = element.borderColor || element.stroke || element.lineColor || fillColor;
  const borderWidth = Number(element.borderWidth ?? element.strokeWidth ?? element.lineWidth ?? 0);
  const opacity = Number(element.opacity);
  const transparency = Number.isFinite(opacity) ? Math.max(0, Math.min(100, 100 - (opacity * 100))) : undefined;
  const options = {
    ...getElementBox(element),
    fill: {
      color: normalizePptxColor(fillColor, 'FFFFFF'),
      ...(transparency === undefined ? {} : { transparency }),
    },
    line: {
      color: normalizePptxColor(borderColor, normalizePptxColor(fillColor, 'FFFFFF')),
      width: Number.isFinite(borderWidth) ? borderWidth : 0,
    },
  };

  slide.addShape(shape, options);
};

const getEmptyTextLabel = (t) => {
  if (typeof t !== 'function') {
    return '';
  }

  try {
    return t('slides.canvas.emptyText');
  } catch {
    return '';
  }
};

const createPptx = () => {
  const PptxGenConstructor = PptxGenJS?.default || PptxGenJS;
  return new PptxGenConstructor();
};

export async function buildEditablePptx({
  deck,
  documentMeta,
  t,
  imageResolver = fetchImageAsDataUri,
  pptxFactory = createPptx,
} = {}) {
  const pptx = await pptxFactory();
  pptx.layout = 'LAYOUT_WIDE';
  pptx.author = 'ELearnGamePlatform';
  pptx.title = deck?.title || documentMeta?.fileName || 'Slide deck';

  let skippedImages = 0;
  const emptyTextLabel = getEmptyTextLabel(t);

  for (const item of deck?.items || []) {
    const pptSlide = pptx.addSlide();
    pptSlide.background = pptSlide.background || {};
    if (typeof pptSlide.background.fill === 'function') {
      pptSlide.background.fill(DEFAULT_BACKGROUND);
    } else {
      pptSlide.background = { color: DEFAULT_BACKGROUND };
    }

    const editorState = normalizeEditorState(item);
    const elements = [...(editorState.elements || [])]
      .filter((element) => element.visible !== false)
      .sort((a, b) => (Number(a.zIndex) || 0) - (Number(b.zIndex) || 0));

    for (const element of elements) {
      if (element.type === 'text') {
        addTextElement(pptSlide, element, emptyTextLabel);
      } else if (element.type === 'image') {
        const src = resolveImageSrc(element, item, t);
        if (!src) {
          continue;
        }

        try {
          const data = await imageResolver(src);
          if (data) {
            pptSlide.addImage({
              data,
              ...getElementBox(element),
              sizing: {
                type: 'contain',
                w: getElementBox(element).w,
                h: getElementBox(element).h,
              },
            });
          }
        } catch (err) {
          skippedImages += 1;
          console.warn('Skipping PPTX image export.', err);
        }
      } else {
        addShapeElement(pptx, pptSlide, element);
      }
    }
  }

  const filename = `${sanitizeFilename(deck?.title || documentMeta?.fileName || 'slide-deck')}.pptx`;
  return { pptx, filename, skippedImages };
}

export async function exportEditablePptx(options = {}) {
  const { pptx, filename, skippedImages } = await buildEditablePptx(options);
  await pptx.writeFile({ fileName: filename });
  return { filename, skippedImages };
}
