import {
  getDefaultQuestionStudioForm,
  getImportableDraftIds,
  getVisibleImportableDraftIds,
} from './questionStudioHelpers';

describe('QuestionStudioPage helpers', () => {
  it('defaults the form to the active deep practice preset', () => {
    expect(getDefaultQuestionStudioForm()).toEqual({
      targetDraftCount: 24,
      mode: 'quality',
      questionTypes: ['MultipleChoice', 'ShortAnswer', 'FillInTheBlank'],
      difficulties: ['Medium', 'Hard'],
    });
  });

  it('selects and imports only verified or borderline drafts', () => {
    const drafts = [
      { id: 1, status: 'Verified' },
      { id: 2, status: 'Rejected' },
      { id: 3, status: 'Borderline' },
      { id: 4, status: 'Quarantined' },
    ];

    expect(getVisibleImportableDraftIds(drafts)).toEqual([1, 3]);
    expect(getImportableDraftIds([1, 2, 3, 4], drafts)).toEqual([1, 3]);
  });
});
