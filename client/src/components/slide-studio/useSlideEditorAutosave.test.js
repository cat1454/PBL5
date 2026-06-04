import React, { act, useEffect } from 'react';
import { createRoot } from 'react-dom/client';
import useSlideEditorAutosave from './useSlideEditorAutosave';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

function AutosaveHarness({ onReady, onSave }) {
  const autosave = useSlideEditorAutosave({ debounceMs: 50, onSave });

  useEffect(() => {
    onReady(autosave);
  }, [autosave, onReady]);

  return null;
}

describe('useSlideEditorAutosave', () => {
  beforeEach(() => {
    jest.useFakeTimers();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('debounces rapid changes and reports autosave status', async () => {
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);
    const onSave = jest.fn().mockResolvedValue({ id: 7 });
    let api;

    await act(async () => {
      root.render(<AutosaveHarness onReady={(value) => { api = value; }} onSave={onSave} />);
    });

    act(() => {
      api.scheduleSave(7, { revision: 1 });
      api.scheduleSave(7, { revision: 2 });
    });

    expect(api.statusBySlideId[7]).toBe('dirty');
    expect(onSave).not.toHaveBeenCalled();

    await act(async () => {
      jest.advanceTimersByTime(50);
      await Promise.resolve();
    });

    expect(onSave).toHaveBeenCalledTimes(1);
    expect(onSave).toHaveBeenCalledWith(7, { revision: 2 });
    expect(api.statusBySlideId[7]).toBe('saved');

    act(() => root.unmount());
    container.remove();
  });

  it('flushes pending changes when the editor unmounts', async () => {
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);
    const onSave = jest.fn().mockResolvedValue({ id: 9 });
    let api;

    await act(async () => {
      root.render(<AutosaveHarness onReady={(value) => { api = value; }} onSave={onSave} />);
    });

    act(() => {
      api.scheduleSave(9, { revision: 3 });
      root.unmount();
    });

    expect(onSave).toHaveBeenCalledWith(9, { revision: 3 });

    container.remove();
  });
});
