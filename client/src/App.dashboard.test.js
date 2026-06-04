import { getNextBestAction } from './services/dashboardActions';

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
