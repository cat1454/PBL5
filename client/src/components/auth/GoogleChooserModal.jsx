import React, { useState } from 'react';
import { FcGoogle } from 'react-icons/fc';
import { useAuth } from '../../context/AuthContext';
import { useLanguage } from '../../context/LanguageContext';
import RoleSelector from './RoleSelector';

function GoogleChooserModal({ isOpen, onClose, defaultRole = 'LEARNER' }) {
  const { login, register } = useAuth();
  const { language } = useLanguage();
  const [view, setView] = useState('list'); // 'list' or 'custom'
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');
  
  // Custom form states
  const [form, setForm] = useState({
    fullName: '',
    email: '',
    role: defaultRole,
    password: 'Password123!',
  });

  if (!isOpen) return null;

  const accounts = [
    {
      name: 'Smoke Student',
      email: 'student_smoke@t.com',
      role: 'LEARNER',
      avatarColor: '#1a6b6b',
      avatarInitials: 'SS',
    },
    {
      name: 'Smoke Teacher',
      email: 'teacher_smoke@t.com',
      role: 'INSTRUCTOR',
      avatarColor: '#5a40b8',
      avatarInitials: 'ST',
    }
  ];

  const handleSelectAccount = async (account) => {
    setSubmitting(true);
    setError('');
    try {
      await login({
        email: account.email,
        password: 'Password123!',
      });
      onClose();
      window.location.href = '/';
    } catch (err) {
      setError(
        language === 'vi'
          ? 'Không thể đăng nhập bằng tài khoản này. Vui lòng kiểm tra lại DB.'
          : 'Unable to log in with this account. Please check the DB seeding.'
      );
    } finally {
      setSubmitting(false);
    }
  };

  const handleCustomSubmit = async (e) => {
    e.preventDefault();
    if (!form.fullName.trim() || !form.email.trim()) {
      setError(
        language === 'vi' ? 'Vui lòng điền đầy đủ họ tên và email.' : 'Please enter full name and email.'
      );
      return;
    }
    setSubmitting(true);
    setError('');
    try {
      // Register new user via real API
      await register({
        fullName: form.fullName.trim(),
        email: form.email.trim(),
        password: form.password,
        role: form.role,
      });
      onClose();
      window.location.href = '/';
    } catch (err) {
      setError(err?.response?.data?.message || (
        language === 'vi' ? 'Đăng ký tài khoản mới thất bại.' : 'Account registration failed.'
      ));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="google-modal-backdrop" onClick={onClose}>
      <div 
        className="google-modal-card" 
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-labelledby="google-modal-title"
      >
        {/* Google Header */}
        <div className="google-modal-header">
          <FcGoogle className="google-logo-icon" />
          <h2 id="google-modal-title" className="google-title">
            {language === 'vi' ? 'Đăng nhập bằng Google' : 'Sign in with Google'}
          </h2>
          <p className="google-subtitle">
            {language === 'vi' ? 'để tiếp tục đến AI Teaching' : 'to continue to AI Teaching'}
          </p>
        </div>

        {error && (
          <div className="google-error-alert" role="alert">
            {error}
          </div>
        )}

        {view === 'list' ? (
          <div className="google-accounts-list">
            <p className="google-list-header">
              {language === 'vi' ? 'Chọn tài khoản' : 'Choose an account'}
            </p>
            
            <div className="accounts-container">
              {accounts.map((acc) => (
                <button
                  key={acc.email}
                  type="button"
                  className="google-account-item"
                  onClick={() => handleSelectAccount(acc)}
                  disabled={submitting}
                >
                  <div 
                    className="account-avatar"
                    style={{ backgroundColor: acc.avatarColor }}
                  >
                    {acc.avatarInitials}
                  </div>
                  <div className="account-details">
                    <div className="account-name">{acc.name}</div>
                    <div className="account-email">{acc.email}</div>
                  </div>
                  <div className="account-role-badge">
                    {acc.role === 'INSTRUCTOR' 
                      ? (language === 'vi' ? 'GV' : 'Teacher') 
                      : (language === 'vi' ? 'HS' : 'Student')
                    }
                  </div>
                </button>
              ))}
            </div>

            <button
              type="button"
              className="google-use-another-btn"
              onClick={() => setView('custom')}
              disabled={submitting}
            >
              <span className="plus-icon">+</span>
              {language === 'vi' ? 'Sử dụng một tài khoản khác' : 'Use another account'}
            </button>
          </div>
        ) : (
          <form className="google-custom-form" onSubmit={handleCustomSubmit}>
            <p className="google-list-header">
              {language === 'vi' ? 'Tạo tài khoản Google mới' : 'Create new Google account'}
            </p>

            <div className="form-group">
              <label className="form-label" htmlFor="google-fullname">
                {language === 'vi' ? 'Họ và tên' : 'Full Name'}
              </label>
              <input
                id="google-fullname"
                type="text"
                value={form.fullName}
                onChange={(e) => setForm(prev => ({ ...prev, fullName: e.target.value }))}
                placeholder={language === 'vi' ? 'Nguyễn Văn B' : 'John Doe'}
                required
                disabled={submitting}
              />
            </div>

            <div className="form-group">
              <label className="form-label" htmlFor="google-email">
                Email
              </label>
              <input
                id="google-email"
                type="email"
                value={form.email}
                onChange={(e) => setForm(prev => ({ ...prev, email: e.target.value }))}
                placeholder="user@gmail.com"
                required
                disabled={submitting}
              />
            </div>

            <RoleSelector
              selectedRole={form.role}
              onChange={(role) => setForm(prev => ({ ...prev, role }))}
            />

            <div className="google-form-actions">
              <button
                type="button"
                className="google-back-btn"
                onClick={() => setView('list')}
                disabled={submitting}
              >
                {language === 'vi' ? 'Quay lại' : 'Back'}
              </button>
              <button
                type="submit"
                className="google-next-btn"
                disabled={submitting}
              >
                {submitting ? (language === 'vi' ? 'Đang kết nối...' : 'Connecting...') : (language === 'vi' ? 'Tiếp theo' : 'Next')}
              </button>
            </div>
          </form>
        )}

        <div className="google-modal-footer">
          <span>
            {language === 'vi' 
              ? 'Để tiếp tục, Google sẽ chia sẻ tên, địa chỉ email, tùy chọn ngôn ngữ và ảnh hồ sơ của bạn với AI Teaching.' 
              : 'To continue, Google will share your name, email address, language preference, and profile picture with AI Teaching.'}
          </span>
        </div>
      </div>
    </div>
  );
}

export default GoogleChooserModal;
