import React, { act } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import DashboardPage from './DashboardPage';
import { LanguageProvider } from '../../context/LanguageContext';
import { ToastProvider } from '../../components/common/ToastProvider';
import { dashboardService } from '../../services/api';

jest.mock('../../services/api', () => ({
  dashboardService: {
    getHome: jest.fn(),
  },
  documentService: {
    uploadDocument: jest.fn(),
  },
  getApiErrorMessage: jest.fn((error, fallback) => error?.message || fallback),
}));

jest.mock('../../services/analytics', () => ({
  trackEvent: jest.fn(),
}));

const baseWorkspace = {
  id: 7,
  name: 'My Workspace',
  latestDeck: null,
};

describe('DashboardPage v2', () => {
  let container;
  let root;

  beforeEach(() => {
    window.localStorage.setItem('elearn-language', 'en');
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    dashboardService.getHome.mockReset();
  });

  afterEach(() => {
    act(() => {
      root.unmount();
    });
    container.remove();
  });

  it('renders the empty upload state', async () => {
    dashboardService.getHome.mockResolvedValue({
      workspace: baseWorkspace,
      sources: [],
      stats: { sourceCount: 0 },
    });

    await renderDashboard();

    expect(container.textContent).toContain('AI Studio Console');
    expect(container.textContent).toContain('No sources in this workspace yet');
  });

  it('renders a processing source', async () => {
    dashboardService.getHome.mockResolvedValue({
      workspace: baseWorkspace,
      sources: [
        {
          id: 10,
          fileName: 'processing.pdf',
          status: 'Analyzing',
          updatedAt: new Date().toISOString(),
          questionsCount: 0,
          processingProgress: { percent: 72 },
        },
      ],
      stats: { sourceCount: 1, processingSourceCount: 1 },
    });

    await renderDashboard();

    expect(container.textContent).toContain('processing.pdf');
    expect(container.textContent).toContain('Analyzing');
  });

  it('renders a completed study-ready source', async () => {
    dashboardService.getHome.mockResolvedValue({
      workspace: baseWorkspace,
      sources: [
        {
          id: 11,
          fileName: 'ready.pdf',
          status: 'Completed',
          updatedAt: new Date().toISOString(),
          questionsCount: 3,
          isStructureReady: true,
          processingProgress: { percent: 100 },
        },
      ],
      stats: { sourceCount: 1, completedSourceCount: 1, studyReadySourceCount: 1 },
    });

    await renderDashboard();

    expect(container.textContent).toContain('ready.pdf');
    expect(container.textContent).toContain('Completed');
    expect(container.textContent).toContain('3 questions');
  });

  it('surfaces a stale workspace deck as an inline warning', async () => {
    dashboardService.getHome.mockResolvedValue({
      workspace: {
        ...baseWorkspace,
        latestDeck: { id: 21, status: 'Completed', isStale: true },
      },
      sources: [
        {
          id: 11,
          fileName: 'Group 7.pdf',
          status: 'Completed',
          updatedAt: new Date().toISOString(),
          questionsCount: 3,
          isStructureReady: true,
          processingProgress: { percent: 100 },
        },
      ],
      stats: {
        sourceCount: 1,
        completedSourceCount: 1,
        studyReadySourceCount: 1,
        hasDeck: true,
        deckReady: true,
        deckStale: true,
      },
    });

    await renderDashboard();

    expect(container.textContent).toContain('AI Studio Console - Group 7.pdf');
    expect(container.textContent).toContain('Workspace deck needs refresh');
    expect(container.textContent).toContain('Refresh deck');
  });

  it('renders a failed source', async () => {
    dashboardService.getHome.mockResolvedValue({
      workspace: baseWorkspace,
      sources: [
        {
          id: 12,
          fileName: 'failed.pdf',
          status: 'Failed',
          updatedAt: new Date().toISOString(),
          questionsCount: 0,
          processingProgress: { percent: 100 },
        },
      ],
      stats: { sourceCount: 1, failedSourceCount: 1 },
    });

    await renderDashboard();

    expect(container.textContent).toContain('failed.pdf');
    expect(container.textContent).toContain('Failed');
  });

  async function renderDashboard() {
    await act(async () => {
      root.render(
        <LanguageProvider>
          <MemoryRouter>
            <ToastProvider>
              <DashboardPage />
            </ToastProvider>
          </MemoryRouter>
        </LanguageProvider>
      );
    });

    await act(async () => {
      await Promise.resolve();
    });
  }
});
