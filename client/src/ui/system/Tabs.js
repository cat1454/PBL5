import React from 'react';
import { cx } from './utils';

function Tabs({ ariaLabel, className, onChange, tabs, value }) {
  const activeTab = tabs.find((tab) => tab.value === value) || tabs[0];

  return (
    <div className={cx('sys-tabs', className)}>
      <div className="sys-tab-list" role="tablist" aria-label={ariaLabel}>
        {tabs.map((tab) => (
          <button
            key={tab.value}
            type="button"
            role="tab"
            className={tab.value === activeTab.value ? 'is-active' : ''}
            aria-selected={tab.value === activeTab.value}
            onClick={() => onChange(tab.value)}
          >
            {tab.label}
          </button>
        ))}
      </div>
      {activeTab?.panel && (
        <div className="sys-tab-panel" role="tabpanel">
          {activeTab.panel}
        </div>
      )}
    </div>
  );
}

export default Tabs;
