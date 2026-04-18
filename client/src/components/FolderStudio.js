import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { folderService, slideService } from '../services/api';
import { buildSlideImageViewModel } from '../services/slideImages';
import { formatEta, getProgressCounterLabel, isActiveProgress, normalizeProgressState } from '../services/progress';

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

function formatRelativeTime(value) {
  if (!value) {
    return '-';
  }

  const diffMs = Date.now() - new Date(value).getTime();
  if (diffMs < 60_000) {
    return 'vua cap nhat';
  }
  if (diffMs < 3_600_000) {
    return `${Math.max(1, Math.floor(diffMs / 60_000))} phut truoc`;
  }
  if (diffMs < 86_400_000) {
    return `${Math.max(1, Math.floor(diffMs / 3_600_000))} gio truoc`;
  }

  return new Date(value).toLocaleString();
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
  const { folderId } = useParams();
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

  const loadWorkspace = useCallback(async ({ silent = false } = {}) => {
    if (!silent) {
      setLoading(true);
    }

    try {
      setError('');
      const [folderData, sourceData, deckData] = await Promise.all([
        folderService.getFolder(folderId),
        folderService.getSources(folderId),
        slideService.getDeckByFolder(folderId),
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
      setError('Khong tai duoc folder studio.');
    } finally {
      if (!silent) {
        setLoading(false);
      }
    }
  }, [folderId]);

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
    () => sources.filter((source) => source.status === 3 && source.includeInFolderSlides),
    [sources]
  );

  const selectedSlide = deck?.items?.find((item) => item.id === selectedSlideId) || deck?.items?.[0] || null;
  const selectedDraft = selectedSlide ? (drafts[selectedSlide.id] || createFallbackEditorState(selectedSlide)) : null;
  const selectedImageVm = selectedSlide ? buildSlideImageViewModel(selectedSlide) : null;
  const activeProgress = progress || normalizeProgressState(deck?.generationProgress || null);

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
      setFeedback(`Da luu slide ${updated.slideIndex}.`);
    } catch (err) {
      console.error(err);
      setError('Khong luu duoc slide hien tai.');
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
      setFeedback(`Dang dua ${files.length} nguon vao folder...`);

      for (const file of files) {
        await folderService.uploadSource(folderId, file, DEMO_USER);
      }

      setFeedback(`Da them ${files.length} nguon vao folder.`);
      await loadWorkspace({ silent: true });
    } catch (err) {
      console.error(err);
      setError('Khong upload duoc nguon cho folder nay.');
    } finally {
      setUploading(false);
    }
  };

  const toggleSourceSelection = async (source) => {
    try {
      setError('');
      await folderService.updateSourceSelection(folderId, source.id, !source.includeInFolderSlides);
      setFeedback(
        !source.includeInFolderSlides
          ? `Da dua ${source.fileName} vao tap nguon sinh slide.`
          : `Da bo ${source.fileName} khoi tap nguon sinh slide.`
      );
      await loadWorkspace({ silent: true });
    } catch (err) {
      console.error(err);
      setError('Khong cap nhat duoc trang thai chon nguon.');
    }
  };

  const handleGenerateDeck = async () => {
    if (!selectedReadySources.length) {
      setError('Can it nhat 1 source da Completed va duoc chon cho slide.');
      return;
    }

    try {
      setError('');
      setFeedback('Dang tao deck moi tu cac source da chon...');
      const response = await slideService.startGenerateSlidesForFolder(folderId, brief);
      setJobId(response.jobId || response.progress?.jobId || null);
      setProgress(normalizeProgressState(response.progress, {
        jobId: response.jobId,
        status: response.status,
        stageLabel: 'Cho xu ly',
        message: 'Da tao job sinh slide cap folder',
      }));
      await loadWorkspace({ silent: true });
    } catch (err) {
      console.error(err);
      setError('Khong bat dau duoc qua trinh sinh slide cap folder.');
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
      setFeedback(`Da lam moi image candidates cho slide ${updated.slideIndex}.`);
    } catch (err) {
      console.error(err);
      setError('Khong refresh duoc image candidates cho slide nay.');
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
      setFeedback(`Da chon anh cho slide ${updated.slideIndex}.`);
    } catch (err) {
      console.error(err);
      setError('Khong chon duoc image candidate nay.');
    } finally {
      setMediaBusy(false);
    }
  };

  const handleDeleteFolder = async () => {
    if (!folder || !window.confirm('Xoa folder project nay va toan bo source ben trong?')) {
      return;
    }

    try {
      await folderService.deleteFolder(folder.id);
      navigate('/folders');
    } catch (err) {
      console.error(err);
      setError('Khong xoa duoc folder project.');
    }
  };

  const notifySoon = (label) => {
    setError('');
    setFeedback(`${label} da duoc dat san trong UI, minh se noi backend flow o phase tiep theo.`);
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
    `${sources.length} nguon`,
    `${selectedReadySources.length} source duoc chon`,
    deck?.items?.length ? `${deck.items.length} slide` : 'Chua co deck',
    `Cap nhat: ${formatRelativeTime(deck?.updatedAt || folder?.updatedAt)}`,
  ];

  if (loading) {
    return (
      <div className="loading">
        <div className="spinner"></div>
        <p>Dang tai folder studio...</p>
      </div>
    );
  }

  if (!folder) {
    return (
      <div className="card folder-studio-missing">
        <h2>Khong tim thay folder project</h2>
        <p>Folder nay co the da bi xoa hoac chua duoc khoi tao.</p>
        <button type="button" className="button" onClick={() => navigate('/folders')}>
          Quay ve Folder Projects
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
          <button type="button" className="folder-studio-mini-btn" onClick={() => navigate('/folders')}>
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
                  Live: {topbarProgress.stageLabel || topbarProgress.message || 'Dang xu ly'}
                  {topbarCounter ? ` | ${topbarCounter}` : ''}
                  {topbarEta ? ` | ETA ${topbarEta}` : ''}
                </span>
              )}
            </div>
          </div>

          <div className="folder-studio-topbar-actions">
            <button type="button" className="folder-studio-mini-btn" onClick={() => loadWorkspace()} disabled={uploading}>
              Lam moi
            </button>
            <button type="button" className="folder-studio-mini-btn" onClick={handleUploadClick} disabled={uploading}>
              {uploading ? 'Dang them...' : 'Them nguon'}
            </button>
            <div className="folder-studio-avatar">GV</div>
            <a
              className={`folder-studio-mini-primary${!deck ? ' is-disabled' : ''}`}
              href={deck ? slideService.getFolderDeckHtmlUrl(folderId) : undefined}
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
            <div className="folder-studio-panel-title">Nguon / Slides</div>

            <div className="folder-studio-filter">
              <input
                type="text"
                value={filterText}
                onChange={(event) => setFilterText(event.target.value)}
                placeholder="Tim trong ten file hoac summary"
              />
              <button type="button" className="folder-studio-mini-btn" onClick={() => setFilterText('')}>
                x
              </button>
            </div>

            <div className="folder-studio-sidebar-cta">
              <button type="button" className="folder-studio-side-button" onClick={handleUploadClick} disabled={uploading}>
                + Them nguon rieng cho folder
              </button>
            </div>

            <div className="folder-studio-section-label">Nguon tai lieu</div>
            <div className="folder-studio-source-list">
              {filteredSources.length === 0 && (
                <div className="folder-studio-empty-sidebar">
                  Chua co nguon nao trong folder nay.
                </div>
              )}

              {filteredSources.map((source) => {
                const isSelected = Boolean(source.includeInFolderSlides);
                const isReady = source.status === 3;
                const tone = String(source.fileType || '').includes('pdf')
                  ? 'pdf'
                  : String(source.fileType || '').includes('doc')
                    ? 'doc'
                    : String(source.fileType || '').includes('image')
                      ? 'image'
                      : 'file';
                const progressState = normalizeProgressState(source.processingProgress || null);
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
                        <span>{isSelected ? 'Da chon cho deck' : 'Chua dua vao deck'}</span>
                      </div>
                      {showLive && (
                        <div className="folder-studio-source-live">
                          {progressState.stageLabel || progressState.message || 'Dang xu ly'}
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
                      {isSelected ? 'Bo' : 'Chon'}
                    </button>
                  </div>
                );
              })}
            </div>

            <div className="folder-studio-section-label">Cau truc slide</div>
            <div className="folder-studio-flow-list">
              {!slideItems.length && (
                <div className="folder-studio-empty-sidebar">
                  Chua co deck. Chon source xong roi bam "Tao slide moi tu noi dung".
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
                    <p>Slide {item.slideIndex}: {item.heading || 'Untitled'}</p>
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
              <button type="button" className="folder-studio-toolbar-btn" onClick={handleUndo} disabled={!activeHistory.past.length}>Undo</button>
              <button type="button" className="folder-studio-toolbar-btn" onClick={handleRedo} disabled={!activeHistory.future.length}>Redo</button>
              <button type="button" className="folder-studio-toolbar-btn" onClick={() => setMediaOpen((current) => !current)} disabled={!selectedSlide}>
                {mediaOpen ? 'An media' : 'Mo media'}
              </button>
            </div>

            <div className="folder-studio-canvas">
              {!selectedSlide || !selectedDraft ? (
                <div className="folder-studio-empty">
                  <h3>Folder studio san sang</h3>
                  <p>
                    Upload nhieu nguon vao folder, chon cac source da Completed, sau do sinh deck de bat dau chinh sua.
                  </p>
                  <div className="folder-studio-empty-actions">
                    <button type="button" className="folder-studio-mini-primary" onClick={handleUploadClick}>
                      Them nguon
                    </button>
                    <button type="button" className="folder-studio-mini-btn" onClick={handleGenerateDeck} disabled={!canGenerate}>
                      Tao deck
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
                            placeholder="Tieu de slide"
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
                            placeholder="Muc tieu / take-away cua slide"
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
                            placeholder="Moi dong tuong ung mot bullet hoac mot y chinh."
                          />
                          <small>
                            {selectedDraft.body.bullet
                              ? 'Bullet mode dang bat: moi dong se duoc luu thanh 1 body block.'
                              : 'Dang o text mode: noi dung van duoc luu theo tung dong.'}
                          </small>
                        </div>
                      </div>

                      <div className="folder-slide-visual">
                        {selectedImage?.localAssetUrl ? (
                          <img src={selectedImage.localAssetUrl} alt={selectedImage.altText || 'Selected media'} />
                        ) : (
                          <div className="folder-slide-visual-placeholder">
                            <span>{selectedImageVm?.badgeLabel || 'Media pending'}</span>
                            <strong>{selectedImageVm?.statusLabel || 'Chua co preview'}</strong>
                            <p>{selectedImageVm?.helperText || 'Media pipeline se noi sau khi noi dung on dinh.'}</p>
                          </div>
                        )}
                        <div className="folder-slide-visual-meta">
                          <span>{selectedImageVm?.badgeLabel || 'No media'}</span>
                          <strong>{selectedImage?.provider || 'Folder visual slot'}</strong>
                          <small>{selectedImageVm?.attributionText || 'Co the refresh de lay image candidates moi.'}</small>
                        </div>
                      </div>
                    </div>

                    <div className="folder-slide-hint">
                      <span>AI</span>
                      <p>
                        {qualityIssues[0]
                          || selectedImageVm?.helperText
                          || 'Deck folder nay dang cho phep sua title, subtitle, body, notes va chon image tuong ung.'}
                        {typeof qualityScore === 'number' ? ` Diem verifier hien tai: ${qualityScore}.` : ''}
                      </p>
                    </div>
                  </article>

                  <div className="folder-studio-panels">
                    <section className="folder-studio-panel-card">
                      <div className="folder-studio-panel-card-head">
                        <strong>Speaker notes</strong>
                        <span>Slide {selectedSlide.slideIndex}</span>
                      </div>
                      <textarea
                        rows={5}
                        value={selectedDraft.notes.text}
                        onFocus={() => setActiveField('notes')}
                        onChange={(event) => handleFieldTextChange('notes', event.target.value)}
                        className={`folder-slide-notes-input${activeField === 'notes' ? ' active' : ''}`}
                        style={applyTextStyle(selectedDraft.notes)}
                        placeholder="Ghi chu thuyet trinh, script, nhac nho..."
                      />
                    </section>

                    <section className="folder-studio-panel-card">
                      <div className="folder-studio-panel-card-head">
                        <strong>Media strip</strong>
                        <div className="folder-studio-inline-actions">
                          <button type="button" className="folder-studio-mini-btn" onClick={handleRefreshImages} disabled={mediaBusy}>
                            {mediaBusy ? 'Dang refresh...' : 'Lam moi anh'}
                          </button>
                          <button type="button" className="folder-studio-mini-btn" onClick={() => setMediaOpen((current) => !current)}>
                            {mediaOpen ? 'Thu gon' : 'Mo'}
                          </button>
                        </div>
                      </div>

                      {mediaOpen ? (
                        <>
                          <p className="folder-studio-media-copy">
                            {selectedImageVm?.helperText || 'Chua co image payload cho slide nay.'}
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
                                Chua co candidate nao. Bam "Lam moi anh" de tao / tai lai media candidates.
                              </div>
                            )}
                          </div>
                        </>
                      ) : (
                        <p className="folder-studio-media-copy">
                          Media strip dang thu gon. Mo ra de doi image, chon candidate hoac refresh media workflow.
                        </p>
                      )}
                    </section>
                  </div>
                </>
              )}
            </div>
          </section>

          <aside className="folder-studio-rpanel">
            <div className="folder-studio-panel-title">Studio / Hanh dong</div>

            <div className="folder-studio-action-section">
              <div className="folder-studio-section-label">Tao moi</div>
              <button type="button" className="folder-studio-action tone-primary" onClick={handleGenerateDeck} disabled={!canGenerate}>
                <span className="folder-studio-action-copy">
                  <strong>Tao slide moi tu noi dung</strong>
                  <span>{selectedReadySources.length} source ready dang duoc chon cho folder</span>
                </span>
                <span className="folder-studio-action-badge">AI</span>
              </button>
              <button type="button" className="folder-studio-action" onClick={() => notifySoon('Tao cau hoi on tap')}>
                <span className="folder-studio-action-copy">
                  <strong>Tao cau hoi on tap</strong>
                  <span>Entry point cho flow question generation cap folder</span>
                </span>
                <span className="folder-studio-action-badge">Soon</span>
              </button>
              <button type="button" className="folder-studio-action" onClick={() => notifySoon('Mo Quiz tuong tac')}>
                <span className="folder-studio-action-copy">
                  <strong>Mo Quiz tuong tac</strong>
                  <span>Dat san de noi folder deck voi game flow sau nay</span>
                </span>
                <span className="folder-studio-action-badge">Soon</span>
              </button>
              <button type="button" className="folder-studio-action" onClick={() => notifySoon('Mo Flashcards')}>
                <span className="folder-studio-action-copy">
                  <strong>Mo Flashcards</strong>
                  <span>Cho phep tao flashcards tu tap source da chon</span>
                </span>
                <span className="folder-studio-action-badge">Soon</span>
              </button>
            </div>

            <div className="folder-studio-action-section">
              <div className="folder-studio-section-label">Brief deck</div>
              <label className="folder-studio-form-row">
                <span>So slide muc tieu</span>
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
                <span>Audience</span>
                <select value={brief.audience} onChange={(event) => setBrief((current) => ({ ...current, audience: event.target.value }))}>
                  {AUDIENCE_OPTIONS.map((option) => (
                    <option key={option} value={option}>{option}</option>
                  ))}
                </select>
              </label>
              <label className="folder-studio-form-row">
                <span>Tone</span>
                <select value={brief.tone} onChange={(event) => setBrief((current) => ({ ...current, tone: event.target.value }))}>
                  {TONE_OPTIONS.map((option) => (
                    <option key={option} value={option}>{option}</option>
                  ))}
                </select>
              </label>
              <label className="folder-studio-form-row">
                <span>Language style</span>
                <select value={brief.languageStyle} onChange={(event) => setBrief((current) => ({ ...current, languageStyle: event.target.value }))}>
                  {LANGUAGE_STYLE_OPTIONS.map((option) => (
                    <option key={option} value={option}>{option}</option>
                  ))}
                </select>
              </label>
              <label className="folder-studio-form-row">
                <span>Narrative goal</span>
                <textarea
                  rows={4}
                  value={brief.narrativeGoal}
                  onChange={(event) => setBrief((current) => ({ ...current, narrativeGoal: event.target.value }))}
                  placeholder="Muc tieu cau truc va nhan manh cho deck folder nay"
                />
              </label>
            </div>

            <div className="folder-studio-action-section">
              <div className="folder-studio-section-label">Phan tich & Tom tat</div>
              <button type="button" className="folder-studio-action" onClick={() => notifySoon('Tom tat noi dung')}>
                <span className="folder-studio-action-copy">
                  <strong>Tom tat noi dung</strong>
                  <span>Tong hop summary cap folder tu cac source da chon</span>
                </span>
                <span className="folder-studio-action-badge">Soon</span>
              </button>
              <button type="button" className="folder-studio-action" onClick={() => notifySoon('Phan tich y chinh')}>
                <span className="folder-studio-action-copy">
                  <strong>Phan tich y chinh</strong>
                  <span>Dat san cho luong concept extraction cap folder</span>
                </span>
                <span className="folder-studio-action-badge">Soon</span>
              </button>
              <button type="button" className="folder-studio-action" onClick={() => notifySoon('Xay dung so do tu duy')}>
                <span className="folder-studio-action-copy">
                  <strong>Xay dung so do tu duy</strong>
                  <span>Noi vao luong mindmap trong phase tiep theo</span>
                </span>
                <span className="folder-studio-action-badge">Soon</span>
              </button>
            </div>

            <div className="folder-studio-action-section">
              <div className="folder-studio-section-label">Xuat ban & Chia se</div>
              <button type="button" className="folder-studio-action" onClick={handleSaveSlide} disabled={!selectedDraft}>
                <span className="folder-studio-action-copy">
                  <strong>Luu slide hien tai</strong>
                  <span>Persist editorState, body blocks va notes vao deck</span>
                </span>
                <span className="folder-studio-action-badge">Save</span>
              </button>
              <a
                className={`folder-studio-action${!deck ? ' is-disabled' : ''}`}
                href={deck ? slideService.getFolderDeckHtmlUrl(folderId) : undefined}
                target={deck ? '_blank' : undefined}
                rel={deck ? 'noreferrer' : undefined}
                onClick={(event) => {
                  if (!deck) {
                    event.preventDefault();
                  }
                }}
              >
                <span className="folder-studio-action-copy">
                  <strong>Tai xuong HTML / PDF</strong>
                  <span>Xuat deck cap folder de preview hoac in PDF tu browser</span>
                </span>
                <span className="folder-studio-action-badge">Export</span>
              </a>
              <button type="button" className="folder-studio-action" onClick={() => notifySoon('Xuat PowerPoint')}>
                <span className="folder-studio-action-copy">
                  <strong>Xuat PowerPoint</strong>
                  <span>Cho phase xuat file pptx sau nay</span>
                </span>
                <span className="folder-studio-action-badge">Soon</span>
              </button>
              <button type="button" className="folder-studio-action" onClick={() => notifySoon('Chia se lien ket')}>
                <span className="folder-studio-action-copy">
                  <strong>Chia se lien ket</strong>
                  <span>Dat san cho shareable review link</span>
                </span>
                <span className="folder-studio-action-badge">Soon</span>
              </button>
            </div>

            <div className="folder-studio-action-section">
              <div className="folder-studio-section-label">Quan tri</div>
              <button type="button" className="folder-studio-action" onClick={() => loadWorkspace()}>
                <span className="folder-studio-action-copy">
                  <strong>Lam moi du lieu</strong>
                  <span>Tai lai sources, deck, progress va metadata cua folder</span>
                </span>
                <span className="folder-studio-action-badge">Sync</span>
              </button>
              <button type="button" className="folder-studio-action tone-danger" onClick={handleDeleteFolder}>
                <span className="folder-studio-action-copy">
                  <strong>Xoa folder project</strong>
                  <span>Thong tac nay se xoa folder va cac source ben trong</span>
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
