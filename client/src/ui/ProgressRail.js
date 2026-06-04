import React from 'react';
import { LuCheck, LuCircle, LuLoader } from 'react-icons/lu';

function ProgressRail({ steps }) {
  return (
    <div className="v2-progress-rail">
      {steps.map((step) => (
        <article key={step.key} className={`v2-progress-step is-${step.state}`}>
          <div className="v2-progress-marker" aria-hidden="true">
            {step.state === 'complete' ? <LuCheck /> : step.state === 'active' ? <LuLoader /> : <LuCircle />}
          </div>
          <div>
            <span>{step.label}</span>
            <strong>{step.title}</strong>
            <p>{step.body}</p>
          </div>
        </article>
      ))}
    </div>
  );
}

export default ProgressRail;
