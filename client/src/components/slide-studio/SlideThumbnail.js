import React, { memo, useMemo } from 'react';
import SlideCanvas from './SlideCanvas';

const THUMB_WIDTH = 116;
const THUMB_HEIGHT = 65.25;

function SlideThumbnail({ editorState, imageVm, labels }) {
  const scale = useMemo(() => {
    const canvas = editorState?.canvas || {};
    const width = Number(canvas.width) || 1280;
    const height = Number(canvas.height) || 720;
    return Math.min(THUMB_WIDTH / width, THUMB_HEIGHT / height);
  }, [editorState]);

  if (!editorState) {
    return (
      <span className="folder-studio-filmstrip-thumb is-empty">
        <i>--</i>
      </span>
    );
  }

  return (
    <span className="folder-studio-filmstrip-thumb is-real-preview">
      <SlideCanvas
        editorState={editorState}
        imageVm={imageVm}
        labels={labels}
        mode="clean"
        scale={scale}
      />
    </span>
  );
}

export default memo(SlideThumbnail);
