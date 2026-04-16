import React, { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { folderService } from '../services/api';

const DEMO_USER = 'demo-user';

function formatRelativeTime(value) {
  if (!value) {
    return '-';
  }

  const diffMs = Date.now() - new Date(value).getTime();
  if (diffMs < 60_000) {
    return 'vua cap nhat';
  }
  if (diffMs < 3_600_000) {
    return `${Math.max(1, Math.floor(diffMs / 60_000))} phut truoc`;
  }
  if (diffMs < 86_400_000) {
    return `${Math.max(1, Math.floor(diffMs / 3_600_000))} gio truoc`;
  }

  return new Date(value).toLocaleString();
}

function getDeckStatusLabel(deck) {
  if (!deck) {
    return 'Chua co deck';
  }

  switch (String(deck.status || '').toLowerCase()) {
    case 'completed':
      return deck.isStale ? 'Can regenerate' : 'Deck san sang';
    case 'failed':
      return 'Deck that bai';
    case 'generatingslides':
    case 'generatingoutline':
      return 'Dang tao deck';
    default:
      return deck.status;
  }
}

function FolderProjects() {
  const [folders, setFolders] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [creating, setCreating] = useState(false);
  const [feedback, setFeedback] = useState('');
  const [form, setForm] = useState({
    name: '',
    description: '',
  });
  const navigate = useNavigate();

  const loadFolders = useCallback(async () => {
    try {
      setError('');
      const data = await folderService.getUserFolders(DEMO_USER);
      setFolders(Array.isArray(data) ? data : []);
    } catch (err) {
      console.error(err);
      setError('Khong tai duoc danh sach folder projects.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadFolders();
  }, [loadFolders]);

  const handleCreate = async (event) => {
    event.preventDefault();

    if (!form.name.trim()) {
      setFeedback('Can nhap ten folder project.');
      return;
    }

    try {
      setCreating(true);
      setFeedback('');
      await folderService.createFolder({
        name: form.name.trim(),
        description: form.description.trim(),
        userId: DEMO_USER,
      });
      setForm({ name: '', description: '' });
      setFeedback('Da tao folder project moi.');
      await loadFolders();
    } catch (err) {
      console.error(err);
      setFeedback('Khong tao duoc folder project.');
    } finally {
      setCreating(false);
    }
  };

  const handleDelete = async (folderId) => {
    if (!window.confirm('Xoa folder project nay va tat ca source ben trong?')) {
      return;
    }

    try {
      await folderService.deleteFolder(folderId);
      setFeedback('Da xoa folder project.');
      await loadFolders();
    } catch (err) {
      console.error(err);
      setFeedback('Khong xoa duoc folder project.');
    }
  };

  if (loading) {
    return (
      <div className="loading">
        <div className="spinner"></div>
        <p>Dang tai folder projects...</p>
      </div>
    );
  }

  return (
    <div className="folders-page">
      <section className="folders-hero card">
        <div className="folders-hero-copy">
          <span className="folders-eyebrow">Project workspace</span>
          <h2>Folder Projects cho phep gom nhieu nguon va tao mot slide deck chung.</h2>
          <p>
            Moi project co danh sach source rieng, quy trinh chon source thu cong cho slide,
            va mot studio editor tach biet de sua deck theo mockup.
          </p>
        </div>

        <form className="folders-create-card" onSubmit={handleCreate}>
          <strong>Tao folder project</strong>
          <input
            type="text"
            value={form.name}
            onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))}
            placeholder="Vi du: Giao an Lich su lop 12"
          />
          <textarea
            rows={3}
            value={form.description}
            onChange={(event) => setForm((current) => ({ ...current, description: event.target.value }))}
            placeholder="Mo ta ngan ve chu de, lop hoc, hoac muc tieu deck"
          />
          <button type="submit" className="button" disabled={creating}>
            {creating ? 'Dang tao...' : 'Tao project'}
          </button>
        </form>
      </section>

      {error && <div className="alert alert-error">{error}</div>}
      {feedback && <div className="alert alert-info">{feedback}</div>}

      <section className="folders-grid">
        {folders.length === 0 && (
          <div className="folders-empty card">
            <h3>Chua co folder project nao</h3>
            <p>Tao project dau tien de upload nhieu nguon va bat dau flow slide deck cap folder.</p>
          </div>
        )}

        {folders.map((folder) => (
          <article key={folder.id} className="folder-card card">
            <div className="folder-card-head">
              <div>
                <span className="folder-card-kicker">Folder Project</span>
                <h3>{folder.name}</h3>
                <p>{folder.description || 'Chua co mo ta cho project nay.'}</p>
              </div>
              <span className={`folder-deck-pill${folder.latestDeck?.isStale ? ' stale' : ''}`}>
                {getDeckStatusLabel(folder.latestDeck)}
              </span>
            </div>

            <div className="folder-stats-row">
              <div>
                <strong>{folder.sourceCount || 0}</strong>
                <span>Nguon</span>
              </div>
              <div>
                <strong>{folder.readySourceCount || 0}</strong>
                <span>Ready</span>
              </div>
              <div>
                <strong>{folder.selectedSourceCount || 0}</strong>
                <span>Duoc chon</span>
              </div>
              <div>
                <strong>{folder.latestDeck?.slideCount || 0}</strong>
                <span>Slides</span>
              </div>
            </div>

            <div className="folder-card-meta">
              <span>Cap nhat: {formatRelativeTime(folder.updatedAt)}</span>
              <span>{folder.latestDeck?.title || 'Chua co deck hien hanh'}</span>
            </div>

            <div className="folder-card-actions">
              <button type="button" className="button" onClick={() => navigate(`/folders/${folder.id}/studio`)}>
                Mo folder studio
              </button>
              <button type="button" className="button button-secondary" onClick={() => navigate(`/folders/${folder.id}/studio`)}>
                Quan ly source
              </button>
              <button type="button" className="button button-secondary" onClick={() => handleDelete(folder.id)}>
                Xoa project
              </button>
            </div>
          </article>
        ))}
      </section>
    </div>
  );
}

export default FolderProjects;
