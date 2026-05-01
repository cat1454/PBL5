import React from 'react';
import StudyHub from './StudyHub';

function StreakGame({ documentId }) {
  return <StudyHub documentId={documentId} forcedMode="streak" showShell={!documentId} />;
}

export default StreakGame;
