import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  LuAward,
  LuBookOpen,
  LuBrain,
  LuCalendarDays,
  LuChartLine,
  LuCheck,
  LuClock3,
  LuFileText,
  LuFlame,
  LuLayoutDashboard,
  LuRadar,
  LuSparkles,
  LuUsers,
} from 'react-icons/lu';
import { useAuth } from '../context/AuthContext';
import { useLanguage } from '../context/LanguageContext';
import { getApiErrorMessage, workspaceService } from '../services/api';

const HEATMAP_WEEK_COUNT = 26;
const HEATMAP_DAYS_PER_WEEK = 7;
const SKILL_KEYS = ['recall', 'concepts', 'questionBank', 'slides', 'consistency'];

function PersonalAnalyticsDashboard() {
  const { currentUser } = useAuth();
  const { language, t } = useLanguage();
  const navigate = useNavigate();
  const { defaultWorkspace, sources, loading, error, reload } = useAnalyticsWorkspaceData();

  const vm = useMemo(
    () => buildAnalyticsViewModel(currentUser, defaultWorkspace, sources, language, t),
    [currentUser, defaultWorkspace, sources, language, t],
  );

  const openAction = useCallback((action) => {
    if (!action?.to) {
      return;
    }

    navigate(action.to);
  }, [navigate]);

  return (
    <div className="analytics-dashboard">
      <section className="analytics-hero-card">
        <div className="analytics-profile-block">
          <div className="analytics-avatar-shell">
            <div className="analytics-avatar" aria-hidden="true">{vm.initials}</div>
            <span>{vm.rankLabel}</span>
          </div>
          <div className="analytics-hero-copy">
            <span className="analytics-kicker">{vm.copy.kicker}</span>
            <h2>{t('analyticsDashboard.hero.title', { name: vm.displayName })}</h2>
            <p>{vm.copy.subtitle}</p>
            <div className="analytics-profile-meta">
              <span>{vm.roleLabel}</span>
              <span>{vm.workspaceName}</span>
              <span>{t('analyticsDashboard.hero.peakLabel', { value: vm.peakActivityLabel })}</span>
            </div>
          </div>
        </div>

        <div className="analytics-hero-action">
          <div className="analytics-rank-card">
            <LuAward aria-hidden="true" />
            <div>
              <span>{t('analyticsDashboard.hero.focusLabel')}</span>
              <strong>{vm.copy.focus}</strong>
            </div>
          </div>
          <div className="analytics-level-card">
            <div>
              <span>{t('analyticsDashboard.hero.levelLabel')}</span>
              <strong>{vm.nextMilestone}</strong>
            </div>
            <div className="analytics-level-track" aria-hidden="true">
              <span style={{ width: `${vm.levelProgress}%` }} />
            </div>
            <small>{t('analyticsDashboard.hero.levelProgress', { count: vm.levelProgress })}</small>
          </div>
          <button type="button" className="button" onClick={() => openAction(vm.primaryAction)}>
            {vm.primaryAction.label}
          </button>
        </div>
      </section>

      {loading && (
        <section className="analytics-state-card">
          <div className="spinner"></div>
          <p>{t('analyticsDashboard.loading')}</p>
        </section>
      )}

      {error && (
        <section className="alert alert-error workspace-home-alert">
          <div>
            <strong>{t('analyticsDashboard.errorTitle')}</strong>
            <p>{error}</p>
          </div>
          <button type="button" className="button button-secondary" onClick={reload}>
            {t('analyticsDashboard.retry')}
          </button>
        </section>
      )}

      <section className="analytics-metric-grid" aria-label={t('analyticsDashboard.metrics.label')}>
        {vm.metrics.map((metric) => (
          <article key={metric.key} className={`analytics-metric-card tone-${metric.key}`}>
            <div className="analytics-card-icon">{metric.icon}</div>
            <span>{metric.label}</span>
            <strong>{metric.value}</strong>
            <p>{metric.hint}</p>
          </article>
        ))}
      </section>

      <section className="analytics-panel analytics-heatmap-panel">
        <div className="analytics-section-head">
          <div>
            <span>{t('analyticsDashboard.heatmap.kicker')}</span>
            <h3><LuCalendarDays aria-hidden="true" /> {t('analyticsDashboard.heatmap.title')}</h3>
          </div>
          <p>{vm.heatmapSummary}</p>
        </div>

        <div className="analytics-heatmap-shell" aria-label={t('analyticsDashboard.heatmap.label')}>
          <div className="analytics-heatmap-months">
            <span aria-hidden="true" />
            {vm.heatmapWeeks.map((week) => (
              <span key={week.key}>{week.monthLabel}</span>
            ))}
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
                      className={`analytics-heatmap-cell level-${day.level}`}
                      title={day.title}
                      aria-label={day.title}
                    />
                  ))}
                </div>
              ))}
            </div>
          </div>
        </div>

        <div className="analytics-heatmap-footer">
          <span>{t('analyticsDashboard.heatmap.weeksWindow', { count: HEATMAP_WEEK_COUNT })}</span>
          <div className="analytics-legend">
            <span>{t('analyticsDashboard.heatmap.less')}</span>
            {[0, 1, 2, 3, 4].map((level) => (
              <span key={level} className={`analytics-heatmap-cell level-${level}`} />
            ))}
            <span>{t('analyticsDashboard.heatmap.more')}</span>
          </div>
        </div>
      </section>

      <div className="analytics-main-grid">
        <section className="analytics-panel analytics-radar-panel">
          <div className="analytics-section-head">
            <div>
              <span>{t('analyticsDashboard.skills.kicker')}</span>
              <h3><LuRadar aria-hidden="true" /> {t('analyticsDashboard.skills.title')}</h3>
            </div>
            <p>{vm.copy.skillHint}</p>
          </div>

          <div className="analytics-radar-layout">
            <RadarChart vm={vm} />
            <div className="analytics-skill-summary">
              <article>
                <span>{t('analyticsDashboard.skills.strongest')}</span>
                <strong>{vm.strongestSkill.label}</strong>
                <small>{vm.strongestSkill.value}%</small>
              </article>
              <article>
                <span>{t('analyticsDashboard.skills.growthArea')}</span>
                <strong>{vm.weakestSkill.label}</strong>
                <small>{vm.weakestSkill.value}%</small>
              </article>
            </div>
          </div>
        </section>

        <section className="analytics-panel analytics-insight-panel">
          <div className="analytics-section-head">
            <div>
              <span>{t('analyticsDashboard.insight.kicker')}</span>
              <h3><LuSparkles aria-hidden="true" /> {t('analyticsDashboard.insight.title')}</h3>
            </div>
          </div>
          <p>{vm.copy.insight}</p>
          <p className="analytics-coach-note">
            {t('analyticsDashboard.insight.skillNudge', {
              weak: vm.weakestSkill.label,
              strong: vm.strongestSkill.label,
            })}
          </p>
          <div className="analytics-action-row">
            {vm.actions.map((action) => (
              <button
                key={action.to}
                type="button"
                className={`button${action.secondary ? ' button-secondary' : ''}`}
                onClick={() => openAction(action)}
              >
                {action.label}
              </button>
            ))}
          </div>
        </section>
      </div>

      <section className="analytics-panel analytics-activity-panel">
        <div className="analytics-section-head">
          <div>
            <span>{t('analyticsDashboard.activity.kicker')}</span>
            <h3><LuChartLine aria-hidden="true" /> {t('analyticsDashboard.activity.title')}</h3>
          </div>
        </div>

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
      </section>
    </div>
  );
}

function RadarChart({ vm }) {
  return (
    <div className="analytics-radar-chart" aria-label={vm.radarLabel}>
      <svg viewBox="0 0 320 300" role="img">
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
          <line key={axis.key} x1="160" y1="148" x2={axis.x} y2={axis.y} className="analytics-radar-axis" />
        ))}
        <polygon points={vm.radarPolygon} className="analytics-radar-area" />
        {vm.radarPoints.map((point) => (
          <g key={point.key}>
            <circle cx={point.x} cy={point.y} r="4.5" className={point.isWeakest ? 'is-weakest' : ''} />
            <text x={point.labelX} y={point.labelY} textAnchor={point.anchor}>
              {point.label}
            </text>
            <text x={point.valueX} y={point.valueY} textAnchor={point.anchor} className="analytics-radar-value">
              {point.value}%
            </text>
          </g>
        ))}
      </svg>
    </div>
  );
}

function useAnalyticsWorkspaceData() {
  const { currentUser } = useAuth();
  const { t } = useLanguage();
  const [defaultWorkspace, setDefaultWorkspace] = useState(null);
  const [sources, setSources] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const loadData = useCallback(async () => {
    if (!currentUser?.id) {
      setDefaultWorkspace(null);
      setSources([]);
      setLoading(false);
      return;
    }

    setLoading(true);
    setError('');

    try {
      const workspace = await workspaceService.getDefault(String(currentUser.id));
      const workspaceSources = workspace?.id ? await workspaceService.listSources(workspace.id) : [];
      setDefaultWorkspace(workspace || null);
      setSources(sortSourcesByRecency(Array.isArray(workspaceSources) ? workspaceSources : []));
    } catch (err) {
      setDefaultWorkspace(null);
      setSources([]);
      setError(getApiErrorMessage(err, t('analyticsDashboard.errorBody')));
    } finally {
      setLoading(false);
    }
  }, [currentUser?.id, t]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  return { defaultWorkspace, sources, loading, error, reload: loadData };
}

function buildAnalyticsViewModel(user, workspace, sources, language, t) {
  const role = normalizeRole(user?.role);
  const completedSources = sources.filter(isCompletedSource);
  const studyReadySources = sources.filter(isStudyReadySource);
  const latestSource = sources[0] || null;
  const rolePath = `analyticsDashboard.roles.${role}`;
  const roleLabel = t(`app.roles.${role}`);
  const workspaceName = workspace?.name || t('analyticsDashboard.workspaceFallback');
  const sourceCount = sources.length;
  const completedCount = completedSources.length;
  const readyCount = studyReadySources.length;
  const deckReady = Boolean(workspace?.latestDeck);
  const questionTotal = sources.reduce((sum, source) => sum + Number(source?.questionsCount || source?.QuestionsCount || 0), 0);
  const derivedHours = Math.max(0, completedCount * 2 + readyCount + Math.ceil(questionTotal / 18) + (deckReady ? 2 : 0));
  const readiness = readyCount > 0
    ? Math.min(98, 64 + readyCount * 7 + completedCount * 3 + (deckReady ? 6 : 0))
    : Math.min(74, 38 + completedCount * 8 + (deckReady ? 5 : 0));
  const streak = Math.min(45, readyCount * 2 + completedCount + (latestSource ? 1 : 0) + (deckReady ? 2 : 0));
  const primaryDocumentId = latestSource?.documentId ?? latestSource?.DocumentId ?? latestSource?.id;
  const skills = SKILL_KEYS.map((key, index) => ({
    key,
    label: t(`analyticsDashboard.skills.items.${key}`),
    value: getSkillScore(key, index, role, completedCount, readyCount, deckReady, questionTotal),
  }));
  const weakestSkill = skills.reduce((weakest, skill) => (skill.value < weakest.value ? skill : weakest), skills[0]);
  const strongestSkill = skills.reduce((strongest, skill) => (skill.value > strongest.value ? skill : strongest), skills[0]);
  const heatmap = buildHeatmapWeeks(sources, language, t, { completedCount, readyCount, deckReady, questionTotal });
  const levelProgress = Math.min(98, Math.max(8, Math.round((readiness + streak + completedCount * 8 + readyCount * 10) / 3)));

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
    heatmapSummary: heatmap.summary,
    peakActivityLabel: heatmap.peakActivityLabel,
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
    actions: getRoleActions(role, workspace, primaryDocumentId, t),
    metrics: [
      {
        key: 'streak',
        icon: <LuFlame aria-hidden="true" />,
        label: t('analyticsDashboard.metrics.streak'),
        value: t('analyticsDashboard.metrics.days', { count: streak }),
        hint: t(`${rolePath}.metricHints.streak`),
      },
      {
        key: 'hours',
        icon: <LuClock3 aria-hidden="true" />,
        label: t('analyticsDashboard.metrics.hours'),
        value: t('analyticsDashboard.metrics.hourValue', { count: derivedHours }),
        hint: t(`${rolePath}.metricHints.hours`),
      },
      {
        key: 'readiness',
        icon: <LuBrain aria-hidden="true" />,
        label: t('analyticsDashboard.metrics.readiness'),
        value: t('analyticsDashboard.metrics.percent', { count: readiness }),
        hint: t(`${rolePath}.metricHints.accuracy`),
      },
      {
        key: 'sources',
        icon: role === 'ADMIN' ? <LuUsers aria-hidden="true" /> : <LuFileText aria-hidden="true" />,
        label: t(`${rolePath}.sourceMetricLabel`),
        value: String(role === 'ADMIN' ? Math.max(sourceCount, readyCount + completedCount) : sourceCount),
        hint: t(`${rolePath}.metricHints.sources`),
      },
    ],
    skills,
    activities: buildActivities(sources, workspace, language, t),
  };
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

function getRoleActions(role, workspace, documentId, t) {
  const studioPath = workspace?.id ? `/workspaces/${workspace.id}` : '/workspaces';
  const actions = [
    { to: studioPath, label: t('analyticsDashboard.actions.openStudio') },
  ];

  if (documentId) {
    actions.push({ to: `/quiz/${documentId}`, label: t('analyticsDashboard.actions.openQuiz'), secondary: true });
  }

  if (role === 'ADMIN') {
    actions.unshift({ to: '/admin', label: t('analyticsDashboard.actions.openAdmin') });
  }

  return actions.slice(0, 3);
}

function buildActivities(sources, workspace, language, t) {
  const latestActivities = sources.slice(0, 4).map((source, index) => ({
    key: source.id || `${source.fileName}-${index}`,
    title: source.fileName || t('analyticsDashboard.activity.sourceFallback'),
    body: t(`analyticsDashboard.activity.status.${normalizeSourceStatus(source.status)}`),
    time: formatRelativeTime(source.updatedAt || source.createdAt, language, t),
    tone: isStudyReadySource(source) ? 'success' : isCompletedSource(source) ? 'info' : 'progress',
    icon: isStudyReadySource(source) ? <LuCheck aria-hidden="true" /> : <LuBookOpen aria-hidden="true" />,
  }));

  if (latestActivities.length > 0) {
    return latestActivities;
  }

  return [
    {
      key: 'empty-workspace',
      title: workspace?.name || t('analyticsDashboard.workspaceFallback'),
      body: t('analyticsDashboard.activity.empty'),
      time: t('analyticsDashboard.activity.now'),
      tone: 'info',
      icon: <LuLayoutDashboard aria-hidden="true" />,
    },
  ];
}

function getSkillScore(key, index, role, completedCount, readyCount, deckReady, questionTotal) {
  const roleBoost = role === 'INSTRUCTOR' && (key === 'slides' || key === 'questionBank')
    ? 10
    : role === 'ADMIN' && key === 'consistency'
      ? 12
      : 0;
  const questionBoost = key === 'questionBank' ? Math.min(14, Math.floor(questionTotal / 12)) : Math.min(8, Math.floor(questionTotal / 24));
  const deckBoost = deckReady && key === 'slides' ? 14 : deckReady ? 6 : 0;
  const base = 34 + completedCount * 7 + readyCount * 8 + questionBoost + deckBoost + index * 4 + roleBoost;
  return Math.max(22, Math.min(97, base));
}

function buildHeatmapWeeks(sources, language, t, signals) {
  const today = startOfDay(new Date());
  const start = new Date(today);
  start.setDate(today.getDate() - ((HEATMAP_WEEK_COUNT * HEATMAP_DAYS_PER_WEEK) - 1));
  const signalByDate = sources.reduce((map, source) => {
    const dateValue = source.updatedAt || source.createdAt;
    if (!dateValue) {
      return map;
    }

    const key = toDateKey(new Date(dateValue));
    const questions = Number(source?.questionsCount || source?.QuestionsCount || 0);
    const sourceSignal = 1 + (isCompletedSource(source) ? 1 : 0) + (questions > 0 ? 2 : 0);
    map.set(key, (map.get(key) || 0) + sourceSignal);
    return map;
  }, new Map());
  const seed = Math.max(1, sources.length + signals.completedCount * 2 + signals.readyCount * 4 + (signals.deckReady ? 3 : 0));
  const weeks = [];
  let activeCells = 0;
  let peakLevel = 0;

  for (let weekIndex = 0; weekIndex < HEATMAP_WEEK_COUNT; weekIndex += 1) {
    const days = [];
    const weekStart = new Date(start);
    weekStart.setDate(start.getDate() + weekIndex * HEATMAP_DAYS_PER_WEEK);
    const previousMonth = weekIndex > 0 ? weeks[weekIndex - 1]?.monthNumber : null;
    const monthNumber = weekStart.getMonth();
    const monthLabel = weekIndex === 0 || monthNumber !== previousMonth
      ? weekStart.toLocaleDateString(language === 'vi' ? 'vi-VN' : 'en-US', { month: 'short' })
      : '';

    for (let dayIndex = 0; dayIndex < HEATMAP_DAYS_PER_WEEK; dayIndex += 1) {
      const date = new Date(weekStart);
      date.setDate(weekStart.getDate() + dayIndex);
      const dateKey = toDateKey(date);
      const explicitSignal = signalByDate.get(dateKey) || 0;
      const ambientSignal = sources.length > 0 ? (weekIndex * 3 + dayIndex * seed + signals.readyCount) % 5 : 0;
      const level = Math.max(0, Math.min(4, explicitSignal || (ambientSignal > 2 ? ambientSignal - 1 : 0)));

      if (level > 0) {
        activeCells += 1;
      }
      peakLevel = Math.max(peakLevel, level);
      days.push({
        key: dateKey,
        level,
        title: t('analyticsDashboard.heatmap.cellTitle', {
          date: date.toLocaleDateString(language === 'vi' ? 'vi-VN' : 'en-US'),
          level,
        }),
      });
    }

    weeks.push({
      key: toDateKey(weekStart),
      monthLabel,
      monthNumber,
      days,
    });
  }

  return {
    weeks,
    summary: t('analyticsDashboard.heatmap.summary', {
      active: activeCells,
      total: HEATMAP_WEEK_COUNT * HEATMAP_DAYS_PER_WEEK,
    }),
    peakActivityLabel: t(`analyticsDashboard.heatmap.peakLevels.${peakLevel}`),
  };
}

function buildRadarGrid(axisCount) {
  return [0.25, 0.5, 0.75, 1].map((scale) => ({
    key: `ring-${scale}`,
    points: Array.from({ length: axisCount }, (_, index) => {
      const point = getRadarCoordinate(index, axisCount, 112 * scale);
      return `${point.x},${point.y}`;
    }).join(' '),
  }));
}

function buildRadarAxis(skills) {
  return skills.map((skill, index) => ({
    key: skill.key,
    ...getRadarCoordinate(index, skills.length, 112),
  }));
}

function buildRadarPoints(skills, weakestKey) {
  return skills.map((skill, index) => {
    const point = getRadarCoordinate(index, skills.length, 112 * (skill.value / 100));
    const labelPoint = getRadarCoordinate(index, skills.length, 136);
    const valuePoint = getRadarCoordinate(index, skills.length, 152);
    return {
      key: skill.key,
      label: skill.label,
      value: skill.value,
      x: point.x,
      y: point.y,
      labelX: labelPoint.x,
      labelY: labelPoint.y,
      valueX: valuePoint.x,
      valueY: valuePoint.y,
      anchor: labelPoint.x < 132 ? 'end' : labelPoint.x > 188 ? 'start' : 'middle',
      isWeakest: skill.key === weakestKey,
    };
  });
}

function getRadarCoordinate(index, axisCount, radius) {
  const angle = ((Math.PI * 2) / axisCount) * index - Math.PI / 2;
  return {
    x: Number((160 + Math.cos(angle) * radius).toFixed(2)),
    y: Number((148 + Math.sin(angle) * radius).toFixed(2)),
  };
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
  return language === 'vi' ? ['T2', '', 'T4', '', 'T6', '', 'CN'] : ['Mon', '', 'Wed', '', 'Fri', '', 'Sun'];
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

export default PersonalAnalyticsDashboard;
