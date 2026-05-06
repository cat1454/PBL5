import React, { useCallback, useEffect, useState } from 'react';
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
    if (!window.confirm(t('workspaces.confirmDelete'))) {
      return;
    }

    try {
      setActionError('');
      await workspaceService.remove(folderId);
      showToast({
        type: 'success',
        message: t('workspaces.feedback.deleted'),
      });
      await loadFolders();
    } catch (err) {
      console.error(err);
      setActionError(getApiErrorMessage(err, t('workspaces.errors.deleteFailed')));
    }
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
      <section className="folders-hero card">
        <div className="folders-hero-copy">
          <span className="folders-eyebrow">{t('workspaces.heroEyebrow')}</span>
          <h2>{t('workspaces.heroTitle')}</h2>
          <p>{t('workspaces.heroBody')}</p>
        </div>

        <form className="folders-create-card" onSubmit={handleCreate}>
          <strong>{t('workspaces.createTitle')}</strong>
          <input
            type="text"
            value={form.name}
            onChange={(event) => {
              setFormError('');
              setForm((current) => ({ ...current, name: event.target.value }));
            }}
            placeholder={t('workspaces.createNamePlaceholder')}
          />
          <textarea
            rows={3}
            value={form.description}
            onChange={(event) => {
              setFormError('');
              setForm((current) => ({ ...current, description: event.target.value }));
            }}
            placeholder={t('workspaces.createDescriptionPlaceholder')}
          />
          {formError && <div className="alert alert-error">{formError}</div>}
          <button type="submit" className="button" disabled={creating}>
            {creating ? t('workspaces.creating') : t('workspaces.createButton')}
          </button>
        </form>
      </section>

      {error && <div className="alert alert-error">{error}</div>}
      {actionError && <div className="alert alert-error">{actionError}</div>}

      <section className="folders-grid">
        {folders.length === 0 && (
          <div className="folders-empty card">
            <h3>{t('workspaces.emptyTitle')}</h3>
            <p>{t('workspaces.emptyBody')}</p>
          </div>
        )}

        {folders.map((folder) => (
          <article key={folder.id} className="folder-card card">
            <div className="folder-card-head">
              <div>
                <span className="folder-card-kicker">{folder.isDefault ? t('workspaces.defaultWorkspace') : t('workspaces.workspace')}</span>
                <h3>{folder.name}</h3>
                <p>{folder.description || t('workspaces.noDescription')}</p>
              </div>
              <span className={`folder-deck-pill${folder.latestDeck?.isStale ? ' stale' : ''}`}>
                {getDeckStatusLabel(folder.latestDeck)}
              </span>
            </div>

            <div className="folder-stats-row">
              <div>
                <strong>{folder.sourceCount || 0}</strong>
                <span>{t('workspaces.stats.sources')}</span>
              </div>
              <div>
                <strong>{folder.readySourceCount || 0}</strong>
                <span>{t('workspaces.stats.ready')}</span>
              </div>
              <div>
                <strong>{folder.selectedSourceCount || 0}</strong>
                <span>{t('workspaces.stats.selected')}</span>
              </div>
              <div>
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
                {t('workspaces.open')}
              </button>
              <button type="button" className="button button-secondary" onClick={() => handleDelete(folder.id)}>
                {t('workspaces.delete')}
              </button>
            </div>
          </article>
        ))}
      </section>
    </div>
  );
}

export default FolderProjects;
