import React, { act } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter, useLocation } from 'react-router-dom';
import PersonalAnalyticsDashboard, {
  AnalyticsErrorState,
  buildHeatmapCalendar,
} from './PersonalAnalyticsDashboard';
import { analyticsService } from '../services/api';

global.IS_REACT_ACT_ENVIRONMENT = true;

jest.mock('../context/AuthContext', () => ({
  useAuth: () => ({
    currentUser: {
      id: 7,
      fullName: 'Test Learner',
      email: 'learner@example.com',
      role: 'LEARNER',
    },
  }),
}));

jest.mock('../context/LanguageContext', () => {
  const translations = require('../i18n').default;
  const translate = (key, vars = {}) => {
    const value = key.split('.').reduce((current, segment) => current?.[segment], translations.en);
    if (typeof value !== 'string') {
      return value ?? key;
    }

    return value.replace(/\{\{(.*?)\}\}/g, (_, rawKey) => String(vars[rawKey.trim()] ?? ''));
  };

  return {
    useLanguage: () => ({ language: 'en', t: translate }),
  };
});

jest.mock('../services/api', () => ({
  analyticsService: {
    getPersonalSummary: jest.fn(),
  },
  getApiErrorMessage: jest.fn(() => 'Network unavailable'),
}));

describe('analytics calendar heatmap', () => {
  it('builds the complete 2026 calendar with Monday-first padding and future cells', () => {
    const calendar = buildHeatmapCalendar(
      {
        calendarYear: 2026,
        days: [
          { date: '2026-01-01', level: 2 },
          { date: '2026-06-07', level: 4 },
        ],
      },
      'en',
      translateHeatmap,
      new Date(2026, 5, 7),
    );

    expect(calendar.calendarYear).toBe(2026);
    expect(calendar.heatmapWeekCount).toBe(53);
    expect(calendar.monthLabels).toHaveLength(12);
    expect(calendar.monthLabels.map((month) => month.label)).toEqual([
      'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun',
      'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec',
    ]);
    expect(calendar.elapsedDayCount).toBe(158);

    const cells = calendar.weeks.flatMap((week) => week.days);
    expect(cells[0]).toMatchObject({ key: '2025-12-29', isOutsideYear: true });
    expect(cells.find((cell) => cell.key === '2026-01-01')).toMatchObject({
      level: 2,
      isInteractive: true,
    });
    expect(cells.find((cell) => cell.key === '2026-06-08')).toMatchObject({
      isFuture: true,
      isInteractive: false,
    });
    expect(cells.at(-1)).toMatchObject({ key: '2027-01-03', isOutsideYear: true });
  });

  it('includes leap day in a leap-year calendar', () => {
    const calendar = buildHeatmapCalendar(
      {
        calendarYear: 2024,
        days: [{ date: '2024-02-29', level: 3 }],
      },
      'vi',
      translateHeatmap,
      new Date(2024, 2, 1),
    );

    const leapDay = calendar.weeks
      .flatMap((week) => week.days)
      .find((cell) => cell.key === '2024-02-29');

    expect(calendar.elapsedDayCount).toBe(61);
    expect(leapDay).toMatchObject({ level: 3, isInteractive: true });
  });

  it('falls back to the latest API date when calendarYear is absent', () => {
    const calendar = buildHeatmapCalendar(
      {
        activeDays: 99,
        days: [
          { date: '2025-12-31', level: 4 },
          { date: '2026-01-01', level: 2 },
        ],
      },
      'en',
      translateHeatmap,
      new Date(2026, 0, 2),
    );

    expect(calendar.calendarYear).toBe(2026);
    expect(calendar.activeCells).toBe(1);
  });
});

describe('PersonalAnalyticsDashboard', () => {
  let container;
  let root;

  beforeEach(() => {
    window.localStorage.setItem('elearn-language', 'en');
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    analyticsService.getPersonalSummary.mockReset();
  });

  afterEach(() => {
    act(() => {
      root.unmount();
    });
    container.remove();
  });

  it('renders the loading state while analytics are pending', async () => {
    analyticsService.getPersonalSummary.mockReturnValue(new Promise(() => {}));

    await renderDashboard();

    expect(container.textContent).toContain('Loading personal analytics...');
  });

  it('renders an error and invokes retry', async () => {
    const onRetry = jest.fn();

    await act(async () => {
      root.render(
        <AnalyticsErrorState
          error="Network unavailable"
          onRetry={onRetry}
          t={(key) => ({
            'analyticsDashboard.errorTitle': 'Could not load analytics',
            'analyticsDashboard.retry': 'Retry',
          })[key]}
        />
      );
    });

    expect(container.textContent).toContain('Could not load analytics');
    expect(container.textContent).toContain('Network unavailable');

    const retryButton = findButton('Retry');
    act(() => {
      retryButton.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it('renders the shared empty activity state', async () => {
    analyticsService.getPersonalSummary.mockResolvedValue(emptySummary());

    await renderDashboard();

    expect(container.querySelector('.v2-empty-state')).not.toBeNull();
    expect(container.textContent).toContain('No learning activity yet');
    expect(container.textContent).toContain('Open Workspace');
  });

  it('keeps workspace CTA navigation behavior', async () => {
    analyticsService.getPersonalSummary.mockResolvedValue(emptySummary());

    await renderDashboard();

    const workspaceButton = findButton('Open Workspace');
    await act(async () => {
      workspaceButton.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    expect(container.querySelector('[data-testid="location"]').textContent).toBe('/workspaces/12');
  });

  async function renderDashboard() {
    await act(async () => {
      root.render(
        <MemoryRouter initialEntries={['/analytics']}>
          <PersonalAnalyticsDashboard />
          <LocationProbe />
        </MemoryRouter>
      );
      await Promise.resolve();
    });
  }

  function findButton(label) {
    return Array.from(container.querySelectorAll('button'))
      .find((button) => button.textContent.trim() === label);
  }

});

function LocationProbe() {
  const location = useLocation();
  return <span data-testid="location">{location.pathname}</span>;
}

function emptySummary() {
  return {
    workspace: { id: 12, name: 'Test Workspace' },
    sources: [],
    metrics: {},
    heatmap: [],
    skills: [],
    activity: [],
  };
}

function translateHeatmap(key, vars = {}) {
  if (key.startsWith('analyticsDashboard.heatmap.levelLabels.')) {
    return key.split('.').at(-1);
  }

  if (key === 'analyticsDashboard.heatmap.summary') {
    return `${vars.active}/${vars.total}`;
  }

  if (key === 'analyticsDashboard.heatmap.emptySummary') {
    return `0/${vars.total}`;
  }

  if (key === 'analyticsDashboard.heatmap.cellTitle') {
    return `${vars.date}: ${vars.levelText}`;
  }

  return key;
}
