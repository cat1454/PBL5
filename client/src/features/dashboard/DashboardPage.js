import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import {
  LuCircleAlert,
  LuBookOpen,
  LuBrain,
  LuFileUp,
  LuFolderOpen,
  LuLayers,
  LuPanelRightOpen,
  LuRefreshCw,
  LuSparkles,
  LuTriangleAlert,
} from 'react-icons/lu';
import DocumentUpload from '../../components/DocumentUpload';
import Button from '../../ui/Button';
import EmptyState from '../../ui/EmptyState';
import Panel from '../../ui/Panel';
import StatusBadge from '../../ui/StatusBadge';
import { useLanguage } from '../../context/LanguageContext';
import { dashboardService, getApiErrorMessage } from '../../services/api';
import { trackEvent } from '../../services/analytics';
import { getNextBestAction } from '../../services/dashboardActions';
import {
  buildDashboardViewModel,
  buildPipelineSteps,
  getSourceAction,
  normalizeSourceStatus,
} from './dashboardViewModel';

function DashboardPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const { language, t } = useLanguage();
  const [home, setHome] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const loadDashboard = useCallback(async () => {
    setLoading(true);
    setError('');

    try {
      setHome(await dashboardService.getHome());
    } catch (err) {
      setError(getApiErrorMessage(err, t('app.dashboard.loadErrorBody')));
      setHome(null);
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => {
    loadDashboard();
  }, [loadDashboard]);

  useEffect(() => {
    if (!location.state?.openGuide) {
      return;
    }

    const guide = document.getElementById('dashboard-next-action');
    guide?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    navigate(location.pathname, { replace: true, state: null });
  }, [location.pathname, location.state, navigate]);

  const vm = useMemo(() => buildDashboardViewModel(home || {}), [home]);
  const pipelineSteps = useMemo(() => buildPipelineSteps(vm, t), [t, vm]);
  const nextAction = useMemo(() => getNextBestAction(vm, t), [t, vm]);

  const executeAction = useCallback((action) => {
    if (!action || action.disabled) {
      return;
    }

    trackEvent('dashboard_v2_action_clicked', {
      actionType: action.type,
      documentId: action.documentId || null,
    });

    switch (action.type) {
      case 'upload':
        document.getElementById('dashboard-upload-section')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
        break;
      case 'workspaces':
      case 'workspaceStudio':
        navigate(vm.defaultWorkspace?.id ? `/workspaces/${vm.defaultWorkspace.id}` : '/workspaces');
        break;
      case 'questionStudio':
        if (action.documentId) {
          navigate(`/question-studio/${action.documentId}`);
        }
        break;
      case 'quiz':
      case 'flashcards':
      case 'streak':
        if (action.documentId) {
          navigate(`/${action.type}/${action.documentId}`);
        }
        break;
      case 'slides':
        navigate(vm.defaultWorkspace?.id ? `/workspaces/${vm.defaultWorkspace.id}` : '/workspaces');
        break;
      default:
        break;
    }
  }, [navigate, vm.defaultWorkspace?.id]);

  const handleUploadSuccess = useCallback(async () => {
    await loadDashboard();
  }, [loadDashboard]);

  return (
    <div className="v2-dashboard">
      <DashboardConsoleHeader
        pipelineSteps={pipelineSteps}
        onAction={executeAction}
        t={t}
        vm={vm}
      />

      {loading && (
        <Panel className="v2-dashboard-state">
          <div className="spinner" />
          <p>{t('app.dashboard.loading')}</p>
        </Panel>
      )}

      {error && (
        <Panel className="v2-dashboard-state" tone="danger">
          <LuCircleAlert aria-hidden="true" />
          <div>
            <strong>{t('app.dashboard.loadErrorTitle')}</strong>
            <p>{error}</p>
          </div>
          <Button variant="secondary" icon={<LuRefreshCw aria-hidden="true" />} onClick={loadDashboard}>
            {t('app.dashboard.retry')}
          </Button>
        </Panel>
      )}

      {!loading && !error && (
        <>
          <NextActionBar nextAction={nextAction} onAction={executeAction} t={t} />

          <div className="v2-console-grid">
            <SourceQueue
              language={language}
              onAction={executeAction}
              sources={vm.recentSources}
              t={t}
            />
            <UploadDock
              onAction={executeAction}
              onUploadSuccess={handleUploadSuccess}
              t={t}
              vm={vm}
            />
          </div>
        </>
      )}
    </div>
  );
}

function DashboardConsoleHeader({ onAction, pipelineSteps, t, vm }) {
  const deckLabel = vm.workspaceDeckStale
    ? t('app.dashboard.deck.stale')
    : vm.workspaceHasDeck
      ? t('app.dashboard.deck.ready')
      : t('app.dashboard.deck.none');
  const consoleTitle = vm.latestSource?.fileName
    ? `${t('app.dashboard.v2.title')} - ${vm.latestSource.fileName}`
    : t('app.dashboard.v2.title');

  return (
    <section className="v2-console-header" aria-label={t('app.dashboard.v2.commandCenter')}>
      <div className="v2-console-title">
        <span className="v2-kicker">{t('app.dashboard.v2.kicker')}</span>
        <h1>{consoleTitle}</h1>
        <p>{t('app.dashboard.v2.summaryCounts', { sources: vm.sourceCount, completed: vm.completedCount })}</p>
      </div>

      {vm.workspaceDeckStale && (
        <DeckRefreshBanner onAction={onAction} t={t} />
      )}

      <WorkspaceStatusStrip deckLabel={deckLabel} t={t} vm={vm} />
      <PipelineStepper steps={pipelineSteps} t={t} />
    </section>
  );
}

function DeckRefreshBanner({ onAction, t }) {
  return (
    <div className="v2-deck-warning" role="status">
      <LuTriangleAlert aria-hidden="true" />
      <div>
        <strong>{t('app.dashboard.v2.deckWarningTitle')}</strong>
        <p>{t('app.dashboard.v2.deckWarningBody')}</p>
      </div>
      <Button
        className="v2-button-compact"
        variant="secondary"
        icon={<LuRefreshCw aria-hidden="true" />}
        onClick={() => onAction({ type: 'workspaceStudio' })}
      >
        {t('app.dashboard.v2.refreshDeck')}
      </Button>
    </div>
  );
}

function WorkspaceStatusStrip({ deckLabel, t, vm }) {
  const statusTone = vm.processingCount > 0 ? 'active' : vm.failedCount > 0 ? 'danger' : 'good';
  const statusLabel = vm.processingCount > 0
    ? t('app.dashboard.v2.processing')
    : vm.failedCount > 0
      ? t('app.dashboard.v2.attention')
      : t('app.dashboard.v2.ready');
  const stats = [
    { icon: <LuBookOpen aria-hidden="true" />, label: t('app.dashboard.stats.sourcesLabel'), value: vm.sourceCount },
    { icon: <LuBrain aria-hidden="true" />, label: t('app.dashboard.stats.completedLabel'), value: vm.completedCount },
    { icon: <LuSparkles aria-hidden="true" />, label: t('app.dashboard.stats.readyLabel'), value: vm.studyReadyCount },
    { icon: <LuLayers aria-hidden="true" />, label: t('app.dashboard.v2.deck'), value: deckLabel },
  ];

  return (
    <div className="v2-status-strip">
      <StatusBadge tone={statusTone} label={statusLabel} />
      {stats.map((item) => (
        <div className="v2-status-stat" key={item.label}>
          <span className="v2-status-icon">{item.icon}</span>
          <small>{item.label}</small>
          <strong>{item.value}</strong>
        </div>
      ))}
    </div>
  );
}

function NextActionBar({ nextAction, onAction, t }) {
  return (
    <Panel id="dashboard-next-action" className="v2-next-action">
      <div className="v2-panel-headline">
        <div>
          <span className="v2-kicker">{nextAction.eyebrow}</span>
          <h2>{nextAction.title}</h2>
        </div>
        <p>{nextAction.body}</p>
      </div>
      <div className="v2-action-row">
        {nextAction.action && (
          <Button
            className="v2-button-compact"
            disabled={nextAction.action.disabled}
            icon={<LuSparkles aria-hidden="true" />}
            onClick={() => onAction(nextAction.action)}
          >
            {nextAction.action.label}
          </Button>
        )}
        {nextAction.secondaryAction && (
          <Button
            className="v2-button-compact"
            variant="secondary"
            disabled={nextAction.secondaryAction.disabled}
            icon={<LuPanelRightOpen aria-hidden="true" />}
            onClick={() => onAction(nextAction.secondaryAction)}
          >
            {nextAction.secondaryAction.label}
          </Button>
        )}
        <Button
          className="v2-button-compact"
          variant="ghost"
          icon={<LuFolderOpen aria-hidden="true" />}
          onClick={() => onAction({ type: 'workspaceStudio' })}
        >
          {t('app.dashboard.v2.workspace')}
        </Button>
      </div>
    </Panel>
  );
}

function PipelineStepper({ steps, t }) {
  return (
    <div className="v2-pipeline-panel">
      <div className="v2-panel-headline">
        <span className="v2-kicker">{t('app.dashboard.pipeline.title')}</span>
      </div>
      <div className="v2-stepper" aria-label={t('app.dashboard.v2.pipelineTitle')}>
        {steps.map((step, index) => (
          <div className={`v2-stepper-item is-${step.state}`} key={step.key}>
            <span className="v2-stepper-index">{index + 1}</span>
            <strong>{step.title}</strong>
            <small>{step.label}</small>
          </div>
        ))}
      </div>
    </div>
  );
}

function SourceQueue({ language, onAction, sources, t }) {
  return (
    <Panel className="v2-sources-panel">
      <div className="v2-panel-headline v2-source-panel-headline">
        <div>
          <span className="v2-kicker">{t('app.dashboard.recentSourcesTitle')}</span>
          <h2>{t('app.dashboard.recentSourcesTitle')}</h2>
        </div>
        <Button className="v2-button-compact" variant="secondary" icon={<LuFileUp aria-hidden="true" />} onClick={() => onAction({ type: 'upload' })}>
          {t('app.dashboard.actions.upload')}
        </Button>
      </div>

      {sources.length === 0 ? (
        <EmptyState
          icon={<LuFileUp aria-hidden="true" />}
          title={t('app.dashboard.emptyTitle')}
          body={t('app.dashboard.emptyBody')}
          action={(
            <Button icon={<LuFileUp aria-hidden="true" />} onClick={() => onAction({ type: 'upload' })}>
              {t('app.dashboard.actions.upload')}
            </Button>
          )}
        />
      ) : (
        <div className="v2-source-list">
          {sources.map((source) => (
            <SourceRow
              key={source.id}
              language={language}
              source={source}
              t={t}
              onAction={(action) => onAction(action)}
            />
          ))}
        </div>
      )}
    </Panel>
  );
}

function UploadDock({ onAction, onUploadSuccess, t, vm }) {
  return (
    <Panel id="dashboard-upload-section" className="v2-upload-panel">
      <div className="v2-panel-headline v2-upload-headline">
        <div>
          <span className="v2-kicker">{t('app.dashboard.actions.upload')}</span>
          <h2>{t('app.dashboard.uploadTitle')}</h2>
        </div>
        <Button className="v2-button-compact" variant="ghost" icon={<LuFolderOpen aria-hidden="true" />} onClick={() => onAction({ type: 'workspaceStudio' })}>
          {vm.defaultWorkspace?.name || t('app.dashboard.defaultWorkspaceFallback')}
        </Button>
      </div>
      <DocumentUpload onUploadSuccess={onUploadSuccess} />
    </Panel>
  );
}

function SourceRow({ language, onAction, source, t }) {
  const status = normalizeSourceStatus(source.status);
  const action = getSourceAction(source, t);
  const tone = status === 'completed' ? 'good' : status === 'failed' ? 'danger' : status === 'extracting' || status === 'analyzing' ? 'active' : 'neutral';
  const progress = source.processingProgress?.percent ?? (status === 'completed' || status === 'failed' ? 100 : 0);

  return (
    <article className="v2-source-row">
      <div className="v2-source-main">
        <div className="v2-source-icon"><LuBookOpen aria-hidden="true" /></div>
        <div>
          <div className="v2-source-title-line">
            <strong>{source.fileName}</strong>
            <StatusBadge tone={tone} label={t(`app.dashboard.status.${status}`)} />
          </div>
          <div className="v2-source-meta">
            <span>{t('app.dashboard.sourceMeta.questions', { count: source.questionsCount || 0 })}</span>
            <span>{t('app.dashboard.sourceMeta.updated', { time: formatRelativeTime(source.updatedAt || source.createdAt, t, language) })}</span>
            <span>{source.isStructureReady ? t('app.dashboard.v2.structureReady') : t('app.dashboard.v2.structurePending')}</span>
          </div>
          <div className="v2-source-progress" aria-label={t('app.dashboard.v2.progressLabel', { percent: progress })}>
            <span style={{ width: `${Math.max(0, Math.min(100, progress))}%` }} />
          </div>
        </div>
      </div>
      {action && (
        <Button variant="ghost" onClick={() => onAction(action)}>
          {action.label}
        </Button>
      )}
    </article>
  );
}

function formatRelativeTime(value, t, language) {
  if (!value) {
    return '-';
  }

  const diffMs = Date.now() - new Date(value).getTime();
  if (diffMs < 60_000) {
    return t('workspaces.relativeTime.justNow');
  }
  if (diffMs < 3_600_000) {
    return t('workspaces.relativeTime.minutesAgo', { count: Math.max(1, Math.floor(diffMs / 60_000)) });
  }
  if (diffMs < 86_400_000) {
    return t('workspaces.relativeTime.hoursAgo', { count: Math.max(1, Math.floor(diffMs / 3_600_000)) });
  }

  return new Date(value).toLocaleString(language === 'vi' ? 'vi-VN' : 'en-US');
}

export default DashboardPage;
