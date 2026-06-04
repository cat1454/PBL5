import React from 'react';
import { shouldShowPageHeader } from '../app/routes';

function ContentFrame({ children, pathname, routeMeta, t }) {
  const showHeader = shouldShowPageHeader(routeMeta, pathname);

  return (
    <div className="app-shell-body v2-shell-body app-content-frame">
      <main className="app-main app-shell-content v2-shell-content">
        <div className="app-shell-content-inner v2-shell-content-inner app-content-frame-inner">
          {showHeader && (
            <div className="app-page-header v2-page-header app-route-header">
              <h1>{t(routeMeta.titleKey)}</h1>
            </div>
          )}
          {children}
        </div>
      </main>
    </div>
  );
}

export default ContentFrame;
