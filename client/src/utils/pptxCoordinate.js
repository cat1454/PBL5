export const DESIGN_WIDTH = 1280;
export const DESIGN_HEIGHT = 720;
export const PPT_WIDTH = 13.333;
export const PPT_HEIGHT = 7.5;

const toFiniteNumber = (value, fallback = 0) => {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : fallback;
};

const roundPpt = (value) => Number(toFiniteNumber(value).toFixed(4));

export const pxToInX = (x) => roundPpt((toFiniteNumber(x) / DESIGN_WIDTH) * PPT_WIDTH);
export const pxToInY = (y) => roundPpt((toFiniteNumber(y) / DESIGN_HEIGHT) * PPT_HEIGHT);
export const pxToInW = (w) => roundPpt((toFiniteNumber(w) / DESIGN_WIDTH) * PPT_WIDTH);
export const pxToInH = (h) => roundPpt((toFiniteNumber(h) / DESIGN_HEIGHT) * PPT_HEIGHT);
export const pxToPt = (px) => roundPpt(toFiniteNumber(px) * 0.75);

export const normalizePptxColor = (value, fallback = 'FFFFFF') => {
  const color = String(value || '').trim().replace(/^#/, '');
  const normalized = color.length === 3
    ? color.split('').map((part) => `${part}${part}`).join('')
    : color;

  return /^[0-9a-fA-F]{6}$/.test(normalized)
    ? normalized.toUpperCase()
    : normalizePptxColor(fallback, 'FFFFFF');
};
