import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  LuArrowRight,
  LuBookOpen,
  LuBrain,
  LuCalendarDays,
  LuChartLine,
  LuCheck,
  LuClock3,
  LuCircle,
  LuFileText,
  LuFlame,
  LuLayoutDashboard,
  LuRadar,
  LuSparkles,
  LuUsers,
} from 'react-icons/lu';
import { useAuth } from '../context/AuthContext';
import { useLanguage } from '../context/LanguageContext';
import { analyticsService, getApiErrorMessage } from '../services/api';
import Button from '../ui/Button';
import EmptyState from '../ui/EmptyState';
import Panel from '../ui/Panel';

const HEATMAP_DAYS_PER_WEEK = 7;
const SKILL_KEYS = ['recall', 'concepts', 'questionBank', 'slides', 'consistency'];

function PersonalAnalyticsDashboard() {
  const { currentUser } = useAuth();
  const { language, t } = useLanguage();
  const navigate = useNavigate();
  const { summary, loading, error, reload } = usePersonalAnalyticsData();

  const vm = useMemo(
    () => buildAnalyticsViewModel(currentUser, summary, language, t),
    [currentUser, summary, language, t],
  );

  const openAction = useCallback((action) => {
    if (!action?.to) {
      return;
    }

    navigate(action.to);
  }, [navigate]);

  return (
    <div className="analytics-dashboard">
      {loading && (
        <Panel className="analytics-state-card">
          <div className="spinner"></div>
          <p>{t('analyticsDashboard.loading')}</p>
        </Panel>
      )}

      {error && (
        <AnalyticsErrorState error={error} onRetry={reload} t={t} />
      )}

      <section className="analytics-metric-grid" aria-label={t('analyticsDashboard.metrics.label')}>
        {vm.metrics.map((metric) => (
          <Panel key={metric.key} className={`analytics-metric-card tone-${metric.key}`}>
            <div className="analytics-card-icon">{metric.icon}</div>
            <div className="analytics-metric-copy">
              <span>{metric.label}</span>
              <strong>{metric.value}</strong>
              <p>{metric.hint}</p>
            </div>
          </Panel>
        ))}
      </section>

      <Panel className="analytics-panel analytics-heatmap-panel">
        <div className="analytics-section-head">
          <div>
            <span>{t('analyticsDashboard.heatmap.kicker')}</span>
            <h3><LuCalendarDays aria-hidden="true" /> {t('analyticsDashboard.heatmap.title')}</h3>
          </div>
          <p>{vm.heatmapSummary}</p>
        </div>

        <div className="analytics-heatmap-layout">
          <div className="analytics-heatmap-chart-card">
            <div
              className="analytics-heatmap-shell"
              aria-label={t('analyticsDashboard.heatmap.label')}
              style={{ '--heatmap-week-count': vm.heatmapWeekCount }}
            >
              <div className="analytics-heatmap-months">
                <span aria-hidden="true" />
                <div className="analytics-heatmap-month-track">
                  {vm.heatmapMonthLabels.map((month) => (
                    <span key={month.key} style={{ gridColumn: month.weekIndex + 1 }}>
                      {month.label}
                    </span>
                  ))}
                </div>
              </div>
              <div className="analytics-heatmap-body">
                <div className="analytics-heatmap-days" aria-hidden="true">
                  {vm.weekdayLabels.map((label, index) => (
                    <span key={`${label}-${index}`}>{label}</span>
                  ))}
                </div>
                <div className="analytics-heatmap-grid">
                  {vm.heatmapWeeks.map((week) => (
                    <div key={week.key} className="analytics-heatmap-week">
                      {week.days.map((day) => (
                        <span
                          key={day.key}
                          className={[
                            'analytics-heatmap-cell',
                            `level-${day.level}`,
                            day.isOutsideYear ? 'is-outside-year' : '',
                            day.isFuture ? 'is-future' : '',
                          ].filter(Boolean).join(' ')}
                          title={day.isInteractive ? day.title : undefined}
                          aria-label={day.isInteractive ? day.title : undefined}
                          aria-hidden={day.isInteractive ? undefined : 'true'}
                        />
                      ))}
                    </div>
                  ))}
                </div>
              </div>
            </div>

            <div className="analytics-heatmap-footer">
              <span>{t('analyticsDashboard.heatmap.calendarYear', { year: vm.calendarYear })}</span>
              <div className="analytics-legend">
                <span>{t('analyticsDashboard.heatmap.less')}</span>
                {[0, 1, 2, 3, 4].map((level) => (
                  <span key={level} className={`analytics-heatmap-cell level-${level}`} />
                ))}
                <span>{t('analyticsDashboard.heatmap.more')}</span>
              </div>
            </div>
          </div>

          <aside className="analytics-heatmap-summary-card">
            <div className="analytics-heatmap-stat-list">
              {vm.heatmapStats.map((stat) => (
                <div key={stat.key} className="analytics-heatmap-stat">
                  <span>{stat.label}</span>
                  <strong>{stat.value}</strong>
                </div>
              ))}
            </div>

            {vm.heatmapActiveCells === 0 ? (
              <div className="analytics-heatmap-empty">
                <strong>{t('analyticsDashboard.heatmap.emptyTitle')}</strong>
                <p>{t('analyticsDashboard.heatmap.emptyBody')}</p>
              </div>
            ) : (
              <div className="analytics-heatmap-active">
                <strong>{vm.heatmapSummary}</strong>
                <p>{t('analyticsDashboard.heatmap.activeBody', { peak: vm.peakActivityLabel })}</p>
              </div>
            )}

            <Button onClick={() => openAction(vm.heatmapCta)}>
              {vm.heatmapCta.label}
            </Button>
          </aside>
        </div>
      </Panel>

      <div className="analytics-main-grid">
        <Panel className="analytics-panel analytics-radar-panel">
          <div className="analytics-section-head">
            <div>
              <span>{t('analyticsDashboard.skills.kicker')}</span>
              <h3><LuRadar aria-hidden="true" /> {t('analyticsDashboard.skills.title')}</h3>
            </div>
            <p>{vm.copy.skillHint}</p>
          </div>

          <div className="analytics-radar-layout">
            <RadarChart vm={vm} />
            <div className="analytics-skill-list">
              {vm.skills.map((skill) => (
                <article key={skill.key} className={`analytics-skill-row tone-${skill.statusTone}`}>
                  <div>
                    <strong>{skill.label}</strong>
                    <span>{skill.statusLabel}</span>
                  </div>
                  <small>{skill.value}%</small>
                </article>
              ))}
            </div>
          </div>
        </Panel>

        <Panel className="analytics-panel analytics-insight-panel">
          <div className="analytics-section-head">
            <div>
              <span>{t('analyticsDashboard.insight.kicker')}</span>
              <h3><LuSparkles aria-hidden="true" /> {t('analyticsDashboard.insight.title')}</h3>
            </div>
          </div>
          <div className="analytics-checklist">
            {vm.checklist.map((item) => (
              <article key={item.key} className={`analytics-checklist-item state-${item.state}`}>
                <span aria-hidden="true">{getChecklistIcon(item.state)}</span>
                <div>
                  <strong>{item.title}</strong>
                  <small>{item.statusLabel}</small>
                </div>
              </article>
            ))}
          </div>
          <p className="analytics-coach-note">{vm.copy.insight}</p>
          <div className="analytics-action-row">
            {vm.actions.map((action) => (
              <Button
                key={action.to}
                variant={action.secondary ? 'secondary' : 'primary'}
                onClick={() => openAction(action)}
              >
                {action.label}
              </Button>
            ))}
          </div>
        </Panel>
      </div>

      <Panel className="analytics-panel analytics-activity-panel">
        <div className="analytics-section-head">
          <div>
            <span>{t('analyticsDashboard.activity.kicker')}</span>
            <h3><LuChartLine aria-hidden="true" /> {t('analyticsDashboard.activity.title')}</h3>
          </div>
        </div>

        {vm.isActivityEmpty ? (
          <EmptyState
            icon={<LuLayoutDashboard aria-hidden="true" />}
            title={t('analyticsDashboard.activity.emptyTitle')}
            body={t('analyticsDashboard.activity.emptyBody')}
            action={(
              <Button onClick={() => openAction(vm.workspaceAction)}>
                {t('analyticsDashboard.actions.openWorkspaces')}
              </Button>
            )}
          />
        ) : (
          <div className="analytics-activity-list">
            {vm.activities.map((activity) => (
              <article key={activity.key} className="analytics-activity-item">
                <div className={`analytics-activity-icon tone-${activity.tone}`}>
                  {activity.icon}
                </div>
                <div>
                  <strong>{activity.title}</strong>
                  <p>{activity.body}</p>
                </div>
                <span>{activity.time}</span>
              </article>
            ))}
          </div>
        )}
      </Panel>
    </div>
  );
}

export function AnalyticsErrorState({ error, onRetry, t }) {
  return (
    <Panel className="analytics-error-card" tone="danger">
      <div>
        <strong>{t('analyticsDashboard.errorTitle')}</strong>
        <p>{error}</p>
      </div>
      <Button variant="secondary" onClick={onRetry}>
        {t('analyticsDashboard.retry')}
      </Button>
    </Panel>
  );
}

function RadarChart({ vm }) {
  return (
    <div className="analytics-radar-chart" aria-label={vm.radarLabel}>
      <svg viewBox="0 0 260 240" role="img">
        <defs>
          <radialGradient id="analyticsRadarFill" cx="50%" cy="46%" r="62%">
            <stop offset="0%" stopColor="#fbbf24" stopOpacity="0.42" />
            <stop offset="100%" stopColor="#38bdf8" stopOpacity="0.2" />
          </radialGradient>
        </defs>
        {vm.radarGrid.map((ring) => (
          <polygon key={ring.key} points={ring.points} className="analytics-radar-ring" />
        ))}
        {vm.radarAxis.map((axis) => (
          <line key={axis.key} x1="130" y1="120" x2={axis.x} y2={axis.y} className="analytics-radar-axis" />
        ))}
        <polygon points={vm.radarPolygon} className="analytics-radar-area" />
        {vm.radarPoints.map((point) => (
          <g key={point.key}>
            <circle cx={point.x} cy={point.y} r="4.5" className={point.isWeakest ? 'is-weakest' : ''} />
          </g>
        ))}
      </svg>
    </div>
  );
}

function getChecklistIcon(state) {
  if (state === 'ready') {
    return <LuCheck aria-hidden="true" />;
  }

  if (state === 'next') {
    return <LuArrowRight aria-hidden="true" />;
  }

  return <LuCircle aria-hidden="true" />;
}

function usePersonalAnalyticsData() {
  const { currentUser } = useAuth();
  const { t } = useLanguage();
  const [summary, setSummary] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const loadData = useCallback(async () => {
    if (!currentUser?.id) {
      setSummary(null);
      setLoading(false);
      return;
    }

    setLoading(true);
    setError('');

    try {
      const nextSummary = await analyticsService.getPersonalSummary();
      setSummary(nextSummary || null);
    } catch (err) {
      setSummary(null);
      setError(getApiErrorMessage(err, t('analyticsDashboard.errorBody')));
    } finally {
      setLoading(false);
    }
  }, [currentUser?.id, t]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  return { summary, loading, error, reload: loadData };
}

function buildAnalyticsViewModel(user, summary, language, t) {
  const role = normalizeRole(user?.role);
  const workspace = summary?.workspace || null;
  const sources = sortSourcesByRecency(Array.isArray(summary?.sources) ? summary.sources : []);
  const metricsData = summary?.metrics || {};
  const completedSources = sources.filter(isCompletedSource);
  const studyReadySources = sources.filter(isStudyReadySource);
  const latestSource = sources[0] || null;
  const rolePath = `analyticsDashboard.roles.${role}`;
  const roleLabel = t(`app.roles.${role}`);
  const workspaceName = workspace?.name || t('analyticsDashboard.workspaceFallback');
  const sourceCount = Number(metricsData.sourceCount ?? sources.length);
  const completedCount = Number(metricsData.completedSourceCount ?? completedSources.length);
  const readyCount = Number(metricsData.readySourceCount ?? studyReadySources.length);
  const deckReady = Boolean(workspace?.latestDeck);
  const questionTotal = Number(metricsData.questionCount ?? sources.reduce((sum, source) => sum + Number(source?.questionsCount || source?.QuestionsCount || 0), 0));
  const attemptCount = Number(metricsData.attemptCount || 0);
  const testCount = Number(metricsData.testCount || 0);
  const hasLearningData = sourceCount > 0 || completedCount > 0 || readyCount > 0 || deckReady || questionTotal > 0 || attemptCount > 0 || testCount > 0;
  const studySeconds = Number(metricsData.studySeconds || 0);
  const readiness = Math.round(Number(metricsData.readinessPercent || 0));
  const streak = Number(metricsData.currentStreakDays || 0);
  const latestSourceId = latestSource?.documentId || latestSource?.DocumentId || latestSource?.id;
  const readyDocumentId = summary?.actionsContext?.latestReadySourceId
    || (isStudyReadySource(latestSource) ? latestSourceId : null);
  const fallbackDocumentId = summary?.actionsContext?.latestCompletedSourceId
    || summary?.actionsContext?.latestSourceId
    || latestSourceId;
  const primaryDocumentId = summary?.actionsContext?.latestReadySourceId
    || summary?.actionsContext?.latestCompletedSourceId
    || summary?.actionsContext?.latestSourceId
    || latestSourceId;
  const skillByKey = new Map((summary?.skills || []).map((skill) => [skill.key, Number(skill.value || 0)]));
  const skills = SKILL_KEYS.map((key) => {
    const value = Math.max(0, Math.min(100, Math.round(skillByKey.get(key) ?? 0)));
    return {
      key,
      label: t(`analyticsDashboard.skills.items.${key}`),
      value,
      ...getSkillStatus(hasLearningData, value, t),
    };
  });
  const weakestSkill = skills.reduce((weakest, skill) => (skill.value < weakest.value ? skill : weakest), skills[0]);
  const strongestSkill = skills.reduce((strongest, skill) => (skill.value > strongest.value ? skill : strongest), skills[0]);
  const heatmap = buildHeatmapCalendar(summary?.heatmap, language, t);
  const levelProgress = Math.min(98, Math.max(8, Math.round((readiness + streak + completedCount * 8 + readyCount * 10) / 3)));
  const workspaceAction = { to: workspace?.id ? `/workspaces/${workspace.id}` : '/workspaces', label: t('analyticsDashboard.actions.openWorkspaces') };
  const heatmapCta = getHeatmapAction(workspace, t, { sourceCount, readyCount, readyDocumentId, fallbackDocumentId });
  const actions = getRoleActions(role, workspace, primaryDocumentId, t, { hasLearningData, completedCount, readyCount, deckReady, latestSource });
  const activities = buildActivities(summary?.activity || [], sources, language, t);
  const currentStreak = heatmap.currentStreak;

  return {
    role,
    roleLabel,
    displayName: user?.fullName || user?.email || t('analyticsDashboard.userFallback'),
    initials: getInitials(user?.fullName || user?.email),
    workspaceName,
    rankLabel: getRankLabel(readiness, streak, readyCount, deckReady, t),
    levelProgress,
    nextMilestone: getNextMilestone(completedCount, readyCount, deckReady, t),
    heatmapWeeks: heatmap.weeks,
    heatmapMonthLabels: heatmap.monthLabels,
    heatmapWeekCount: heatmap.heatmapWeekCount,
    calendarYear: heatmap.calendarYear,
    heatmapSummary: heatmap.summary,
    heatmapActiveCells: heatmap.activeCells,
    peakActivityLabel: heatmap.peakActivityLabel,
    heatmapCta,
    heatmapStats: [
      {
        key: 'activeDays',
        label: t('analyticsDashboard.heatmap.stats.activeDays'),
        value: heatmap.activeCells,
      },
      {
        key: 'currentStreak',
        label: t('analyticsDashboard.heatmap.stats.currentStreak'),
        value: currentStreak > 0
          ? t('analyticsDashboard.heatmap.streakDays', { count: currentStreak })
          : t('analyticsDashboard.metrics.notStarted'),
      },
      {
        key: 'peakLevel',
        label: t('analyticsDashboard.heatmap.stats.peakLevel'),
        value: heatmap.peakActivityLabel,
      },
    ],
    weekdayLabels: getWeekdayLabels(language),
    radarAxis: buildRadarAxis(skills),
    radarGrid: buildRadarGrid(skills.length),
    radarPoints: buildRadarPoints(skills, weakestSkill.key),
    radarPolygon: buildRadarPoints(skills, weakestSkill.key).map((point) => `${point.x},${point.y}`).join(' '),
    radarLabel: t('analyticsDashboard.skills.label'),
    weakestSkill,
    strongestSkill,
    copy: {
      kicker: t(`${rolePath}.kicker`),
      subtitle: t(`${rolePath}.subtitle`),
      focus: t(`${rolePath}.focus`),
      skillHint: t(`${rolePath}.skillHint`),
      insight: t(`${rolePath}.insight`, {
        sourceCount,
        readyCount,
        workspaceName,
      }),
    },
    primaryAction: getPrimaryAction(role, workspace, latestSource, t),
    workspaceAction,
    actions,
    metrics: [
      {
        key: 'streak',
        icon: <LuFlame aria-hidden="true" />,
        label: t('analyticsDashboard.metrics.streak'),
        value: hasLearningData ? t('analyticsDashboard.metrics.days', { count: streak }) : t('analyticsDashboard.metrics.notStarted'),
        hint: t(`${rolePath}.metricHints.streak`),
      },
      {
        key: 'hours',
        icon: <LuClock3 aria-hidden="true" />,
        label: t('analyticsDashboard.metrics.hours'),
        value: studySeconds > 0 ? t('analyticsDashboard.metrics.hourValue', { count: Math.max(1, Math.round(studySeconds / 3600)) }) : t('analyticsDashboard.metrics.noStudySession'),
        hint: t(`${rolePath}.metricHints.hours`),
      },
      {
        key: 'readiness',
        icon: <LuBrain aria-hidden="true" />,
        label: t('analyticsDashboard.metrics.readiness'),
        value: hasLearningData ? t('analyticsDashboard.metrics.percent', { count: readiness }) : t('analyticsDashboard.metrics.estimated'),
        hint: hasLearningData ? t(`${rolePath}.metricHints.accuracy`) : t('analyticsDashboard.metrics.needsLearningData'),
      },
      {
        key: 'sources',
        icon: role === 'ADMIN' ? <LuUsers aria-hidden="true" /> : <LuFileText aria-hidden="true" />,
        label: t(`${rolePath}.sourceMetricLabel`),
        value: sourceCount > 0 ? String(role === 'ADMIN' ? Math.max(sourceCount, readyCount + completedCount) : sourceCount) : t('analyticsDashboard.metrics.noDocuments'),
        hint: t(`${rolePath}.metricHints.sources`),
      },
    ],
    skills,
    checklist: buildInsightChecklist({
      sourceCount,
      completedCount,
      readyCount,
      deckReady,
      attemptCount,
      testCount,
      serverChecklist: summary?.checklist,
    }, t),
    activities,
    isActivityEmpty: activities.length === 0,
  };
}

function getHeatmapAction(workspace, t, state) {
  const workspacePath = workspace?.id ? `/workspaces/${workspace.id}` : '/workspaces';

  if (state.readyCount > 0 && state.readyDocumentId) {
    return { to: `/quiz/${state.readyDocumentId}`, label: t('analyticsDashboard.actions.studyQuiz') };
  }

  if (state.sourceCount > 0) {
    return {
      to: state.fallbackDocumentId ? `/question-studio/${state.fallbackDocumentId}` : workspacePath,
      label: t('analyticsDashboard.actions.createQuestionBank'),
    };
  }

  return { to: workspacePath, label: t('analyticsDashboard.actions.openWorkspaces') };
}

function getPrimaryAction(role, workspace, latestSource, t) {
  if (role === 'ADMIN') {
    return { to: '/admin', label: t('analyticsDashboard.actions.openAdmin') };
  }

  if (role === 'INSTRUCTOR') {
    return { to: workspace?.id ? `/workspaces/${workspace.id}` : '/workspaces', label: t('analyticsDashboard.actions.openStudio') };
  }

  const documentId = latestSource?.documentId ?? latestSource?.DocumentId ?? latestSource?.id;
  if (isStudyReadySource(latestSource) && documentId) {
    return { to: `/quiz/${documentId}`, label: t('analyticsDashboard.actions.continueLearning') };
  }

  return { to: '/workspaces', label: t('analyticsDashboard.actions.openWorkspaces') };
}

function getRoleActions(role, workspace, documentId, t, state = {}) {
  const studioPath = workspace?.id ? `/workspaces/${workspace.id}` : '/workspaces';
  const actions = [];

  if (!state.hasLearningData) {
    actions.push({ to: studioPath, label: t('analyticsDashboard.actions.openWorkspaces') });
    return actions;
  }

  if (state.completedCount > 0 && state.readyCount === 0 && documentId) {
    actions.push({ to: `/question-studio/${documentId}`, label: t('analyticsDashboard.actions.createQuestionBank') });
  } else if (state.readyCount > 0 && documentId) {
    actions.push({ to: `/quiz/${documentId}`, label: t('analyticsDashboard.actions.openQuiz') });
    actions.push({ to: `/flashcards/${documentId}`, label: t('analyticsDashboard.actions.openFlashcards'), secondary: true });
  } else {
    actions.push({ to: studioPath, label: t('analyticsDashboard.actions.openStudio') });
  }

  if (!state.deckReady) {
    actions.push({ to: studioPath, label: t('analyticsDashboard.actions.createSlides'), secondary: true });
  }

  if (role === 'ADMIN') {
    actions.unshift({ to: '/admin', label: t('analyticsDashboard.actions.openAdmin') });
  }

  return actions.slice(0, 3);
}

function buildActivities(activity, sources, language, t) {
  if (!Array.isArray(activity) || activity.length === 0) {
    return sources.slice(0, 4).map((source, index) => ({
      key: source.id || `${source.fileName}-${index}`,
      title: source.fileName || t('analyticsDashboard.activity.sourceFallback'),
      body: t(`analyticsDashboard.activity.status.${normalizeSourceStatus(source.status)}`),
      time: formatRelativeTime(source.updatedAt || source.createdAt, language, t),
      tone: isStudyReadySource(source) ? 'success' : isCompletedSource(source) ? 'info' : 'progress',
      icon: isStudyReadySource(source) ? <LuCheck aria-hidden="true" /> : <LuBookOpen aria-hidden="true" />,
    }));
  }

  return activity.slice(0, 8).map((item, index) => {
    const kind = String(item.kind || '').toLowerCase();
    const sourceStatus = kind === 'source' ? normalizeSourceStatus(item.status) : 'completed';
    return {
      key: item.key || `${kind || 'activity'}-${item.documentId || index}`,
      title: item.title || t('analyticsDashboard.activity.sourceFallback'),
      body: t(`analyticsDashboard.activity.status.${sourceStatus}`),
      time: formatRelativeTime(item.occurredAt, language, t),
      tone: getActivityTone(kind, item.status),
      icon: getActivityIcon(kind, item.status),
    };
  });
}

function getActivityTone(kind, status) {
  if (kind === 'study' || kind === 'test') {
    return String(status).toLowerCase() === 'incorrect' ? 'progress' : 'success';
  }

  if (kind === 'deck') {
    return String(status) === 'Completed' ? 'success' : 'info';
  }

  return 'info';
}

function getActivityIcon(kind, status) {
  if (kind === 'study' || kind === 'test') {
    return String(status).toLowerCase() === 'incorrect'
      ? <LuBookOpen aria-hidden="true" />
      : <LuCheck aria-hidden="true" />;
  }

  if (kind === 'deck') {
    return <LuLayoutDashboard aria-hidden="true" />;
  }

  return <LuBookOpen aria-hidden="true" />;
}

function buildInsightChecklist(state, t) {
  const fallbackItems = [
    {
      key: 'upload',
      title: t('analyticsDashboard.insight.checklist.upload'),
      state: state.sourceCount > 0 ? 'ready' : 'next',
    },
    {
      key: 'questions',
      title: t('analyticsDashboard.insight.checklist.questions'),
      state: state.readyCount > 0 ? 'ready' : state.completedCount > 0 ? 'next' : 'pending',
    },
    {
      key: 'study',
      title: t('analyticsDashboard.insight.checklist.study'),
      state: state.attemptCount > 0 || state.testCount > 0 ? 'ready' : state.readyCount > 0 ? 'next' : 'pending',
    },
    {
      key: 'slides',
      title: t('analyticsDashboard.insight.checklist.slides'),
      state: state.deckReady ? 'ready' : state.completedCount > 0 ? 'later' : 'pending',
    },
  ];
  const serverItems = Array.isArray(state.serverChecklist)
    ? state.serverChecklist
        .filter((item) => item?.key)
        .map((item) => ({
          key: item.key,
          title: t(`analyticsDashboard.insight.checklist.${item.key}`),
          state: normalizeChecklistState(item.state),
        }))
    : [];
  const items = serverItems.length > 0 ? serverItems : fallbackItems;

  return items.map((item) => ({
    ...item,
    statusLabel: t(`analyticsDashboard.insight.status.${item.state}`),
  }));
}

function normalizeChecklistState(state) {
  const normalized = String(state || '').trim().toLowerCase();
  return ['ready', 'next', 'pending', 'later'].includes(normalized) ? normalized : 'pending';
}

function getSkillStatus(hasLearningData, value, t) {
  if (!hasLearningData) {
    return {
      statusTone: 'muted',
      statusLabel: t('analyticsDashboard.skills.status.insufficient'),
    };
  }

  if (value >= 70) {
    return {
      statusTone: 'strong',
      statusLabel: t('analyticsDashboard.skills.status.strong'),
    };
  }

  return {
    statusTone: 'growth',
    statusLabel: t('analyticsDashboard.skills.status.improve'),
  };
}

export function buildHeatmapCalendar(heatmap, language, t, currentDate = new Date()) {
  const today = startOfDay(currentDate);
  const apiDays = Array.isArray(heatmap?.days) ? heatmap.days : [];
  const latestApiYear = getLatestHeatmapYear(apiDays);
  const hasCalendarYear = Number.isInteger(Number(heatmap?.calendarYear))
    && Number(heatmap.calendarYear) > 0;
  const requestedYear = Number(heatmap?.calendarYear || latestApiYear || today.getFullYear());
  const calendarYear = Number.isInteger(requestedYear) && requestedYear > 0
    ? requestedYear
    : today.getFullYear();
  const yearStart = new Date(calendarYear, 0, 1);
  const yearEnd = new Date(calendarYear, 11, 31);
  const gridStart = startOfCalendarWeek(yearStart);
  const gridEnd = endOfCalendarWeek(yearEnd);
  const heatmapWeekCount = Math.floor((gridEnd - gridStart) / 86_400_000 / HEATMAP_DAYS_PER_WEEK) + 1;
  const apiDaysByDate = new Map(apiDays.map((day) => [day?.date, day]));
  const weeks = [];
  const cells = [];
  const monthLabels = buildHeatmapMonthLabels(calendarYear, gridStart, language);

  for (let weekIndex = 0; weekIndex < heatmapWeekCount; weekIndex += 1) {
    const days = [];
    const weekStart = addDays(gridStart, weekIndex * HEATMAP_DAYS_PER_WEEK);

    for (let dayIndex = 0; dayIndex < HEATMAP_DAYS_PER_WEEK; dayIndex += 1) {
      const cellDate = addDays(weekStart, dayIndex);
      const dateKey = toDateKey(cellDate);
      const apiDay = apiDaysByDate.get(dateKey) || null;
      const isOutsideYear = cellDate.getFullYear() !== calendarYear;
      const isFuture = !isOutsideYear && cellDate > today;
      const level = Math.max(0, Math.min(4, Number(apiDay?.level || 0)));
      const isInteractive = !isOutsideYear && !isFuture;

      if (isInteractive) {
        cells.push({ dateKey, level });
      }
      days.push({
        key: dateKey,
        level,
        isOutsideYear,
        isFuture,
        isInteractive,
        title: t('analyticsDashboard.heatmap.cellTitle', {
          date: formatHeatmapDate(cellDate, language),
          levelText: t(`analyticsDashboard.heatmap.levelLabels.${level}`),
        }),
      });
    }

    weeks.push({
      key: toDateKey(weekStart),
      days,
    });
  }

  const activeCells = Number(hasCalendarYear && heatmap?.activeDays !== undefined
    ? heatmap.activeDays
    : cells.filter((cell) => cell.level > 0).length);
  const peakLevel = Number(hasCalendarYear && heatmap?.peakLevel !== undefined
    ? heatmap.peakLevel
    : cells.reduce((peak, cell) => Math.max(peak, cell.level), 0));
  const elapsedDayCount = getElapsedCalendarDayCount(calendarYear, today);

  return {
    weeks,
    monthLabels,
    calendarYear,
    heatmapWeekCount,
    elapsedDayCount,
    activeCells,
    currentStreak: Number(heatmap?.currentStreakDays ?? getCurrentHeatmapStreak(cells)),
    summary: t(activeCells === 0 ? 'analyticsDashboard.heatmap.emptySummary' : 'analyticsDashboard.heatmap.summary', {
      active: activeCells,
      total: elapsedDayCount,
      year: calendarYear,
    }),
    peakActivityLabel: t(`analyticsDashboard.heatmap.peakLevels.${peakLevel}`),
  };
}

function buildHeatmapMonthLabels(calendarYear, gridStart, language) {
  return Array.from({ length: 12 }, (_, index) => {
    const date = new Date(calendarYear, index, 1);
    const weekIndex = Math.floor((date - gridStart) / 86_400_000 / HEATMAP_DAYS_PER_WEEK);

    return {
      key: `${calendarYear}-${index + 1}`,
      label: getCompactMonthLabel(date, language),
      weekIndex,
    };
  });
}

function getLatestHeatmapYear(days) {
  for (let index = days.length - 1; index >= 0; index -= 1) {
    const match = /^(\d{4})-\d{2}-\d{2}$/.exec(String(days[index]?.date || ''));
    if (match) {
      return Number(match[1]);
    }
  }

  return null;
}

function startOfCalendarWeek(value) {
  const date = startOfDay(value);
  const mondayOffset = (date.getDay() + 6) % 7;
  return addDays(date, -mondayOffset);
}

function endOfCalendarWeek(value) {
  const date = startOfDay(value);
  const sundayOffset = 6 - ((date.getDay() + 6) % 7);
  return addDays(date, sundayOffset);
}

function addDays(value, count) {
  const date = new Date(value);
  date.setDate(date.getDate() + count);
  return date;
}

function getElapsedCalendarDayCount(calendarYear, today) {
  const yearStart = new Date(calendarYear, 0, 1);
  const yearEnd = new Date(calendarYear, 11, 31);
  const effectiveEnd = today < yearStart ? yearStart : today > yearEnd ? yearEnd : today;
  return Math.floor((startOfDay(effectiveEnd) - yearStart) / 86_400_000) + 1;
}

function getCurrentHeatmapStreak(cells) {
  let streak = 0;

  for (let index = cells.length - 1; index >= 0; index -= 1) {
    if (cells[index].level <= 0) {
      break;
    }

    streak += 1;
  }

  return streak;
}

function buildRadarGrid(axisCount) {
  return [0.25, 0.5, 0.75, 1].map((scale) => ({
    key: `ring-${scale}`,
    points: Array.from({ length: axisCount }, (_, index) => {
      const point = getRadarCoordinate(index, axisCount, 86 * scale);
      return `${point.x},${point.y}`;
    }).join(' '),
  }));
}

function buildRadarAxis(skills) {
  return skills.map((skill, index) => ({
    key: skill.key,
    ...getRadarCoordinate(index, skills.length, 86),
  }));
}

function buildRadarPoints(skills, weakestKey) {
  return skills.map((skill, index) => {
    const point = getRadarCoordinate(index, skills.length, 86 * (skill.value / 100));
    return {
      key: skill.key,
      label: skill.label,
      value: skill.value,
      x: point.x,
      y: point.y,
      isWeakest: skill.key === weakestKey,
    };
  });
}

function getRadarCoordinate(index, axisCount, radius) {
  const angle = ((Math.PI * 2) / axisCount) * index - Math.PI / 2;
  return {
    x: Number((130 + Math.cos(angle) * radius).toFixed(2)),
    y: Number((120 + Math.sin(angle) * radius).toFixed(2)),
  };
}

function getCompactMonthLabel(date, language) {
  if (language === 'vi') {
    return `T${date.getMonth() + 1}`;
  }

  return date.toLocaleDateString('en-US', { month: 'short' });
}

function getRankLabel(readiness, streak, readyCount, deckReady, t) {
  const score = readiness + streak + readyCount * 4 + (deckReady ? 10 : 0);
  if (score >= 125) {
    return t('analyticsDashboard.rank.legend');
  }
  if (score >= 95) {
    return t('analyticsDashboard.rank.elite');
  }
  if (score >= 70) {
    return t('analyticsDashboard.rank.rising');
  }
  return t('analyticsDashboard.rank.starter');
}

function getNextMilestone(completedCount, readyCount, deckReady, t) {
  if (readyCount === 0) {
    return t('analyticsDashboard.milestones.firstQuestionBank');
  }
  if (!deckReady) {
    return t('analyticsDashboard.milestones.firstDeck');
  }
  return t('analyticsDashboard.milestones.keepStreak', { count: Math.max(3, completedCount + readyCount) });
}

function getWeekdayLabels(language) {
  return language === 'vi'
    ? ['T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'CN']
    : ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
}

function normalizeRole(role) {
  const normalized = typeof role === 'string' ? role.trim().toUpperCase() : '';
  return ['LEARNER', 'INSTRUCTOR', 'ADMIN'].includes(normalized) ? normalized : 'LEARNER';
}

function getInitials(value) {
  if (!value) {
    return 'AI';
  }

  const parts = String(value).trim().split(/\s+/).filter(Boolean);
  if (parts.length === 1) {
    return parts[0].slice(0, 2).toUpperCase();
  }

  return `${parts[0][0]}${parts[parts.length - 1][0]}`.toUpperCase();
}

function sortSourcesByRecency(sources) {
  return [...sources].sort((left, right) => {
    const rightTime = new Date(right.updatedAt || right.createdAt || 0).getTime();
    const leftTime = new Date(left.updatedAt || left.createdAt || 0).getTime();
    return rightTime - leftTime;
  });
}

function isCompletedSource(source) {
  return source?.status === 3 || String(source?.status) === 'Completed';
}

function isStudyReadySource(source) {
  return isCompletedSource(source) && Number(source?.questionsCount || source?.QuestionsCount || 0) > 0;
}

function normalizeSourceStatus(status) {
  if (status === 0 || String(status) === 'Uploaded') {
    return 'uploaded';
  }
  if (status === 1 || String(status) === 'Extracting') {
    return 'extracting';
  }
  if (status === 2 || String(status) === 'Analyzing') {
    return 'analyzing';
  }
  if (status === 3 || String(status) === 'Completed') {
    return 'completed';
  }
  if (status === 4 || String(status) === 'Failed') {
    return 'failed';
  }
  return 'unknown';
}

function formatRelativeTime(value, language, t) {
  if (!value) {
    return t('analyticsDashboard.activity.now');
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

  return new Date(value).toLocaleDateString(language === 'vi' ? 'vi-VN' : 'en-US');
}

function startOfDay(value) {
  const date = new Date(value);
  date.setHours(0, 0, 0, 0);
  return date;
}

function toDateKey(value) {
  const date = startOfDay(value);
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

function formatHeatmapDate(value, language) {
  return language === 'vi'
    ? value.toLocaleDateString('vi-VN')
    : toDateKey(value);
}

export default PersonalAnalyticsDashboard;
