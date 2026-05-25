export const DEFAULT_PRESET_KEY = 'deep_practice';
export const IMPORTABLE_DRAFT_STATUSES = ['Verified', 'Borderline'];
export const PRESET_KEYS = ['quick_review', 'deep_practice', 'mock_exam', 'flashcard_first', 'review_bank'];

export const PRESETS = {
  quick_review: {
    targetDraftCount: 12,
    mode: 'fast',
    questionTypes: ['MultipleChoice', 'TrueFalse'],
    difficulties: ['Easy', 'Medium'],
  },
  deep_practice: {
    targetDraftCount: 24,
    mode: 'quality',
    questionTypes: ['MultipleChoice', 'ShortAnswer', 'FillInTheBlank'],
    difficulties: ['Medium', 'Hard'],
  },
  mock_exam: {
    targetDraftCount: 30,
    mode: 'balanced',
    questionTypes: ['MultipleChoice', 'ShortAnswer'],
    difficulties: ['Medium', 'Hard'],
  },
  flashcard_first: {
    targetDraftCount: 20,
    mode: 'balanced',
    questionTypes: ['Flashcard', 'ShortAnswer'],
    difficulties: ['Easy', 'Medium'],
  },
  review_bank: {
    targetDraftCount: 50,
    mode: 'max_draft',
    questionTypes: ['MultipleChoice', 'Flashcard', 'ShortAnswer', 'TrueFalse'],
    difficulties: ['Easy', 'Medium', 'Hard'],
  },
};

export function getDefaultQuestionStudioForm() {
  return { ...PRESETS[DEFAULT_PRESET_KEY] };
}

export function getVisibleImportableDraftIds(drafts) {
  return (drafts || [])
    .filter((draft) => IMPORTABLE_DRAFT_STATUSES.includes(draft.status))
    .map((draft) => draft.id);
}

export function getImportableDraftIds(selectedDraftIds, drafts) {
  return (selectedDraftIds || []).filter((id) => {
    const draft = (drafts || []).find((item) => item.id === id);
    return draft && IMPORTABLE_DRAFT_STATUSES.includes(draft.status);
  });
}
