import React, { useCallback, useEffect, useMemo, useState } from 'react';
import {
  LuCircleCheck,
  LuClock3,
  LuEllipsis,
  LuExternalLink,
  LuFolderOpen,
  LuPlus,
  LuRefreshCw,
  LuSearch,
  LuTrash2,
  LuX,
} from 'react-icons/lu';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { getApiErrorMessage, workspaceService } from '../services/api';
import { useToast } from './common/ToastProvider';
import { useLanguage } from '../context/LanguageContext';

function FolderProjects() {
  const { currentUser } = useAuth();
  const { t } = useLanguage();
  const { showToast } = useToast();
  const [folders, setFolders] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [actionError, setActionError] = useState('');
  const [formError, setFormError] = useState('');
  const [creating, setCreating] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState('all');
  const [sortMode, setSortMode] = useState('updated');
  const [createModalOpen, setCreateModalOpen] = useState(false);
  const [openMenuId, setOpenMenuId] = useState(null);
  const [confirmDeleteId, setConfirmDeleteId] = useState(null);
  const [deletingId, setDeletingId] = useState(null);
  const [form, setForm] = useState({
    name: '',
    description: '',
  });
  const navigate = useNavigate();

  const formatRelativeTime = (value) => {
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

    return new Date(value).toLocaleString();
  };

  const getDeckStatusLabel = (deck) => {
    if (!deck) {
      return t('workspaces.deckStatus.none');
    }

    switch (String(deck.status || '').toLowerCase()) {
      case 'completed':
        return deck.isStale ? t('workspaces.deckStatus.needsRegenerate') : t('workspaces.deckStatus.ready');
      case 'failed':
        return t('workspaces.deckStatus.failed');
      case 'generatingslides':
      case 'generatingoutline':
        return t('workspaces.deckStatus.generating');
      default:
        return deck.status;
    }
  };

  const getDeckStatusKey = (deck) => {
    if (!deck) {
      return 'none';
    }

    const status = String(deck.status || '').toLowerCase();
    if (status === 'completed') {
      return deck.isStale ? 'stale' : 'ready';
    }
    if (status === 'failed') {
      return 'failed';
    }
    if (status === 'generatingslides' || status === 'generatingoutline') {
      return 'generating';
    }

    return 'other';
  };

  const getDeckStatusTone = (deck) => {
    const key = getDeckStatusKey(deck);
    if (key === 'ready') {
      return ' ready';
    }
    if (key === 'stale' || key === 'failed') {
      return ' stale';
    }
    if (key === 'generating') {
      return ' generating';
    }

    return '';
  };

  const loadFolders = useCallback(async () => {
    try {
      setError('');
      const data = await workspaceService.list(String(currentUser?.id || ''));
      setFolders(Array.isArray(data) ? data : []);
    } catch (err) {
      console.error(err);
      setError(getApiErrorMessage(err, t('workspaces.errors.loadFailed')));
    } finally {
      setLoading(false);
    }
  }, [currentUser?.id, t]);

  useEffect(() => {
    loadFolders();
  }, [loadFolders]);

  const handleCreate = async (event) => {
    event.preventDefault();

    if (!form.name.trim()) {
      setFormError(t('workspaces.errors.nameRequired'));
      return;
    }

    try {
      setCreating(true);
      setFormError('');
      setActionError('');
      await workspaceService.create({
        name: form.name.trim(),
        description: form.description.trim(),
      });
      setForm({ name: '', description: '' });
      setCreateModalOpen(false);
      showToast({
        type: 'success',
        message: t('workspaces.feedback.created'),
      });
      await loadFolders();
    } catch (err) {
      console.error(err);
      setActionError(getApiErrorMessage(err, t('workspaces.errors.createFailed')));
    } finally {
      setCreating(false);
    }
  };

  const handleDelete = async (folderId) => {
    try {
      setActionError('');
      setDeletingId(folderId);
      await workspaceService.remove(folderId);
      setConfirmDeleteId(null);
      setOpenMenuId(null);
      showToast({
        type: 'success',
        message: t('workspaces.feedback.deleted'),
      });
      await loadFolders();
    } catch (err) {
      console.error(err);
      setActionError(getApiErrorMessage(err, t('workspaces.errors.deleteFailed')));
    } finally {
      setDeletingId(null);
    }
  };

  const filteredFolders = useMemo(() => {
    const normalizedQuery = searchQuery.trim().toLowerCase();

    return folders
      .filter((folder) => {
        const matchesQuery = !normalizedQuery
          || String(folder.name || '').toLowerCase().includes(normalizedQuery)
          || String(folder.description || '').toLowerCase().includes(normalizedQuery)
          || String(folder.latestDeck?.title || '').toLowerCase().includes(normalizedQuery);
        const matchesStatus = statusFilter === 'all' || getDeckStatusKey(folder.latestDeck) === statusFilter;

        return matchesQuery && matchesStatus;
      })
      .sort((left, right) => {
        if (sortMode === 'name') {
          return String(left.name || '').localeCompare(String(right.name || ''));
        }
        if (sortMode === 'sources') {
          return Number(right.sourceCount || 0) - Number(left.sourceCount || 0);
        }

        return new Date(right.updatedAt || 0).getTime() - new Date(left.updatedAt || 0).getTime();
      });
  }, [folders, searchQuery, sortMode, statusFilter]);

  const summaryMetrics = useMemo(() => {
    return folders.reduce((summary, folder) => {
      const deckKey = getDeckStatusKey(folder.latestDeck);
      const sourceCount = Number(folder.sourceCount || 0);
      const readySourceCount = Number(folder.readySourceCount || 0);

      return {
        totalWorkspaces: summary.totalWorkspaces + 1,
        totalSources: summary.totalSources + sourceCount,
        deckReady: summary.deckReady + (deckKey === 'ready' ? 1 : 0),
        needsProcessing: summary.needsProcessing + (
          !folder.latestDeck
          || deckKey === 'stale'
          || deckKey === 'generating'
          || deckKey === 'failed'
          || readySourceCount <= 0
            ? 1
            : 0
        ),
      };
    }, {
      totalWorkspaces: 0,
      totalSources: 0,
      deckReady: 0,
      needsProcessing: 0,
    });
  }, [folders]);

  const hasActiveFilters = searchQuery.trim().length > 0 || statusFilter !== 'all';
  const getStatHelp = (key, count) => t(`workspaces.statsHelp.${key}`, { count: Number(count || 0) });
  const openCreateModal = () => {
    setFormError('');
    setActionError('');
    setCreateModalOpen(true);
  };
  const closeCreateModal = () => {
    if (creating) {
      return;
    }
    setFormError('');
    setCreateModalOpen(false);
  };
  const resetFilters = () => {
    setSearchQuery('');
    setStatusFilter('all');
    setSortMode('updated');
  };

  if (loading) {
    return (
      <div className="loading">
        <div className="spinner"></div>
        <p>{t('workspaces.loading')}</p>
      </div>
    );
  }

  return (
    <div className="folders-page">
      <section className="folders-hero card" aria-labelledby="workspaces-hub-title">
        <div className="folders-hero-copy">
          <div className="folders-hero-head">
            <span className="folders-eyebrow">{t('workspaces.heroEyebrow')}</span>
            <button type="button" className="button folders-new-button" onClick={openCreateModal}>
              <LuPlus aria-hidden="true" />
              {t('workspaces.newWorkspace')}
            </button>
          </div>
          <h2 id="workspaces-hub-title">{t('workspaces.heroTitle')}</h2>
          <p>{t('workspaces.heroBody')}</p>

          <div className="folders-summary-grid" aria-label={t('workspaces.summary.label')}>
            <div className="folders-summary-card">
              <span className="folders-summary-icon"><LuFolderOpen aria-hidden="true" /></span>
              <strong>{summaryMetrics.totalWorkspaces}</strong>
              <span>{t('workspaces.summary.workspaces')}</span>
            </div>
            <div className="folders-summary-card">
              <span className="folders-summary-icon"><LuSearch aria-hidden="true" /></span>
              <strong>{summaryMetrics.totalSources}</strong>
              <span>{t('workspaces.summary.sources')}</span>
            </div>
            <div className="folders-summary-card is-ready">
              <span className="folders-summary-icon"><LuCircleCheck aria-hidden="true" /></span>
              <strong>{summaryMetrics.deckReady}</strong>
              <span>{t('workspaces.summary.deckReady')}</span>
            </div>
            <div className="folders-summary-card is-attention">
              <span className="folders-summary-icon"><LuClock3 aria-hidden="true" /></span>
              <strong>{summaryMetrics.needsProcessing}</strong>
              <span>{t('workspaces.summary.needsProcessing')}</span>
            </div>
          </div>
        </div>
      </section>

      {createModalOpen && (
        <div className="folders-modal-backdrop" role="presentation" onMouseDown={closeCreateModal}>
          <form
            className="folders-create-modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="workspace-create-title"
            onSubmit={handleCreate}
            onMouseDown={(event) => event.stopPropagation()}
          >
            <div className="folders-modal-head">
              <strong id="workspace-create-title">{t('workspaces.createTitle')}</strong>
              <button type="button" className="folders-icon-button" onClick={closeCreateModal} aria-label={t('workspaces.closeCreate')}>
                <LuX aria-hidden="true" />
              </button>
            </div>
            <label className="folders-field-label">
              <span>{t('workspaces.createNameLabel')}</span>
              <input
                type="text"
                value={form.name}
                onChange={(event) => {
                  setFormError('');
                  setForm((current) => ({ ...current, name: event.target.value }));
                }}
                placeholder={t('workspaces.createNamePlaceholder')}
                autoFocus
              />
            </label>
            <label className="folders-field-label">
              <span>{t('workspaces.createDescriptionLabel')}</span>
              <textarea
                rows={3}
                value={form.description}
                onChange={(event) => {
                  setFormError('');
                  setForm((current) => ({ ...current, description: event.target.value }));
                }}
                placeholder={t('workspaces.createDescriptionPlaceholder')}
              />
            </label>
            {formError && <div className="alert alert-error">{formError}</div>}
            <div className="folders-modal-actions">
              <button type="button" className="button button-secondary" onClick={closeCreateModal} disabled={creating}>
                {t('workspaces.cancelCreate')}
              </button>
              <button type="submit" className="button" disabled={creating}>
                {creating ? t('workspaces.creating') : t('workspaces.createButton')}
              </button>
            </div>
          </form>
        </div>
      )}

      {error && <div className="alert alert-error">{error}</div>}
      {actionError && <div className="alert alert-error">{actionError}</div>}

      <section className="folders-toolbar card" aria-label={t('workspaces.filters.label')}>
        <label className="folders-field-label folders-search-field">
          <span>{t('workspaces.filters.searchLabel')}</span>
          <span className="folders-input-wrap">
            <LuSearch aria-hidden="true" />
            <input
              type="search"
              value={searchQuery}
              onChange={(event) => setSearchQuery(event.target.value)}
              placeholder={t('workspaces.filters.searchPlaceholder')}
            />
          </span>
        </label>
        <label className="folders-field-label">
          <span>{t('workspaces.filters.statusLabel')}</span>
          <select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}>
            <option value="all">{t('workspaces.filters.statusAll')}</option>
            <option value="ready">{t('workspaces.filters.statusReady')}</option>
            <option value="stale">{t('workspaces.filters.statusStale')}</option>
            <option value="generating">{t('workspaces.filters.statusGenerating')}</option>
            <option value="failed">{t('workspaces.filters.statusFailed')}</option>
            <option value="none">{t('workspaces.filters.statusNone')}</option>
          </select>
        </label>
        <label className="folders-field-label">
          <span>{t('workspaces.filters.sortLabel')}</span>
          <select value={sortMode} onChange={(event) => setSortMode(event.target.value)}>
            <option value="updated">{t('workspaces.filters.sortUpdated')}</option>
            <option value="name">{t('workspaces.filters.sortName')}</option>
            <option value="sources">{t('workspaces.filters.sortSources')}</option>
          </select>
        </label>
        <span className="folders-result-count">
          {t('workspaces.filters.resultCount', { count: filteredFolders.length })}
        </span>
      </section>

      <section className="folders-grid">
        {folders.length === 0 && (
          <div className="folders-empty card">
            <h3>{t('workspaces.emptyTitle')}</h3>
            <p>{t('workspaces.emptyBody')}</p>
          </div>
        )}

        {folders.length > 0 && filteredFolders.length === 0 && (
          <div className="folders-empty card">
            <h3>{t('workspaces.filters.emptyTitle')}</h3>
            <p>{t('workspaces.filters.emptyBody')}</p>
            {hasActiveFilters && (
              <button type="button" className="button button-secondary" onClick={resetFilters}>
                <LuRefreshCw aria-hidden="true" />
                {t('workspaces.filters.reset')}
              </button>
            )}
          </div>
        )}

        {filteredFolders.map((folder) => (
          <article key={folder.id} className="folder-card card">
            <div className="folder-card-head">
              <div>
                <span className="folder-card-kicker">{folder.isDefault ? t('workspaces.defaultWorkspace') : t('workspaces.workspace')}</span>
                <h3>{folder.name}</h3>
                <p>{folder.description || t('workspaces.noDescription')}</p>
              </div>
              <span className={`folder-deck-pill${getDeckStatusTone(folder.latestDeck)}`}>
                {getDeckStatusLabel(folder.latestDeck)}
              </span>
            </div>

            <div className="folder-stats-row">
              <div title={getStatHelp('sources', folder.sourceCount)} aria-label={getStatHelp('sources', folder.sourceCount)}>
                <strong>{folder.sourceCount || 0}</strong>
                <span>{t('workspaces.stats.sources')}</span>
              </div>
              <div title={getStatHelp('ready', folder.readySourceCount)} aria-label={getStatHelp('ready', folder.readySourceCount)}>
                <strong>{folder.readySourceCount || 0}</strong>
                <span>{t('workspaces.stats.ready')}</span>
              </div>
              <div title={getStatHelp('selected', folder.selectedSourceCount)} aria-label={getStatHelp('selected', folder.selectedSourceCount)}>
                <strong>{folder.selectedSourceCount || 0}</strong>
                <span>{t('workspaces.stats.selected')}</span>
              </div>
              <div title={getStatHelp('slides', folder.latestDeck?.slideCount)} aria-label={getStatHelp('slides', folder.latestDeck?.slideCount)}>
                <strong>{folder.latestDeck?.slideCount || 0}</strong>
                <span>{t('workspaces.stats.slides')}</span>
              </div>
            </div>

            <div className="folder-card-meta">
              <span>{t('workspaces.updated', { time: formatRelativeTime(folder.updatedAt) })}</span>
              <span>{folder.latestDeck?.title || t('workspaces.noCurrentDeck')}</span>
            </div>

            <div className="folder-card-actions">
              <button type="button" className="button" onClick={() => navigate(`/workspaces/${folder.id}`)}>
                <LuExternalLink aria-hidden="true" />
                {t('workspaces.open')}
              </button>
              <div className="folder-card-menu">
                <button
                  type="button"
                  className="folders-icon-button"
                  aria-label={t('workspaces.moreActions', { name: folder.name })}
                  aria-expanded={openMenuId === folder.id}
                  onClick={() => {
                    setConfirmDeleteId(null);
                    setOpenMenuId((current) => (current === folder.id ? null : folder.id));
                  }}
                >
                  <LuEllipsis aria-hidden="true" />
                </button>
                {openMenuId === folder.id && (
                  <div className="folder-overflow-menu" role="menu">
                    <button
                      type="button"
                      className="folder-overflow-item danger"
                      role="menuitem"
                      onClick={() => {
                        setConfirmDeleteId(folder.id);
                        setOpenMenuId(null);
                      }}
                    >
                      <LuTrash2 aria-hidden="true" />
                      {t('workspaces.delete')}
                    </button>
                  </div>
                )}
              </div>
            </div>

            {confirmDeleteId === folder.id && (
              <div className="folders-inline-confirm" role="alert">
                <span>{t('workspaces.inlineDeleteConfirm', { name: folder.name })}</span>
                <div>
                  <button type="button" className="button button-secondary" onClick={() => setConfirmDeleteId(null)} disabled={deletingId === folder.id}>
                    {t('workspaces.cancelDelete')}
                  </button>
                  <button type="button" className="button folders-delete-button" onClick={() => handleDelete(folder.id)} disabled={deletingId === folder.id}>
                    {deletingId === folder.id ? t('workspaces.deleting') : t('workspaces.deleteShort')}
                  </button>
                </div>
              </div>
            )}
          </article>
        ))}
      </section>
    </div>
  );
}

export default FolderProjects;
