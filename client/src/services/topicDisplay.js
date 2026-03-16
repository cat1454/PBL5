export const formatTopicForDisplay = (rawTopic) => {
  if (!rawTopic || typeof rawTopic !== 'string') {
    return {
      friendlyLabel: 'Chủ đề con: Chưa phân loại',
      technicalTag: null,
      mainTopic: null,
      subTopic: null,
    };
  }

  const trimmed = rawTopic.trim();
  if (!trimmed) {
    return {
      friendlyLabel: 'Chủ đề con: Chưa phân loại',
      technicalTag: null,
      mainTopic: null,
      subTopic: null,
    };
  }

  const splitIndex = trimmed.indexOf(':');
  if (splitIndex === -1) {
    return {
      friendlyLabel: `Chủ đề con: ${trimmed}`,
      technicalTag: null,
      mainTopic: null,
      subTopic: trimmed,
    };
  }

  const mainToken = trimmed.slice(0, splitIndex).trim();
  const subToken = trimmed.slice(splitIndex + 1).trim();

  const prettifyToken = (token) =>
    token
      .split('-')
      .filter(Boolean)
      .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
      .join(' ');

  const friendlyMain = prettifyToken(mainToken);
  const friendlySub = prettifyToken(subToken) || 'Chưa rõ';

  return {
    friendlyLabel: `Chủ đề con: ${friendlySub}`,
    technicalTag: trimmed,
    mainTopic: friendlyMain || null,
    subTopic: friendlySub,
  };
};
