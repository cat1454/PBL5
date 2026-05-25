export function getNextBestAction(vm, t) {
  if (!vm.hasSource) {
    return {
      eyebrow: t('app.dashboard.nextAction.eyebrow'),
      title: t('app.dashboard.nextAction.states.noSource.title'),
      body: t('app.dashboard.nextAction.states.noSource.body'),
      action: { type: 'upload', label: t('app.dashboard.actions.upload') },
      secondaryAction: { type: 'workspaces', label: t('app.dashboard.actions.openWorkspace'), secondary: true },
    };
  }

  if (vm.processingSource && !vm.hasCompletedSource) {
    return {
      eyebrow: t('app.dashboard.nextAction.eyebrow'),
      title: t('app.dashboard.nextAction.states.processing.title'),
      body: t('app.dashboard.nextAction.states.processing.body'),
      action: { type: 'workspaceStudio', label: t('app.dashboard.guide.actions.viewProgress'), disabled: !vm.defaultWorkspace?.id },
    };
  }

  if (vm.latestCompletedSource && Number(vm.latestCompletedSource.questionsCount || 0) === 0) {
    return {
      eyebrow: t('app.dashboard.nextAction.eyebrow'),
      title: t('app.dashboard.nextAction.states.noQuestions.title'),
      body: t('app.dashboard.nextAction.states.noQuestions.body'),
      action: { type: 'questionStudio', documentId: vm.latestCompletedSource.id, label: t('app.dashboard.guide.actions.createQuestions') },
    };
  }

  if (vm.studyReadySource && !vm.workspaceHasDeck) {
    return {
      eyebrow: t('app.dashboard.nextAction.eyebrow'),
      title: t('app.dashboard.nextAction.states.noDeck.title'),
      body: t('app.dashboard.nextAction.states.noDeck.body'),
      action: { type: 'workspaceStudio', label: t('app.dashboard.guide.actions.createDeck'), disabled: !vm.defaultWorkspace?.id },
      secondaryAction: { type: 'quiz', documentId: vm.studyReadySource.id, label: t('app.dashboard.guide.actions.openQuiz') },
    };
  }

  if (vm.studyReadySource) {
    return {
      eyebrow: t('app.dashboard.nextAction.eyebrow'),
      title: t('app.dashboard.nextAction.states.studyReady.title'),
      body: t('app.dashboard.nextAction.states.studyReady.body'),
      action: { type: 'quiz', documentId: vm.studyReadySource.id, label: t('app.dashboard.guide.actions.openQuiz') },
      secondaryAction: { type: 'flashcards', documentId: vm.studyReadySource.id, label: t('app.dashboard.guide.actions.openFlashcards'), secondary: true },
    };
  }

  return {
    eyebrow: t('app.dashboard.nextAction.eyebrow'),
    title: t('app.dashboard.nextAction.states.workspace.title'),
    body: t('app.dashboard.nextAction.states.workspace.body'),
    action: { type: 'workspaceStudio', label: t('app.dashboard.guide.actions.openStudio'), disabled: !vm.defaultWorkspace?.id },
  };
}
