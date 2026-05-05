import React, { useEffect, useState } from 'react';
import { useAuth } from '../context/AuthContext';
import { documentService } from '../services/api';
import { useLanguage } from '../context/LanguageContext';

function AnalysisContent({ data: initialData }) {
  const { currentUser } = useAuth();
  const { t } = useLanguage();
  const [fullData, setFullData] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!initialData?.id) {
      return undefined;
    }

    let intervalId;

    const loadRealData = async () => {
      try {
        const docs = await documentService.getUserDocuments(String(currentUser?.id || ''));
        const matchedDoc = docs.find((doc) => doc.id === initialData.id);

        if (matchedDoc && matchedDoc.mainTopics && matchedDoc.mainTopics.length > 0) {
          setFullData(matchedDoc);
          setLoading(false);
          return true;
        }
      } catch (err) {
        console.error('Failed to load analysis data:', err);
      }
      return false;
    };

    loadRealData().then((isDone) => {
      if (!isDone) {
        intervalId = setInterval(async () => {
          const finished = await loadRealData();
          if (finished) {
            clearInterval(intervalId);
          }
        }, 3000);
      }
    });

    return () => {
      if (intervalId) {
        clearInterval(intervalId);
      }
    };
  }, [currentUser?.id, initialData?.id]);

  if (loading && !fullData) {
    return (
      <div style={{ padding: '20px', textAlign: 'center' }}>
        <div className="spinner"></div>
        <p>{t('analysis.loading')}</p>
      </div>
    );
  }

  return (
    <div className="analysis-internal-view" style={{ padding: '20px' }}>
      {fullData.mainTopics && (
        <div className="analysis-section">
          <h3 style={{ color: '#6366f1' }}>{t('analysis.topics')}</h3>
          <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap', marginTop: '10px' }}>
            {fullData.mainTopics.map((topic, index) => (
              <span key={index} style={{ backgroundColor: '#6366f1', color: 'white', padding: '5px 12px', borderRadius: '15px', fontSize: '14px' }}>
                {topic}
              </span>
            ))}
          </div>
        </div>
      )}

      {fullData.keyPoints && (
        <div className="analysis-section" style={{ marginTop: '25px' }}>
          <h3 style={{ color: '#6366f1' }}>{t('analysis.keyPoints')}</h3>
          <ul style={{ marginTop: '10px', paddingLeft: '20px' }}>
            {fullData.keyPoints.map((point, index) => (
              <li key={index} style={{ marginBottom: '10px', color: '#334155' }}>{point}</li>
            ))}
          </ul>
        </div>
      )}

      {fullData.summary && (
        <div className="analysis-section" style={{ marginTop: '25px' }}>
          <h3 style={{ color: '#6366f1' }}>{t('analysis.summary')}</h3>
          <p style={{ backgroundColor: '#f8fafc', padding: '15px', borderRadius: '8px', borderLeft: '4px solid #6366f1', marginTop: '10px', color: '#475569', lineHeight: '1.6' }}>
            {fullData.summary}
          </p>
        </div>
      )}

      {fullData.extractedText && (
        <div className="analysis-section" style={{ marginTop: '25px' }}>
          <h3 style={{ color: '#6366f1' }}>{t('analysis.extractedText')}</h3>
          <div
            style={{
              backgroundColor: '#f1f5f9',
              padding: '15px',
              borderRadius: '8px',
              marginTop: '10px',
              maxHeight: '300px',
              overflowY: 'auto',
              fontSize: '14px',
              whiteSpace: 'pre-wrap',
            }}
          >
            {fullData.extractedText}
          </div>
        </div>
      )}
    </div>
  );
}

export default AnalysisContent;
