import baseTranslations from './translations';
import overrides from './overrides';

function isPlainObject(value) {
  return value && typeof value === 'object' && !Array.isArray(value);
}

function mergeDeep(base, override) {
  if (!isPlainObject(base) || !isPlainObject(override)) {
    return override === undefined ? base : override;
  }

  const keys = new Set([...Object.keys(base), ...Object.keys(override)]);
  const result = {};

  keys.forEach((key) => {
    result[key] = mergeDeep(base[key], override[key]);
  });

  return result;
}

const translations = {
  vi: mergeDeep(baseTranslations.vi || {}, overrides.vi || {}),
  en: mergeDeep(baseTranslations.en || {}, overrides.en || {}),
};

export default translations;
