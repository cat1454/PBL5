import { buildSlideImageViewModel } from './slideImages';

describe('buildSlideImageViewModel', () => {
  it('normalizes backend text-only image plans as hidden media state', () => {
    const vm = buildSlideImageViewModel({
      slideType: 'Content',
      imageState: {
        needsImage: false,
        status: 'no-image-needed',
        message: 'This slide is text-only.',
      },
      imageCandidates: [],
    });

    expect(vm.needsImage).toBe(false);
    expect(vm.status).toBe('no-image-needed');
    expect(vm.hasCandidates).toBe(false);
    expect(vm.selectedImage).toBeNull();
  });

  it('normalizes invalid image plans as text-only without candidates', () => {
    const vm = buildSlideImageViewModel({
      slideType: 'Content',
      imageState: {
        needsImage: false,
        status: 'image-plan-invalid',
      },
      imageCandidates: [],
    });

    expect(vm.needsImage).toBe(false);
    expect(vm.status).toBe('image-plan-invalid');
    expect(vm.badgeLabel).toBe('Text only');
  });

  it('keeps media controls available for image-needed slides', () => {
    const vm = buildSlideImageViewModel({
      slideType: 'Content',
      imageState: {
        needsImage: true,
        status: 'ready',
      },
      imageCandidates: [
        {
          key: 'generated-1',
          sourceType: 'generated',
          provider: 'OpenAI',
          localAssetUrl: '/slide-assets/generated-1.png',
          isSelected: true,
        },
      ],
      selectedImageKey: 'generated-1',
    });

    expect(vm.needsImage).toBe(true);
    expect(vm.hasCandidates).toBe(true);
    expect(vm.selectedImage.key).toBe('generated-1');
    expect(vm.badgeLabel).toBe('AI image');
  });

  it('normalizes selected PDF-region image candidates with source metadata', () => {
    const vm = buildSlideImageViewModel({
      slideType: 'Content',
      imageState: {
        needsImage: true,
        status: 'ready',
      },
      imageCandidates: [
        {
          key: 'pdf-region-1-2-1',
          sourceType: 'pdf-region',
          provider: 'Source PDF',
          localAssetUrl: '/uploads/slide-assets/deck-1/slide-2/pdf-region-4-1.png',
          pageNumber: 4,
          regionType: 'ChartCandidate',
          regionText: 'Chart caption',
          isSelected: true,
        },
      ],
      selectedImageKey: 'pdf-region-1-2-1',
    });

    expect(vm.selectedImage.sourceType).toBe('pdf-region');
    expect(vm.selectedImage.pageNumber).toBe(4);
    expect(vm.selectedImage.regionType).toBe('ChartCandidate');
    expect(vm.badgeLabel).toBe('PDF image');
    expect(vm.attributionText).toBe('Source PDF, page 4');
  });
});
