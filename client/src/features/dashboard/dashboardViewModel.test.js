import {
  buildDashboardViewModel,
  buildPipelineSteps,
  getSourceAction,
  normalizeSourceStatus,
} from './dashboardViewModel';

const t = (key) => key;

describe('dashboard v2 view model', () => {
  it('normalizes status values from numeric and string payloads', () => {
    expect(normalizeSourceStatus(0)).toBe('uploaded');
    expect(normalizeSourceStatus(2)).toBe('analyzing');
    expect(normalizeSourceStatus('Completed')).toBe('completed');
    expect(normalizeSourceStatus('Failed')).toBe('failed');
  });

  it('builds empty state from the dashboard home contract', () => {
    const vm = buildDashboardViewModel({
      workspace: { id: 7, name: 'My Workspace' },
      sources: [],
      stats: { sourceCount: 0 },
    });

    expect(vm.defaultWorkspace.id).toBe(7);
    expect(vm.hasSource).toBe(false);
    expect(vm.sourceCount).toBe(0);
  });

  it('detects processing, study-ready, and stale deck states', () => {
    const vm = buildDashboardViewModel({
      workspace: {
        id: 7,
        latestDeck: { id: 11, status: 'Completed', isStale: true },
      },
      sources: [
        { id: 1, status: 'Analyzing', updatedAt: '2026-06-03T01:00:00Z', questionsCount: 0 },
        { id: 2, status: 'Completed', updatedAt: '2026-06-03T02:00:00Z', questionsCount: 4 },
      ],
      stats: {
        processingSourceCount: 1,
        studyReadySourceCount: 1,
        deckStale: true,
      },
    });

    expect(vm.processingCount).toBe(1);
    expect(vm.studyReadySource.id).toBe(2);
    expect(vm.workspaceDeckStale).toBe(true);
    expect(getSourceAction(vm.studyReadySource, t)).toEqual({
      type: 'quiz',
      documentId: 2,
      label: 'app.dashboard.sourceActions.openQuiz',
    });
  });

  it('builds pipeline states for empty, active, and complete stages', () => {
    const emptySteps = buildPipelineSteps(buildDashboardViewModel({ sources: [] }), t);
    const activeSteps = buildPipelineSteps(buildDashboardViewModel({
      sources: [{ id: 1, status: 'Analyzing' }],
    }), t);
    const completeSteps = buildPipelineSteps(buildDashboardViewModel({
      workspace: { latestDeck: { id: 1, status: 'Completed' } },
      sources: [{ id: 2, status: 'Completed', questionsCount: 2 }],
    }), t);

    expect(emptySteps.map((step) => step.state)).toEqual(['pending', 'pending', 'pending', 'pending', 'pending']);
    expect(activeSteps.map((step) => step.state)).toEqual(['complete', 'active', 'active', 'pending', 'pending']);
    expect(completeSteps.map((step) => step.state)).toEqual(['complete', 'complete', 'complete', 'complete', 'complete']);
  });
});
