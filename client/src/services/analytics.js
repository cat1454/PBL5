const DEV_EVENT_PREFIX = '[analytics]';
const MAX_BATCH_SIZE = 25;
const FLUSH_DELAY_MS = 1500;

let queue = [];
let flushTimer = null;

async function flushEvents() {
  if (flushTimer) {
    window.clearTimeout(flushTimer);
    flushTimer = null;
  }

  if (queue.length === 0) {
    return;
  }

  const events = queue.slice(0, MAX_BATCH_SIZE);
  queue = queue.slice(MAX_BATCH_SIZE);

  try {
    const { analyticsService } = await import('./api');
    await analyticsService.recordEvents(events);
  } catch (error) {
    // Analytics must never block study or generation flows.
  }

  if (queue.length > 0) {
    scheduleFlush();
  }
}

function scheduleFlush() {
  if (flushTimer || typeof window === 'undefined') {
    return;
  }

  flushTimer = window.setTimeout(flushEvents, FLUSH_DELAY_MS);
}

export function trackEvent(name, properties = {}) {
  if (!name || typeof name !== 'string') {
    return;
  }

  const payload = {
    name,
    properties: properties && typeof properties === 'object' ? properties : {},
    at: new Date().toISOString(),
  };

  if (process.env.NODE_ENV !== 'production') {
    // Frontend-only event layer for now; replace this body with a provider later.
    // eslint-disable-next-line no-console
    console.info(DEV_EVENT_PREFIX, payload);
  }

  queue.push({
    name: payload.name,
    properties: payload.properties,
    occurredAt: payload.at,
  });

  if (queue.length >= MAX_BATCH_SIZE) {
    flushEvents();
    return;
  }

  scheduleFlush();
}
