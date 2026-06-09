import React from 'react';
import { useLanguage } from '../../context/LanguageContext';
import { TbCheck, TbSchool, TbPresentation } from 'react-icons/tb';

function RoleSelector({ selectedRole, onChange }) {
  const { t, language } = useLanguage();

  const options = [
    {
      value: 'LEARNER',
      name: t('auth.roles.LEARNER'),
      desc: language === 'vi'
        ? 'Vào lớp, làm bài và xem kết quả'
        : 'Join classes, take tests, and view results',
      icon: <TbSchool />,
      iconClass: 'student',
    },
    {
      value: 'INSTRUCTOR',
      name: t('auth.roles.INSTRUCTOR'),
      desc: language === 'vi'
        ? 'Tạo học liệu, giao bài và theo dõi lớp'
        : 'Create source materials, assign tests, and track class progress',
      icon: <TbPresentation />,
      iconClass: 'teacher',
    },
  ];

  return (
    <div className="role-section">
      <span className="role-label" id="role-selector-label">
        {t('auth.common.role')}
      </span>
      <div
        className="role-grid"
        role="radiogroup"
        aria-labelledby="role-selector-label"
      >
        {options.map((opt) => {
          const isSelected = selectedRole === opt.value;
          return (
            <button
              key={opt.value}
              type="button"
              role="radio"
              aria-checked={isSelected}
              className={`role-card ${isSelected ? 'selected' : ''}`}
              onClick={() => onChange(opt.value)}
              onKeyDown={(e) => {
                if (e.key === ' ' || e.key === 'Enter') {
                  e.preventDefault();
                  onChange(opt.value);
                }
              }}
              style={{ display: 'block', width: '100%' }}
            >
              <div className="role-check" aria-hidden="true">
                <TbCheck />
              </div>
              <div className={`role-icon ${opt.iconClass}`} aria-hidden="true">
                {opt.icon}
              </div>
              <div className="role-name">{opt.name}</div>
              <div className="role-desc">{opt.desc}</div>
            </button>
          );
        })}
      </div>
    </div>
  );
}

export default RoleSelector;
