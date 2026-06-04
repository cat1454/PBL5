import {
  getBoundedPresentationIndex,
  getNextPresentationIndex,
  getPresentationCanvasScale,
  getPresentationStartIndex,
} from './slide-studio/presentationMode';

const slides = [
  { id: 'cover', heading: 'Cover' },
  { id: 'middle', heading: 'Middle' },
  { id: 'ending', heading: 'Ending' },
];

describe('Slide Studio presentation helpers', () => {
  it('starts presentation from the selected slide when it is visible', () => {
    expect(getPresentationStartIndex(slides, 'middle')).toBe(1);
  });

  it('falls back to the first slide when the selected slide is missing', () => {
    expect(getPresentationStartIndex(slides, 'hidden')).toBe(0);
    expect(getPresentationStartIndex([], 'middle')).toBe(0);
  });

  it('keeps previous and next navigation inside deck bounds', () => {
    expect(getNextPresentationIndex(0, slides.length, -1)).toBe(0);
    expect(getNextPresentationIndex(1, slides.length, 1)).toBe(2);
    expect(getNextPresentationIndex(2, slides.length, 1)).toBe(2);
    expect(getBoundedPresentationIndex(Number.NaN, slides.length)).toBe(0);
  });

  it('scales the presentation canvas to fit the viewport without becoming tiny', () => {
    expect(getPresentationCanvasScale({
      viewportWidth: 1440,
      viewportHeight: 900,
      canvasWidth: 1280,
      canvasHeight: 720,
    })).toBeCloseTo(0.99, 2);

    expect(getPresentationCanvasScale({
      viewportWidth: 390,
      viewportHeight: 844,
      canvasWidth: 1280,
      canvasHeight: 720,
    })).toBe(0.35);
  });
});
