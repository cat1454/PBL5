import { getNextBestAction } from './services/dashboardActions';
import { buildDashboardViewModel } from './features/dashboard/dashboardViewModel';

const t = (key) => key;

describe('dashboard next action', () => {
  it('routes completed sources without questions to Question Studio', () => {
    const action = getNextBestAction({
      hasSource: true,
      hasCompletedSource: true,
      latestCompletedSource: { id: 42, questionsCount: 0 },
      processingSource: null,
      studyReadySource: null,
      workspaceHasDeck: false,
      defaultWorkspace: { id: 7 },
    }, t);

    expect(action.action).toEqual({
      type: 'questionStudio',
      documentId: 42,
      label: 'app.dashboard.guide.actions.createQuestions',
    });
  });
});

describe('dashboard v2 contract adapter', () => {
  it('keeps completed sources without questions routed to Question Studio', () => {
    const vm = buildDashboardViewModel({
      workspace: { id: 7 },
      sources: [{ id: 42, status: 'Completed', questionsCount: 0, updatedAt: '2026-06-03T00:00:00Z' }],
    });
    const action = getNextBestAction(vm, t);

    expect(action.action).toEqual({
      type: 'questionStudio',
      documentId: 42,
      label: 'app.dashboard.guide.actions.createQuestions',
    });
  });
});
