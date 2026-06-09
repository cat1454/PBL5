import React, { useState } from 'react';
import { useLanguage } from '../../context/LanguageContext';
import { TbEye, TbEyeOff } from 'react-icons/tb';

function PasswordField({
  label,
  value,
  onChange,
  id,
  autoComplete = 'current-password',
  placeholder = '••••••••••',
  onFocus,
  onBlur,
  error,
  helperText,
  required = true,
}) {
  const { language } = useLanguage();
  const [showPassword, setShowPassword] = useState(false);

  const toggleVisibility = () => {
    setShowPassword((prev) => !prev);
  };

  const ariaLabel = language === 'vi'
    ? (showPassword ? 'Ẩn mật khẩu' : 'Hiện mật khẩu')
    : (showPassword ? 'Hide password' : 'Show password');

  return (
    <div className="form-group">
      <label className="form-label" htmlFor={id}>
        {label}
      </label>
      <div className="pw-wrap">
        <input
          id={id}
          type={showPassword ? 'text' : 'password'}
          value={value}
          onChange={onChange}
          onFocus={onFocus}
          onBlur={onBlur}
          autoComplete={autoComplete}
          placeholder={placeholder}
          required={required}
          aria-invalid={!!error}
          aria-describedby={error ? `${id}-error` : helperText ? `${id}-helper` : undefined}
        />
        <button
          className="pw-toggle"
          type="button"
          onClick={toggleVisibility}
          aria-label={ariaLabel}
          title={ariaLabel}
        >
          {showPassword ? <TbEyeOff /> : <TbEye />}
        </button>
      </div>
      {/* Only render container if there is something to display, avoiding layout waste */}
      {(error || helperText) && (
        <div style={{ minHeight: '16px', marginTop: '2px' }}>
          {error ? (
            <span id={`${id}-error`} className="form-error-text" role="alert">
              {error}
            </span>
          ) : (
            <span id={`${id}-helper`} className="form-helper-text">
              {helperText}
            </span>
          )}
        </div>
      )}
    </div>
  );
}

export default PasswordField;
