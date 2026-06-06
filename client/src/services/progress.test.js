import {
  getGenerationControlCapabilities,
  isActiveProgress,
} from './progress';

describe('generation progress controls', () => {
  it('treats paused generation as active', () => {
    expect(isActiveProgress({ status: 'paused' })).toBe(true);
  });

  it('shows pause and cancel for running generation', () => {
    expect(getGenerationControlCapabilities('running')).toEqual({
      canPause: true,
      canResume: false,
      canCancel: true,
    });
  });

  it('shows resume and cancel for paused generation', () => {
    expect(getGenerationControlCapabilities('paused')).toEqual({
      canPause: false,
      canResume: true,
      canCancel: true,
    });
  });

  it('hides controls for terminal generation', () => {
    expect(getGenerationControlCapabilities('completed')).toEqual({
      canPause: false,
      canResume: false,
      canCancel: false,
    });
  });
});
