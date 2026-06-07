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
