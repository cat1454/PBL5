import React from 'react';
import StudyHub from './StudyHub';

function QuizGame({ documentId }) {
  return <StudyHub documentId={documentId} forcedMode="quiz" showShell={!documentId} />;
}

export default QuizGame;
