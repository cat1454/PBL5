import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { slideService, workspaceService } from '../services/api';
import { buildSlideImageViewModel } from '../services/slideImages';
import { formatEta, getProgressCounterLabel, isActiveProgress, normalizeProgressState } from '../services/progress';
import { useLanguage } from '../context/LanguageContext';

const DEMO_USER = 'demo-user';

const DEFAULT_BRIEF = {
  desiredSlideCount: 8,
  themeKey: 'editorial-sunrise',
  audience: 'Sinh vien va nguoi hoc',
  tone: 'Ro rang, hien dai, de nho',
  narrativeGoal: 'Tong hop cac y chinh de tao mot deck giang day ngan gon, de doc, de sua.',
  languageStyle: 'Tieng Viet ngan gon, chuyen nghiep, de doc tren slide',
};

const FONT_OPTIONS = ['Georgia', 'Trebuchet MS', 'Segoe UI', 'Palatino Linotype', 'Courier New'];
const FONT_SIZES = [14, 16, 18, 20, 24, 28, 32, 36];
const THEME_OPTIONS = [
  { value: 'editorial-sunrise', label: 'Editorial Sunrise' },
  { value: 'paper-mint', label: 'Paper Mint' },
  { value: 'cobalt-grid', label: 'Cobalt Grid' },
  { value: 'midnight-signal', label: 'Midnight Signal' },
];
const AUDIENCE_OPTIONS = [
  'Sinh vien va nguoi hoc',
  'Giao vien / nguoi thuyet trinh',
  'Nguoi moi bat dau',
  'Quan ly / lanh dao',
];
const TONE_OPTIONS = [
  'Ro rang, hien dai, de nho',
  'Hoc thuat nhung de tiep thu',
  'Tu tin, co nhan manh',
  'Kich thich tri to mo',
];
const LANGUAGE_STYLE_OPTIONS = [
  'Tieng Viet ngan gon, chuyen nghiep, de doc tren slide',
  'Tieng Viet than thien, de doc tren web',
  'Tieng Viet hoc thuat, co cau truc',
  'Tieng Viet thuyet trinh, nhan y manh',
];

function createFallbackEditorState(item) {
  return item?.editorState || {
    layoutVariant: 'standard',
    title: { text: item?.heading || '', fontFamily: 'Georgia', fontSize: 28, bold: true, italic: false, underline: false, align: 'left', bullet: false },
    subtitle: { text: item?.subheading || '', fontFamily: 'Segoe UI', fontSize: 16, bold: false, italic: false, underline: false, align: 'left', bullet: false },
    goal: { text: item?.goal || '', fontFamily: 'Segoe UI', fontSize: 14, bold: true, italic: false, underline: false, align: 'left', bullet: false },
    body: { text: Array.isArray(item?.bodyBlocks) ? item.bodyBlocks.join('\n') : '', fontFamily: 'Segoe UI', fontSize: 18, bold: false, italic: false, underline: false, align: 'left', bullet: true },
    notes: { text: item?.speakerNotes || '', fontFamily: 'Segoe UI', fontSize: 14, bold: false, italic: false, underline: false, align: 'left', bullet: false },
  };
}

function cloneDraft(draft) {
  return JSON.parse(JSON.stringify(draft));
}

function normalizeStatusLabel(status) {
  switch (status) {
    case 0:
      return 'Uploaded';
    case 1:
      return 'Extracting';
    case 2:
      return 'Analyzing';
    case 3:
      return 'Completed';
    case 4:
      return 'Failed';
    default:
      return 'Unknown';
  }
}

function applyTextStyle(block = {}) {
  return {
    fontFamily: block.fontFamily || 'Segoe UI',
    fontSize: `${block.fontSize || 18}px`,
    fontWeight: block.bold ? 700 : 400,
    fontStyle: block.italic ? 'italic' : 'normal',
    textDecoration: block.underline ? 'underline' : 'none',
    textAlign: block.align || 'left',
  };
}

function FolderStudio() {
  const { t, language } = useLanguage();
  const { workspaceId } = useParams();
  const navigate = useNavigate();
  const fileInputRef = useRef(null);

  const [folder, setFolder] = useState(null);
  const [sources, setSources] = useState([]);
  const [deck, setDeck] = useState(null);
  const [drafts, setDrafts] = useState({});
  const [history, setHistory] = useState({});
  const [selectedSlideId, setSelectedSlideId] = useState(null);
  const [activeField, setActiveField] = useState('body');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [feedback, setFeedback] = useState('');
  const [jobId, setJobId] = useState(null);
  const [progress, setProgress] = useState(null);
  const [mediaOpen, setMediaOpen] = useState(false);
  const [mediaBusy, setMediaBusy] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [brief, setBrief] = useState(DEFAULT_BRIEF);
  const [filterText, setFilterText] = useState('');
  const audienceLabels = t('slides.options.audiences');
  const toneLabels = t('slides.options.tones');
  const languageStyleLabels = t('slides.options.languageStyles');
  const slideTitlePlaceholder = language === 'vi' ? 'Tiêu đề slide' : 'Slide title';
  const slideGoalPlaceholder = language === 'vi' ? 'Mục tiêu / take-away của slide' : 'Slide goal / take-away';
  const bodyPlaceholder = language === 'vi' ? 'Mỗi dòng tương ứng một bullet hoặc một ý chính.' : 'Each line becomes one bullet or one key point.';
  const notesPlaceholder = language === 'vi' ? 'Ghi chú thuyết trình, script, nhắc nhở...' : 'Speaker notes, script, reminders...';

  const formatRelativeTimeLabel = (value) => {
    if (!value) {
      return '-';
    }

    const diffMs = Date.now() - new Date(value).getTime();
    if (diffMs < 60_000) {
      return language === 'vi' ? 'vừa cập nhật' : 'just updated';
    }
    if (diffMs < 3_600_000) {
      const count = Math.max(1, Math.floor(diffMs / 60_000));
      return language === 'vi' ? `${count} phút trước` : `${count} minutes ago`;
    }
    if (diffMs < 86_400_000) {
      const count = Math.max(1, Math.floor(diffMs / 3_600_000));
      return language === 'vi' ? `${count} giờ trước` : `${count} hours ago`;
    }

    return new Date(value).toLocaleString();
  };

  const loadWorkspace = useCallback(async ({ silent = false } = {}) => {
    if (!silent) {
      setLoading(true);
    }

    try {
      setError('');
      const [folderData, sourceData, deckData] = await Promise.all([
        workspaceService.get(workspaceId),
        workspaceService.listSources(workspaceId),
        slideService.getDeckByFolder(workspaceId),
      ]);

      setFolder(folderData);
      setSources(Array.isArray(sourceData) ? sourceData : []);
      setDeck(deckData || null);

      if (deckData?.generationProgress) {
        const nextProgress = normalizeProgressState(deckData.generationProgress);
        setProgress(nextProgress);
        if (nextProgress.jobId) {
          setJobId(nextProgress.jobId);
        }
      } else {
        setProgress(null);
        setJobId(null);
      }

      if (deckData?.outline?.brief) {
        setBrief((current) => ({
          desiredSlideCount: deckData.outline.brief.desiredSlideCount || current.desiredSlideCount,
          themeKey: deckData.outline.brief.themeKey || current.themeKey,
          audience: deckData.outline.brief.audience || current.audience,
          tone: deckData.outline.brief.tone || current.tone,
          narrativeGoal: deckData.outline.brief.narrativeGoal || current.narrativeGoal,
          languageStyle: deckData.outline.brief.languageStyle || current.languageStyle,
        }));
      }
    } catch (err) {
      console.error(err);
      setError(language === 'vi' ? 'Không tải được workspace studio.' : 'Could not load the workspace studio.');
    } finally {
      if (!silent) {
        setLoading(false);
      }
    }
  }, [language, workspaceId]);

  useEffect(() => {
    loadWorkspace();
  }, [loadWorkspace]);

  useEffect(() => {
    if (!deck?.items?.length) {
      setSelectedSlideId(null);
      return;
    }

    setSelectedSlideId((current) => (
      deck.items.some((item) => item.id === current) ? current : deck.items[0].id
    ));
  }, [deck]);

  useEffect(() => {
    if (!deck?.items?.length) {
      return;
    }

    setDrafts((current) => {
      const next = { ...current };
      deck.items.forEach((item) => {
        if (!next[item.id]) {
          next[item.id] = createFallbackEditorState(item);
        }
      });
      return next;
    });
  }, [deck]);

  useEffect(() => {
    const hasRunningSources = sources.some((source) => source.processingProgress && isActiveProgress(source.processingProgress));
    const hasRunningDeck = progress && isActiveProgress(progress);

    if (!hasRunningSources && !hasRunningDeck) {
      return undefined;
    }

    const interval = setInterval(async () => {
      try {
        if (jobId) {
          const nextProgress = normalizeProgressState(await slideService.getGenerateProgress(jobId), progress);
          setProgress(nextProgress);
        }
        await loadWorkspace({ silent: true });
      } catch (err) {
        console.error(err);
      }
    }, 1800);

    return () => clearInterval(interval);
  }, [jobId, loadWorkspace, progress, sources]);

  const selectedReadySources = useMemo(
    () => sources.filter((source) => source.status === 3 && (source.includeInWorkspaceSlides ?? source.includeInFolderSlides)),
    [sources]
  );

  const selectedSlide = deck?.items?.find((item) => item.id === selectedSlideId) || deck?.items?.[0] || null;
  const selectedDraft = selectedSlide ? (drafts[selectedSlide.id] || createFallbackEditorState(selectedSlide)) : null;
  const selectedImageVm = selectedSlide ? buildSlideImageViewModel(selectedSlide) : null;
  const activeProgress = progress || (deck?.generationProgress ? normalizeProgressState(deck.generationProgress) : null);

  const pushHistory = (slideId, previousDraft) => {
    setHistory((current) => {
      const previous = current[slideId] || { past: [], future: [] };
      return {
        ...current,
        [slideId]: {
          past: [...previous.past, cloneDraft(previousDraft)].slice(-30),
          future: [],
        },
      };
    });
  };

  const mutateDraft = (slideId, updater, { trackHistory = true } = {}) => {
    setDrafts((current) => {
      const base = current[slideId] || createFallbackEditorState(deck?.items?.find((item) => item.id === slideId));
      if (trackHistory) {
        pushHistory(slideId, base);
      }

      return {
        ...current,
        [slideId]: updater(cloneDraft(base)),
      };
    });
  };

  const handleFieldTextChange = (fieldKey, value) => {
    if (!selectedSlide) {
      return;
    }

    mutateDraft(selectedSlide.id, (draft) => {
      draft[fieldKey].text = value;
      return draft;
    });
  };

  const handleStyleChange = (updater) => {
    if (!selectedSlide || !selectedDraft) {
      return;
    }

    mutateDraft(selectedSlide.id, (draft) => {
      draft[activeField] = updater({ ...draft[activeField] });
      return draft;
    });
  };

  const handleUndo = () => {
    if (!selectedSlide) {
      return;
    }

    setHistory((current) => {
      const state = current[selectedSlide.id];
      if (!state?.past?.length) {
        return current;
      }

      const previousDraft = state.past[state.past.length - 1];
      const currentDraft = drafts[selectedSlide.id] || createFallbackEditorState(selectedSlide);

      setDrafts((draftState) => ({
        ...draftState,
        [selectedSlide.id]: cloneDraft(previousDraft),
      }));

      return {
        ...current,
        [selectedSlide.id]: {
          past: state.past.slice(0, -1),
          future: [cloneDraft(currentDraft), ...(state.future || [])].slice(0, 30),
        },
      };
    });
  };

  const handleRedo = () => {
    if (!selectedSlide) {
      return;
    }

    setHistory((current) => {
      const state = current[selectedSlide.id];
      if (!state?.future?.length) {
        return current;
      }

      const nextDraft = state.future[0];
      const currentDraft = drafts[selectedSlide.id] || createFallbackEditorState(selectedSlide);

      setDrafts((draftState) => ({
        ...draftState,
        [selectedSlide.id]: cloneDraft(nextDraft),
      }));

      return {
        ...current,
        [selectedSlide.id]: {
          past: [...(state.past || []), cloneDraft(currentDraft)].slice(-30),
          future: state.future.slice(1),
        },
      };
    });
  };

  const handleSaveSlide = async () => {
    if (!deck || !selectedSlide || !selectedDraft) {
      return;
    }

    try {
      setError('');
      const updated = await slideService.updateSlideItem(deck.id, selectedSlide.id, {
        heading: selectedDraft.title.text,
        subheading: selectedDraft.subtitle.text,
        goal: selectedDraft.goal.text,
        bodyBlocks: selectedDraft.body.text.split('\n').map((line) => line.trim()).filter(Boolean),
        speakerNotes: selectedDraft.notes.text,
        accentTone: selectedSlide.accentTone || '',
        editorState: selectedDraft,
      });

      setDeck((current) => ({
        ...current,
        items: current.items.map((item) => (item.id === updated.id ? updated : item)),
      }));
      setDrafts((current) => ({
        ...current,
        [updated.id]: createFallbackEditorState(updated),
      }));
      setHistory((current) => ({
        ...current,
        [updated.id]: { past: [], future: [] },
      }));
      setFeedback(language === 'vi' ? `Đã lưu slide ${updated.slideIndex}.` : `Saved slide ${updated.slideIndex}.`);
    } catch (err) {
      console.error(err);
      setError(language === 'vi' ? 'Không lưu được slide hiện tại.' : 'Could not save the current slide.');
    }
  };

  const handleUploadClick = () => {
    fileInputRef.current?.click();
  };

  const handleSourceUpload = async (event) => {
    const files = Array.from(event.target.files || []);
    event.target.value = '';

    if (!files.length) {
      return;
    }

    try {
      setUploading(true);
      setError('');
      setFeedback(language === 'vi' ? `Đang đưa ${files.length} nguồn vào workspace...` : `Adding ${files.length} sources to the workspace...`);

      for (const file of files) {
        await workspaceService.uploadSource(workspaceId, file, DEMO_USER);
      }

      setFeedback(language === 'vi' ? `Đã thêm ${files.length} nguồn vào workspace.` : `Added ${files.length} sources to the workspace.`);
      await loadWorkspace({ silent: true });
    } catch (err) {
      console.error(err);
      setError(language === 'vi' ? 'Không upload được source cho workspace này.' : 'Could not upload sources for this workspace.');
    } finally {
      setUploading(false);
    }
  };

  const toggleSourceSelection = async (source) => {
    try {
      setError('');
      await workspaceService.updateSourceSelection(
        workspaceId,
        source.id,
        !(source.includeInWorkspaceSlides ?? source.includeInFolderSlides)
      );
      setFeedback(
        !(source.includeInWorkspaceSlides ?? source.includeInFolderSlides)
          ? (language === 'vi' ? `Đã đưa ${source.fileName} vào tập nguồn sinh slide.` : `Added ${source.fileName} to the slide source set.`)
          : (language === 'vi' ? `Đã bỏ ${source.fileName} khỏi tập nguồn sinh slide.` : `Removed ${source.fileName} from the slide source set.`)
      );
      await loadWorkspace({ silent: true });
    } catch (err) {
      console.error(err);
      setError(language === 'vi' ? 'Không cập nhật được trạng thái chọn nguồn.' : 'Could not update the source selection state.');
    }
  };

  const handleGenerateDeck = async () => {
    if (!selectedReadySources.length) {
      setError(language === 'vi' ? 'Cần ít nhất 1 source đã Completed và được chọn cho slide.' : 'At least one completed source must be selected for slide generation.');
      return;
    }

    try {
      setError('');
      setFeedback(language === 'vi' ? 'Đang tạo deck mới từ các source đã chọn...' : 'Generating a new deck from the selected sources...');
      const response = await slideService.startGenerateSlidesForFolder(workspaceId, brief);
      setJobId(response.jobId || response.progress?.jobId || null);
      setProgress(normalizeProgressState(response.progress, {
        jobId: response.jobId,
        status: response.status,
        stageLabel: language === 'vi' ? 'Chờ xử lý' : 'Queued',
        message: language === 'vi' ? 'Đã tạo job sinh slide cấp workspace' : 'Workspace slide generation job created',
      }));
      await loadWorkspace({ silent: true });
    } catch (err) {
      console.error(err);
      setError(language === 'vi' ? 'Không bắt đầu được quá trình sinh slide cấp workspace.' : 'Could not start workspace slide generation.');
    }
  };

  const handleRefreshImages = async () => {
    if (!deck || !selectedSlide) {
      return;
    }

    try {
      setMediaBusy(true);
      setError('');
      const updated = await slideService.refreshSlideItemImages(deck.id, selectedSlide.id);
      setDeck((current) => ({
        ...current,
        items: current.items.map((item) => (item.id === updated.id ? updated : item)),
      }));
      setMediaOpen(true);
      setFeedback(language === 'vi' ? `Đã làm mới image candidates cho slide ${updated.slideIndex}.` : `Refreshed image candidates for slide ${updated.slideIndex}.`);
    } catch (err) {
      console.error(err);
      setError(language === 'vi' ? 'Không refresh được image candidates cho slide này.' : 'Could not refresh image candidates for this slide.');
    } finally {
      setMediaBusy(false);
    }
  };

  const handleSelectImage = async (candidateKey) => {
    if (!deck || !selectedSlide) {
      return;
    }

    try {
      setMediaBusy(true);
      setError('');
      const updated = await slideService.selectSlideItemImage(deck.id, selectedSlide.id, candidateKey);
      setDeck((current) => ({
        ...current,
        items: current.items.map((item) => (item.id === updated.id ? updated : item)),
      }));
      setFeedback(language === 'vi' ? `Đã chọn ảnh cho slide ${updated.slideIndex}.` : `Selected an image for slide ${updated.slideIndex}.`);
    } catch (err) {
      console.error(err);
      setError(language === 'vi' ? 'Không chọn được image candidate này.' : 'Could not select this image candidate.');
    } finally {
      setMediaBusy(false);
    }
  };

  const handleDeleteFolder = async () => {
    if (!folder || !window.confirm(language === 'vi' ? 'Xóa workspace này và toàn bộ source bên trong?' : 'Delete this workspace and all sources inside it?')) {
      return;
    }

    try {
      await workspaceService.remove(folder.id);
      navigate('/workspaces');
    } catch (err) {
      console.error(err);
      setError(language === 'vi' ? 'Không xóa được workspace.' : 'Could not delete the workspace.');
    }
  };

  const notifySoon = (label) => {
    setError('');
    setFeedback(language === 'vi' ? `${label} đã được đặt sẵn trong UI, mình sẽ nối backend flow ở phase tiếp theo.` : `${label} is scaffolded in the UI and can be wired to backend flow in the next phase.`);
  };

  const slideItems = deck?.items || [];
  const normalizedFilter = filterText.trim().toLowerCase();
  const filteredSources = normalizedFilter
    ? sources.filter((source) => [source.fileName, source.summary]
      .filter(Boolean)
      .some((value) => String(value).toLowerCase().includes(normalizedFilter)))
    : sources;
  const runningSource = sources.find((source) => isActiveProgress(source.processingProgress));
  const topbarProgress = activeProgress && isActiveProgress(activeProgress)
    ? activeProgress
    : runningSource?.processingProgress || null;
  const topbarCounter = getProgressCounterLabel(topbarProgress);
  const topbarEta = formatEta(topbarProgress?.estimatedRemainingSeconds);
  const activeFieldState = selectedDraft?.[activeField] || null;
  const activeHistory = selectedSlide ? (history[selectedSlide.id] || { past: [], future: [] }) : { past: [], future: [] };
  const selectedImage = selectedImageVm?.selectedImage || null;
  const canGenerate = selectedReadySources.length > 0 && !(activeProgress && isActiveProgress(activeProgress));
  const qualityIssues = selectedSlide?.quality?.issues || [];
  const qualityScore = selectedSlide?.quality?.score;
  const topbarMeta = [
    language === 'vi' ? `${sources.length} nguồn` : `${sources.length} sources`,
    language === 'vi' ? `${selectedReadySources.length} source được chọn` : `${selectedReadySources.length} selected sources`,
    deck?.items?.length ? `${deck.items.length} ${language === 'vi' ? 'slide' : 'slides'}` : (language === 'vi' ? 'Chưa có deck' : 'No deck yet'),
    `${language === 'vi' ? 'Cập nhật' : 'Updated'}: ${formatRelativeTimeLabel(deck?.updatedAt || folder?.updatedAt)}`,
  ];

  if (loading) {
    return (
      <div className="loading">
        <div className="spinner"></div>
        <p>{language === 'vi' ? 'Đang tải workspace studio...' : 'Loading workspace studio...'}</p>
      </div>
    );
  }

  if (!folder) {
    return (
      <div className="card folder-studio-missing">
        <h2>{language === 'vi' ? 'Không tìm thấy workspace' : 'Workspace not found'}</h2>
        <p>{language === 'vi' ? 'Workspace này có thể đã bị xóa hoặc chưa được khởi tạo.' : 'This workspace may have been deleted or not initialized yet.'}</p>
        <button type="button" className="button" onClick={() => navigate('/workspaces')}>
          {t('slides.back')}
        </button>
      </div>
    );
  }

  return (
    <div className="folder-studio-page">
      <input
        ref={fileInputRef}
        type="file"
        multiple
        hidden
        onChange={handleSourceUpload}
        accept=".pdf,.doc,.docx,.ppt,.pptx,.png,.jpg,.jpeg,.webp,.txt"
      />

      {error && <div className="alert alert-error">{error}</div>}
      {feedback && <div className="alert alert-info">{feedback}</div>}

      <section className="folder-studio-shell">
        <div className="folder-studio-topbar">
          <button type="button" className="folder-studio-mini-btn" onClick={() => navigate('/workspaces')}>
            &lt;
          </button>

          <div className="folder-studio-topbar-copy">
            <strong>{folder.name}</strong>
            <div className="folder-studio-topbar-meta">
              {topbarMeta.map((item) => (
                <span key={item}>{item}</span>
              ))}
              {topbarProgress && (
                <span className="folder-studio-live">
                  Live: {topbarProgress.stageLabel || topbarProgress.message || (language === 'vi' ? 'Đang xử lý' : 'Processing')}
                  {topbarCounter ? ` | ${topbarCounter}` : ''}
                  {topbarEta ? ` | ETA ${topbarEta}` : ''}
                </span>
              )}
            </div>
          </div>

          <div className="folder-studio-topbar-actions">
            <button type="button" className="folder-studio-mini-btn" onClick={() => loadWorkspace()} disabled={uploading}>
              {language === 'vi' ? 'Làm mới' : 'Refresh'}
            </button>
            <button type="button" className="folder-studio-mini-btn" onClick={handleUploadClick} disabled={uploading}>
              {uploading ? (language === 'vi' ? 'Đang thêm...' : 'Adding...') : (language === 'vi' ? 'Thêm nguồn' : 'Add source')}
            </button>
            <div className="folder-studio-avatar">GV</div>
            <a
              className={`folder-studio-mini-primary${!deck ? ' is-disabled' : ''}`}
              href={deck ? slideService.getFolderDeckHtmlUrl(workspaceId) : undefined}
              target={deck ? '_blank' : undefined}
              rel={deck ? 'noreferrer' : undefined}
              onClick={(event) => {
                if (!deck) {
                  event.preventDefault();
                }
              }}
            >
              HTML / PDF
            </a>
          </div>
        </div>

        <div className="folder-studio-main">
          <aside className="folder-studio-sidebar">
            <div className="folder-studio-panel-title">{language === 'vi' ? 'Nguồn / Slides' : 'Sources / Slides'}</div>

            <div className="folder-studio-filter">
              <input
                type="text"
                value={filterText}
                onChange={(event) => setFilterText(event.target.value)}
                placeholder={language === 'vi' ? 'Tìm trong tên file hoặc summary' : 'Search by file name or summary'}
              />
              <button type="button" className="folder-studio-mini-btn" onClick={() => setFilterText('')}>
                x
              </button>
            </div>

            <div className="folder-studio-sidebar-cta">
              <button type="button" className="folder-studio-side-button" onClick={handleUploadClick} disabled={uploading}>
                {language === 'vi' ? '+ Thêm source vào workspace' : '+ Add source to workspace'}
              </button>
            </div>

            <div className="folder-studio-section-label">{language === 'vi' ? 'Nguồn tài liệu' : 'Document sources'}</div>
            <div className="folder-studio-source-list">
              {filteredSources.length === 0 && (
                <div className="folder-studio-empty-sidebar">
                  {language === 'vi' ? 'Chưa có source nào trong workspace này.' : 'No sources in this workspace yet.'}
                </div>
              )}

              {filteredSources.map((source) => {
                const isSelected = Boolean(source.includeInWorkspaceSlides ?? source.includeInFolderSlides);
                const isReady = source.status === 3;
                const tone = String(source.fileType || '').includes('pdf')
                  ? 'pdf'
                  : String(source.fileType || '').includes('doc')
                    ? 'doc'
                    : String(source.fileType || '').includes('image')
                      ? 'image'
                      : 'file';
                const progressState = source.processingProgress
                  ? normalizeProgressState(source.processingProgress)
                  : null;
                const showLive = isActiveProgress(progressState);

                return (
                  <div key={source.id} className={`folder-studio-source-item${isSelected ? ' selected' : ''}`}>
                    <div className={`folder-studio-source-icon tone-${tone}`}>
                      {String(source.fileType || '').slice(0, 3).toUpperCase()}
                    </div>
                    <div className="folder-studio-source-copy">
                      <p title={source.fileName}>{source.fileName}</p>
                      <div className="folder-studio-source-meta">
                        <span className={`folder-studio-source-badge tone-${isReady ? 'completed' : showLive ? 'active' : source.status === 4 ? 'failed' : 'uploaded'}`}>
                          {showLive ? `${Math.round(progressState.percent || 0)}%` : normalizeStatusLabel(source.status)}
                        </span>
                        <span>{isSelected ? (language === 'vi' ? 'Đã chọn cho deck' : 'Selected for deck') : (language === 'vi' ? 'Chưa đưa vào deck' : 'Not in deck yet')}</span>
                      </div>
                      {showLive && (
                        <div className="folder-studio-source-live">
                          {progressState.stageLabel || progressState.message || (language === 'vi' ? 'Đang xử lý' : 'Processing')}
                          {progressState.estimatedRemainingSeconds ? ` | ${formatEta(progressState.estimatedRemainingSeconds)}` : ''}
                        </div>
                      )}
                    </div>
                    <button
                      type="button"
                      className={`folder-studio-pick-btn${isSelected ? ' active' : ''}`}
                      onClick={() => toggleSourceSelection(source)}
                      disabled={!isReady}
                    >
                      {isSelected ? (language === 'vi' ? 'Bỏ' : 'Remove') : (language === 'vi' ? 'Chọn' : 'Select')}
                    </button>
                  </div>
                );
              })}
            </div>

            <div className="folder-studio-section-label">{language === 'vi' ? 'Cấu trúc slide' : 'Slide structure'}</div>
            <div className="folder-studio-flow-list">
              {!slideItems.length && (
                <div className="folder-studio-empty-sidebar">
                  {language === 'vi' ? 'Chưa có deck. Chọn source xong rồi bấm "Tạo slide mới từ nội dung".' : 'No deck yet. Select sources and click "Generate slides from content".'}
                </div>
              )}

              {slideItems.map((item) => (
                <button
                  key={item.id}
                  type="button"
                  className={`folder-studio-flow-item${item.id === selectedSlideId ? ' active' : ''}`}
                  onClick={() => setSelectedSlideId(item.id)}
                >
                  <div className="folder-studio-flow-thumb">
                    <span className="w-80"></span>
                    <span className="w-60"></span>
                    <span className="w-42"></span>
                  </div>
                  <div className="folder-studio-flow-copy">
                    <p>{t('slides.slideLabel', { index: item.slideIndex })}: {item.heading || 'Untitled'}</p>
                    <span>{item.goal || item.subheading || item.slideType}</span>
                  </div>
                  <span className={`folder-studio-flow-state${item.status === 'Ready' ? ' ready' : item.status === 'Generating' ? ' working' : ''}`}>
                    {item.status}
                  </span>
                </button>
              ))}
            </div>
          </aside>

          <section className="folder-studio-center">
            <div className="folder-studio-toolbar">
              <select
                value={activeFieldState?.fontFamily || 'Segoe UI'}
                onChange={(event) => handleStyleChange((block) => ({ ...block, fontFamily: event.target.value }))}
                disabled={!selectedDraft}
              >
                {FONT_OPTIONS.map((option) => (
                  <option key={option} value={option}>{option}</option>
                ))}
              </select>

              <select
                value={activeFieldState?.fontSize || 18}
                onChange={(event) => handleStyleChange((block) => ({ ...block, fontSize: Number(event.target.value) }))}
                disabled={!selectedDraft}
              >
                {FONT_SIZES.map((option) => (
                  <option key={option} value={option}>{option}</option>
                ))}
              </select>

              <div className="folder-studio-toolbar-sep"></div>
              <button type="button" className={`folder-studio-toolbar-btn${activeFieldState?.bold ? ' active' : ''}`} onClick={() => handleStyleChange((block) => ({ ...block, bold: !block.bold }))} disabled={!selectedDraft}>B</button>
              <button type="button" className={`folder-studio-toolbar-btn${activeFieldState?.italic ? ' active' : ''}`} onClick={() => handleStyleChange((block) => ({ ...block, italic: !block.italic }))} disabled={!selectedDraft}>I</button>
              <button type="button" className={`folder-studio-toolbar-btn${activeFieldState?.underline ? ' active' : ''}`} onClick={() => handleStyleChange((block) => ({ ...block, underline: !block.underline }))} disabled={!selectedDraft}>U</button>
              <div className="folder-studio-toolbar-sep"></div>
              {['left', 'center', 'right'].map((align) => (
                <button
                  key={align}
                  type="button"
                  className={`folder-studio-toolbar-btn${activeFieldState?.align === align ? ' active' : ''}`}
                  onClick={() => handleStyleChange((block) => ({ ...block, align }))}
                  disabled={!selectedDraft}
                >
                  {align === 'left' ? 'L' : align === 'center' ? 'C' : 'R'}
                </button>
              ))}
              <button
                type="button"
                className={`folder-studio-toolbar-btn${activeFieldState?.bullet ? ' active' : ''}`}
                onClick={() => handleStyleChange((block) => ({ ...block, bullet: !block.bullet }))}
                disabled={!selectedDraft || activeField !== 'body'}
              >
                *
              </button>
              <div className="folder-studio-toolbar-sep"></div>
              <button type="button" className="folder-studio-toolbar-btn" onClick={handleUndo} disabled={!activeHistory.past.length}>{language === 'vi' ? 'Hoàn tác' : 'Undo'}</button>
              <button type="button" className="folder-studio-toolbar-btn" onClick={handleRedo} disabled={!activeHistory.future.length}>{language === 'vi' ? 'Làm lại' : 'Redo'}</button>
              <button type="button" className="folder-studio-toolbar-btn" onClick={() => setMediaOpen((current) => !current)} disabled={!selectedSlide}>
                {mediaOpen ? (language === 'vi' ? 'Ẩn media' : 'Hide media') : (language === 'vi' ? 'Mở media' : 'Open media')}
              </button>
            </div>

            <div className="folder-studio-canvas">
              {!selectedSlide || !selectedDraft ? (
                <div className="folder-studio-empty">
                  <h3>{language === 'vi' ? 'Workspace studio sẵn sàng' : 'Workspace studio is ready'}</h3>
                  <p>
                    {language === 'vi' ? 'Upload nhiều source vào workspace, chọn các source đã Completed, sau đó sinh deck để bắt đầu chỉnh sửa.' : 'Upload multiple sources, select completed ones, then generate a deck to start editing.'}
                  </p>
                  <div className="folder-studio-empty-actions">
                    <button type="button" className="folder-studio-mini-primary" onClick={handleUploadClick}>
                      {language === 'vi' ? 'Thêm nguồn' : 'Add source'}
                    </button>
                    <button type="button" className="folder-studio-mini-btn" onClick={handleGenerateDeck} disabled={!canGenerate}>
                      {language === 'vi' ? 'Tạo deck' : 'Generate deck'}
                    </button>
                  </div>
                </div>
              ) : (
                <>
                  <article className="folder-slide-card">
                    <div className="folder-slide-layout">
                      <div className="folder-slide-copy">
                        <div className={`folder-editable-block${activeField === 'title' ? ' active' : ''}`} onClick={() => setActiveField('title')}>
                          <textarea
                            rows={2}
                            value={selectedDraft.title.text}
                            onFocus={() => setActiveField('title')}
                            onChange={(event) => handleFieldTextChange('title', event.target.value)}
                            className="folder-slide-title-input"
                            style={applyTextStyle(selectedDraft.title)}
                            placeholder={slideTitlePlaceholder}
                          />
                        </div>

                        <div className={`folder-editable-block${activeField === 'subtitle' ? ' active' : ''}`} onClick={() => setActiveField('subtitle')}>
                          <textarea
                            rows={2}
                            value={selectedDraft.subtitle.text}
                            onFocus={() => setActiveField('subtitle')}
                            onChange={(event) => handleFieldTextChange('subtitle', event.target.value)}
                            className="folder-slide-subtitle-input"
                            style={applyTextStyle(selectedDraft.subtitle)}
                            placeholder="Subheading / context"
                          />
                        </div>

                        <div className={`folder-editable-block tone-soft${activeField === 'goal' ? ' active' : ''}`} onClick={() => setActiveField('goal')}>
                          <textarea
                            rows={2}
                            value={selectedDraft.goal.text}
                            onFocus={() => setActiveField('goal')}
                            onChange={(event) => handleFieldTextChange('goal', event.target.value)}
                            className="folder-slide-goal-input"
                            style={applyTextStyle(selectedDraft.goal)}
                            placeholder={slideGoalPlaceholder}
                          />
                        </div>

                        <div className={`folder-editable-block${activeField === 'body' ? ' active' : ''}`} onClick={() => setActiveField('body')}>
                          <textarea
                            rows={8}
                            value={selectedDraft.body.text}
                            onFocus={() => setActiveField('body')}
                            onChange={(event) => handleFieldTextChange('body', event.target.value)}
                            className="folder-slide-body-input"
                            style={applyTextStyle(selectedDraft.body)}
                            placeholder={bodyPlaceholder}
                          />
                          <small>
                            {selectedDraft.body.bullet
                              ? (language === 'vi' ? 'Bullet mode đang bật: mỗi dòng sẽ được lưu thành 1 body block.' : 'Bullet mode is on: each line will be saved as one body block.')
                              : (language === 'vi' ? 'Đang ở text mode: nội dung vẫn được lưu theo từng dòng.' : 'Text mode is on: content is still saved line by line.')}
                          </small>
                        </div>
                      </div>

                      <div className="folder-slide-visual">
                        {selectedImage?.localAssetUrl ? (
                          <img src={selectedImage.localAssetUrl} alt={selectedImage.altText || 'Selected media'} />
                        ) : (
                          <div className="folder-slide-visual-placeholder">
                            <span>{selectedImageVm?.badgeLabel || 'Media pending'}</span>
                            <strong>{selectedImageVm?.statusLabel || (language === 'vi' ? 'Chưa có preview' : 'No preview yet')}</strong>
                            <p>{selectedImageVm?.helperText || (language === 'vi' ? 'Media pipeline sẽ nối sau khi nội dung ổn định.' : 'The media pipeline can continue once the content is stable.')}</p>
                          </div>
                        )}
                        <div className="folder-slide-visual-meta">
                          <span>{selectedImageVm?.badgeLabel || (language === 'vi' ? 'Chưa có media' : 'No media')}</span>
                          <strong>{selectedImage?.provider || 'Folder visual slot'}</strong>
                          <small>{selectedImageVm?.attributionText || (language === 'vi' ? 'Có thể refresh để lấy image candidates mới.' : 'You can refresh to get new image candidates.')}</small>
                        </div>
                      </div>
                    </div>

                    <div className="folder-slide-hint">
                      <span>AI</span>
                      <p>
                        {qualityIssues[0]
                          || selectedImageVm?.helperText
                          || (language === 'vi' ? 'Deck workspace này đang cho phép sửa title, subtitle, body, notes và chọn image tương ứng.' : 'This workspace deck currently supports editing title, subtitle, body, notes, and the selected image.')}
                        {typeof qualityScore === 'number' ? (language === 'vi' ? ` Điểm verifier hiện tại: ${qualityScore}.` : ` Current verifier score: ${qualityScore}.`) : ''}
                      </p>
                    </div>
                  </article>

                  <div className="folder-studio-panels">
                    <section className="folder-studio-panel-card">
                      <div className="folder-studio-panel-card-head">
                        <strong>{language === 'vi' ? 'Speaker notes' : 'Speaker notes'}</strong>
                        <span>{t('slides.slideLabel', { index: selectedSlide.slideIndex })}</span>
                      </div>
                      <textarea
                        rows={5}
                        value={selectedDraft.notes.text}
                        onFocus={() => setActiveField('notes')}
                        onChange={(event) => handleFieldTextChange('notes', event.target.value)}
                        className={`folder-slide-notes-input${activeField === 'notes' ? ' active' : ''}`}
                        style={applyTextStyle(selectedDraft.notes)}
                        placeholder={notesPlaceholder}
                      />
                    </section>

                    <section className="folder-studio-panel-card">
                      <div className="folder-studio-panel-card-head">
                        <strong>{language === 'vi' ? 'Dải media' : 'Media strip'}</strong>
                        <div className="folder-studio-inline-actions">
                          <button type="button" className="folder-studio-mini-btn" onClick={handleRefreshImages} disabled={mediaBusy}>
                            {mediaBusy ? (language === 'vi' ? 'Đang refresh...' : 'Refreshing...') : (language === 'vi' ? 'Làm mới ảnh' : 'Refresh images')}
                          </button>
                          <button type="button" className="folder-studio-mini-btn" onClick={() => setMediaOpen((current) => !current)}>
                            {mediaOpen ? (language === 'vi' ? 'Thu gọn' : 'Collapse') : (language === 'vi' ? 'Mở' : 'Open')}
                          </button>
                        </div>
                      </div>

                      {mediaOpen ? (
                        <>
                          <p className="folder-studio-media-copy">
                            {selectedImageVm?.helperText || (language === 'vi' ? 'Chưa có image payload cho slide này.' : 'No image payload for this slide yet.')}
                          </p>
                          <div className="folder-studio-media-grid">
                            {selectedImageVm?.candidates?.length ? selectedImageVm.candidates.map((candidate) => (
                              <button
                                type="button"
                                key={candidate.key}
                                className={`folder-studio-media-card${candidate.isSelected ? ' active' : ''}`}
                                onClick={() => handleSelectImage(candidate.key)}
                                disabled={mediaBusy}
                              >
                                {candidate.thumbnailUrl ? (
                                  <img src={candidate.thumbnailUrl} alt={candidate.altText || 'Media candidate'} />
                                ) : (
                                  <div className="folder-studio-media-fallback">No preview</div>
                                )}
                                <div className="folder-studio-media-meta">
                                  <strong>{candidate.provider || candidate.sourceType}</strong>
                                  <span>{candidate.licenseLabel || candidate.sourceType}</span>
                                </div>
                              </button>
                            )) : (
                              <div className="folder-studio-empty-sidebar">
                                {language === 'vi' ? 'Chưa có candidate nào. Bấm "Làm mới ảnh" để tạo / tải lại media candidates.' : 'No candidates yet. Click "Refresh images" to generate or reload media candidates.'}
                              </div>
                            )}
                          </div>
                        </>
                      ) : (
                        <p className="folder-studio-media-copy">
                          {language === 'vi' ? 'Media strip đang thu gọn. Mở ra để đổi image, chọn candidate hoặc refresh media workflow.' : 'The media strip is collapsed. Open it to swap images, pick a candidate, or refresh the media workflow.'}
                        </p>
                      )}
                    </section>
                  </div>
                </>
              )}
            </div>
          </section>

          <aside className="folder-studio-rpanel">
            <div className="folder-studio-panel-title">{language === 'vi' ? 'Studio / Hành động' : 'Studio / Actions'}</div>

            <div className="folder-studio-action-section">
              <div className="folder-studio-section-label">{language === 'vi' ? 'Tạo mới' : 'Create'}</div>
              <button type="button" className="folder-studio-action tone-primary" onClick={handleGenerateDeck} disabled={!canGenerate}>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Tạo slide mới từ nội dung' : 'Generate slides from content'}</strong>
                  <span>{language === 'vi' ? `${selectedReadySources.length} source ready đang được chọn cho workspace` : `${selectedReadySources.length} ready sources selected for this workspace`}</span>
                </span>
                <span className="folder-studio-action-badge">AI</span>
              </button>
              <button type="button" className="folder-studio-action" onClick={() => notifySoon(language === 'vi' ? 'Tạo câu hỏi ôn tập' : 'Generate review questions')}>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Tạo câu hỏi ôn tập' : 'Generate review questions'}</strong>
                  <span>{language === 'vi' ? 'Entry point cho flow question generation cấp workspace' : 'Entry point for workspace-level question generation'}</span>
                </span>
                <span className="folder-studio-action-badge">Soon</span>
              </button>
              <button type="button" className="folder-studio-action" onClick={() => notifySoon(language === 'vi' ? 'Mở Quiz tương tác' : 'Open interactive quiz')}>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Mở Quiz tương tác' : 'Open interactive quiz'}</strong>
                  <span>{language === 'vi' ? 'Đặt sẵn để nối workspace deck với game flow sau này' : 'Scaffolded for future workspace-to-game flow'}</span>
                </span>
                <span className="folder-studio-action-badge">Soon</span>
              </button>
              <button type="button" className="folder-studio-action" onClick={() => notifySoon(language === 'vi' ? 'Mở Flashcards' : 'Open flashcards')}>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Mở Flashcards' : 'Open flashcards'}</strong>
                  <span>{language === 'vi' ? 'Cho phép tạo flashcards từ tập source đã chọn trong workspace' : 'Allow flashcards from the selected workspace sources'}</span>
                </span>
                <span className="folder-studio-action-badge">Soon</span>
              </button>
            </div>

            <div className="folder-studio-action-section">
              <div className="folder-studio-section-label">{t('slides.deckBrief')}</div>
              <label className="folder-studio-form-row">
                <span>{t('slides.desiredSlides')}</span>
                <input
                  type="number"
                  min="5"
                  max="12"
                  value={brief.desiredSlideCount}
                  onChange={(event) => setBrief((current) => ({ ...current, desiredSlideCount: Number(event.target.value) || 8 }))}
                />
              </label>
              <label className="folder-studio-form-row">
                <span>Theme</span>
                <select value={brief.themeKey} onChange={(event) => setBrief((current) => ({ ...current, themeKey: event.target.value }))}>
                  {THEME_OPTIONS.map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                  ))}
                </select>
              </label>
              <label className="folder-studio-form-row">
                <span>{t('slides.audience')}</span>
                <select value={brief.audience} onChange={(event) => setBrief((current) => ({ ...current, audience: event.target.value }))}>
                  {AUDIENCE_OPTIONS.map((option, index) => (
                    <option key={option} value={option}>{audienceLabels[index] || option}</option>
                  ))}
                </select>
              </label>
              <label className="folder-studio-form-row">
                <span>{t('slides.tone')}</span>
                <select value={brief.tone} onChange={(event) => setBrief((current) => ({ ...current, tone: event.target.value }))}>
                  {TONE_OPTIONS.map((option, index) => (
                    <option key={option} value={option}>{toneLabels[index] || option}</option>
                  ))}
                </select>
              </label>
              <label className="folder-studio-form-row">
                <span>{t('slides.languageStyle')}</span>
                <select value={brief.languageStyle} onChange={(event) => setBrief((current) => ({ ...current, languageStyle: event.target.value }))}>
                  {LANGUAGE_STYLE_OPTIONS.map((option, index) => (
                    <option key={option} value={option}>{languageStyleLabels[index] || option}</option>
                  ))}
                </select>
              </label>
              <label className="folder-studio-form-row">
                <span>{t('slides.narrativeGoal')}</span>
                <textarea
                  rows={4}
                  value={brief.narrativeGoal}
                  onChange={(event) => setBrief((current) => ({ ...current, narrativeGoal: event.target.value }))}
                  placeholder={language === 'vi' ? 'Mục tiêu cấu trúc và nhấn mạnh cho deck workspace này' : 'Structure and emphasis goal for this workspace deck'}
                />
              </label>
            </div>

            <div className="folder-studio-action-section">
              <div className="folder-studio-section-label">{language === 'vi' ? 'Phân tích & Tóm tắt' : 'Analysis & Summary'}</div>
              <button type="button" className="folder-studio-action" onClick={() => notifySoon(language === 'vi' ? 'Tóm tắt nội dung' : 'Summarize content')}>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Tóm tắt nội dung' : 'Summarize content'}</strong>
                  <span>{language === 'vi' ? 'Tổng hợp summary cấp workspace từ các source đã chọn' : 'Build a workspace-level summary from selected sources'}</span>
                </span>
                <span className="folder-studio-action-badge">Soon</span>
              </button>
              <button type="button" className="folder-studio-action" onClick={() => notifySoon(language === 'vi' ? 'Phân tích ý chính' : 'Analyze key ideas')}>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Phân tích ý chính' : 'Analyze key ideas'}</strong>
                  <span>{language === 'vi' ? 'Đặt sẵn cho luồng concept extraction cấp workspace' : 'Scaffold for workspace-level concept extraction'}</span>
                </span>
                <span className="folder-studio-action-badge">Soon</span>
              </button>
              <button type="button" className="folder-studio-action" onClick={() => notifySoon(language === 'vi' ? 'Xây dựng sơ đồ tư duy' : 'Build a mind map')}>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Xây dựng sơ đồ tư duy' : 'Build a mind map'}</strong>
                  <span>{language === 'vi' ? 'Nối vào luồng mindmap trong phase tiếp theo' : 'Reserved for the next-phase mindmap flow'}</span>
                </span>
                <span className="folder-studio-action-badge">Soon</span>
              </button>
            </div>

            <div className="folder-studio-action-section">
              <div className="folder-studio-section-label">{language === 'vi' ? 'Xuất bản & Chia sẻ' : 'Publish & Share'}</div>
              <button type="button" className="folder-studio-action" onClick={handleSaveSlide} disabled={!selectedDraft}>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Lưu slide hiện tại' : 'Save current slide'}</strong>
                  <span>{language === 'vi' ? 'Persist editorState, body blocks và notes vào deck' : 'Persist editorState, body blocks, and notes into the deck'}</span>
                </span>
                <span className="folder-studio-action-badge">Save</span>
              </button>
              <a
                className={`folder-studio-action${!deck ? ' is-disabled' : ''}`}
                href={deck ? slideService.getFolderDeckHtmlUrl(workspaceId) : undefined}
                target={deck ? '_blank' : undefined}
                rel={deck ? 'noreferrer' : undefined}
                onClick={(event) => {
                  if (!deck) {
                    event.preventDefault();
                  }
                }}
              >
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Tải xuống HTML / PDF' : 'Download HTML / PDF'}</strong>
                  <span>{language === 'vi' ? 'Xuất deck cấp workspace để preview hoặc in PDF từ browser' : 'Export the workspace deck for preview or browser-based PDF printing'}</span>
                </span>
                <span className="folder-studio-action-badge">Export</span>
              </a>
              <button type="button" className="folder-studio-action" onClick={() => notifySoon(language === 'vi' ? 'Xuất PowerPoint' : 'Export PowerPoint')}>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Xuất PowerPoint' : 'Export PowerPoint'}</strong>
                  <span>{language === 'vi' ? 'Cho phase xuất file pptx sau này' : 'Reserved for a future PPTX export phase'}</span>
                </span>
                <span className="folder-studio-action-badge">Soon</span>
              </button>
              <button type="button" className="folder-studio-action" onClick={() => notifySoon(language === 'vi' ? 'Chia sẻ liên kết' : 'Share link')}>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Chia sẻ liên kết' : 'Share link'}</strong>
                  <span>{language === 'vi' ? 'Đặt sẵn cho shareable review link' : 'Scaffold for a shareable review link'}</span>
                </span>
                <span className="folder-studio-action-badge">Soon</span>
              </button>
            </div>

            <div className="folder-studio-action-section">
              <div className="folder-studio-section-label">{language === 'vi' ? 'Quản trị' : 'Admin'}</div>
              <button type="button" className="folder-studio-action" onClick={() => loadWorkspace()}>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Làm mới dữ liệu' : 'Refresh data'}</strong>
                  <span>{language === 'vi' ? 'Tải lại sources, deck, progress và metadata của workspace' : 'Reload sources, deck, progress, and workspace metadata'}</span>
                </span>
                <span className="folder-studio-action-badge">Sync</span>
              </button>
              <button type="button" className="folder-studio-action tone-danger" onClick={handleDeleteFolder}>
                <span className="folder-studio-action-copy">
                  <strong>{language === 'vi' ? 'Xóa workspace' : 'Delete workspace'}</strong>
                  <span>{language === 'vi' ? 'Thao tác này sẽ xóa workspace và các source bên trong' : 'This action will delete the workspace and all sources inside it'}</span>
                </span>
                <span className="folder-studio-action-badge">Delete</span>
              </button>
            </div>
          </aside>
        </div>
      </section>
    </div>
  );
}

export default FolderStudio;
