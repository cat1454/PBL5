import React, { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  LuArrowDown,
  LuArrowUp,
  LuBan,
  LuCheck,
  LuClipboard,
  LuCopy,
  LuFileQuestion,
  LuDoorOpen,
  LuGraduationCap,
  LuListChecks,
  LuPlus,
  LuRefreshCw,
  LuSave,
  LuSchool,
  LuTrash2,
} from 'react-icons/lu';
import { useLanguage } from '../../context/LanguageContext';
import {
  classroomService,
  getApiErrorMessage,
  isApiForbidden,
} from '../../services/api';
import { useAuth } from '../../context/AuthContext';
import './classrooms.css';

const ROLE_TEACHER = 'Teacher';

function getClassroomId(classroom) {
  return classroom?.id || classroom?.classroomWorkspaceId;
}

function formatDateTime(value) {
  if (!value) {
    return '-';
  }

  return new Date(value).toLocaleString();
}

function getText(t, key, fallback, vars) {
  const value = t(key, vars);
  return value === key ? fallback : value;
}

function parseQuestionOptions(options) {
  if (!options) {
    return [];
  }

  if (Array.isArray(options)) {
    return options;
  }

  if (typeof options === 'string') {
    const trimmed = options.trim();
    if (!trimmed) {
      return [];
    }

    try {
      const parsed = JSON.parse(trimmed);
      if (Array.isArray(parsed)) {
        return parsed;
      }
      if (parsed && typeof parsed === 'object') {
        return Object.entries(parsed).map(([key, text]) => ({ key, text: String(text) }));
      }
    } catch {
      return [{ key: '', text: trimmed }];
    }

    return [{ key: '', text: trimmed }];
  }

  return [];
}

function getOptionValue(option, index) {
  return String(option?.key || option?.value || option?.id || index + 1);
}

function getOptionText(option) {
  return String(option?.text || option?.label || option?.value || option?.key || '');
}

function buildAssignmentPayload(form) {
  return {
    title: form.title.trim(),
    description: form.description.trim() || null,
    questionSetId: Number(form.questionSetId),
    type: form.type,
    startAt: form.startAt ? new Date(form.startAt).toISOString() : null,
    dueAt: form.dueAt ? new Date(form.dueAt).toISOString() : null,
    timeLimitMinutes: form.timeLimitMinutes ? Number(form.timeLimitMinutes) : null,
    attemptLimit: Number(form.attemptLimit) || 1,
    shuffleQuestions: Boolean(form.shuffleQuestions),
    shuffleOptions: Boolean(form.shuffleOptions),
    showAnswerAfterSubmit: Boolean(form.showAnswerAfterSubmit),
    scoringMode: form.scoringMode || 'Percent',
    minQuestionWeight: form.minQuestionWeight ? Number(form.minQuestionWeight) : 0.3,
    maxQuestionWeight: form.maxQuestionWeight ? Number(form.maxQuestionWeight) : 2.0,
    smoothingAlpha: form.smoothingAlpha ? Number(form.smoothingAlpha) : 1.0,
    smoothingBeta: form.smoothingBeta ? Number(form.smoothingBeta) : 1.0,
  };
}

function validateScoringForm(form, t) {
  if (form.scoringMode !== 'EmpiricalDifficulty') {
    return null;
  }

  const minWeight = Number(form.minQuestionWeight);
  const maxWeight = Number(form.maxQuestionWeight);
  const alpha = Number(form.smoothingAlpha);
  const beta = Number(form.smoothingBeta);

  if (isNaN(minWeight) || minWeight <= 0) {
    return getText(t, 'classrooms.assignments.errors.minWeightPositive', 'Trọng số tối thiểu phải lớn hơn 0.');
  }

  if (isNaN(maxWeight) || maxWeight <= minWeight) {
    return getText(t, 'classrooms.assignments.errors.maxWeightGreater', 'Trọng số tối đa phải lớn hơn trọng số tối thiểu.');
  }

  if (isNaN(alpha) || alpha < 0 || isNaN(beta) || beta < 0) {
    return getText(t, 'classrooms.assignments.errors.smoothingNonNegative', 'Hệ số alpha và beta phải không âm.');
  }

  if (alpha + beta <= 0) {
    return getText(t, 'classrooms.assignments.errors.smoothingSumPositive', 'Tổng alpha và beta phải lớn hơn 0.');
  }

  return null;
}

const emptyAssignmentForm = {
  title: '',
  description: '',
  questionSetId: '',
  type: 'Quiz',
  startAt: '',
  dueAt: '',
  timeLimitMinutes: '',
  attemptLimit: '1',
  shuffleQuestions: false,
  shuffleOptions: false,
  showAnswerAfterSubmit: true,
  scoringMode: 'Percent',
  minQuestionWeight: '0.3',
  maxQuestionWeight: '2.0',
  smoothingAlpha: '1',
  smoothingBeta: '1',
};

export function TeachingClassroomsPage() {
  const { t } = useLanguage();
  const { currentUser } = useAuth();
  const [classrooms, setClassrooms] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [creating, setCreating] = useState(false);
  const [form, setForm] = useState({ name: '', description: '' });
  const canCreateClassroom = ['INSTRUCTOR', 'ADMIN'].includes(String(currentUser?.role || '').toUpperCase());

  const loadClassrooms = useCallback(async () => {
    setLoading(true);
    setError('');

    try {
      const data = await classroomService.getTeachingClassrooms();
      setClassrooms(Array.isArray(data) ? data : []);
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.errors.loadTeaching', 'Khong tai duoc danh sach lop dang day.')));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => {
    loadClassrooms();
  }, [loadClassrooms]);

  const handleCreate = async (event) => {
    event.preventDefault();
    if (!form.name.trim()) {
      setError(getText(t, 'classrooms.errors.nameRequired', 'Hay nhap ten lop.'));
      return;
    }

    setCreating(true);
    setError('');
    setSuccess('');

    try {
      const classroom = await classroomService.createClassroomWorkspace({
        name: form.name.trim(),
        description: form.description.trim(),
      });
      setForm({ name: '', description: '' });
      setSuccess(getText(t, 'classrooms.feedback.created', 'Da tao lop hoc.'));
      setClassrooms((current) => [classroom, ...current]);
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.errors.createFailed', 'Khong tao duoc lop hoc.')));
    } finally {
      setCreating(false);
    }
  };

  return (
    <ClassroomShell
      title={getText(t, 'classrooms.teaching.title', 'Lop dang day')}
      subtitle={getText(t, 'classrooms.teaching.subtitle', 'Tao lop, chia se ma tham gia va xem thanh vien.')}
    >
      <ClassroomTabs active="teaching" />
      <MessageBar error={error} success={success} />

      <section className="classroom-layout">
        {canCreateClassroom ? (
          <form className="classroom-panel classroom-form" onSubmit={handleCreate}>
            <div>
              <span className="classroom-kicker">{getText(t, 'classrooms.create.kicker', 'Teacher')}</span>
              <h2>{getText(t, 'classrooms.create.title', 'Tao lop moi')}</h2>
            </div>
            <label>
              <span>{getText(t, 'classrooms.create.name', 'Ten lop')}</span>
              <input
                value={form.name}
                onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))}
                placeholder={getText(t, 'classrooms.create.namePlaceholder', 'Vi du: JLPT N5 Reading')}
              />
            </label>
            <label>
              <span>{getText(t, 'classrooms.create.description', 'Mo ta')}</span>
              <textarea
                value={form.description}
                onChange={(event) => setForm((current) => ({ ...current, description: event.target.value }))}
                placeholder={getText(t, 'classrooms.create.descriptionPlaceholder', 'Muc tieu, lich hoc hoac ghi chu ngan')}
                rows={4}
              />
            </label>
            <button className="classroom-button primary" type="submit" disabled={creating}>
              <LuPlus aria-hidden="true" />
              {creating ? getText(t, 'classrooms.create.creating', 'Dang tao...') : getText(t, 'classrooms.create.submit', 'Tao lop')}
            </button>
          </form>
        ) : (
          <section className="classroom-panel classroom-empty">
            <LuSchool aria-hidden="true" />
            <h2>{getText(t, 'classrooms.teaching.teacherOnlyTitle', 'Can tai khoan giao vien')}</h2>
            <p>{getText(t, 'classrooms.teaching.teacherOnlyBody', 'Hoc vien co the xem lop da tham gia hoac nhap join code tu giao vien.')}</p>
            <Link className="classroom-button primary" to="/classrooms/join">
              <LuDoorOpen aria-hidden="true" />
              {getText(t, 'classrooms.tabs.join', 'Nhap code')}
            </Link>
          </section>
        )}

        <ClassroomList
          classrooms={classrooms}
          emptyBody={getText(t, 'classrooms.teaching.emptyBody', 'Tao lop dau tien de moi hoc vien bang join code.')}
          emptyTitle={getText(t, 'classrooms.teaching.emptyTitle', 'Chua co lop dang day')}
          loading={loading}
          onRetry={loadClassrooms}
          retryLabel={getText(t, 'classrooms.actions.retry', 'Thu lai')}
          t={t}
        />
      </section>
    </ClassroomShell>
  );
}

export function JoinedClassroomsPage() {
  const { t } = useLanguage();
  const [classrooms, setClassrooms] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const loadClassrooms = useCallback(async () => {
    setLoading(true);
    setError('');

    try {
      const data = await classroomService.getJoinedClassrooms();
      setClassrooms(Array.isArray(data) ? data : []);
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.errors.loadJoined', 'Khong tai duoc danh sach lop da tham gia.')));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => {
    loadClassrooms();
  }, [loadClassrooms]);

  return (
    <ClassroomShell
      title={getText(t, 'classrooms.joined.title', 'Lop da tham gia')}
      subtitle={getText(t, 'classrooms.joined.subtitle', 'Xem cac classroom ban dang la hoc vien.')}
    >
      <ClassroomTabs active="joined" />
      <MessageBar error={error} />
      <ClassroomList
        classrooms={classrooms}
        emptyBody={getText(t, 'classrooms.joined.emptyBody', 'Nhap join code giao vien cung cap de tham gia lop dau tien.')}
        emptyTitle={getText(t, 'classrooms.joined.emptyTitle', 'Chua tham gia lop nao')}
        loading={loading}
        onRetry={loadClassrooms}
        retryLabel={getText(t, 'classrooms.actions.retry', 'Thu lai')}
        t={t}
      />
    </ClassroomShell>
  );
}

export function JoinClassroomPage() {
  const { t } = useLanguage();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const initialCode = searchParams.get('code') || '';
  const [code, setCode] = useState(initialCode.toUpperCase());
  const [joining, setJoining] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const handleJoin = async (event) => {
    event.preventDefault();
    if (!code.trim()) {
      setError(getText(t, 'classrooms.errors.codeRequired', 'Hay nhap ma tham gia.'));
      return;
    }

    setJoining(true);
    setError('');
    setSuccess('');

    try {
      const classroom = await classroomService.joinClassroomByCode(code.trim());
      setSuccess(getText(t, 'classrooms.feedback.joined', 'Da tham gia lop.'));
      const classroomId = getClassroomId(classroom);
      window.setTimeout(() => {
        navigate(classroomId ? `/classrooms/${classroomId}` : '/classrooms/joined');
      }, 450);
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.errors.joinFailed', 'Khong tham gia duoc lop.')));
    } finally {
      setJoining(false);
    }
  };

  return (
    <ClassroomShell
      title={getText(t, 'classrooms.join.title', 'Tham gia lop')}
      subtitle={getText(t, 'classrooms.join.subtitle', 'Nhap join code tu giao vien de vao classroom.')}
    >
      <ClassroomTabs active="join" />
      <MessageBar error={error} success={success} />
      <form className="classroom-panel classroom-join-form" onSubmit={handleJoin}>
        <LuDoorOpen className="classroom-form-icon" aria-hidden="true" />
        <label>
          <span>{getText(t, 'classrooms.join.codeLabel', 'Join code')}</span>
          <input
            value={code}
            onChange={(event) => setCode(event.target.value.toUpperCase())}
            placeholder="ABC123"
          />
        </label>
        <button className="classroom-button primary" type="submit" disabled={joining}>
          <LuCheck aria-hidden="true" />
          {joining ? getText(t, 'classrooms.join.joining', 'Dang tham gia...') : getText(t, 'classrooms.join.submit', 'Tham gia lop')}
        </button>
      </form>
    </ClassroomShell>
  );
}

export function ClassroomDetailPage({ membersOnly = false }) {
  const { classroomId } = useParams();
  const { t } = useLanguage();
  const [classroom, setClassroom] = useState(null);
  const [members, setMembers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [membersLoading, setMembersLoading] = useState(false);
  const [error, setError] = useState('');
  const [membersError, setMembersError] = useState('');
  const [success, setSuccess] = useState('');
  const [creatingCode, setCreatingCode] = useState(false);
  const [disablingCodeId, setDisablingCodeId] = useState(null);

  const isTeacher = classroom?.currentUserRole === ROLE_TEACHER;

  const loadDetail = useCallback(async () => {
    setLoading(true);
    setError('');

    try {
      const data = await classroomService.getClassroomDetail(classroomId);
      setClassroom(data);
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.errors.detailFailed', 'Khong tai duoc chi tiet lop.')));
    } finally {
      setLoading(false);
    }
  }, [classroomId, t]);

  const loadMembers = useCallback(async () => {
    setMembersLoading(true);
    setMembersError('');

    try {
      const data = await classroomService.getClassroomMembers(classroomId);
      setMembers(Array.isArray(data) ? data : []);
    } catch (err) {
      if (isApiForbidden(err)) {
        setMembersError(getText(t, 'classrooms.errors.membersForbidden', 'Chi giao vien cua lop moi xem duoc danh sach thanh vien.'));
      } else {
        setMembersError(getApiErrorMessage(err, getText(t, 'classrooms.errors.membersFailed', 'Khong tai duoc danh sach thanh vien.')));
      }
    } finally {
      setMembersLoading(false);
    }
  }, [classroomId, t]);

  useEffect(() => {
    loadDetail();
  }, [loadDetail]);

  useEffect(() => {
    if (isTeacher || membersOnly) {
      loadMembers();
    }
  }, [isTeacher, loadMembers, membersOnly]);

  const refreshAfterCodeChange = async (message) => {
    setSuccess(message);
    await loadDetail();
  };

  const handleCreateCode = async () => {
    setCreatingCode(true);
    setError('');
    setSuccess('');

    try {
      await classroomService.createClassroomJoinCode(classroomId, {});
      await refreshAfterCodeChange(getText(t, 'classrooms.feedback.codeCreated', 'Da tao join code.'));
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.errors.codeCreateFailed', 'Khong tao duoc join code.')));
    } finally {
      setCreatingCode(false);
    }
  };

  const handleDisableCode = async (codeId) => {
    setDisablingCodeId(codeId);
    setError('');
    setSuccess('');

    try {
      await classroomService.disableClassroomJoinCode(classroomId, codeId);
      await refreshAfterCodeChange(getText(t, 'classrooms.feedback.codeDisabled', 'Da tat join code.'));
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.errors.codeDisableFailed', 'Khong tat duoc join code.')));
    } finally {
      setDisablingCodeId(null);
    }
  };

  const handleCopyCode = async (code) => {
    try {
      await navigator.clipboard.writeText(code);
      setSuccess(getText(t, 'classrooms.feedback.codeCopied', 'Da copy join code.'));
    } catch {
      setError(getText(t, 'classrooms.errors.copyFailed', 'Khong copy duoc code. Hay copy thu cong.'));
    }
  };

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.detail.title', 'Chi tiet lop')} subtitle="">
        <ClassroomTabs />
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Dang tai...')} />
      </ClassroomShell>
    );
  }

  if (error && !classroom) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.detail.title', 'Chi tiet lop')} subtitle="">
        <ClassroomTabs />
        <MessageBar error={error} />
        <button className="classroom-button" type="button" onClick={loadDetail}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.actions.retry', 'Thu lai')}
        </button>
      </ClassroomShell>
    );
  }

  return (
    <ClassroomShell title={classroom?.name || getText(t, 'classrooms.detail.title', 'Chi tiet lop')} subtitle={classroom?.description || getText(t, 'classrooms.detail.noDescription', 'Chua co mo ta.')}>
      <ClassroomTabs />
      <MessageBar error={error} success={success} />

      <section className="classroom-detail-grid">
        <article className="classroom-panel classroom-summary">
          <span className="classroom-kicker">{classroom?.currentUserRole || '-'}</span>
          <h2>{classroom?.name}</h2>
          <div className="classroom-metrics">
            <Metric label={getText(t, 'classrooms.metrics.members', 'Thanh vien')} value={classroom?.memberCount || 0} />
            <Metric label={getText(t, 'classrooms.metrics.teachers', 'Giao vien')} value={classroom?.teacherCount || 0} />
            <Metric label={getText(t, 'classrooms.metrics.students', 'Hoc vien')} value={classroom?.studentCount || 0} />
          </div>
          <p className="classroom-muted">
            {getText(t, 'classrooms.detail.updated', 'Cap nhat')}: {formatDateTime(classroom?.updatedAt)}
          </p>
          <Link className="classroom-button primary classroom-inline-action" to={`/classrooms/${classroomId}/question-sets`}>
            <LuListChecks aria-hidden="true" />
            {getText(t, 'classrooms.questionSets.open', 'Bo cau hoi')}
          </Link>
          <Link className="classroom-button classroom-inline-action" to={isTeacher ? `/classrooms/${classroomId}/assignments` : `/classrooms/${classroomId}/student/assignments`}>
            <LuClipboard aria-hidden="true" />
            {getText(t, 'classrooms.assignments.open', 'Assignments')}
          </Link>
        </article>

        {isTeacher && (
          <article className="classroom-panel classroom-code-panel">
            <div className="classroom-section-head">
              <div>
                <span className="classroom-kicker">{getText(t, 'classrooms.codes.kicker', 'Join code')}</span>
                <h2>{getText(t, 'classrooms.codes.title', 'Moi hoc vien')}</h2>
              </div>
              <button className="classroom-button primary" type="button" onClick={handleCreateCode} disabled={creatingCode}>
                <LuPlus aria-hidden="true" />
                {creatingCode ? getText(t, 'classrooms.codes.creating', 'Dang tao...') : getText(t, 'classrooms.codes.create', 'Tao code')}
              </button>
            </div>
            <JoinCodeList
              codes={Array.isArray(classroom?.joinCodes) ? classroom.joinCodes : []}
              disablingCodeId={disablingCodeId}
              onCopy={handleCopyCode}
              onDisable={handleDisableCode}
              t={t}
            />
          </article>
        )}
      </section>

      {isTeacher || membersOnly ? (
        <MembersPanel
          error={membersError}
          loading={membersLoading}
          members={members}
          onRetry={loadMembers}
          t={t}
        />
      ) : (
        <section className="classroom-panel classroom-student-note">
          <LuGraduationCap aria-hidden="true" />
          <div>
            <h2>{getText(t, 'classrooms.student.title', 'Ban dang la hoc vien')}</h2>
            <p>{getText(t, 'classrooms.student.body', 'Trang classroom cua hoc vien chi hien thi thong tin lop. Cac flow xu ly tai lieu van nam trong personal workspace rieng cua ban.')}</p>
          </div>
        </section>
      )}
    </ClassroomShell>
  );
}

export function ClassroomQuestionSetsPage() {
  const { classroomId } = useParams();
  const { t } = useLanguage();
  const [classroom, setClassroom] = useState(null);
  const [questionSets, setQuestionSets] = useState([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [form, setForm] = useState({ title: '', description: '', documentId: '' });
  const isTeacher = classroom?.currentUserRole === ROLE_TEACHER;

  const loadPage = useCallback(async () => {
    setLoading(true);
    setError('');

    try {
      const [classroomData, setsData] = await Promise.all([
        classroomService.getClassroomDetail(classroomId),
        classroomService.getClassroomQuestionSets(classroomId),
      ]);
      setClassroom(classroomData);
      setQuestionSets(Array.isArray(setsData) ? setsData : []);
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.questionSets.errors.load', 'Khong tai duoc bo cau hoi.')));
    } finally {
      setLoading(false);
    }
  }, [classroomId, t]);

  useEffect(() => {
    loadPage();
  }, [loadPage]);

  const handleCreate = async (event) => {
    event.preventDefault();
    if (!form.title.trim()) {
      setError(getText(t, 'classrooms.questionSets.errors.titleRequired', 'Nhap tieu de bo cau hoi.'));
      return;
    }

    setSaving(true);
    setError('');
    setSuccess('');

    try {
      const created = await classroomService.createClassroomQuestionSet(classroomId, {
        title: form.title.trim(),
        description: form.description.trim() || null,
        documentId: form.documentId ? Number(form.documentId) : null,
      });
      setForm({ title: '', description: '', documentId: '' });
      setSuccess(getText(t, 'classrooms.questionSets.feedback.created', 'Da tao bo cau hoi.'));
      setQuestionSets((current) => [created, ...current]);
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.questionSets.errors.create', 'Khong tao duoc bo cau hoi.')));
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.questionSets.title', 'Bo cau hoi')} subtitle="">
        <ClassroomTabs />
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Dang tai...')} />
      </ClassroomShell>
    );
  }

  return (
    <ClassroomShell
      title={getText(t, 'classrooms.questionSets.title', 'Bo cau hoi')}
      subtitle={classroom?.name || getText(t, 'classrooms.detail.title', 'Chi tiet lop')}
    >
      <ClassroomTabs />
      <MessageBar error={error} success={success} />

      <div className="classroom-page-actions">
        <Link className="classroom-button" to={`/classrooms/${classroomId}`}>
          <LuSchool aria-hidden="true" />
          {getText(t, 'classrooms.questionSets.backToClassroom', 'Ve lop hoc')}
        </Link>
        <button className="classroom-button" type="button" onClick={loadPage}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.actions.refresh', 'Lam moi')}
        </button>
      </div>

      <section className="classroom-layout">
        {isTeacher ? (
          <form className="classroom-panel classroom-form" onSubmit={handleCreate}>
            <div>
              <span className="classroom-kicker">{getText(t, 'classrooms.questionSets.teacherTools', 'Teacher tools')}</span>
              <h2>{getText(t, 'classrooms.questionSets.createTitle', 'Tao bo cau hoi')}</h2>
            </div>
            <QuestionSetFields form={form} onChange={setForm} t={t} />
            <button className="classroom-button primary" type="submit" disabled={saving}>
              <LuPlus aria-hidden="true" />
              {saving ? getText(t, 'classrooms.questionSets.creating', 'Dang tao...') : getText(t, 'classrooms.questionSets.create', 'Tao bo cau hoi')}
            </button>
          </form>
        ) : (
          <section className="classroom-panel classroom-student-note">
            <LuGraduationCap aria-hidden="true" />
            <div>
              <h2>{getText(t, 'classrooms.questionSets.studentTitle', 'Bo cau hoi da cong bo')}</h2>
              <p>{getText(t, 'classrooms.questionSets.studentBody', 'Hoc vien chi xem duoc bo cau hoi da Published va khong co cong cu quan tri.')}</p>
            </div>
          </section>
        )}

        <QuestionSetList
          classroomId={classroomId}
          questionSets={questionSets}
          loading={false}
          onRetry={loadPage}
          t={t}
        />
      </section>
    </ClassroomShell>
  );
}

export function ClassroomQuestionSetDetailPage() {
  const { classroomId, questionSetId } = useParams();
  const { t } = useLanguage();
  const navigate = useNavigate();
  const [classroom, setClassroom] = useState(null);
  const [questionSet, setQuestionSet] = useState(null);
  const [availableQuestions, setAvailableQuestions] = useState([]);
  const [loading, setLoading] = useState(true);
  const [availableLoading, setAvailableLoading] = useState(false);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [editForm, setEditForm] = useState({ title: '', description: '', documentId: '' });
  const [sourceDocumentId, setSourceDocumentId] = useState('');
  const [itemForm, setItemForm] = useState({ questionId: '', pointWeight: '1', sectionCode: '' });
  const isTeacher = classroom?.currentUserRole === ROLE_TEACHER;

  const syncEditForm = (data) => {
    setEditForm({
      title: data?.title || '',
      description: data?.description || '',
      documentId: data?.documentId ? String(data.documentId) : '',
    });
    setSourceDocumentId(data?.documentId ? String(data.documentId) : '');
  };

  const loadDetail = useCallback(async () => {
    setLoading(true);
    setError('');

    try {
      const classroomData = await classroomService.getClassroomDetail(classroomId);
      setClassroom(classroomData);

      let questionSetData;
      if (classroomData?.currentUserRole === ROLE_TEACHER) {
        questionSetData = await classroomService.getClassroomQuestionSetDetail(questionSetId);
      } else {
        const visibleQuestionSets = await classroomService.getClassroomQuestionSets(classroomId);
        const visibleQuestionSet = (Array.isArray(visibleQuestionSets) ? visibleQuestionSets : [])
          .find((candidate) => String(candidate.id) === String(questionSetId));
        if (!visibleQuestionSet) {
          setQuestionSet(null);
          setError(getText(t, 'classrooms.questionSets.errors.forbidden', 'Ban khong co quyen xem hoac quan ly bo cau hoi nay.'));
          return;
        }

        questionSetData = await classroomService.getClassroomQuestionSetDetail(questionSetId);
      }

      setQuestionSet(questionSetData);
      syncEditForm(questionSetData);
    } catch (err) {
      const fallback = isApiForbidden(err)
        ? getText(t, 'classrooms.questionSets.errors.forbidden', 'Ban khong co quyen xem hoac quan ly bo cau hoi nay.')
        : getText(t, 'classrooms.questionSets.errors.detail', 'Khong tai duoc chi tiet bo cau hoi.');
      setError(getApiErrorMessage(err, fallback));
    } finally {
      setLoading(false);
    }
  }, [classroomId, questionSetId, t]);

  useEffect(() => {
    loadDetail();
  }, [loadDetail]);

  const reloadQuestionSet = async () => {
    const data = await classroomService.getClassroomQuestionSetDetail(questionSetId);
    setQuestionSet(data);
    syncEditForm(data);
    return data;
  };

  const runTeacherAction = async (action, successMessage, fallbackMessage) => {
    setWorking(true);
    setError('');
    setSuccess('');

    try {
      await action();
      await reloadQuestionSet();
      setSuccess(successMessage);
    } catch (err) {
      setError(getApiErrorMessage(err, fallbackMessage));
    } finally {
      setWorking(false);
    }
  };

  const handleUpdate = async (event) => {
    event.preventDefault();
    if (!editForm.title.trim()) {
      setError(getText(t, 'classrooms.questionSets.errors.titleRequired', 'Nhap tieu de bo cau hoi.'));
      return;
    }

    await runTeacherAction(
      () => classroomService.updateClassroomQuestionSet(questionSetId, {
        title: editForm.title.trim(),
        description: editForm.description.trim() || null,
        documentId: editForm.documentId ? Number(editForm.documentId) : null,
      }),
      getText(t, 'classrooms.questionSets.feedback.updated', 'Da cap nhat bo cau hoi.'),
      getText(t, 'classrooms.questionSets.errors.update', 'Khong cap nhat duoc bo cau hoi.')
    );
  };

  const handleDelete = async () => {
    setWorking(true);
    setError('');
    setSuccess('');

    try {
      await classroomService.deleteClassroomQuestionSet(questionSetId);
      navigate(`/classrooms/${classroomId}/question-sets`);
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.questionSets.errors.delete', 'Khong xoa duoc bo cau hoi.')));
      setWorking(false);
    }
  };

  const loadAvailableQuestions = async () => {
    setAvailableLoading(true);
    setError('');
    setSuccess('');

    try {
      const data = await classroomService.getClassroomAvailableQuestions(
        classroomId,
        sourceDocumentId ? Number(sourceDocumentId) : undefined
      );
      setAvailableQuestions(Array.isArray(data) ? data : []);
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.questionSets.errors.available', 'Khong tai duoc cau hoi kha dung.')));
    } finally {
      setAvailableLoading(false);
    }
  };

  const addQuestion = async (questionId) => {
    const resolvedQuestionId = questionId || Number(itemForm.questionId);
    if (!resolvedQuestionId) {
      setError(getText(t, 'classrooms.questionSets.errors.questionRequired', 'Nhap Question ID.'));
      return;
    }

    await runTeacherAction(
      () => classroomService.addQuestionToClassroomQuestionSet(questionSetId, {
        questionId: Number(resolvedQuestionId),
        pointWeight: Number(itemForm.pointWeight) || 1,
        sectionCode: itemForm.sectionCode.trim() || null,
      }),
      getText(t, 'classrooms.questionSets.feedback.questionAdded', 'Da them cau hoi.'),
      getText(t, 'classrooms.questionSets.errors.addQuestion', 'Khong them duoc cau hoi.')
    );
    setItemForm((current) => ({ ...current, questionId: '' }));
  };

  const removeQuestion = async (itemId) => {
    await runTeacherAction(
      () => classroomService.removeQuestionFromClassroomQuestionSet(questionSetId, itemId),
      getText(t, 'classrooms.questionSets.feedback.questionRemoved', 'Da xoa cau hoi khoi bo.'),
      getText(t, 'classrooms.questionSets.errors.removeQuestion', 'Khong xoa duoc cau hoi.')
    );
  };

  const reorderItem = async (itemId, direction) => {
    const items = [...(questionSet?.items || [])].sort(compareQuestionSetItems);
    const index = items.findIndex((item) => item.id === itemId);
    const nextIndex = index + direction;
    if (index < 0 || nextIndex < 0 || nextIndex >= items.length) {
      return;
    }

    const swapped = [...items];
    [swapped[index], swapped[nextIndex]] = [swapped[nextIndex], swapped[index]];
    await runTeacherAction(
      () => classroomService.reorderClassroomQuestionSetItems(
        questionSetId,
        swapped.map((item, orderIndex) => ({ itemId: item.id, orderIndex }))
      ),
      getText(t, 'classrooms.questionSets.feedback.reordered', 'Da sap xep lai cau hoi.'),
      getText(t, 'classrooms.questionSets.errors.reorder', 'Khong sap xep duoc cau hoi.')
    );
  };

  const publish = () => runTeacherAction(
    () => classroomService.publishClassroomQuestionSet(questionSetId),
    getText(t, 'classrooms.questionSets.feedback.published', 'Da publish bo cau hoi.'),
    getText(t, 'classrooms.questionSets.errors.publish', 'Khong publish duoc bo cau hoi.')
  );

  const unpublish = () => runTeacherAction(
    () => classroomService.unpublishClassroomQuestionSet(questionSetId),
    getText(t, 'classrooms.questionSets.feedback.unpublished', 'Da dua bo cau hoi ve draft.'),
    getText(t, 'classrooms.questionSets.errors.unpublish', 'Khong unpublish duoc bo cau hoi.')
  );

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.questionSets.detailTitle', 'Chi tiet bo cau hoi')} subtitle="">
        <ClassroomTabs />
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Dang tai...')} />
      </ClassroomShell>
    );
  }

  if (error && !questionSet) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.questionSets.detailTitle', 'Chi tiet bo cau hoi')} subtitle="">
        <ClassroomTabs />
        <MessageBar error={error} />
        <div className="classroom-page-actions">
          <Link className="classroom-button" to={`/classrooms/${classroomId}/question-sets`}>
            <LuListChecks aria-hidden="true" />
            {getText(t, 'classrooms.questionSets.backToList', 'Ve danh sach')}
          </Link>
          <button className="classroom-button" type="button" onClick={loadDetail}>
            <LuRefreshCw aria-hidden="true" />
            {getText(t, 'classrooms.actions.retry', 'Thu lai')}
          </button>
        </div>
      </ClassroomShell>
    );
  }

  const orderedItems = [...(questionSet?.items || [])].sort(compareQuestionSetItems);
  const isPublished = questionSet?.visibility === 'Published';

  return (
    <ClassroomShell title={questionSet?.title || getText(t, 'classrooms.questionSets.detailTitle', 'Chi tiet bo cau hoi')} subtitle={classroom?.name || ''}>
      <ClassroomTabs />
      <MessageBar error={error} success={success} />

      <div className="classroom-page-actions">
        <Link className="classroom-button" to={`/classrooms/${classroomId}/question-sets`}>
          <LuListChecks aria-hidden="true" />
          {getText(t, 'classrooms.questionSets.backToList', 'Ve danh sach')}
        </Link>
        <button className="classroom-button" type="button" onClick={loadDetail}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.actions.refresh', 'Lam moi')}
        </button>
      </div>

      <section className="classroom-detail-grid">
        <article className="classroom-panel classroom-summary">
          <span className={`classroom-badge ${isPublished ? '' : 'muted'}`}>{questionSet?.visibility || 'Draft'}</span>
          <h2>{questionSet?.title}</h2>
          <p>{questionSet?.description || getText(t, 'classrooms.questionSets.noDescription', 'Chua co mo ta.')}</p>
          <div className="classroom-metrics">
            <Metric label={getText(t, 'classrooms.questionSets.itemCount', 'Cau hoi')} value={questionSet?.itemCount || orderedItems.length} />
            <Metric label={getText(t, 'classrooms.questionSets.totalPoints', 'Diem')} value={questionSet?.totalPoints || 0} />
            <Metric label="Document ID" value={questionSet?.documentId || '-'} />
          </div>
          {!isTeacher && (
            <p className="classroom-muted">{getText(t, 'classrooms.questionSets.readOnly', 'Ban dang xem o che do chi doc.')}</p>
          )}
        </article>

        {isTeacher && (
          <form className="classroom-panel classroom-form" onSubmit={handleUpdate}>
            <div className="classroom-section-head">
              <div>
                <span className="classroom-kicker">{getText(t, 'classrooms.questionSets.teacherTools', 'Teacher tools')}</span>
                <h2>{getText(t, 'classrooms.questionSets.editTitle', 'Sua bo cau hoi')}</h2>
              </div>
              <div className="classroom-row-actions">
                {isPublished ? (
                  <button className="classroom-button" type="button" onClick={unpublish} disabled={working}>
                    <LuBan aria-hidden="true" />
                    {getText(t, 'classrooms.questionSets.unpublish', 'Unpublish')}
                  </button>
                ) : (
                  <button className="classroom-button primary" type="button" onClick={publish} disabled={working}>
                    <LuCheck aria-hidden="true" />
                    {getText(t, 'classrooms.questionSets.publish', 'Publish')}
                  </button>
                )}
                <button className="classroom-icon-button danger" type="button" onClick={handleDelete} disabled={working} title={getText(t, 'classrooms.questionSets.delete', 'Xoa')}>
                  <LuTrash2 aria-hidden="true" />
                </button>
              </div>
            </div>
            <QuestionSetFields form={editForm} onChange={setEditForm} t={t} />
            <button className="classroom-button primary" type="submit" disabled={working}>
              <LuSave aria-hidden="true" />
              {getText(t, 'classrooms.questionSets.save', 'Luu')}
            </button>
          </form>
        )}
      </section>

      {isTeacher && (
        <section className="classroom-panel classroom-question-picker">
          <div className="classroom-section-head">
            <div>
              <span className="classroom-kicker">{getText(t, 'classrooms.questionSets.availableKicker', 'Question source')}</span>
              <h2>{getText(t, 'classrooms.questionSets.availableTitle', 'Cau hoi kha dung')}</h2>
            </div>
            <button className="classroom-button" type="button" onClick={loadAvailableQuestions} disabled={availableLoading}>
              <LuRefreshCw aria-hidden="true" />
              {availableLoading ? getText(t, 'classrooms.states.loading', 'Dang tai...') : getText(t, 'classrooms.questionSets.loadQuestions', 'Tai cau hoi')}
            </button>
          </div>

          <div className="classroom-question-tools">
            <label>
              <span>{getText(t, 'classrooms.questionSets.documentId', 'Document ID')}</span>
              <input
                inputMode="numeric"
                value={sourceDocumentId}
                onChange={(event) => setSourceDocumentId(event.target.value.replace(/\D/g, ''))}
                placeholder="123"
              />
            </label>
            <form className="classroom-inline-form" onSubmit={(event) => { event.preventDefault(); addQuestion(); }}>
              <label>
                <span>Question ID</span>
                <input
                  inputMode="numeric"
                  value={itemForm.questionId}
                  onChange={(event) => setItemForm((current) => ({ ...current, questionId: event.target.value.replace(/\D/g, '') }))}
                  placeholder="456"
                />
              </label>
              <label>
                <span>{getText(t, 'classrooms.questionSets.pointWeight', 'Diem')}</span>
                <input
                  inputMode="decimal"
                  value={itemForm.pointWeight}
                  onChange={(event) => setItemForm((current) => ({ ...current, pointWeight: event.target.value }))}
                />
              </label>
              <label>
                <span>{getText(t, 'classrooms.questionSets.sectionCode', 'Section')}</span>
                <input
                  value={itemForm.sectionCode}
                  onChange={(event) => setItemForm((current) => ({ ...current, sectionCode: event.target.value }))}
                  placeholder="Knowledge"
                />
              </label>
              <button className="classroom-button primary" type="submit" disabled={working}>
                <LuPlus aria-hidden="true" />
                {getText(t, 'classrooms.questionSets.addById', 'Them bang ID')}
              </button>
            </form>
          </div>

          <AvailableQuestionList
            loading={availableLoading}
            onAdd={addQuestion}
            questions={availableQuestions}
            t={t}
            working={working}
          />
        </section>
      )}

      <section className="classroom-panel classroom-question-set-items">
        <div className="classroom-section-head">
          <div>
            <span className="classroom-kicker">{getText(t, 'classrooms.questionSets.itemsKicker', 'Questions')}</span>
            <h2>{getText(t, 'classrooms.questionSets.itemsTitle', 'Cau hoi trong bo')}</h2>
          </div>
        </div>
        <QuestionSetItems
          isTeacher={isTeacher}
          items={orderedItems}
          onMove={reorderItem}
          onRemove={removeQuestion}
          t={t}
          working={working}
        />
      </section>
    </ClassroomShell>
  );
}

export function ClassroomAssignmentsPage() {
  const { classroomId } = useParams();
  const { t } = useLanguage();
  const [classroom, setClassroom] = useState(null);
  const [assignments, setAssignments] = useState([]);
  const [questionSets, setQuestionSets] = useState([]);
  const [form, setForm] = useState(emptyAssignmentForm);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const isTeacher = classroom?.currentUserRole === ROLE_TEACHER;

  const loadPage = useCallback(async () => {
    setLoading(true);
    setError('');

    try {
      const classroomData = await classroomService.getClassroomDetail(classroomId);
      setClassroom(classroomData);
      if (classroomData?.currentUserRole !== ROLE_TEACHER) {
        setAssignments([]);
        setQuestionSets([]);
        return;
      }

      const [assignmentData, questionSetData] = await Promise.all([
        classroomService.getClassroomAssignments(classroomId),
        classroomService.getClassroomQuestionSets(classroomId),
      ]);
      setAssignments(Array.isArray(assignmentData) ? assignmentData : []);
      setQuestionSets((Array.isArray(questionSetData) ? questionSetData : []).filter((set) => set.visibility === 'Published'));
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.load', 'Khong tai duoc assignments.')));
    } finally {
      setLoading(false);
    }
  }, [classroomId, t]);

  useEffect(() => {
    loadPage();
  }, [loadPage]);

  const handleCreate = async (event) => {
    event.preventDefault();
    if (!form.title.trim() || !form.questionSetId) {
      setError(getText(t, 'classrooms.assignments.errors.required', 'Nhap tieu de va chon bo cau hoi.'));
      return;
    }

    const validationError = validateScoringForm(form, t);
    if (validationError) {
      setError(validationError);
      return;
    }

    setSaving(true);
    setError('');
    setSuccess('');

    try {
      const created = await classroomService.createClassroomAssignment(classroomId, buildAssignmentPayload(form));
      setAssignments((current) => [created, ...current]);
      setForm(emptyAssignmentForm);
      setSuccess(getText(t, 'classrooms.assignments.feedback.created', 'Da tao assignment.'));
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.create', 'Khong tao duoc assignment.')));
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.title', 'Assignments')} subtitle="">
        <ClassroomTabs />
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Dang tai...')} />
      </ClassroomShell>
    );
  }

  if (!isTeacher) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.title', 'Assignments')} subtitle={classroom?.name || ''}>
        <ClassroomTabs />
        <MessageBar error={getText(t, 'classrooms.assignments.errors.teacherOnly', 'Chi giao vien cua lop moi quan ly assignment.')} />
        <Link className="classroom-button" to={`/classrooms/${classroomId}/student/assignments`}>
          <LuGraduationCap aria-hidden="true" />
          {getText(t, 'classrooms.assignments.studentList', 'Assignments cua hoc vien')}
        </Link>
      </ClassroomShell>
    );
  }

  return (
    <ClassroomShell title={getText(t, 'classrooms.assignments.title', 'Assignments')} subtitle={classroom?.name || ''}>
      <ClassroomTabs />
      <MessageBar error={error} success={success} />
      <ClassroomResourceLinks classroomId={classroomId} isTeacher={isTeacher} t={t} />

      <section className="classroom-layout">
        <form className="classroom-panel classroom-form" onSubmit={handleCreate}>
          <div>
            <span className="classroom-kicker">{getText(t, 'classrooms.assignments.teacherTools', 'Teacher tools')}</span>
            <h2>{getText(t, 'classrooms.assignments.createTitle', 'Tao assignment')}</h2>
          </div>
          <AssignmentFields form={form} onChange={setForm} questionSets={questionSets} t={t} />
          <button className="classroom-button primary" type="submit" disabled={saving}>
            <LuPlus aria-hidden="true" />
            {saving ? getText(t, 'classrooms.assignments.creating', 'Dang tao...') : getText(t, 'classrooms.assignments.create', 'Tao assignment')}
          </button>
        </form>

        <AssignmentList
          assignments={assignments}
          classroomId={classroomId}
          emptyBody={getText(t, 'classrooms.assignments.emptyBody', 'Tao assignment tu bo cau hoi da Published.')}
          loading={false}
          onRetry={loadPage}
          t={t}
          teacher
        />
      </section>
    </ClassroomShell>
  );
}

export function ClassroomAssignmentDetailPage() {
  const { classroomId, assignmentId } = useParams();
  const { t } = useLanguage();
  const navigate = useNavigate();
  const [classroom, setClassroom] = useState(null);
  const [assignment, setAssignment] = useState(null);
  const [questionSets, setQuestionSets] = useState([]);
  const [form, setForm] = useState(emptyAssignmentForm);
  const [loading, setLoading] = useState(true);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [questionStats, setQuestionStats] = useState([]);

  const isTeacher = classroom?.currentUserRole === ROLE_TEACHER;

  const syncForm = (data) => {
    setForm({
      title: data?.title || '',
      description: data?.description || '',
      questionSetId: data?.questionSetId ? String(data.questionSetId) : '',
      type: data?.type || 'Quiz',
      startAt: data?.startAt ? String(data.startAt).slice(0, 16) : '',
      dueAt: data?.dueAt ? String(data.dueAt).slice(0, 16) : '',
      timeLimitMinutes: data?.timeLimitMinutes ? String(data.timeLimitMinutes) : '',
      attemptLimit: data?.attemptLimit ? String(data.attemptLimit) : '1',
      shuffleQuestions: Boolean(data?.shuffleQuestions),
      shuffleOptions: Boolean(data?.shuffleOptions),
      showAnswerAfterSubmit: Boolean(data?.showAnswerAfterSubmit),
      scoringMode: data?.scoringMode || 'Percent',
      minQuestionWeight: data?.minQuestionWeight != null ? String(data.minQuestionWeight) : '0.3',
      maxQuestionWeight: data?.maxQuestionWeight != null ? String(data.maxQuestionWeight) : '2.0',
      smoothingAlpha: data?.smoothingAlpha != null ? String(data.smoothingAlpha) : '1',
      smoothingBeta: data?.smoothingBeta != null ? String(data.smoothingBeta) : '1',
    });
  };

  const loadDetail = useCallback(async () => {
    setLoading(true);
    setError('');

    try {
      const classroomData = await classroomService.getClassroomDetail(classroomId);
      setClassroom(classroomData);
      if (classroomData?.currentUserRole !== ROLE_TEACHER) {
        setAssignment(null);
        return;
      }

      const [assignmentData, questionSetData] = await Promise.all([
        classroomService.getClassroomAssignmentDetail(assignmentId),
        classroomService.getClassroomQuestionSets(classroomId),
      ]);
      setAssignment(assignmentData);
      setQuestionSets((Array.isArray(questionSetData) ? questionSetData : []).filter((set) => set.visibility === 'Published'));
      syncForm(assignmentData);

      if (assignmentData?.status === 'Closed' && assignmentData?.scoringMode === 'EmpiricalDifficulty') {
        try {
          const stats = await classroomService.getClassroomAssignmentQuestionStats(assignmentId);
          setQuestionStats(stats || []);
        } catch (err) {
          console.error('Failed to load assignment question stats:', err);
        }
      } else {
        setQuestionStats([]);
      }
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.detail', 'Khong tai duoc assignment.')));
    } finally {
      setLoading(false);
    }
  }, [assignmentId, classroomId, t]);

  useEffect(() => {
    loadDetail();
  }, [loadDetail]);

  const runAction = async (action, successMessage, fallbackMessage) => {
    setWorking(true);
    setError('');
    setSuccess('');

    try {
      const updated = await action();
      if (updated) {
        setAssignment(updated);
        syncForm(updated);
        if (updated.status === 'Closed' && updated.scoringMode === 'EmpiricalDifficulty') {
          try {
            const stats = await classroomService.getClassroomAssignmentQuestionStats(assignmentId);
            setQuestionStats(stats || []);
          } catch (err) {
            console.error('Failed to load assignment question stats after action:', err);
          }
        } else {
          setQuestionStats([]);
        }
      } else {
        await loadDetail();
      }
      setSuccess(successMessage);
    } catch (err) {
      setError(getApiErrorMessage(err, fallbackMessage));
    } finally {
      setWorking(false);
    }
  };

  const handleUpdate = async (event) => {
    event.preventDefault();
    if (!form.title.trim()) {
      setError(getText(t, 'classrooms.assignments.errors.titleRequired', 'Nhap tieu de assignment.'));
      return;
    }

    const validationError = validateScoringForm(form, t);
    if (validationError) {
      setError(validationError);
      return;
    }

    await runAction(
      () => classroomService.updateClassroomAssignment(assignmentId, buildAssignmentPayload(form)),
      getText(t, 'classrooms.assignments.feedback.updated', 'Da cap nhat assignment.'),
      getText(t, 'classrooms.assignments.errors.update', 'Khong cap nhat duoc assignment.')
    );
  };

  const handleDelete = async () => {
    if (!window.confirm(getText(t, 'classrooms.assignments.confirmDelete', 'Xoa assignment nay?'))) {
      return;
    }

    setWorking(true);
    setError('');
    try {
      await classroomService.deleteClassroomAssignment(assignmentId);
      navigate(`/classrooms/${classroomId}/assignments`);
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.delete', 'Khong xoa duoc assignment.')));
      setWorking(false);
    }
  };

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.detailTitle', 'Chi tiet assignment')} subtitle="">
        <ClassroomTabs />
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Dang tai...')} />
      </ClassroomShell>
    );
  }

  if (!isTeacher) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.detailTitle', 'Chi tiet assignment')} subtitle={classroom?.name || ''}>
        <ClassroomTabs />
        <MessageBar error={getText(t, 'classrooms.assignments.errors.teacherOnly', 'Chi giao vien cua lop moi quan ly assignment.')} />
        <Link className="classroom-button" to={`/classrooms/${classroomId}/student/assignments/${assignmentId}`}>
          <LuGraduationCap aria-hidden="true" />
          {getText(t, 'classrooms.assignments.openStudentView', 'Mo trang hoc vien')}
        </Link>
      </ClassroomShell>
    );
  }

  if (error && !assignment) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.detailTitle', 'Chi tiet assignment')} subtitle={classroom?.name || ''}>
        <ClassroomTabs />
        <MessageBar error={error} />
        <Link className="classroom-button" to={`/classrooms/${classroomId}/assignments`}>
          <LuClipboard aria-hidden="true" />
          {getText(t, 'classrooms.assignments.backToList', 'Ve danh sach')}
        </Link>
      </ClassroomShell>
    );
  }

  const items = Array.isArray(assignment?.items) ? assignment.items : [];

  return (
    <ClassroomShell title={assignment?.title || getText(t, 'classrooms.assignments.detailTitle', 'Chi tiet assignment')} subtitle={classroom?.name || ''}>
      <ClassroomTabs />
      <MessageBar error={error} success={success} />
      <div className="classroom-page-actions">
        <Link className="classroom-button" to={`/classrooms/${classroomId}/assignments`}>
          <LuClipboard aria-hidden="true" />
          {getText(t, 'classrooms.assignments.backToList', 'Ve danh sach')}
        </Link>
        <Link className="classroom-button" to={`/classrooms/${classroomId}/assignments/${assignmentId}/attempts`}>
          <LuListChecks aria-hidden="true" />
          {getText(t, 'classrooms.assignments.viewAttempts', 'Attempts')}
        </Link>
        <button className="classroom-button" type="button" onClick={loadDetail}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.actions.refresh', 'Lam moi')}
        </button>
      </div>

      <section className="classroom-detail-grid">
        <article className="classroom-panel classroom-summary">
          <span className={`classroom-badge ${assignment?.status === 'Published' ? '' : 'muted'}`}>{assignment?.status || 'Draft'}</span>
          <h2>{assignment?.title}</h2>
          <p>{assignment?.description || getText(t, 'classrooms.assignments.noDescription', 'Chua co mo ta.')}</p>
          <div className="classroom-metrics">
            <Metric label={getText(t, 'classrooms.assignments.itemCount', 'Cau hoi')} value={assignment?.itemCount || items.length} />
            <Metric label={getText(t, 'classrooms.assignments.totalPoints', 'Diem')} value={assignment?.totalPoints || 0} />
            <Metric label={getText(t, 'classrooms.assignments.attemptLimit', 'Lan lam')} value={assignment?.attemptLimit || 1} />
          </div>
          <small className="classroom-muted">
            {assignment?.type} | Due: {formatDateTime(assignment?.dueAt)}
          </small>

          <div className="classroom-scoring-summary">
            <h3>{getText(t, 'classrooms.assignments.fields.scoringMode', 'Cách chấm điểm')}</h3>
            {assignment?.scoringMode === 'EmpiricalDifficulty' ? (
              <div className="empirical-summary-box">
                <p className="scoring-mode-name text-primary">
                  {getText(t, 'classrooms.assignments.empiricalScoring', 'Chấm theo độ khó thực nghiệm')}
                </p>
                <div className="classroom-scoring-params">
                  <div><strong>{getText(t, 'classrooms.assignments.minQuestionWeight', 'Trọng số tối thiểu')}:</strong> {assignment?.minQuestionWeight}</div>
                  <div><strong>{getText(t, 'classrooms.assignments.maxQuestionWeight', 'Trọng số tối đa')}:</strong> {assignment?.maxQuestionWeight}</div>
                  <div><strong>Smoothing alpha:</strong> {assignment?.smoothingAlpha}</div>
                  <div><strong>Smoothing beta:</strong> {assignment?.smoothingBeta}</div>
                </div>
                <p className="classroom-help-text text-muted">
                  {getText(t, 'classrooms.assignments.empiricalDetailHelp', 'Điểm chính thức được tính khi giảng viên đóng assignment. Hệ thống dùng tỷ lệ trả lời đúng của cả lớp để tính trọng số từng câu.')}
                </p>
              </div>
            ) : (
              <p className="scoring-mode-name text-muted">
                {getText(t, 'classrooms.assignments.percentScoring', 'Chấm theo phần trăm')}
              </p>
            )}
          </div>
        </article>

        <form className="classroom-panel classroom-form" onSubmit={handleUpdate}>
          <div className="classroom-section-head">
            <div>
              <span className="classroom-kicker">{getText(t, 'classrooms.assignments.teacherTools', 'Teacher tools')}</span>
              <h2>{getText(t, 'classrooms.assignments.editTitle', 'Sua assignment')}</h2>
            </div>
            <div className="classroom-row-actions">
              {assignment?.status !== 'Published' && (
                <button className="classroom-button primary" type="button" onClick={() => runAction(
                  () => classroomService.publishClassroomAssignment(assignmentId),
                  getText(t, 'classrooms.assignments.feedback.published', 'Da publish assignment.'),
                  getText(t, 'classrooms.assignments.errors.publish', 'Khong publish duoc assignment.')
                )} disabled={working}>
                  <LuCheck aria-hidden="true" />
                  {getText(t, 'classrooms.assignments.publish', 'Publish')}
                </button>
              )}
              {assignment?.status !== 'Closed' && (
                <button className="classroom-button" type="button" onClick={() => runAction(
                  () => classroomService.closeClassroomAssignment(assignmentId),
                  getText(t, 'classrooms.assignments.feedback.closed', 'Da dong assignment.'),
                  getText(t, 'classrooms.assignments.errors.close', 'Khong dong duoc assignment.')
                )} disabled={working}>
                  <LuBan aria-hidden="true" />
                  {getText(t, 'classrooms.assignments.close', 'Close')}
                </button>
              )}
              <button className="classroom-icon-button danger" type="button" onClick={handleDelete} disabled={working} title={getText(t, 'classrooms.assignments.delete', 'Xoa')}>
                <LuTrash2 aria-hidden="true" />
              </button>
            </div>
          </div>
          <AssignmentFields form={form} onChange={setForm} questionSets={questionSets} t={t} />
          <button className="classroom-button primary" type="submit" disabled={working}>
            <LuSave aria-hidden="true" />
            {getText(t, 'classrooms.assignments.save', 'Luu')}
          </button>
        </form>
      </section>

      {assignment?.status === 'Closed' && assignment?.scoringMode === 'EmpiricalDifficulty' && (
        <section className="classroom-panel classroom-question-stats">
          <div className="classroom-section-head">
            <div>
              <span className="classroom-kicker">{getText(t, 'classrooms.assignments.empiricalScoring', 'Chấm theo độ khó thực nghiệm')}</span>
              <h2>{getText(t, 'classrooms.assignments.questionStatsTitle', 'Thống kê độ khó câu hỏi')}</h2>
            </div>
          </div>
          {questionStats && questionStats.length > 0 ? (
            <div className="classroom-table-wrapper">
              <table className="classroom-stats-table">
                <thead>
                  <tr>
                    <th>{getText(t, 'classrooms.assignments.stats.questionId', 'Question ID')}</th>
                    <th>{getText(t, 'classrooms.assignments.stats.answeredCount', 'Lượt làm')}</th>
                    <th>{getText(t, 'classrooms.assignments.stats.correctCount', 'Lượt đúng')}</th>
                    <th>{getText(t, 'classrooms.assignments.stats.smoothedCorrectRate', 'Tỷ lệ đúng đã làm mượt')}</th>
                    <th>{getText(t, 'classrooms.assignments.stats.difficultyWeight', 'Trọng số độ khó')}</th>
                    <th>{getText(t, 'classrooms.assignments.stats.qualityFlag', 'Trạng thái chất lượng')}</th>
                    <th>{getText(t, 'classrooms.assignments.stats.calculatedAt', 'Thời gian tính')}</th>
                  </tr>
                </thead>
                <tbody>
                  {questionStats.map((stat) => {
                    let qualityText = getText(t, 'classrooms.assignments.stable', 'Ổn');
                    let qualityClass = 'badge-success';
                    if (stat.qualityFlag === 'InsufficientData') {
                      qualityText = getText(t, 'classrooms.assignments.insufficientData', 'Chưa đủ dữ liệu');
                      qualityClass = 'badge-warning';
                    } else if (stat.qualityFlag === 'LowDiscrimination') {
                      qualityText = getText(t, 'classrooms.assignments.lowDiscrimination', 'Khả năng phân loại thấp');
                      qualityClass = 'badge-warning';
                    } else if (stat.qualityFlag === 'SuspiciousItem') {
                      qualityText = getText(t, 'classrooms.assignments.suspiciousItem', 'Câu hỏi cần xem lại');
                      qualityClass = 'badge-danger';
                    }
                    return (
                      <tr key={stat.id}>
                        <td>#{stat.questionId}</td>
                        <td>{stat.answeredCount}</td>
                        <td>{stat.correctCount}</td>
                        <td>{(stat.smoothedCorrectRate * 100).toFixed(1)}%</td>
                        <td>{Number(stat.difficultyWeight).toFixed(3)}</td>
                        <td>
                          <span className={`classroom-stat-badge ${qualityClass}`}>{qualityText}</span>
                        </td>
                        <td>{formatDateTime(stat.calculatedAt)}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          ) : (
            <p className="classroom-muted">{getText(t, 'classrooms.assignments.stats.noData', 'Chưa có dữ liệu thống kê câu hỏi.')}</p>
          )}
        </section>
      )}

      <section className="classroom-panel classroom-question-set-items">
        <div className="classroom-section-head">
          <div>
            <span className="classroom-kicker">{getText(t, 'classrooms.assignments.questions', 'Questions')}</span>
            <h2>{getText(t, 'classrooms.assignments.questionsInAssignment', 'Cau hoi trong assignment')}</h2>
          </div>
        </div>
        <AssignmentQuestions items={items} showSensitive />
      </section>
    </ClassroomShell>
  );
}

export function ClassroomAssignmentTeacherAttemptsPage() {
  const { classroomId, assignmentId } = useParams();
  const { t } = useLanguage();
  const [classroom, setClassroom] = useState(null);
  const [assignment, setAssignment] = useState(null);
  const [attempts, setAttempts] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const loadPage = useCallback(async () => {
    setLoading(true);
    setError('');

    try {
      const classroomData = await classroomService.getClassroomDetail(classroomId);
      setClassroom(classroomData);
      if (classroomData?.currentUserRole !== ROLE_TEACHER) {
        setAttempts([]);
        return;
      }

      const [assignmentData, attemptsData] = await Promise.all([
        classroomService.getClassroomAssignmentDetail(assignmentId),
        classroomService.getClassroomAssignmentAttempts(assignmentId),
      ]);
      setAssignment(assignmentData);
      setAttempts(Array.isArray(attemptsData) ? attemptsData : []);
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.attempts', 'Khong tai duoc attempts.')));
    } finally {
      setLoading(false);
    }
  }, [assignmentId, classroomId, t]);

  useEffect(() => {
    loadPage();
  }, [loadPage]);

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.attemptsTitle', 'Attempts')} subtitle="">
        <ClassroomTabs />
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Dang tai...')} />
      </ClassroomShell>
    );
  }

  if (classroom?.currentUserRole !== ROLE_TEACHER) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.attemptsTitle', 'Attempts')} subtitle={classroom?.name || ''}>
        <ClassroomTabs />
        <MessageBar error={getText(t, 'classrooms.assignments.errors.teacherOnly', 'Chi giao vien cua lop moi xem attempts.')} />
      </ClassroomShell>
    );
  }

  return (
    <ClassroomShell title={getText(t, 'classrooms.assignments.attemptsTitle', 'Attempts')} subtitle={assignment?.title || classroom?.name || ''}>
      <ClassroomTabs />
      <MessageBar error={error} />
      <div className="classroom-page-actions">
        <Link className="classroom-button" to={`/classrooms/${classroomId}/assignments/${assignmentId}`}>
          <LuClipboard aria-hidden="true" />
          {getText(t, 'classrooms.assignments.backToDetail', 'Ve assignment')}
        </Link>
        <button className="classroom-button" type="button" onClick={loadPage}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.actions.refresh', 'Lam moi')}
        </button>
      </div>

      <section className="classroom-panel classroom-question-set-items">
        {!attempts.length ? (
          <p className="classroom-muted">{getText(t, 'classrooms.assignments.noAttempts', 'Chua co attempt nao.')}</p>
        ) : (
          <div className="classroom-question-list">
            {attempts.map((attempt) => (
              <article className="classroom-question-row" key={attempt.id}>
                <div>
                  <strong>{attempt.user?.fullName || attempt.user?.email || `User ${attempt.userId}`}</strong>
                  <small>
                    Attempt #{attempt.attemptNumber || '-'} | {attempt.status}
                    {' | '}
                    Score: {attempt.rawScore ?? '-'} / {attempt.percentScore != null ? `${attempt.percentScore}%` : '-'}
                    {' | '}
                    Started: {formatDateTime(attempt.startedAt)}
                    {' | '}
                    Submitted: {formatDateTime(attempt.submittedAt)}
                    {' | '}
                    Duration: {attempt.durationSeconds ?? 0}s
                  </small>
                  <AttemptAnswers answers={attempt.answers || []} reveal />
                </div>
                <Link className="classroom-button" to={`/classroom-attempts/${attempt.id}/result`}>
                  {getText(t, 'classrooms.assignments.viewAttempt', 'Xem chi tiet')}
                </Link>
              </article>
            ))}
          </div>
        )}
      </section>
    </ClassroomShell>
  );
}

export function StudentClassroomAssignmentsPage() {
  const { classroomId } = useParams();
  const { t } = useLanguage();
  const navigate = useNavigate();
  const [classroom, setClassroom] = useState(null);
  const [assignments, setAssignments] = useState([]);
  const [attempts, setAttempts] = useState([]);
  const [loading, setLoading] = useState(true);
  const [startingId, setStartingId] = useState(null);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const loadPage = useCallback(async () => {
    setLoading(true);
    setError('');

    try {
      const [classroomData, assignmentData, attemptsData] = await Promise.all([
        classroomService.getClassroomDetail(classroomId),
        classroomService.getStudentClassroomAssignments(classroomId),
        classroomService.getMyClassroomAssignmentAttempts(),
      ]);
      setClassroom(classroomData);
      setAssignments(Array.isArray(assignmentData) ? assignmentData : []);
      setAttempts(Array.isArray(attemptsData) ? attemptsData : []);
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.studentLoad', 'Khong tai duoc assignment cua hoc vien.')));
    } finally {
      setLoading(false);
    }
  }, [classroomId, t]);

  useEffect(() => {
    loadPage();
  }, [loadPage]);

  const startAssignment = async (assignmentId) => {
    setStartingId(assignmentId);
    setError('');
    setSuccess('');
    try {
      const attempt = await classroomService.startClassroomAssignmentAttempt(assignmentId);
      setSuccess(getText(t, 'classrooms.assignments.feedback.started', 'Da mo attempt.'));
      navigate(`/classroom-attempts/${attempt.id}`);
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.start', 'Khong bat dau duoc assignment.')));
    } finally {
      setStartingId(null);
    }
  };

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.studentTitle', 'Assignments cua hoc vien')} subtitle="">
        <ClassroomTabs />
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Dang tai...')} />
      </ClassroomShell>
    );
  }

  return (
    <ClassroomShell title={getText(t, 'classrooms.assignments.studentTitle', 'Assignments cua hoc vien')} subtitle={classroom?.name || ''}>
      <ClassroomTabs />
      <MessageBar error={error} success={success} />
      <ClassroomResourceLinks classroomId={classroomId} isTeacher={false} t={t} />
      <div className="classroom-page-actions">
        <Link className="classroom-button" to="/classroom-attempts/history">
          <LuClipboard aria-hidden="true" />
          {getText(t, 'classrooms.assignments.history', 'Lich su lam bai')}
        </Link>
      </div>
      <AssignmentList
        assignments={assignments}
        attempts={attempts}
        classroomId={classroomId}
        emptyBody={getText(t, 'classrooms.assignments.studentEmpty', 'Chua co assignment da Published.')}
        loading={false}
        onRetry={loadPage}
        onStart={startAssignment}
        startingId={startingId}
        t={t}
      />
    </ClassroomShell>
  );
}

export function StudentClassroomAssignmentDetailPage() {
  const { classroomId, assignmentId } = useParams();
  const { t } = useLanguage();
  const navigate = useNavigate();
  const [classroom, setClassroom] = useState(null);
  const [assignment, setAssignment] = useState(null);
  const [attempts, setAttempts] = useState([]);
  const [loading, setLoading] = useState(true);
  const [starting, setStarting] = useState(false);
  const [error, setError] = useState('');

  const loadPage = useCallback(async () => {
    setLoading(true);
    setError('');

    try {
      const [classroomData, assignmentsData, attemptsData] = await Promise.all([
        classroomService.getClassroomDetail(classroomId),
        classroomService.getStudentClassroomAssignments(classroomId),
        classroomService.getMyClassroomAssignmentAttempts(),
      ]);
      setClassroom(classroomData);
      setAttempts(Array.isArray(attemptsData) ? attemptsData : []);
      const visibleAssignment = (Array.isArray(assignmentsData) ? assignmentsData : [])
        .find((item) => String(item.id) === String(assignmentId));
      if (!visibleAssignment) {
        setAssignment(null);
        setError(getText(t, 'classrooms.assignments.errors.studentForbidden', 'Assignment khong kha dung cho hoc vien nay.'));
        return;
      }
      setAssignment(visibleAssignment);
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.studentDetail', 'Khong tai duoc assignment.')));
    } finally {
      setLoading(false);
    }
  }, [assignmentId, classroomId, t]);

  useEffect(() => {
    loadPage();
  }, [loadPage]);

  const startAssignment = async () => {
    setStarting(true);
    setError('');
    try {
      const attempt = await classroomService.startClassroomAssignmentAttempt(assignmentId);
      navigate(`/classroom-attempts/${attempt.id}`);
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.start', 'Khong bat dau duoc assignment.')));
    } finally {
      setStarting(false);
    }
  };

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.studentDetailTitle', 'Assignment')} subtitle="">
        <ClassroomTabs />
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Dang tai...')} />
      </ClassroomShell>
    );
  }

  const attempt = findLatestAttemptForAssignment(attempts, assignmentId);
  const items = Array.isArray(assignment?.items) ? assignment.items : [];

  return (
    <ClassroomShell title={assignment?.title || getText(t, 'classrooms.assignments.studentDetailTitle', 'Assignment')} subtitle={classroom?.name || ''}>
      <ClassroomTabs />
      <MessageBar error={error} />
      <div className="classroom-page-actions">
        <Link className="classroom-button" to={`/classrooms/${classroomId}/student/assignments`}>
          <LuClipboard aria-hidden="true" />
          {getText(t, 'classrooms.assignments.backToList', 'Ve danh sach')}
        </Link>
        {attempt?.status === 'InProgress' ? (
          <Link className="classroom-button primary" to={`/classroom-attempts/${attempt.id}`}>
            <LuCheck aria-hidden="true" />
            {getText(t, 'classrooms.assignments.resume', 'Tiep tuc lam')}
          </Link>
        ) : (
          <button className="classroom-button primary" type="button" onClick={startAssignment} disabled={starting || !assignment}>
            <LuCheck aria-hidden="true" />
            {starting ? getText(t, 'classrooms.assignments.starting', 'Dang mo...') : getText(t, 'classrooms.assignments.start', 'Start')}
          </button>
        )}
      </div>

      {assignment && (
        <>
          <section className="classroom-panel classroom-summary">
            <span className="classroom-badge">{assignment.status}</span>
            <h2>{assignment.title}</h2>
            <p>{assignment.description || getText(t, 'classrooms.assignments.noDescription', 'Chua co mo ta.')}</p>
            <div className="classroom-metrics">
              <Metric label={getText(t, 'classrooms.assignments.itemCount', 'Cau hoi')} value={assignment.itemCount || items.length} />
              <Metric label={getText(t, 'classrooms.assignments.totalPoints', 'Diem')} value={assignment.totalPoints || 0} />
              <Metric label={getText(t, 'classrooms.assignments.attemptLimit', 'Lan lam')} value={assignment.attemptLimit || 1} />
            </div>
            {assignment.scoringMode === 'EmpiricalDifficulty' && (
              <div className="classroom-scoring-mode-notice text-primary" style={{ marginTop: '1rem', fontWeight: 500 }}>
                {getText(t, 'classrooms.assignments.empiricalScoringNote', 'Điểm được tính theo độ khó thực nghiệm của câu hỏi.')}
              </div>
            )}
          </section>

          {assignment.scoringMode === 'EmpiricalDifficulty' && (
            <div className="classroom-info-banner warning" style={{ marginBottom: '1rem' }}>
              <p>
                {assignment.status === 'Closed'
                  ? getText(t, 'classrooms.assignments.empiricalScoringFinalizedNote', 'Assignment dùng cơ chế chấm theo độ khó thực nghiệm. Giảng viên đã đóng bài thi, điểm số này đã được tính toán chính thức.')
                  : getText(t, 'classrooms.assignments.empiricalScoringAttemptNote', 'Assignment dùng cơ chế chấm theo độ khó thực nghiệm. Điểm chính thức được xác định khi giảng viên đóng assignment.')}
              </p>
            </div>
          )}

          <section className="classroom-panel classroom-question-set-items">
            <AssignmentQuestions items={items} />
          </section>
        </>
      )}
    </ClassroomShell>
  );
}

export function ClassroomAssignmentAttemptPage() {
  const { attemptId } = useParams();
  const { t } = useLanguage();
  const navigate = useNavigate();
  const [attempt, setAttempt] = useState(null);
  const [answers, setAnswers] = useState({});
  const [loading, setLoading] = useState(true);
  const [workingQuestionId, setWorkingQuestionId] = useState(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const loadAttempt = useCallback(async () => {
    setLoading(true);
    setError('');

    try {
      const data = await classroomService.getClassroomAssignmentAttemptDetail(attemptId);
      setAttempt(data);
      const nextAnswers = {};
      (Array.isArray(data?.answers) ? data.answers : []).forEach((answer) => {
        nextAnswers[answer.questionId] = answer.selectedAnswer || '';
      });
      setAnswers(nextAnswers);
      if (data?.status === 'Submitted') {
        navigate(`/classroom-attempts/${attemptId}/result`, { replace: true });
      }
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.attemptDetail', 'Khong tai duoc attempt.')));
    } finally {
      setLoading(false);
    }
  }, [attemptId, navigate, t]);

  useEffect(() => {
    loadAttempt();
  }, [loadAttempt]);

  const items = Array.isArray(attempt?.assignment?.items) ? attempt.assignment.items : [];
  const answeredCount = items.filter((item) => answers[item.questionId]).length;

  const submitAnswer = async (questionId) => {
    setWorkingQuestionId(questionId);
    setError('');
    setSuccess('');

    try {
      await classroomService.submitClassroomAssignmentAnswer(attemptId, {
        questionId,
        selectedAnswer: answers[questionId] || '',
        timeSpentSeconds: null,
      });
      setSuccess(getText(t, 'classrooms.assignments.feedback.answerSaved', 'Da luu cau tra loi.'));
      await loadAttempt();
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.answer', 'Khong luu duoc cau tra loi.')));
    } finally {
      setWorkingQuestionId(null);
    }
  };

  const submitAttempt = async () => {
    if (!window.confirm(getText(t, 'classrooms.assignments.confirmSubmit', 'Nop bai? Ban se khong the sua cau tra loi sau khi nop.'))) {
      return;
    }

    setSubmitting(true);
    setError('');
    setSuccess('');

    try {
      await classroomService.submitClassroomAssignmentAttempt(attemptId);
      navigate(`/classroom-attempts/${attemptId}/result`);
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.submitAttempt', 'Khong nop duoc bai.')));
    } finally {
      setSubmitting(false);
    }
  };

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.attemptTitle', 'Lam assignment')} subtitle="">
        <ClassroomTabs />
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Dang tai...')} />
      </ClassroomShell>
    );
  }

  return (
    <ClassroomShell title={attempt?.assignment?.title || getText(t, 'classrooms.assignments.attemptTitle', 'Lam assignment')} subtitle={`${answeredCount}/${items.length} ${getText(t, 'classrooms.assignments.answered', 'da tra loi')}`}>
      <ClassroomTabs />
      <MessageBar error={error} success={success} />
      <section className="classroom-panel classroom-attempt-toolbar">
        <div>
          <span className="classroom-kicker">{attempt?.status || 'InProgress'}</span>
          <h2>{getText(t, 'classrooms.assignments.progress', 'Tien do')}: {answeredCount}/{items.length}</h2>
        </div>
        <button className="classroom-button primary" type="button" onClick={submitAttempt} disabled={submitting || !items.length}>
          <LuCheck aria-hidden="true" />
          {submitting ? getText(t, 'classrooms.assignments.submitting', 'Dang nop...') : getText(t, 'classrooms.assignments.submitAttempt', 'Nop bai')}
        </button>
      </section>

      <section className="classroom-question-list">
        {items.map((item, index) => (
          <QuestionAttemptCard
            answer={answers[item.questionId] || ''}
            item={item}
            key={item.id || item.questionId}
            onAnswer={(value) => setAnswers((current) => ({ ...current, [item.questionId]: value }))}
            onSubmit={() => submitAnswer(item.questionId)}
            saving={workingQuestionId === item.questionId}
            t={t}
            index={index}
          />
        ))}
      </section>
    </ClassroomShell>
  );
}

export function ClassroomAssignmentResultPage() {
  const { attemptId } = useParams();
  const { t } = useLanguage();
  const [attempt, setAttempt] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const loadAttempt = useCallback(async () => {
    setLoading(true);
    setError('');

    try {
      const data = await classroomService.getClassroomAssignmentAttemptDetail(attemptId);
      setAttempt(data);
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.result', 'Khong tai duoc ket qua.')));
    } finally {
      setLoading(false);
    }
  }, [attemptId, t]);

  useEffect(() => {
    loadAttempt();
  }, [loadAttempt]);

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.resultTitle', 'Ket qua assignment')} subtitle="">
        <ClassroomTabs />
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Dang tai...')} />
      </ClassroomShell>
    );
  }

  const answers = Array.isArray(attempt?.answers) ? attempt.answers : [];
  const reveal = answers.some((answer) => Object.prototype.hasOwnProperty.call(answer, 'isCorrect') || answer.question?.correctAnswer);

  return (
    <ClassroomShell title={getText(t, 'classrooms.assignments.resultTitle', 'Ket qua assignment')} subtitle={attempt?.assignment?.title || ''}>
      <ClassroomTabs />
      <MessageBar error={error} />
      <div className="classroom-page-actions">
        <Link className="classroom-button" to="/classroom-attempts/history">
          <LuClipboard aria-hidden="true" />
          {getText(t, 'classrooms.assignments.history', 'Lich su lam bai')}
        </Link>
      </div>
      {attempt && (
        <>
          <section className="classroom-panel classroom-summary">
            <span className="classroom-badge">{attempt.status}</span>
            <div className="classroom-metrics">
              <Metric label={getText(t, 'classrooms.assignments.rawScore', 'Diem')} value={attempt.rawScore ?? '-'} />
              <Metric label={getText(t, 'classrooms.assignments.percentScore', 'Phan tram')} value={attempt.percentScore != null ? `${attempt.percentScore}%` : '-'} />
              <Metric label={getText(t, 'classrooms.assignments.answeredCount', 'Da tra loi')} value={answers.length} />
            </div>
            {attempt.assignment?.scoringMode === 'EmpiricalDifficulty' && (
              <div className="classroom-scoring-mode-notice text-primary" style={{ marginTop: '1rem', fontWeight: 500 }}>
                {getText(t, 'classrooms.assignments.empiricalScoringNote', 'Điểm được tính theo độ khó thực nghiệm của câu hỏi.')}
              </div>
            )}
            {!attempt.assignment?.showAnswerAfterSubmit && (
              <p className="classroom-muted" style={{ marginTop: '0.5rem' }}>{getText(t, 'classrooms.assignments.hiddenAnswers', 'Giao vien dang an dap an dung; trang nay chi hien tong diem.')}</p>
            )}
          </section>

          {attempt.assignment?.scoringMode === 'EmpiricalDifficulty' && (
            <div className="classroom-info-banner warning" style={{ marginBottom: '1rem' }}>
              <p>
                {attempt.assignment?.status === 'Closed'
                  ? getText(t, 'classrooms.assignments.empiricalScoringFinalizedNote', 'Assignment dùng cơ chế chấm theo độ khó thực nghiệm. Giảng viên đã đóng bài thi, điểm số này đã được tính toán chính thức.')
                  : getText(t, 'classrooms.assignments.empiricalScoringAttemptNote', 'Assignment dùng cơ chế chấm theo độ khó thực nghiệm. Điểm chính thức được xác định khi giảng viên đóng assignment.')}
              </p>
            </div>
          )}

          {reveal && (
            <section className="classroom-panel classroom-question-set-items">
              <div className="classroom-section-head">
                <div>
                  <span className="classroom-kicker">{getText(t, 'classrooms.assignments.review', 'Review')}</span>
                  <h2>{getText(t, 'classrooms.assignments.answerReview', 'Xem lai cau tra loi')}</h2>
                </div>
              </div>
              <AttemptAnswers answers={answers} reveal />
            </section>
          )}
        </>
      )}
    </ClassroomShell>
  );
}

export function ClassroomAssignmentHistoryPage() {
  const { t } = useLanguage();
  const [attempts, setAttempts] = useState([]);
  const [classrooms, setClassrooms] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const loadHistory = useCallback(async () => {
    setLoading(true);
    setError('');

    try {
      const [attemptData, joinedData] = await Promise.all([
        classroomService.getMyClassroomAssignmentAttempts(),
        classroomService.getJoinedClassrooms(),
      ]);
      setAttempts(Array.isArray(attemptData) ? attemptData : []);
      setClassrooms(Array.isArray(joinedData) ? joinedData : []);
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.history', 'Khong tai duoc lich su assignment.')));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => {
    loadHistory();
  }, [loadHistory]);

  const classroomById = new Map(classrooms.map((classroom) => [String(getClassroomId(classroom)), classroom]));

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.historyTitle', 'Lich su assignment')} subtitle="">
        <ClassroomTabs />
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Dang tai...')} />
      </ClassroomShell>
    );
  }

  return (
    <ClassroomShell title={getText(t, 'classrooms.assignments.historyTitle', 'Lich su assignment')} subtitle={getText(t, 'classrooms.assignments.historySubtitle', 'Tat ca attempt cua ban trong classroom.')}>
      <ClassroomTabs />
      <MessageBar error={error} />
      <div className="classroom-page-actions">
        <button className="classroom-button" type="button" onClick={loadHistory}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.actions.refresh', 'Lam moi')}
        </button>
      </div>

      {!attempts.length ? (
        <section className="classroom-panel classroom-empty">
          <LuClipboard aria-hidden="true" />
          <h2>{getText(t, 'classrooms.assignments.historyEmptyTitle', 'Chua co attempt')}</h2>
          <p>{getText(t, 'classrooms.assignments.historyEmptyBody', 'Bat dau mot assignment de lich su xuat hien tai day.')}</p>
        </section>
      ) : (
        <section className="classroom-list">
          {attempts.map((attempt) => {
            const classroom = classroomById.get(String(attempt.assignment?.classroomWorkspaceId));
            return (
              <article className="classroom-card" key={attempt.id}>
                <span className="classroom-card-icon"><LuClipboard aria-hidden="true" /></span>
                <div>
                  <div className="classroom-card-title-row">
                    <h2>{attempt.assignment?.title || `Assignment #${attempt.classroomAssignmentId}`}</h2>
                    <span className={`classroom-badge ${attempt.status === 'Submitted' ? '' : 'muted'}`}>{attempt.status}</span>
                  </div>
                  <p>{classroom?.name || `Classroom #${attempt.assignment?.classroomWorkspaceId || '-'}`}</p>
                  <small style={{ display: 'block', marginBottom: '0.5rem' }}>
                    Attempt #{attempt.attemptNumber || '-'} | Started: {formatDateTime(attempt.startedAt)}
                    {' | '}
                    Submitted: {formatDateTime(attempt.submittedAt)}
                    {' | '}
                    Score: {attempt.rawScore ?? '-'} / {attempt.percentScore != null ? `${attempt.percentScore}%` : '-'}
                    {attempt.assignment?.scoringMode === 'EmpiricalDifficulty' && (
                      <span className="scoring-badge-pill" style={{ marginLeft: '0.5rem', color: 'var(--color-primary)', fontWeight: 500 }}>
                        {attempt.assignment?.status === 'Closed'
                          ? `(${getText(t, 'classrooms.assignments.final', 'Chính thức')})`
                          : `(${getText(t, 'classrooms.assignments.tempScore', 'Điểm tạm thời')})`}
                      </span>
                    )}
                  </small>
                  <div className="classroom-row-actions classroom-card-actions">
                    {attempt.status === 'InProgress' ? (
                      <Link className="classroom-button primary" to={`/classroom-attempts/${attempt.id}`}>
                        {getText(t, 'classrooms.assignments.resume', 'Tiep tuc')}
                      </Link>
                    ) : (
                      <Link className="classroom-button primary" to={`/classroom-attempts/${attempt.id}/result`}>
                        {getText(t, 'classrooms.assignments.result', 'Ket qua')}
                      </Link>
                    )}
                  </div>
                </div>
              </article>
            );
          })}
        </section>
      )}
    </ClassroomShell>
  );
}

function ClassroomShell({ children, subtitle, title }) {
  return (
    <main className="classroom-page">
      <header className="classroom-hero">
        <div>
          <span className="classroom-kicker">Classroom</span>
          <h1>{title}</h1>
          {subtitle && <p>{subtitle}</p>}
        </div>
      </header>
      {children}
    </main>
  );
}

function ClassroomTabs({ active }) {
  const { t } = useLanguage();
  const tabs = [
    { id: 'teaching', to: '/classrooms/teaching', label: getText(t, 'classrooms.tabs.teaching', 'Dang day'), icon: <LuSchool aria-hidden="true" /> },
    { id: 'joined', to: '/classrooms/joined', label: getText(t, 'classrooms.tabs.joined', 'Da tham gia'), icon: <LuGraduationCap aria-hidden="true" /> },
    { id: 'join', to: '/classrooms/join', label: getText(t, 'classrooms.tabs.join', 'Nhap code'), icon: <LuDoorOpen aria-hidden="true" /> },
  ];

  return (
    <nav className="classroom-tabs" aria-label="Classroom navigation">
      {tabs.map((tab) => (
        <Link key={tab.id} className={active === tab.id ? 'active' : ''} to={tab.to}>
          {tab.icon}
          {tab.label}
        </Link>
      ))}
    </nav>
  );
}

function ClassroomList({ classrooms, emptyBody, emptyTitle, loading, onRetry, retryLabel, t }) {
  if (loading) {
    return <LoadingCard label={getText(t, 'classrooms.states.loading', 'Dang tai...')} />;
  }

  if (!classrooms.length) {
    return (
      <section className="classroom-panel classroom-empty">
        <LuClipboard aria-hidden="true" />
        <h2>{emptyTitle}</h2>
        <p>{emptyBody}</p>
        <button className="classroom-button" type="button" onClick={onRetry}>
          <LuRefreshCw aria-hidden="true" />
          {retryLabel}
        </button>
      </section>
    );
  }

  return (
    <section className="classroom-list">
      {classrooms.map((classroom) => (
        <Link className="classroom-card" key={getClassroomId(classroom)} to={`/classrooms/${getClassroomId(classroom)}`}>
          <span className="classroom-card-icon"><LuSchool aria-hidden="true" /></span>
          <div>
            <h2>{classroom.name}</h2>
            <p>{classroom.description || getText(t, 'classrooms.detail.noDescription', 'Chua co mo ta.')}</p>
            <small>
              {getText(t, 'classrooms.metrics.members', 'Thanh vien')}: {classroom.memberCount || 0}
              {' · '}
              {getText(t, 'classrooms.detail.updated', 'Cap nhat')}: {formatDateTime(classroom.updatedAt)}
            </small>
          </div>
        </Link>
      ))}
    </section>
  );
}

function QuestionSetFields({ form, onChange, t }) {
  return (
    <>
      <label>
        <span>{getText(t, 'classrooms.questionSets.fields.title', 'Tieu de')}</span>
        <input
          value={form.title}
          onChange={(event) => onChange((current) => ({ ...current, title: event.target.value }))}
          placeholder={getText(t, 'classrooms.questionSets.fields.titlePlaceholder', 'Vi du: N5 vocabulary review')}
        />
      </label>
      <label>
        <span>{getText(t, 'classrooms.questionSets.fields.description', 'Mo ta')}</span>
        <textarea
          rows={3}
          value={form.description}
          onChange={(event) => onChange((current) => ({ ...current, description: event.target.value }))}
          placeholder={getText(t, 'classrooms.questionSets.fields.descriptionPlaceholder', 'Ghi chu ngan cho giao vien')}
        />
      </label>
      <label>
        <span>{getText(t, 'classrooms.questionSets.fields.documentId', 'Document ID (MVP)')}</span>
        <input
          inputMode="numeric"
          value={form.documentId}
          onChange={(event) => onChange((current) => ({ ...current, documentId: event.target.value.replace(/\D/g, '') }))}
          placeholder="123"
        />
      </label>
    </>
  );
}

function QuestionSetList({ classroomId, questionSets, loading, onRetry, t }) {
  if (loading) {
    return <LoadingCard label={getText(t, 'classrooms.states.loading', 'Dang tai...')} />;
  }

  if (!questionSets.length) {
    return (
      <section className="classroom-panel classroom-empty">
        <LuFileQuestion aria-hidden="true" />
        <h2>{getText(t, 'classrooms.questionSets.emptyTitle', 'Chua co bo cau hoi')}</h2>
        <p>{getText(t, 'classrooms.questionSets.emptyBody', 'Tao bo cau hoi dau tien de gom cac cau hoi da sinh trong classroom.')}</p>
        <button className="classroom-button" type="button" onClick={onRetry}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.actions.refresh', 'Lam moi')}
        </button>
      </section>
    );
  }

  return (
    <section className="classroom-list">
      {questionSets.map((questionSet) => (
        <Link className="classroom-card" key={questionSet.id} to={`/classrooms/${classroomId}/question-sets/${questionSet.id}`}>
          <span className="classroom-card-icon"><LuFileQuestion aria-hidden="true" /></span>
          <div>
            <div className="classroom-card-title-row">
              <h2>{questionSet.title}</h2>
              <span className={`classroom-badge ${questionSet.visibility === 'Published' ? '' : 'muted'}`}>{questionSet.visibility}</span>
            </div>
            <p>{questionSet.description || getText(t, 'classrooms.questionSets.noDescription', 'Chua co mo ta.')}</p>
            <small>
              {getText(t, 'classrooms.questionSets.itemCount', 'Cau hoi')}: {questionSet.itemCount || 0}
              {' | '}
              {getText(t, 'classrooms.detail.updated', 'Cap nhat')}: {formatDateTime(questionSet.updatedAt)}
            </small>
          </div>
        </Link>
      ))}
    </section>
  );
}

function AvailableQuestionList({ loading, onAdd, questions, t, working }) {
  if (loading) {
    return <LoadingCard label={getText(t, 'classrooms.states.loading', 'Dang tai...')} />;
  }

  if (!questions.length) {
    return <p className="classroom-muted">{getText(t, 'classrooms.questionSets.availableEmpty', 'Chua co cau hoi kha dung cho Document ID nay.')}</p>;
  }

  return (
    <div className="classroom-question-list">
      {questions.map((question) => (
        <div className="classroom-question-row" key={question.id}>
          <div>
            <strong>#{question.id} - {question.questionText}</strong>
            <small>
              Document {question.documentId}
              {' | '}
              {question.questionType}
              {' | '}
              {question.difficulty}
              {question.topic ? ` | ${question.topic}` : ''}
            </small>
          </div>
          <button className="classroom-button" type="button" onClick={() => onAdd(question.id)} disabled={working}>
            <LuPlus aria-hidden="true" />
            {getText(t, 'classrooms.questionSets.add', 'Them')}
          </button>
        </div>
      ))}
    </div>
  );
}

function QuestionSetItems({ isTeacher, items, onMove, onRemove, t, working }) {
  if (!items.length) {
    return <p className="classroom-muted">{getText(t, 'classrooms.questionSets.itemsEmpty', 'Bo cau hoi nay chua co cau hoi nao.')}</p>;
  }

  return (
    <div className="classroom-question-list">
      {items.map((item, index) => (
        <div className="classroom-question-row" key={item.id}>
          <div>
            <strong>
              {index + 1}. {item.question?.questionText || `Question #${item.questionId}`}
            </strong>
            <small>
              ID {item.questionId}
              {' | '}
              Document {item.question?.documentId || '-'}
              {' | '}
              {getText(t, 'classrooms.questionSets.pointWeight', 'Diem')}: {item.pointWeight}
              {item.sectionCode ? ` | ${item.sectionCode}` : ''}
            </small>
          </div>
          {isTeacher && (
            <div className="classroom-row-actions">
              <button className="classroom-icon-button" type="button" onClick={() => onMove(item.id, -1)} disabled={working || index === 0} title={getText(t, 'classrooms.questionSets.moveUp', 'Len')}>
                <LuArrowUp aria-hidden="true" />
              </button>
              <button className="classroom-icon-button" type="button" onClick={() => onMove(item.id, 1)} disabled={working || index === items.length - 1} title={getText(t, 'classrooms.questionSets.moveDown', 'Xuong')}>
                <LuArrowDown aria-hidden="true" />
              </button>
              <button className="classroom-icon-button danger" type="button" onClick={() => onRemove(item.id)} disabled={working} title={getText(t, 'classrooms.questionSets.remove', 'Xoa')}>
                <LuTrash2 aria-hidden="true" />
              </button>
            </div>
          )}
        </div>
      ))}
    </div>
  );
}

function compareQuestionSetItems(left, right) {
  return (left.orderIndex ?? 0) - (right.orderIndex ?? 0) || (left.id ?? 0) - (right.id ?? 0);
}

function ClassroomResourceLinks({ classroomId, isTeacher, t }) {
  return (
    <div className="classroom-page-actions">
      <Link className="classroom-button" to={`/classrooms/${classroomId}`}>
        <LuSchool aria-hidden="true" />
        {getText(t, 'classrooms.assignments.classOverview', 'Tong quan lop')}
      </Link>
      <Link className="classroom-button" to={`/classrooms/${classroomId}/question-sets`}>
        <LuListChecks aria-hidden="true" />
        {getText(t, 'classrooms.questionSets.open', 'Bo cau hoi')}
      </Link>
      <Link className="classroom-button primary" to={isTeacher ? `/classrooms/${classroomId}/assignments` : `/classrooms/${classroomId}/student/assignments`}>
        <LuClipboard aria-hidden="true" />
        {getText(t, 'classrooms.assignments.open', 'Assignments')}
      </Link>
      {isTeacher && (
        <Link className="classroom-button" to={`/classrooms/${classroomId}/members`}>
          <LuGraduationCap aria-hidden="true" />
          {getText(t, 'classrooms.members.title', 'Danh sach thanh vien')}
        </Link>
      )}
    </div>
  );
}

function AssignmentFields({ form, onChange, questionSets, t }) {
  const update = (patch) => onChange((current) => ({ ...current, ...patch }));

  return (
    <>
      <label>
        <span>{getText(t, 'classrooms.assignments.fields.title', 'Tieu de')}</span>
        <input
          value={form.title}
          onChange={(event) => update({ title: event.target.value })}
          placeholder={getText(t, 'classrooms.assignments.fields.titlePlaceholder', 'Vi du: N5 midterm quiz')}
        />
      </label>
      <label>
        <span>{getText(t, 'classrooms.assignments.fields.description', 'Mo ta')}</span>
        <textarea
          rows={3}
          value={form.description}
          onChange={(event) => update({ description: event.target.value })}
          placeholder={getText(t, 'classrooms.assignments.fields.descriptionPlaceholder', 'Huong dan ngan cho hoc vien')}
        />
      </label>
      <label>
        <span>{getText(t, 'classrooms.assignments.fields.questionSet', 'Published QuestionSet')}</span>
        {questionSets.length ? (
          <select value={form.questionSetId} onChange={(event) => update({ questionSetId: event.target.value })}>
            <option value="">{getText(t, 'classrooms.assignments.fields.selectQuestionSet', 'Chon bo cau hoi')}</option>
            {questionSets.map((questionSet) => (
              <option key={questionSet.id} value={questionSet.id}>
                #{questionSet.id} - {questionSet.title}
              </option>
            ))}
          </select>
        ) : (
          <input
            inputMode="numeric"
            value={form.questionSetId}
            onChange={(event) => update({ questionSetId: event.target.value.replace(/\D/g, '') })}
            placeholder="QuestionSet ID"
          />
        )}
      </label>
      <div className="classroom-form-grid">
        <label>
          <span>{getText(t, 'classrooms.assignments.fields.type', 'Type')}</span>
          <select value={form.type} onChange={(event) => update({ type: event.target.value })}>
            {['Quiz', 'Test', 'Flashcard', 'Mixed'].map((type) => (
              <option key={type} value={type}>{type}</option>
            ))}
          </select>
        </label>
        <label>
          <span>{getText(t, 'classrooms.assignments.fields.attemptLimit', 'Attempt limit')}</span>
          <input
            inputMode="numeric"
            min="1"
            value={form.attemptLimit}
            onChange={(event) => update({ attemptLimit: event.target.value.replace(/\D/g, '') || '1' })}
          />
        </label>
        <label>
          <span>{getText(t, 'classrooms.assignments.fields.timeLimit', 'Time limit')}</span>
          <input
            inputMode="numeric"
            value={form.timeLimitMinutes}
            onChange={(event) => update({ timeLimitMinutes: event.target.value.replace(/\D/g, '') })}
            placeholder={getText(t, 'classrooms.assignments.fields.optionalMinutes', 'Phut, optional')}
          />
        </label>
      </div>
      <div className="classroom-form-grid">
        <label>
          <span>{getText(t, 'classrooms.assignments.fields.startAt', 'Start at')}</span>
          <input type="datetime-local" value={form.startAt} onChange={(event) => update({ startAt: event.target.value })} />
        </label>
        <label>
          <span>{getText(t, 'classrooms.assignments.fields.dueAt', 'Due at')}</span>
          <input type="datetime-local" value={form.dueAt} onChange={(event) => update({ dueAt: event.target.value })} />
        </label>
      </div>

      <label>
        <span>{getText(t, 'classrooms.assignments.fields.scoringMode', 'Cách chấm điểm')}</span>
        <select value={form.scoringMode} onChange={(event) => update({ scoringMode: event.target.value })}>
          <option value="Percent">{getText(t, 'classrooms.assignments.percentScoring', 'Chấm theo phần trăm')}</option>
          <option value="EmpiricalDifficulty">{getText(t, 'classrooms.assignments.empiricalScoring', 'Chấm theo độ khó thực nghiệm')}</option>
        </select>
      </label>

      {form.scoringMode === 'EmpiricalDifficulty' && (
        <div className="classroom-empirical-config">
          <p className="classroom-config-help-text text-muted">
            {getText(t, 'classrooms.assignments.empiricalHelp', 'Câu càng nhiều người trả lời đúng thì trọng số càng thấp. Câu càng ít người trả lời đúng thì trọng số càng cao.')}
          </p>
          <div className="classroom-form-grid">
            <label>
              <span>{getText(t, 'classrooms.assignments.minQuestionWeight', 'Trọng số tối thiểu')}</span>
              <input
                type="number"
                step="0.1"
                min="0.0001"
                value={form.minQuestionWeight}
                onChange={(event) => update({ minQuestionWeight: event.target.value })}
              />
            </label>
            <label>
              <span>{getText(t, 'classrooms.assignments.maxQuestionWeight', 'Trọng số tối đa')}</span>
              <input
                type="number"
                step="0.1"
                value={form.maxQuestionWeight}
                onChange={(event) => update({ maxQuestionWeight: event.target.value })}
              />
            </label>
          </div>
          <div className="classroom-form-grid">
            <label>
              <span>{getText(t, 'classrooms.assignments.smoothingAlpha', 'Smoothing alpha')}</span>
              <input
                type="number"
                step="1"
                min="0"
                value={form.smoothingAlpha}
                onChange={(event) => update({ smoothingAlpha: event.target.value })}
              />
            </label>
            <label>
              <span>{getText(t, 'classrooms.assignments.smoothingBeta', 'Smoothing beta')}</span>
              <input
                type="number"
                step="1"
                min="0"
                value={form.smoothingBeta}
                onChange={(event) => update({ smoothingBeta: event.target.value })}
              />
            </label>
          </div>
        </div>
      )}

      <label className="classroom-checkbox">
        <input type="checkbox" checked={form.shuffleQuestions} onChange={(event) => update({ shuffleQuestions: event.target.checked })} />
        <span>{getText(t, 'classrooms.assignments.fields.shuffleQuestions', 'Shuffle questions')}</span>
      </label>
      <label className="classroom-checkbox">
        <input type="checkbox" checked={form.shuffleOptions} onChange={(event) => update({ shuffleOptions: event.target.checked })} />
        <span>{getText(t, 'classrooms.assignments.fields.shuffleOptions', 'Shuffle options')}</span>
      </label>
      <label className="classroom-checkbox">
        <input type="checkbox" checked={form.showAnswerAfterSubmit} onChange={(event) => update({ showAnswerAfterSubmit: event.target.checked })} />
        <span>{getText(t, 'classrooms.assignments.fields.showAnswers', 'Show answer after submit')}</span>
      </label>
    </>
  );
}

function AssignmentList({ assignments, attempts = [], classroomId, emptyBody, loading, onRetry, onStart, startingId, t, teacher = false }) {
  if (loading) {
    return <LoadingCard label={getText(t, 'classrooms.states.loading', 'Dang tai...')} />;
  }

  if (!assignments.length) {
    return (
      <section className="classroom-panel classroom-empty">
        <LuClipboard aria-hidden="true" />
        <h2>{getText(t, 'classrooms.assignments.emptyTitle', 'Chua co assignment')}</h2>
        <p>{emptyBody}</p>
        <button className="classroom-button" type="button" onClick={onRetry}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.actions.refresh', 'Lam moi')}
        </button>
      </section>
    );
  }

  return (
    <section className="classroom-list">
      {assignments.map((assignment) => {
        const assignmentAttempts = getAttemptsForAssignment(attempts, assignment.id);
        const attempt = assignmentAttempts[0];
        const studentStatus = getStudentAssignmentStatus(assignment, assignmentAttempts);
        return (
          <article className="classroom-card" key={assignment.id}>
            <span className="classroom-card-icon"><LuClipboard aria-hidden="true" /></span>
            <div>
              <div className="classroom-card-title-row">
                <h2>{assignment.title}</h2>
                <span className={`classroom-badge ${assignment.status === 'Published' ? '' : 'muted'}`}>{teacher ? assignment.status : studentStatus}</span>
              </div>
              <p>{assignment.description || getText(t, 'classrooms.assignments.noDescription', 'Chua co mo ta.')}</p>
              <small>
                {assignment.type} | {getText(t, 'classrooms.assignments.attemptLimit', 'Lan lam')}: {assignment.attemptLimit || 1}
                {' | '}
                Due: {formatDateTime(assignment.dueAt)}
              </small>
              <div className="classroom-row-actions classroom-card-actions">
                {teacher ? (
                  <>
                    <Link className="classroom-button" to={`/classrooms/${classroomId}/assignments/${assignment.id}`}>
                      {getText(t, 'classrooms.assignments.openDetail', 'Chi tiet')}
                    </Link>
                    <Link className="classroom-button" to={`/classrooms/${classroomId}/assignments/${assignment.id}/attempts`}>
                      {getText(t, 'classrooms.assignments.viewAttempts', 'Attempts')}
                    </Link>
                  </>
                ) : (
                  <>
                    <Link className="classroom-button" to={`/classrooms/${classroomId}/student/assignments/${assignment.id}`}>
                      {getText(t, 'classrooms.assignments.openDetail', 'Chi tiet')}
                    </Link>
                    {attempt?.status === 'InProgress' ? (
                      <Link className="classroom-button primary" to={`/classroom-attempts/${attempt.id}`}>
                        {getText(t, 'classrooms.assignments.resume', 'Tiep tuc')}
                      </Link>
                    ) : attempt?.status === 'Submitted' ? (
                      <Link className="classroom-button primary" to={`/classroom-attempts/${attempt.id}/result`}>
                        {getText(t, 'classrooms.assignments.result', 'Ket qua')}
                      </Link>
                    ) : (
                      <button className="classroom-button primary" type="button" onClick={() => onStart?.(assignment.id)} disabled={startingId === assignment.id}>
                        {startingId === assignment.id ? getText(t, 'classrooms.assignments.starting', 'Dang mo...') : getText(t, 'classrooms.assignments.start', 'Start')}
                      </button>
                    )}
                  </>
                )}
              </div>
            </div>
          </article>
        );
      })}
    </section>
  );
}

function findLatestAttemptForAssignment(attempts, assignmentId) {
  return getAttemptsForAssignment(attempts, assignmentId)[0];
}

function getAttemptsForAssignment(attempts, assignmentId) {
  return (Array.isArray(attempts) ? attempts : [])
    .filter((attempt) => String(attempt.classroomAssignmentId) === String(assignmentId))
    .sort((left, right) => new Date(right.startedAt || 0) - new Date(left.startedAt || 0));
}

function getStudentAssignmentStatus(assignment, assignmentAttempts) {
  const attempts = Array.isArray(assignmentAttempts) ? assignmentAttempts : [];
  const attempt = attempts[0];
  if (attempt?.status === 'Submitted') {
    return 'Da nop';
  }
  if (attempt?.status === 'InProgress') {
    return 'Dang lam';
  }
  if (attempt?.status === 'Expired') {
    return 'Het han';
  }
  if (assignment?.dueAt && new Date(assignment.dueAt) < new Date()) {
    return 'Het han';
  }
  if (attempts.length >= (Number(assignment?.attemptLimit) || 1)) {
    return 'Het luot lam';
  }
  return 'Chua lam';
}

function AssignmentQuestions({ items, showSensitive = false }) {
  if (!items.length) {
    return <p className="classroom-muted">Chua co cau hoi.</p>;
  }

  return (
    <div className="classroom-question-list">
      {items.map((item, index) => (
        <div className="classroom-question-row" key={item.id || item.questionId}>
          <div>
            <strong>{index + 1}. {item.question?.questionText || `Question #${item.questionId}`}</strong>
            <small>
              ID {item.questionId} | {item.question?.questionType || '-'} | Diem: {item.pointWeight ?? '-'}
            </small>
            <OptionPreview options={item.question?.options} />
            {showSensitive && item.question?.correctAnswer && (
              <p className="classroom-answer-key">Correct: {item.question.correctAnswer}</p>
            )}
            {showSensitive && item.question?.explanation && (
              <p className="classroom-muted">{item.question.explanation}</p>
            )}
          </div>
        </div>
      ))}
    </div>
  );
}

function OptionPreview({ options }) {
  const parsedOptions = parseQuestionOptions(options);
  if (!parsedOptions.length) {
    return null;
  }

  return (
    <ul className="classroom-option-list">
      {parsedOptions.map((option, index) => (
        <li key={`${getOptionValue(option, index)}-${index}`}>
          <strong>{getOptionValue(option, index)}.</strong> {getOptionText(option)}
        </li>
      ))}
    </ul>
  );
}

function QuestionAttemptCard({ answer, item, onAnswer, onSubmit, saving, t, index }) {
  const options = parseQuestionOptions(item.question?.options);
  return (
    <article className="classroom-panel classroom-attempt-question">
      <div className="classroom-section-head">
        <div>
          <span className="classroom-kicker">Question {index + 1}</span>
          <h2>{item.question?.questionText || `Question #${item.questionId}`}</h2>
          <p className="classroom-muted">Diem: {item.pointWeight ?? '-'}</p>
        </div>
      </div>

      {options.length ? (
        <div className="classroom-answer-options">
          {options.map((option, optionIndex) => {
            const value = getOptionValue(option, optionIndex);
            return (
              <label className="classroom-answer-option" key={`${value}-${optionIndex}`}>
                <input
                  checked={answer === value}
                  name={`question-${item.questionId}`}
                  onChange={() => onAnswer(value)}
                  type="radio"
                  value={value}
                />
                <span><strong>{value}.</strong> {getOptionText(option)}</span>
              </label>
            );
          })}
        </div>
      ) : (
        <label className="classroom-form">
          <span>{getText(t, 'classrooms.assignments.selectedAnswer', 'Cau tra loi')}</span>
          <input value={answer} onChange={(event) => onAnswer(event.target.value)} placeholder="A" />
        </label>
      )}

      <button className="classroom-button" type="button" onClick={onSubmit} disabled={saving || !answer}>
        <LuSave aria-hidden="true" />
        {saving ? getText(t, 'classrooms.assignments.savingAnswer', 'Dang luu...') : getText(t, 'classrooms.assignments.submitAnswer', 'Luu cau tra loi')}
      </button>
    </article>
  );
}

function AttemptAnswers({ answers, reveal }) {
  if (!answers.length) {
    return <p className="classroom-muted">Chua co cau tra loi.</p>;
  }

  return (
    <div className="classroom-answer-review">
      {answers.map((answer) => (
        <div className="classroom-answer-review-row" key={answer.id || answer.questionId}>
          <strong>{answer.question?.questionText || `Question #${answer.questionId}`}</strong>
          <small>Selected: {answer.selectedAnswer || '-'}</small>
          {reveal && Object.prototype.hasOwnProperty.call(answer, 'isCorrect') && (
            <small>{answer.isCorrect ? 'Dung' : 'Sai'} | Diem: {answer.pointEarned ?? 0}</small>
          )}
          {reveal && answer.question?.correctAnswer && (
            <small>Correct: {answer.question.correctAnswer}</small>
          )}
          {reveal && answer.question?.explanation && (
            <small>{answer.question.explanation}</small>
          )}
        </div>
      ))}
    </div>
  );
}

function JoinCodeList({ codes, disablingCodeId, onCopy, onDisable, t }) {
  if (!codes.length) {
    return <p className="classroom-muted">{getText(t, 'classrooms.codes.empty', 'Chua co join code nao.')}</p>;
  }

  return (
    <div className="classroom-code-list">
      {codes.map((code) => (
        <div className={`classroom-code-row${code.isActive ? '' : ' disabled'}`} key={code.id}>
          <div>
            <strong>{code.code}</strong>
            <small>
              {code.isActive ? getText(t, 'classrooms.codes.active', 'Dang bat') : getText(t, 'classrooms.codes.disabled', 'Da tat')}
              {' · '}
              {code.usedCount || 0}/{code.maxUses || getText(t, 'classrooms.codes.unlimited', 'khong gioi han')}
            </small>
          </div>
          <div className="classroom-row-actions">
            <button className="classroom-icon-button" type="button" onClick={() => onCopy(code.code)} title={getText(t, 'classrooms.actions.copy', 'Copy')}>
              <LuCopy aria-hidden="true" />
            </button>
            {code.isActive && (
              <button className="classroom-icon-button danger" type="button" onClick={() => onDisable(code.id)} disabled={disablingCodeId === code.id} title={getText(t, 'classrooms.actions.disable', 'Tat code')}>
                <LuBan aria-hidden="true" />
              </button>
            )}
          </div>
        </div>
      ))}
    </div>
  );
}

function MembersPanel({ error, loading, members, onRetry, t }) {
  return (
    <section className="classroom-panel classroom-members">
      <div className="classroom-section-head">
        <div>
          <span className="classroom-kicker">{getText(t, 'classrooms.members.kicker', 'Members')}</span>
          <h2>{getText(t, 'classrooms.members.title', 'Danh sach thanh vien')}</h2>
        </div>
        <button className="classroom-button" type="button" onClick={onRetry}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.actions.refresh', 'Lam moi')}
        </button>
      </div>
      <MessageBar error={error} />
      {loading && <LoadingCard label={getText(t, 'classrooms.states.loadingMembers', 'Dang tai thanh vien...')} />}
      {!loading && !members.length && !error && (
        <p className="classroom-muted">{getText(t, 'classrooms.members.empty', 'Chua co hoc vien nao trong lop.')}</p>
      )}
      {!loading && members.length > 0 && (
        <div className="classroom-member-table">
          {members.map((member) => (
            <div className="classroom-member-row" key={member.id}>
              <span className="classroom-avatar">{(member.user?.fullName || member.user?.email || '?').charAt(0).toUpperCase()}</span>
              <div>
                <strong>{member.user?.fullName || member.user?.email || `User ${member.userId}`}</strong>
                <small>{member.user?.email || '-'}</small>
              </div>
              <span className="classroom-badge">{member.role}</span>
              <span className="classroom-badge muted">{member.status}</span>
            </div>
          ))}
        </div>
      )}
    </section>
  );
}

function LoadingCard({ label }) {
  return (
    <section className="classroom-panel classroom-loading">
      <span className="classroom-spinner" aria-hidden="true" />
      <p>{label}</p>
    </section>
  );
}

function MessageBar({ error, success }) {
  if (!error && !success) {
    return null;
  }

  return (
    <div className={`classroom-message ${error ? 'error' : 'success'}`} role={error ? 'alert' : 'status'}>
      {error || success}
    </div>
  );
}

function Metric({ label, value }) {
  return (
    <div className="classroom-metric">
      <strong>{value}</strong>
      <span>{label}</span>
    </div>
  );
}
