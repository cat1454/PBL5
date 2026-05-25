import React from 'react';

export default function StudySessionBrief({ title, caption, pill, actions }) {
  return (
    <div className="study-card-toolbar">
      <div>
        <h3>{title}</h3>
        {caption && <p className="study-card-caption">{caption}</p>}
      </div>
      {(pill || actions) && (
        <div className="study-card-tools">
          {pill}
          {actions}
        </div>
      )}
    </div>
  );
}
