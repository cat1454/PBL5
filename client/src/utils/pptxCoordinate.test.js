import {
  pxToInH,
  pxToInW,
  pxToInX,
  pxToInY,
  pxToPt,
  normalizePptxColor,
} from './pptxCoordinate';

describe('pptx coordinate helpers', () => {
  it('maps the 1280x720 canvas to a 16:9 PowerPoint slide', () => {
    expect(pxToInX(1280)).toBeCloseTo(13.333, 3);
    expect(pxToInY(720)).toBeCloseTo(7.5, 3);
    expect(pxToInW(1280)).toBeCloseTo(13.333, 3);
    expect(pxToInH(720)).toBeCloseTo(7.5, 3);
  });

  it('converts CSS pixel font sizes to PowerPoint points', () => {
    expect(pxToPt(64)).toBe(48);
  });

  it('normalizes CSS hex colors for PowerPoint', () => {
    expect(normalizePptxColor('#ffffff')).toBe('FFFFFF');
    expect(normalizePptxColor('#abc')).toBe('AABBCC');
    expect(normalizePptxColor('1d4ed8')).toBe('1D4ED8');
    expect(normalizePptxColor('not-a-color', '111827')).toBe('111827');
  });
});
