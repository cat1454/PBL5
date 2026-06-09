import React, { useState, useEffect, useRef } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { useLanguage } from '../../context/LanguageContext';
import AiAssistantCanvas from './AiAssistantCanvas';
import AuthFlowModal from './AuthFlowModal';
import PasswordField from './PasswordField';
import RoleSelector from './RoleSelector';
import GoogleChooserModal from './GoogleChooserModal';
import { TbSparkles } from 'react-icons/tb';
import { FcGoogle } from 'react-icons/fc';
import '../../styles/pages/auth.css';

function AuthPage({ initialTab = 'login' }) {
  const navigate = useNavigate();
  const location = useLocation();
  const { login, register } = useAuth();
  const { t, language, setLanguage } = useLanguage();

  // Tab State
  const [activeTab, setActiveTab] = useState(initialTab);

  // Form States
  const [loginForm, setLoginForm] = useState({ email: '', password: '' });
  const [registerForm, setRegisterForm] = useState({
    fullName: '',
    email: '',
    password: '',
    confirmPassword: '',
    role: 'LEARNER',
  });

  // Focus and Animation State for Canvas Character
  const [isPasswordFocused, setIsPasswordFocused] = useState(false);

  // Status States
  const [submitting, setSubmitting] = useState(false);
  const [loginError, setLoginError] = useState('');
  const [registerError, setRegisterError] = useState('');

  // Modal State
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [isGoogleModalOpen, setIsGoogleModalOpen] = useState(false);

  // Redirect target
  const loginTabRef = useRef(null);
  const registerTabRef = useRef(null);
  const [indicatorStyle, setIndicatorStyle] = useState({ left: 0, width: 0 });
  const redirectTo = location.state?.from?.pathname || '/';

  // Sync activeTab state if initialTab prop changes
  useEffect(() => {
    setActiveTab(initialTab);
  }, [initialTab]);

  // Handle Tab Switch and URL sync
  const handleTabSwitch = (tab) => {
    if (submitting) return;
    setActiveTab(tab);
    setLoginError('');
    setRegisterError('');
    navigate(tab === 'login' ? '/login' : '/register', { replace: true });
  };

  // Calculate tab indicator position
  useEffect(() => {
    const activeBtn = activeTab === 'login' ? loginTabRef.current : registerTabRef.current;
    if (activeBtn) {
      setIndicatorStyle({
        left: activeBtn.offsetLeft,
        width: activeBtn.offsetWidth,
      });
    }
  }, [activeTab, language]);

  // Login Submit Handler
  const handleLoginSubmit = async (e) => {
    e.preventDefault();
    setSubmitting(true);
    setLoginError('');

    try {
      await login(loginForm);
      navigate(redirectTo, { replace: true });
    } catch (err) {
      // Security UX: General error message to prevent account enumeration (OWASP recommendation)
      const genericMessage = language === 'vi'
        ? 'Email hoặc mật khẩu không đúng.'
        : 'Incorrect email or password.';
      setLoginError(genericMessage);
    } finally {
      setSubmitting(false);
    }
  };

  // Register Submit Handler
  const handleRegisterSubmit = async (e) => {
    e.preventDefault();
    setRegisterError('');

    // Client-side confirm password check
    if (registerForm.password !== registerForm.confirmPassword) {
      setRegisterError(t('auth.register.errors.passwordMismatch'));
      return;
    }

    // Client-side basic length check (min 8 chars as per spec)
    if (registerForm.password.length < 8) {
      setRegisterError(
        language === 'vi'
          ? 'Mật khẩu phải chứa ít nhất 8 ký tự.'
          : 'Password must be at least 8 characters.'
      );
      return;
    }

    setSubmitting(true);

    try {
      await register({
        fullName: registerForm.fullName.trim(),
        email: registerForm.email.trim(),
        password: registerForm.password,
        role: registerForm.role,
      });
      navigate('/', { replace: true });
    } catch (err) {
      // Register error: display backend error if present
      const message = err?.response?.data?.message || t('auth.register.errors.failed');
      setRegisterError(message);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="auth-page" style={{ display: 'grid' }}>
      <div className="auth-stage">
        <div className="auth-wrapper" style={{ width: '100%' }}>
          {/* Cột Trái - Brand & AI Character */}
          <div className="auth-left">
            <div className="brand">
              <div className="brand-icon" aria-hidden="true">
                <TbSparkles />
              </div>
              <div>
                <div className="brand-text">{t('app.brand')}</div>
                <div className="brand-sub">{t('app.topbar.productTag')}</div>
              </div>
            </div>

            <div className="character-area">
              <AiAssistantCanvas hideEyes={isPasswordFocused} />
              <div className="char-label">
                <strong>
                  {language === 'vi' ? 'Trợ lý AI' : 'AI Assistant'}
                </strong>
                {language === 'vi' ? 'Luôn đồng hành cùng bạn 🔮' : 'Always here to guide you 🔮'}
              </div>
              <div className="how-btn-wrapper">
                <button
                  className="how-btn"
                  type="button"
                  onClick={() => setIsModalOpen(true)}
                >
                  {t('auth.howItWorks.button')}
                </button>
                <span className="sparkling-arrow" aria-hidden="true">
                  ⬅ ✨
                </span>
              </div>
            </div>

            {/* Accent tag at bottom left */}
            <div
              style={{
                width: '40px',
                height: '40px',
                background: '#d4a017',
                borderRadius: '10px',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                alignSelf: 'flex-end',
                color: '#ffffff',
                fontSize: '18px',
              }}
              aria-hidden="true"
            >
              ✓
            </div>
          </div>

          {/* Cột Phải - Form login/register */}
          <div className="auth-right">
            <div className="auth-header-row" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '10px' }}>
              <div className="eyebrow" style={{ marginBottom: 0 }}>{t('auth.common.kicker')}</div>
              <div className="auth-lang-selector">
                <button
                  type="button"
                  className={`lang-btn ${language === 'vi' ? 'active' : ''}`}
                  onClick={() => setLanguage('vi')}
                >
                  VN
                </button>
                <span className="lang-separator">|</span>
                <button
                  type="button"
                  className={`lang-btn ${language === 'en' ? 'active' : ''}`}
                  onClick={() => setLanguage('en')}
                >
                  EN
                </button>
              </div>
            </div>

            {/* Custom Tab Switches */}
            <div className="tab-row" role="tablist">
              <button
                ref={loginTabRef}
                className={`tab-btn ${activeTab === 'login' ? 'active' : ''}`}
                role="tab"
                aria-selected={activeTab === 'login'}
                aria-controls="panel-login"
                id="tab-login"
                onClick={() => handleTabSwitch('login')}
                type="button"
              >
                {t('auth.login.title')}
              </button>
              <button
                ref={registerTabRef}
                className={`tab-btn ${activeTab === 'register' ? 'active' : ''}`}
                role="tab"
                aria-selected={activeTab === 'register'}
                aria-controls="panel-register"
                id="tab-register"
                onClick={() => handleTabSwitch('register')}
                type="button"
              >
                {t('auth.register.title')}
              </button>
              <div
                className="tab-indicator"
                style={{
                  left: `${indicatorStyle.left}px`,
                  width: `${indicatorStyle.width}px`,
                }}
              />
            </div>

            {/* Panels Container */}
            <div className="panels-container">
              {/* LOGIN FORM PANEL */}
              {activeTab === 'login' && (
                <div
                  id="panel-login"
                  className="form-panel active"
                  role="tabpanel"
                  aria-labelledby="tab-login"
                >
                  <div className="auth-desc">
                    {t('auth.login.subtitle')}
                    <button
                      className="how-inline"
                      type="button"
                      onClick={() => setIsModalOpen(true)}
                      style={{ marginLeft: '4px' }}
                    >
                      {t('auth.howItWorks.inlineLink')}
                    </button>
                  </div>

                  {loginError && (
                    <div className="auth-error-alert" role="alert">
                      {loginError}
                    </div>
                  )}

                  <form className="auth-form" onSubmit={handleLoginSubmit}>
                    <div className="form-group">
                      <label className="form-label" htmlFor="login-email">
                        {t('auth.common.email')}
                      </label>
                      <input
                        id="login-email"
                        type="email"
                        value={loginForm.email}
                        onChange={(e) =>
                          setLoginForm((prev) => ({ ...prev, email: e.target.value }))
                        }
                        autoComplete="username"
                        placeholder="teacher.demo@elearn.local"
                        required
                        style={{ height: '38px' }}
                      />
                    </div>

                    <PasswordField
                      label={t('auth.common.password')}
                      id="login-password"
                      value={loginForm.password}
                      onChange={(e) =>
                        setLoginForm((prev) => ({ ...prev, password: e.target.value }))
                      }
                      autoComplete="current-password"
                      onFocus={() => setIsPasswordFocused(true)}
                      onBlur={() => setIsPasswordFocused(false)}
                      required
                    />

                    <button
                      className="submit-btn"
                      type="submit"
                      disabled={submitting}
                      style={{ height: '40px' }}
                    >
                      {submitting ? t('auth.login.submitting') : t('auth.login.submit')}
                    </button>

                    <div className="auth-divider">
                      <span>{language === 'vi' ? 'Hoặc' : 'Or'}</span>
                    </div>

                    <button
                      className="google-btn"
                      type="button"
                      onClick={() => setIsGoogleModalOpen(true)}
                      disabled={submitting}
                    >
                      <FcGoogle />
                      <span>
                        {language === 'vi' ? 'Đăng nhập với Google' : 'Sign in with Google'}
                      </span>
                    </button>
                  </form>

                  <p className="switch-link">
                    {t('auth.login.switchPrompt')}{' '}
                    <button
                      type="button"
                      onClick={() => handleTabSwitch('register')}
                    >
                      {t('auth.login.switchAction')}
                    </button>
                  </p>
                </div>
              )}

              {/* REGISTER FORM PANEL */}
              {activeTab === 'register' && (
                <div
                  id="panel-register"
                  className="form-panel active"
                  role="tabpanel"
                  aria-labelledby="tab-register"
                >

                  {registerError && (
                    <div className="auth-error-alert" role="alert">
                      {registerError}
                    </div>
                  )}

                  <form className="auth-form" onSubmit={handleRegisterSubmit}>
                    <div className="form-group">
                      <label className="form-label" htmlFor="reg-fullname">
                        {t('auth.common.fullName')}
                      </label>
                      <input
                        id="reg-fullname"
                        type="text"
                        value={registerForm.fullName}
                        onChange={(e) =>
                          setRegisterForm((prev) => ({ ...prev, fullName: e.target.value }))
                        }
                        autoComplete="name"
                        placeholder="Nguyễn Văn A"
                        required
                        style={{ height: '38px' }}
                      />
                    </div>

                    <div className="form-group">
                      <label className="form-label" htmlFor="reg-email">
                        {t('auth.common.email')}
                      </label>
                      <input
                        id="reg-email"
                        type="email"
                        value={registerForm.email}
                        onChange={(e) =>
                          setRegisterForm((prev) => ({ ...prev, email: e.target.value }))
                        }
                        autoComplete="email"
                        placeholder="example@email.com"
                        required
                        style={{ height: '38px' }}
                      />
                    </div>

                    <div className="register-pw-grid">
                      <PasswordField
                        label={t('auth.common.password')}
                        id="reg-password"
                        value={registerForm.password}
                        onChange={(e) =>
                          setRegisterForm((prev) => ({ ...prev, password: e.target.value }))
                        }
                        autoComplete="new-password"
                        onFocus={() => setIsPasswordFocused(true)}
                        onBlur={() => setIsPasswordFocused(false)}
                        placeholder={language === 'vi' ? 'Tối thiểu 8 ký tự' : 'At least 8 characters'}
                        required
                      />

                      <PasswordField
                        label={t('auth.common.confirmPassword')}
                        id="reg-confirm-password"
                        value={registerForm.confirmPassword}
                        onChange={(e) =>
                          setRegisterForm((prev) => ({
                            ...prev,
                            confirmPassword: e.target.value,
                          }))
                        }
                        autoComplete="new-password"
                        onFocus={() => setIsPasswordFocused(true)}
                        onBlur={() => setIsPasswordFocused(false)}
                        placeholder={language === 'vi' ? 'Nhập lại mật khẩu' : 'Re-enter password'}
                        required
                      />
                    </div>

                    <RoleSelector
                      selectedRole={registerForm.role}
                      onChange={(role) =>
                        setRegisterForm((prev) => ({ ...prev, role }))
                      }
                    />

                    <button
                      className="submit-btn"
                      type="submit"
                      disabled={submitting}
                      style={{ height: '40px' }}
                    >
                      {submitting ? t('auth.register.submitting') : t('auth.register.submit')}
                    </button>

                    <div className="auth-divider">
                      <span>{language === 'vi' ? 'Hoặc' : 'Or'}</span>
                    </div>

                    <button
                      className="google-btn"
                      type="button"
                      onClick={() => setIsGoogleModalOpen(true)}
                      disabled={submitting}
                    >
                      <FcGoogle />
                      <span>
                        {language === 'vi' ? 'Đăng ký với Google' : 'Sign up with Google'}
                      </span>
                    </button>
                  </form>

                  <p className="switch-link">
                    {t('auth.register.switchPrompt')}{' '}
                    <button
                      type="button"
                      onClick={() => handleTabSwitch('login')}
                    >
                      {t('auth.register.switchAction')}
                    </button>
                  </p>
                </div>
              )}
            </div>
          </div>
        </div>
      </div>

      {/* Modal - Cách hoạt động */}
      <AuthFlowModal isOpen={isModalOpen} onClose={() => setIsModalOpen(false)} />
      <GoogleChooserModal isOpen={isGoogleModalOpen} onClose={() => setIsGoogleModalOpen(false)} defaultRole={registerForm.role} />
    </div>
  );
}

export default AuthPage;
