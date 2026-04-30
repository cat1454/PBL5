import React from 'react';
import StudyHub from './StudyHub';

function FlashcardGame({ documentId }) {
  return <StudyHub documentId={documentId} forcedMode="flashcards" showShell={!documentId} />;
}

export default FlashcardGame;
