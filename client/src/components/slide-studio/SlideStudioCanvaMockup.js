import React, { useMemo, useState } from 'react';
import {
  LuArrowLeft,
  LuBookOpen,
  LuDownload,
  LuFileText,
  LuImage,
  LuLayers,
  LuLayoutTemplate,
  LuMousePointer2,
  LuPalette,
  LuPanelLeft,
  LuPresentation,
  LuSave,
  LuSettings2,
  LuShapes,
  LuSparkles,
  LuType,
  LuUpload,
} from 'react-icons/lu';
import { useLanguage } from '../../context/LanguageContext';
import '../../styles/pages/slide-studio-canva-mockup.css';

const slides = [
  { id: 1, title: 'Opening', tone: 'coral', kicker: '01' },
  { id: 2, title: 'Problem map', tone: 'mint', kicker: '02' },
  { id: 3, title: 'Core concept', tone: 'indigo', kicker: '03' },
  { id: 4, title: 'Visual proof', tone: 'amber', kicker: '04' },
  { id: 5, title: 'Compare', tone: 'steel', kicker: '05' },
  { id: 6, title: 'Practice', tone: 'rose', kicker: '06' },
  { id: 7, title: 'Wrap-up', tone: 'teal', kicker: '07' },
];

const copy = {
  en: {
    mockup: 'Mockup only',
    saved: 'Saved locally',
    back: 'Back to workspace',
    preview: 'Preview',
    text: 'Edit text',
    editLayout: 'Edit layout',
    export: 'Export',
    workspace: 'Workspace slide deck',
    drawerHint: 'Actions live here instead of taking a fixed right panel.',
    canvasTitle: 'From dense workspace to canvas-first studio',
    canvasSubtitle: 'A compact editor shell for source-backed learning slides.',
    focusLabel: 'Selected text block',
    inspectorTitle: 'Properties',
    inspectorSubtitle: 'Floating inspector',
    font: 'Font',
    size: 'Size',
    color: 'Color',
    opacity: 'Opacity',
    layoutLabel: 'Layout',
    sources: 'Sources',
    slides: 'Slides',
    actions: 'Actions',
    addText: 'Add text',
    regenerate: 'Generate deck',
    questionBank: 'Regenerate questions',
    study: 'Open Study Hub',
    pptx: 'Export PPTX',
    pdf: 'Export PDF',
    drawerTabs: {
      slides: 'Slides',
      sources: 'Sources',
      text: 'Text',
      elements: 'Elements',
      uploads: 'Uploads',
      actions: 'Actions',
    },
  },
  vi: {
    mockup: 'Chi la mockup',
    saved: 'Da luu cuc bo',
    back: 'Quay lai workspace',
    preview: 'Preview',
    text: 'Sua text',
    editLayout: 'Sua layout',
    export: 'Export',
    workspace: 'Slide deck workspace',
    drawerHint: 'Actions nam trong drawer, khong chiem panel phai co dinh.',
    canvasTitle: 'Tu workspace day panel sang studio uu tien canvas',
    canvasSubtitle: 'Khung editor gon cho slide hoc tap dua tren source.',
    focusLabel: 'Text block dang chon',
    inspectorTitle: 'Properties',
    inspectorSubtitle: 'Inspector noi',
    font: 'Font',
    size: 'Size',
    color: 'Mau',
    opacity: 'Opacity',
    layoutLabel: 'Layout',
    sources: 'Sources',
    slides: 'Slides',
    actions: 'Actions',
    addText: 'Them text',
    regenerate: 'Generate deck',
    questionBank: 'Tao lai question bank',
    study: 'Mo Study Hub',
    pptx: 'Export PPTX',
    pdf: 'Export PDF',
    drawerTabs: {
      slides: 'Slides',
      sources: 'Sources',
      text: 'Text',
      elements: 'Elements',
      uploads: 'Uploads',
      actions: 'Actions',
    },
  },
};

const tools = [
  { id: 'slides', icon: LuLayoutTemplate },
  { id: 'sources', icon: LuBookOpen },
  { id: 'text', icon: LuType },
  { id: 'elements', icon: LuShapes },
  { id: 'uploads', icon: LuUpload },
  { id: 'actions', icon: LuSparkles },
];

function SlideStudioCanvaMockup({ workspaceName = 'Workspace demo' }) {
  const { language } = useLanguage();
  const c = copy[language === 'vi' ? 'vi' : 'en'];
  const [activeTool, setActiveTool] = useState('slides');
  const [mode, setMode] = useState('preview');
  const [activeSlideId, setActiveSlideId] = useState(slides[1].id);

  const activeSlide = useMemo(
    () => slides.find((slide) => slide.id === activeSlideId) || slides[0],
    [activeSlideId]
  );

  const modeOptions = [
    { id: 'preview', label: c.preview, icon: LuMousePointer2 },
    { id: 'text', label: c.text, icon: LuType },
    { id: 'layout', label: c.editLayout, icon: LuPanelLeft },
  ];

  const drawerTitle = c.drawerTabs[activeTool] || c.drawerTabs.slides;

  return (
    <section className="canva-mockup-shell" aria-label="Canva-like Slide Studio mockup">
      <header className="canva-mockup-topbar">
        <button type="button" className="canva-mockup-icon-command" title={c.back} aria-label={c.back}>
          <LuArrowLeft aria-hidden="true" />
        </button>
        <div className="canva-mockup-title">
          <span>{c.mockup}</span>
          <strong>{workspaceName}</strong>
        </div>
        <span className="canva-mockup-save-state">
          <LuSave aria-hidden="true" />
          {c.saved}
        </span>
        <div className="canva-mockup-mode-group" role="group" aria-label="Studio mode">
          {modeOptions.map(({ id, label, icon: Icon }) => (
            <button
              key={id}
              type="button"
              className={mode === id ? 'active' : ''}
              onClick={() => setMode(id)}
            >
              <Icon aria-hidden="true" />
              <span>{label}</span>
            </button>
          ))}
        </div>
        <button type="button" className="canva-mockup-export">
          <LuDownload aria-hidden="true" />
          <span>{c.export}</span>
        </button>
      </header>

      <nav className="canva-mockup-rail" aria-label="Studio tools">
        {tools.map(({ id, icon: Icon }) => (
          <button
            key={id}
            type="button"
            className={activeTool === id ? 'active' : ''}
            title={c.drawerTabs[id]}
            aria-label={c.drawerTabs[id]}
            onClick={() => setActiveTool((current) => (current === id ? null : id))}
          >
            <Icon aria-hidden="true" />
            <span>{c.drawerTabs[id]}</span>
          </button>
        ))}
      </nav>

      <main className="canva-mockup-main">
        {activeTool && (
          <aside className="canva-mockup-drawer" aria-label={`${drawerTitle} drawer`}>
            <div className="canva-mockup-drawer-head">
              <strong>{drawerTitle}</strong>
              <span>{c.drawerHint}</span>
            </div>
            {activeTool === 'slides' && (
              <div className="canva-mockup-drawer-list">
                {slides.slice(0, 5).map((slide) => (
                  <button
                    key={slide.id}
                    type="button"
                    className={activeSlideId === slide.id ? 'active' : ''}
                    onClick={() => setActiveSlideId(slide.id)}
                  >
                    <span>{slide.kicker}</span>
                    <strong>{slide.title}</strong>
                  </button>
                ))}
              </div>
            )}
            {activeTool === 'sources' && (
              <div className="canva-mockup-source-stack">
                <article>
                  <LuFileText aria-hidden="true" />
                  <div>
                    <strong>Lecture notes.pdf</strong>
                    <span>12 sections selected</span>
                  </div>
                </article>
                <article>
                  <LuBookOpen aria-hidden="true" />
                  <div>
                    <strong>Workspace summary</strong>
                    <span>Ready for slide generation</span>
                  </div>
                </article>
              </div>
            )}
            {activeTool === 'text' && (
              <div className="canva-mockup-text-grid">
                <button type="button">{c.addText}</button>
                <button type="button">Heading</button>
                <button type="button">Quote block</button>
                <button type="button">Callout</button>
              </div>
            )}
            {activeTool === 'elements' && (
              <div className="canva-mockup-element-grid">
                <span><LuLayers aria-hidden="true" /> Cards</span>
                <span><LuPalette aria-hidden="true" /> Accent</span>
                <span><LuImage aria-hidden="true" /> Image</span>
                <span><LuPresentation aria-hidden="true" /> Layout</span>
              </div>
            )}
            {activeTool === 'uploads' && (
              <div className="canva-mockup-upload-drop">
                <LuUpload aria-hidden="true" />
                <strong>Drop images or diagrams</strong>
                <span>Mock upload area, no file handling.</span>
              </div>
            )}
            {activeTool === 'actions' && (
              <div className="canva-mockup-action-stack">
                {[c.regenerate, c.questionBank, c.study, c.pptx, c.pdf].map((label) => (
                  <button key={label} type="button">
                    <LuSparkles aria-hidden="true" />
                    <span>{label}</span>
                  </button>
                ))}
              </div>
            )}
          </aside>
        )}

        <section className="canva-mockup-stage" aria-label="Slide canvas">
          <div className={`canva-mockup-canvas tone-${activeSlide.tone}`}>
            <div className="canva-mockup-slide-badge">{activeSlide.kicker}</div>
            <div className="canva-mockup-slide-copy">
              <span>{c.workspace}</span>
              <h1>{c.canvasTitle}</h1>
              <p>{c.canvasSubtitle}</p>
            </div>
            <div className="canva-mockup-slide-visual">
              <span />
              <span />
              <span />
            </div>
            <div className="canva-mockup-selected-text">
              <span>{c.focusLabel}</span>
            </div>
          </div>
          <aside className="canva-mockup-inspector" aria-label="Properties inspector">
            <div>
              <span>{c.inspectorSubtitle}</span>
              <strong>{c.inspectorTitle}</strong>
            </div>
            <label>
              <span>{c.font}</span>
              <select defaultValue="Lexend" aria-label={c.font}>
                <option>Lexend</option>
              </select>
            </label>
            <label>
              <span>{c.size}</span>
              <input type="range" min="18" max="64" defaultValue="38" aria-label={c.size} />
            </label>
            <label>
              <span>{c.color}</span>
              <div className="canva-mockup-swatches">
                <i className="swatch-ink" />
                <i className="swatch-coral" />
                <i className="swatch-mint" />
                <i className="swatch-indigo" />
              </div>
            </label>
            <label>
              <span>{c.opacity}</span>
              <input type="range" min="0" max="100" defaultValue="92" aria-label={c.opacity} />
            </label>
          </aside>
        </section>
      </main>

      <footer className="canva-mockup-filmstrip" aria-label="Slide thumbnails">
        {slides.map((slide) => (
          <button
            key={slide.id}
            type="button"
            className={activeSlideId === slide.id ? 'active' : ''}
            onClick={() => setActiveSlideId(slide.id)}
          >
            <span className={`canva-mockup-thumb tone-${slide.tone}`}>
              <i>{slide.kicker}</i>
            </span>
            <strong>{slide.title}</strong>
          </button>
        ))}
        <button type="button" className="canva-mockup-filmstrip-add">
          <LuSettings2 aria-hidden="true" />
          <span>More</span>
        </button>
      </footer>
    </section>
  );
}

export default SlideStudioCanvaMockup;
