import React, { useEffect, useState } from 'react';
import { adminService } from '../services/api';
import { useLanguage } from '../context/LanguageContext';

function AdminPage() {
  const { t } = useLanguage();
  const [overview, setOverview] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    let isMounted = true;

    const loadOverview = async () => {
      try {
        const result = await adminService.getOverview();
        if (isMounted) {
          setOverview(result);
        }
      } catch (err) {
        if (isMounted) {
          setError(err?.response?.data?.message || t('admin.errors.loadFailed'));
        }
      } finally {
        if (isMounted) {
          setLoading(false);
        }
      }
    };

    loadOverview();

    return () => {
      isMounted = false;
    };
  }, [t]);

  if (loading) {
    return (
      <div className="card">
        <div className="spinner"></div>
      </div>
    );
  }

  if (error) {
    return <div className="alert alert-error">{error}</div>;
  }

  return (
    <div className="admin-page">
      <section className="card admin-hero">
        <h2>{t('admin.title')}</h2>
        <p>{t('admin.subtitle')}</p>
      </section>

      <section className="admin-grid">
        <div className="card admin-card">
          <span>{t('admin.cards.users')}</span>
          <strong>{overview?.totals?.users || 0}</strong>
        </div>
        <div className="card admin-card">
          <span>{t('admin.cards.documents')}</span>
          <strong>{overview?.totals?.documents || 0}</strong>
        </div>
      </section>

      <section className="card admin-table-card">
        <h3>{t('admin.users')}</h3>
        <div className="admin-table-wrap">
          <table className="admin-table">
            <thead>
              <tr>
                <th>{t('admin.columns.name')}</th>
                <th>{t('admin.columns.email')}</th>
                <th>{t('admin.columns.role')}</th>
              </tr>
            </thead>
            <tbody>
              {(overview?.users || []).map((user) => (
                <tr key={user.id}>
                  <td>{user.fullName}</td>
                  <td>{user.email}</td>
                  <td>{user.role}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="card admin-table-card">
        <h3>{t('admin.documents')}</h3>
        <div className="admin-table-wrap">
          <table className="admin-table">
            <thead>
              <tr>
                <th>{t('admin.columns.fileName')}</th>
                <th>{t('admin.columns.status')}</th>
                <th>{t('admin.columns.owner')}</th>
              </tr>
            </thead>
            <tbody>
              {(overview?.documents || []).map((document) => (
                <tr key={document.id}>
                  <td>{document.fileName}</td>
                  <td>{document.status}</td>
                  <td>{document.uploadedBy}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  );
}

export default AdminPage;
