export const DASHBOARD_PIPELINE_KEYS = ['upload', 'ocr', 'analysis', 'questions', 'slides'];

export function buildDashboardViewModel(home = {}) {
  const sources = Array.isArray(home.sources) ? home.sources : [];
  const stats = home.stats || {};
  const defaultWorkspace = home.workspace || null;
  const sortedSources = sortSourcesByRecency(sources);
  const completedSources = sortedSources.filter(isCompletedSource);
  const studyReadySources = sortedSources.filter(isStudyReadySource);
  const processingSources = sortedSources.filter(isProcessingSource);
  const failedSources = sortedSources.filter((source) => normalizeSourceStatus(source.status) === 'failed');
  const latestDeck = defaultWorkspace?.latestDeck || null;

  return {
    defaultWorkspace,
    sources: sortedSources,
    recentSources: sortedSources.slice(0, 5),
    sourceCount: numberOrFallback(stats.sourceCount, sortedSources.length),
    completedCount: numberOrFallback(stats.completedSourceCount, completedSources.length),
    studyReadyCount: numberOrFallback(stats.studyReadySourceCount, studyReadySources.length),
    processingCount: numberOrFallback(stats.processingSourceCount, processingSources.length),
    failedCount: numberOrFallback(stats.failedSourceCount, failedSources.length),
    selectedCount: numberOrFallback(stats.selectedSourceCount, sortedSources.filter((source) => source.includeInWorkspaceSlides).length),
    hasSource: sortedSources.length > 0,
    hasCompletedSource: completedSources.length > 0,
    studyReadySource: studyReadySources[0] || null,
    processingSource: processingSources[0] || null,
    failedSource: failedSources[0] || null,
    latestSource: sortedSources[0] || null,
    latestCompletedSource: completedSources[0] || null,
    workspaceDeck: latestDeck,
    workspaceHasDeck: Boolean(stats.hasDeck ?? latestDeck),
    workspaceDeckReady: Boolean(stats.deckReady ?? latestDeck?.status === 'Completed'),
    workspaceDeckStale: Boolean(stats.deckStale ?? latestDeck?.isStale),
  };
}

export function buildPipelineSteps(vm, t) {
  return DASHBOARD_PIPELINE_KEYS.map((key) => {
    const state = getPipelineState(key, vm);
    return {
      key,
      state,
      label: t(`app.dashboard.pipeline.labels.${state}`),
      title: t(`app.dashboard.pipeline.steps.${key}.title`),
      body: t(`app.dashboard.pipeline.steps.${key}.${state}`),
    };
  });
}

export function getSourceAction(source, t) {
  if (!source) {
    return null;
  }

  if (normalizeSourceStatus(source.status) === 'failed') {
    return {
      type: 'workspaceStudio',
      label: t('app.dashboard.sourceActions.openWorkspace'),
    };
  }

  if (isStudyReadySource(source)) {
    return {
      type: 'quiz',
      documentId: source.id,
      label: t('app.dashboard.sourceActions.openQuiz'),
    };
  }

  return {
    type: 'workspaceStudio',
    label: t('app.dashboard.sourceActions.openWorkspace'),
  };
}

export function normalizeSourceStatus(status) {
  if (status === 0 || String(status) === 'Uploaded') {
    return 'uploaded';
  }

  if (status === 1 || String(status) === 'Extracting') {
    return 'extracting';
  }

  if (status === 2 || String(status) === 'Analyzing') {
    return 'analyzing';
  }

  if (status === 3 || String(status) === 'Completed') {
    return 'completed';
  }

  if (status === 4 || String(status) === 'Failed') {
    return 'failed';
  }

  return 'unknown';
}

export function isCompletedSource(source) {
  return normalizeSourceStatus(source?.status) === 'completed';
}

export function isStudyReadySource(source) {
  return isCompletedSource(source) && Number(source?.questionsCount || 0) > 0;
}

function isProcessingSource(source) {
  const status = normalizeSourceStatus(source?.status);
  return status === 'uploaded' || status === 'extracting' || status === 'analyzing';
}

function getPipelineState(key, vm) {
  switch (key) {
    case 'upload':
      return vm.hasSource ? 'complete' : 'pending';
    case 'ocr':
      return vm.hasCompletedSource ? 'complete' : vm.hasSource ? 'active' : 'pending';
    case 'analysis':
      return vm.hasCompletedSource ? 'complete' : vm.processingSource ? 'active' : 'pending';
    case 'questions':
      return vm.studyReadySource ? 'complete' : vm.hasCompletedSource ? 'active' : 'pending';
    case 'slides':
    default:
      return vm.workspaceHasDeck ? 'complete' : vm.hasCompletedSource ? 'active' : 'pending';
  }
}

function sortSourcesByRecency(sources) {
  return [...sources].sort((left, right) => {
    const rightTime = new Date(right.updatedAt || right.createdAt || 0).getTime();
    const leftTime = new Date(left.updatedAt || left.createdAt || 0).getTime();
    return rightTime - leftTime;
  });
}

function numberOrFallback(value, fallback) {
  return Number.isFinite(Number(value)) ? Number(value) : fallback;
}
