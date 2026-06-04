import React from 'react';

function MetricTile({ icon, label, value, detail }) {
  return (
    <div className="v2-metric-tile">
      {icon && <div className="v2-metric-icon">{icon}</div>}
      <div>
        <span>{label}</span>
        <strong>{value}</strong>
        {detail && <small>{detail}</small>}
      </div>
    </div>
  );
}

export default MetricTile;
