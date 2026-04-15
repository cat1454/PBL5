import React, { useEffect, useState } from 'react';
import { documentService } from '../services/api';


function AnalysisContent({ data: initialData }) {
  const [fullData, setFullData] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!initialData?.id) return;

    const loadRealData = async () => {
      try {
        // Lấy danh sách để tìm data "xịn" có mainTopics, summary
        const docs = await documentService.getUserDocuments('demo-user');
        const matchedDoc = docs.find(d => d.id === initialData.id);

        // Nếu đã có dữ liệu phân tích (mainTopics) thì lưu lại và dừng loading
        if (matchedDoc && matchedDoc.mainTopics && matchedDoc.mainTopics.length > 0) {
          setFullData(matchedDoc);
          setLoading(false);
          return true; // Đã xong
        }
      } catch (err) {
        console.error("Lỗi lấy dữ liệu:", err);
      }
      return false; // Chưa có dữ liệu
    };

    // Chạy lần đầu
    loadRealData().then(isDone => {
      if (!isDone) {
        // Nếu chưa có data, cứ 3 giây hỏi lại 1 lần cho đến khi có thì thôi
        const interval = setInterval(async () => {
          const finished = await loadRealData();
          if (finished) clearInterval(interval);
        }, 3000);
        return () => clearInterval(interval);
      }
    });
  }, [initialData?.id]);

  // 1. Khi đang tải lần đầu
  if (loading && !fullData) {
    return (
      <div style={{ padding: '20px', textAlign: 'center' }}>
        <div className="spinner"></div>
        <p>⌛ Đang đợi AI phân tích nội dung... (Tự động cập nhật)</p>
      </div>
    );
  }

  // 2. Khi đã có dữ liệu, vẽ ra màn hình
  return (
    <div className="analysis-internal-view" style={{ padding: '20px' }}>
      {fullData.mainTopics && (
        <div className="analysis-section">
          <h3 style={{ color: '#6366f1' }}>🎯 Chu de chinh</h3>
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
          <h3 style={{ color: '#6366f1' }}>💡 Y chinh</h3>
          <ul style={{ marginTop: '10px', paddingLeft: '20px' }}>
            {fullData.keyPoints.map((point, index) => (
              <li key={index} style={{ marginBottom: '10px', color: '#334155' }}>{point}</li>
            ))}
          </ul>
        </div>
      )}

      {fullData.summary && (
        <div className="analysis-section" style={{ marginTop: '25px' }}>
          <h3 style={{ color: '#6366f1' }}>📝 Tom tat</h3>
          <p style={{ backgroundColor: '#f8fafc', padding: '15px', borderRadius: '8px', borderLeft: '4px solid #6366f1', marginTop: '10px', color: '#475569', lineHeight: '1.6' }}>
            {fullData.summary}
          </p>
        </div>
      )}
      {/* Thêm đoạn này vào cuối file AnalysisContent.js */}
      {fullData.extractedText && (
        <div className="analysis-section" style={{ marginTop: '25px' }}>
          <h3 style={{ color: '#6366f1' }}>📄 Van ban da trich xuat</h3>
          <div style={{
            backgroundColor: '#f1f5f9',
            padding: '15px',
            borderRadius: '8px',
            marginTop: '10px',
            maxHeight: '300px',
            overflowY: 'auto',
            fontSize: '14px',
            whiteSpace: 'pre-wrap'
          }}>
            {fullData.extractedText}
          </div>
        </div>
      )}
    </div>
  );
}

export default AnalysisContent;