import React, { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { LuBookOpen, LuCheck, LuLayers3, LuSparkles } from 'react-icons/lu';
import { useAuth } from '../../context/AuthContext';
import { useLanguage } from '../../context/LanguageContext';

const ROLE_OPTIONS = ['LEARNER', 'INSTRUCTOR'];

function RegisterPage() {
  const navigate = useNavigate();
  const { register } = useAuth();
  const { t } = useLanguage();
  const [form, setForm] = useState({
    fullName: '',
    email: '',
    password: '',
    confirmPassword: '',
    role: 'LEARNER',
  });
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');

  const handleSubmit = async (event) => {
    event.preventDefault();
    setError('');

    if (form.password !== form.confirmPassword) {
      setError(t('auth.register.errors.passwordMismatch'));
      return;
    }

    setSubmitting(true);

    try {
      await register({
        fullName: form.fullName,
        email: form.email,
        password: form.password,
        role: form.role,
      });
      navigate('/', { replace: true });
    } catch (err) {
      setError(err?.response?.data?.message || t('auth.register.errors.failed'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="auth-page">
      <div className="auth-stage">
        <aside className="auth-product-panel" aria-hidden="true">
          <div className="auth-brand-lockup">
            <div className="auth-brand-mark">AI</div>
            <div>
              <strong>{t('app.brand')}</strong>
              <span>{t('app.topbar.productTag')}</span>
            </div>
          </div>
          <div className="auth-product-visual">
            <div className="auth-visual-node auth-visual-node-primary">
              <LuSparkles />
              <span>{t('app.dashboard.guide.title')}</span>
            </div>
            <div className="auth-visual-node">
              <LuLayers3 />
              <span>{t('app.dashboard.pipeline.title')}</span>
            </div>
            <div className="auth-visual-node">
              <LuBookOpen />
              <span>{t('app.dashboard.checklist.title')}</span>
            </div>
            <div className="auth-visual-check">
              <LuCheck />
            </div>
          </div>
        </aside>

        <div className="auth-card">
        <div className="auth-copy">
          <span className="auth-kicker">{t('auth.common.kicker')}</span>
          <h1>{t('auth.register.title')}</h1>
          <p>{t('auth.register.subtitle')}</p>
        </div>

        <form className="auth-form" onSubmit={handleSubmit}>
          <label>
            <span>{t('auth.common.fullName')}</span>
            <input
              type="text"
              value={form.fullName}
              onChange={(event) => setForm((current) => ({ ...current, fullName: event.target.value }))}
              autoComplete="name"
              required
            />
          </label>

          <label>
            <span>{t('auth.common.email')}</span>
            <input
              type="email"
              value={form.email}
              onChange={(event) => setForm((current) => ({ ...current, email: event.target.value }))}
              autoComplete="email"
              required
            />
          </label>

          <label>
            <span>{t('auth.common.password')}</span>
            <input
              type="password"
              value={form.password}
              onChange={(event) => setForm((current) => ({ ...current, password: event.target.value }))}
              autoComplete="new-password"
              required
            />
          </label>

          <label>
            <span>{t('auth.common.confirmPassword')}</span>
            <input
              type="password"
              value={form.confirmPassword}
              onChange={(event) => setForm((current) => ({ ...current, confirmPassword: event.target.value }))}
              autoComplete="new-password"
              required
            />
          </label>

          <label>
            <span>{t('auth.common.role')}</span>
            <select
              value={form.role}
              onChange={(event) => setForm((current) => ({ ...current, role: event.target.value }))}
            >
              {ROLE_OPTIONS.map((role) => (
                <option key={role} value={role}>{t(`auth.roles.${role}`)}</option>
              ))}
            </select>
          </label>

          {error && <div className="alert alert-error">{error}</div>}

          <button type="submit" className="button auth-submit" disabled={submitting}>
            {submitting ? t('auth.register.submitting') : t('auth.register.submit')}
          </button>
        </form>

        <p className="auth-switch">
          {t('auth.register.switchPrompt')} <Link to="/login">{t('auth.register.switchAction')}</Link>
        </p>
        </div>
      </div>
    </div>
  );
}

export default RegisterPage;
