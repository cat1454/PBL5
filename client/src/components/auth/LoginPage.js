import React, { useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { LuBookOpen, LuCheck, LuLayers3, LuSparkles } from 'react-icons/lu';
import { useAuth } from '../../context/AuthContext';
import { useLanguage } from '../../context/LanguageContext';

function LoginPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const { login } = useAuth();
  const { t } = useLanguage();
  const [form, setForm] = useState({
    email: '',
    password: '',
  });
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');

  const redirectTo = location.state?.from?.pathname || '/';

  const handleSubmit = async (event) => {
    event.preventDefault();
    setSubmitting(true);
    setError('');

    try {
      await login(form);
      navigate(redirectTo, { replace: true });
    } catch (err) {
      setError(err?.response?.data?.message || t('auth.login.errors.failed'));
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
          <h1>{t('auth.login.title')}</h1>
          <p>{t('auth.login.subtitle')}</p>
        </div>

        <form className="auth-form" onSubmit={handleSubmit}>
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
              autoComplete="current-password"
              required
            />
          </label>

          {error && <div className="alert alert-error">{error}</div>}

          <button type="submit" className="button auth-submit" disabled={submitting}>
            {submitting ? t('auth.login.submitting') : t('auth.login.submit')}
          </button>
        </form>

        <p className="auth-switch">
          {t('auth.login.switchPrompt')} <Link to="/register">{t('auth.login.switchAction')}</Link>
        </p>
        </div>
      </div>
    </div>
  );
}

export default LoginPage;
