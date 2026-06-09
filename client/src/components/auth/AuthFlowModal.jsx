import React, { useEffect, useRef } from 'react';
import { useLanguage } from '../../context/LanguageContext';
import { TbX, TbArrowRight, TbListCheck, TbFileCheck, TbTrophy, TbChartBar } from 'react-icons/tb';

function AuthFlowModal({ isOpen, onClose }) {
  const { t } = useLanguage();
  const closeButtonRef = useRef(null);

  useEffect(() => {
    const handleKeyDown = (e) => {
      if (e.key === 'Escape') {
        onClose();
      }
    };

    if (isOpen) {
      window.addEventListener('keydown', handleKeyDown);
      document.body.style.overflow = 'hidden';
      // accessibility: focus the close button when opened
      setTimeout(() => {
        closeButtonRef.current?.focus();
      }, 50);
    }

    return () => {
      window.removeEventListener('keydown', handleKeyDown);
      document.body.style.overflow = '';
    };
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  const handleBackdropClick = (e) => {
    if (e.target === e.currentTarget) {
      onClose();
    }
  };

  return (
    <div
      className={`modal-backdrop ${isOpen ? 'open' : ''}`}
      onClick={handleBackdropClick}
      role="presentation"
    >
      <div
        className="modal-box"
        role="dialog"
        aria-modal="true"
        aria-labelledby="flow-modal-title"
      >
        <button
          ref={closeButtonRef}
          className="modal-close"
          onClick={onClose}
          aria-label="Close modal"
          type="button"
        >
          <TbX />
        </button>
        <h2 id="flow-modal-title" className="modal-title">
          {t('auth.howItWorks.title')}
        </h2>
        <p className="modal-subtitle">
          {t('auth.howItWorks.subtitle')}
        </p>

        <div className="flow-grid">
          {/* Instructor Column */}
          <div className="flow-col teacher">
            <h3 className="flow-col-title">{t('auth.howItWorks.teacher.title')}</h3>
            <p className="flow-col-summary">{t('auth.howItWorks.teacher.summary')}</p>
            <div className="flow-step">
              <div className="flow-step-num">1</div>
              {t('auth.howItWorks.teacher.step1')}
            </div>
            <div className="flow-step">
              <div className="flow-step-num">2</div>
              {t('auth.howItWorks.teacher.step2')}
            </div>
            <div className="flow-step">
              <div className="flow-step-num">3</div>
              {t('auth.howItWorks.teacher.step3')}
            </div>
          </div>

          {/* Arrow */}
          <div className="flow-arrow" aria-hidden="true">
            <span className="flow-arrow-label">assign</span>
            <div className="flow-arrow-line">
              <TbArrowRight />
            </div>
          </div>

          {/* Classroom Center Column */}
          <div className="flow-col-center">
            <h3 className="flow-col-title">{t('auth.howItWorks.classroom.title')}</h3>
            <p className="flow-col-summary" dangerouslySetInnerHTML={{ __html: t('auth.howItWorks.classroom.summary') }} />
            <div className="module-grid">
              <div className="module-chip">
                <TbListCheck />
                <span>{t('auth.howItWorks.classroom.moduleQuestions')}</span>
              </div>
              <div className="module-chip">
                <TbFileCheck />
                <span>{t('auth.howItWorks.classroom.moduleTests')}</span>
              </div>
              <div className="module-chip">
                <TbTrophy />
                <span>{t('auth.howItWorks.classroom.moduleRank')}</span>
              </div>
              <div className="module-chip">
                <TbChartBar />
                <span>{t('auth.howItWorks.classroom.moduleProgress')}</span>
              </div>
            </div>
          </div>

          {/* Arrow */}
          <div className="flow-arrow" aria-hidden="true">
            <span className="flow-arrow-label">study</span>
            <div className="flow-arrow-line">
              <TbArrowRight />
            </div>
          </div>

          {/* Learner Column */}
          <div className="flow-col">
            <h3 className="flow-col-title">{t('auth.howItWorks.student.title')}</h3>
            <p className="flow-col-summary">{t('auth.howItWorks.student.summary')}</p>
            <div className="flow-step">
              <div className="flow-step-num">1</div>
              {t('auth.howItWorks.student.step1')}
            </div>
            <div className="flow-step">
              <div className="flow-step-num">2</div>
              {t('auth.howItWorks.student.step2')}
            </div>
            <div className="flow-step">
              <div className="flow-step-num">3</div>
              {t('auth.howItWorks.student.step3')}
            </div>
          </div>
        </div>

        <div className="flow-footer">
          <div className="flow-footer-main">{t('auth.howItWorks.footerMain')}</div>
          <div className="flow-footer-sub">{t('auth.howItWorks.footerSub')}</div>
        </div>
      </div>
    </div>
  );
}

export default AuthFlowModal;
