import React, { useEffect, useMemo, useState } from 'react';
import { useLanguage } from '../context/LanguageContext';
import { documentService, getApiErrorMessage } from '../services/api';

const TAB_AI = 'ai';
const TAB_OCR = 'ocr';

function hasExtractedTextField(document) {
  return Object.prototype.hasOwnProperty.call(document || {}, 'extractedText');
}

function getWordCount(text) {
  const trimmed = text.trim();
  return trimmed ? trimmed.split(/\s+/).length : 0;
}

function AnalysisModal({ document, onClose }) {
  const { t } = useLanguage();
  const [activeTab, setActiveTab] = useState(TAB_AI);
  const [fullDocument, setFullDocument] = useState(document);
  const [ocrLoading, setOcrLoading] = useState(false);
  const [ocrError, setOcrError] = useState('');
  const [copyState, setCopyState] = useState('idle');

  useEffect(() => {
    setActiveTab(TAB_AI);
    setFullDocument(document);
    setOcrLoading(false);
    setOcrError('');
    setCopyState('idle');
  }, [document]);

  useEffect(() => {
    if (activeTab !== TAB_OCR || !fullDocument?.id || hasExtractedTextField(fullDocument)) {
      return undefined;
    }

    let cancelled = false;

    const loadDocument = async () => {
      setOcrLoading(true);
      setOcrError('');

      try {
        const loadedDocument = await documentService.getDocument(fullDocument.id);
        if (!cancelled) {
          setFullDocument((current) => ({
            ...current,
            ...loadedDocument,
          }));
        }
      } catch (err) {
        if (!cancelled) {
          setOcrError(getApiErrorMessage(err, t('analysis.ocrLoadError')));
        }
      } finally {
        if (!cancelled) {
          setOcrLoading(false);
        }
      }
    };

    loadDocument();

    return () => {
      cancelled = true;
    };
  }, [activeTab, fullDocument, t]);

  const extractedText = typeof fullDocument?.extractedText === 'string' ? fullDocument.extractedText : '';
  const textStats = useMemo(() => ({
    characters: extractedText.length,
    words: getWordCount(extractedText),
  }), [extractedText]);

  const handleCopy = async () => {
    if (!extractedText || !navigator.clipboard) {
      return;
    }

    try {
      await navigator.clipboard.writeText(extractedText);
      setCopyState('copied');
      window.setTimeout(() => setCopyState('idle'), 1600);
    } catch {
      setCopyState('failed');
      window.setTimeout(() => setCopyState('idle'), 2200);
    }
  };

  if (!document) {
    return null;
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-content analysis-modal" onClick={(event) => event.stopPropagation()}>
        <div className="modal-header">
          <h2>{t('analysis.modalTitle', { fileName: document.fileName })}</h2>
          <button className="close-btn" onClick={onClose} aria-label={t('analysis.close')}>x</button>
        </div>

        <div className="analysis-modal-tabs" role="tablist" aria-label={t('analysis.tabsLabel')}>
          <button
            type="button"
            role="tab"
            aria-selected={activeTab === TAB_AI}
            className={`analysis-modal-tab${activeTab === TAB_AI ? ' active' : ''}`}
            onClick={() => setActiveTab(TAB_AI)}
          >
            {t('analysis.aiTab')}
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={activeTab === TAB_OCR}
            className={`analysis-modal-tab${activeTab === TAB_OCR ? ' active' : ''}`}
            onClick={() => setActiveTab(TAB_OCR)}
          >
            {t('analysis.ocrTab')}
          </button>
        </div>

        <div className="modal-body analysis-modal-body">
          {activeTab === TAB_AI ? (
            <>
              {fullDocument.summary && (
                <div className="analysis-section">
                  <h3>{t('analysis.summary')}</h3>
                  <p className="summary-text">{fullDocument.summary}</p>
                </div>
              )}

              {fullDocument.mainTopics && fullDocument.mainTopics.length > 0 && (
                <div className="analysis-section">
                  <h3>{t('analysis.topics')}</h3>
                  <div className="topics-list">
                    {fullDocument.mainTopics.map((topic, index) => (
                      <span key={`${topic}-${index}`} className="topic-tag">{topic}</span>
                    ))}
                  </div>
                </div>
              )}

              {fullDocument.keyPoints && fullDocument.keyPoints.length > 0 && (
                <div className="analysis-section">
                  <h3>{t('analysis.keyPoints')}</h3>
                  <ul className="key-points-list">
                    {fullDocument.keyPoints.map((point, index) => (
                      <li key={`${point}-${index}`}>{point}</li>
                    ))}
                  </ul>
                </div>
              )}

              {fullDocument.language && (
                <div className="analysis-section">
                  <h3>{t('analysis.language')}</h3>
                  <p><strong>{fullDocument.language}</strong></p>
                </div>
              )}
            </>
          ) : (
            <div className="analysis-ocr-panel">
              <div className="analysis-ocr-toolbar">
                <div className="analysis-ocr-stats">
                  <span>{t('analysis.characterCount', { count: textStats.characters })}</span>
                  <span>{t('analysis.wordCount', { count: textStats.words })}</span>
                </div>
                <button
                  type="button"
                  className="button button-secondary analysis-copy-btn"
                  onClick={handleCopy}
                  disabled={!extractedText || ocrLoading}
                >
                  {copyState === 'copied'
                    ? t('analysis.copied')
                    : copyState === 'failed'
                      ? t('analysis.copyFailed')
                      : t('analysis.copy')}
                </button>
              </div>

              {ocrLoading ? (
                <div className="analysis-ocr-state">
                  <div className="spinner"></div>
                  <p>{t('analysis.ocrLoading')}</p>
                </div>
              ) : ocrError ? (
                <div className="analysis-ocr-state analysis-ocr-error">{ocrError}</div>
              ) : extractedText.trim() ? (
                <div className="analysis-ocr-text">{extractedText}</div>
              ) : (
                <div className="analysis-ocr-state">{t('analysis.ocrEmpty')}</div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

export default AnalysisModal;
