import { useEffect, useRef, useState } from 'react';

function clampPercent(value) {
  const numeric = Number(value);
  if (!Number.isFinite(numeric)) {
    return 0;
  }

  return Math.max(0, Math.min(100, numeric));
}

export function useAnimatedProgress(targetPercent, duration = 500) {
  const [displayPercent, setDisplayPercent] = useState(clampPercent(targetPercent));
  const displayPercentRef = useRef(displayPercent);
  const frameRef = useRef(null);

  useEffect(() => {
    displayPercentRef.current = displayPercent;
  }, [displayPercent]);

  useEffect(() => {
    const target = clampPercent(targetPercent);
    const from = displayPercentRef.current;
    const startedAt = performance.now();
    const safeDuration = Math.max(0, Number(duration) || 0);

    if (frameRef.current) {
      cancelAnimationFrame(frameRef.current);
    }

    if (safeDuration === 0 || from === target) {
      setDisplayPercent(target);
      displayPercentRef.current = target;
      return undefined;
    }

    const tick = (now) => {
      const elapsed = now - startedAt;
      const ratio = Math.min(1, elapsed / safeDuration);
      const eased = 1 - Math.pow(1 - ratio, 3);
      const next = from + (target - from) * eased;

      setDisplayPercent(next);
      displayPercentRef.current = next;

      if (ratio < 1) {
        frameRef.current = requestAnimationFrame(tick);
      }
    };

    frameRef.current = requestAnimationFrame(tick);

    return () => {
      if (frameRef.current) {
        cancelAnimationFrame(frameRef.current);
      }
    };
  }, [duration, targetPercent]);

  return displayPercent;
}
