const DEV_EVENT_PREFIX = '[analytics]';

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
}
