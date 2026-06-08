import React, { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  LuArrowDown,
  LuArrowUp,
  LuBan,
  LuChartBar,
  LuTriangleAlert,
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

const normalizeRole = (value) => String(value || "").trim().toLowerCase();

const isClassroomTeacherRole = (role) => {
  const normalized = normalizeRole(role);
  return normalized === "teacher" || normalized === "owner";
};

const isSystemAdmin = (user) => {
  const role = normalizeRole(user?.role);
  return role === "admin" || role === "administrator";
};

const sameId = (a, b) => {
  if (a === null || a === undefined || b === null || b === undefined) return false;
  return String(a) === String(b);
};

const localizeBackendError = (apiErrorMsg, t) => {
  if (!apiErrorMsg) return '';
  const msg = String(apiErrorMsg).toLowerCase();
  
  if (msg.includes("only active classroom students") || msg.includes("not a member") || msg.includes("cannot view assignments")) {
    return getText(t, 'classrooms.assignments.errors.studentForbidden', 'Bạn không có quyền truy cập bài kiểm tra này.');
  }
  if (msg.includes("not published") || msg.includes("not started yet") || msg.includes("past due") || msg.includes("not available")) {
    return getText(t, 'classrooms.assignments.errors.notAvailable', 'Bài kiểm tra chưa được phát hành, chưa đến thời gian mở hoặc đã hết hạn.');
  }
  if (msg.includes("limit has been reached") || msg.includes("attempt limit")) {
    return getText(t, 'classrooms.assignments.errors.attemptLimitReached', 'Bạn đã hết lượt làm bài.');
  }
  if (msg.includes("has been closed") || msg.includes("closed")) {
    return getText(t, 'classrooms.assignments.errors.closed', 'Bài kiểm tra đã được đóng.');
  }
  if (msg.includes("attempt has expired") || msg.includes("expired")) {
    return getText(t, 'classrooms.assignments.errors.expired', 'Bài làm đã hết hạn.');
  }
  return apiErrorMsg;
};

function getClassroomId(classroom) {
  return classroom?.id || classroom?.classroomWorkspaceId;
}

function formatDateTime(value) {
  if (!value) {
    return '-';
  }

  return new Date(value).toLocaleString();
}

function translateAssignmentStatus(status, t) {
  const s = String(status || '').toLowerCase();
  if (s === 'draft') return getText(t, 'classrooms.assignments.status.draft', 'Bản nháp');
  if (s === 'published') return getText(t, 'classrooms.assignments.status.published', 'Đã công bố');
  if (s === 'closed') return getText(t, 'classrooms.assignments.status.closed', 'Đã đóng');
  return status || '-';
}

function translateAttemptStatus(status, t) {
  const s = String(status || '').toLowerCase();
  if (s === 'inprogress' || s === 'in_progress') return getText(t, 'classrooms.assignments.status.inProgress', 'Đang làm');
  if (s === 'submitted') return getText(t, 'classrooms.assignments.status.submitted', 'Đã nộp');
  if (s === 'expired') return getText(t, 'classrooms.assignments.status.expired', 'Hết hạn');
  if (s === 'notstarted' || s === 'not_started') return getText(t, 'classrooms.assignments.status.notStarted', 'Chưa làm');
  if (s === 'noattemptsleft' || s === 'no_attempts_left') return getText(t, 'classrooms.assignments.status.noAttemptsLeft', 'Hết lượt làm');
  return status || '-';
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
      setError(getApiErrorMessage(err, getText(t, 'classrooms.errors.loadTeaching', 'Không tải được danh sách lớp đang dạy.')));
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
      setError(getText(t, 'classrooms.errors.nameRequired', 'Hãy nhập tên lớp.'));
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
      setSuccess(getText(t, 'classrooms.feedback.created', 'Đã tạo lớp học.'));
      setClassrooms((current) => [classroom, ...current]);
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.errors.createFailed', 'Không tạo được lớp học.')));
    } finally {
      setCreating(false);
    }
  };

  return (
    <ClassroomShell
      title={getText(t, 'classrooms.teaching.title', 'Lớp đang dạy')}
      subtitle={getText(t, 'classrooms.teaching.subtitle', 'Tạo lớp, chia sẻ mã tham gia và xem thành viên.')}
    >
      <ClassroomTabs active="teaching" />
      <MessageBar error={error} success={success} />

      <section className="classroom-layout">
        {canCreateClassroom ? (
          <form className="classroom-panel classroom-form" onSubmit={handleCreate}>
            <div>
              <span className="classroom-kicker">{getText(t, 'classrooms.create.kicker', 'Giảng viên')}</span>
              <h2>{getText(t, 'classrooms.create.title', 'Tạo lớp mới')}</h2>
            </div>
            <label>
              <span>{getText(t, 'classrooms.create.name', 'Tên lớp')}</span>
              <input
                value={form.name}
                onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))}
                placeholder={getText(t, 'classrooms.create.namePlaceholder', 'Ví dụ: JLPT N5 Reading')}
              />
            </label>
            <label>
              <span>{getText(t, 'classrooms.create.description', 'Mô tả')}</span>
              <textarea
                value={form.description}
                onChange={(event) => setForm((current) => ({ ...current, description: event.target.value }))}
                placeholder={getText(t, 'classrooms.create.descriptionPlaceholder', 'Mục tiêu, lịch học hoặc ghi chú ngắn')}
                rows={4}
              />
            </label>
            <button className="classroom-button primary" type="submit" disabled={creating}>
              <LuPlus aria-hidden="true" />
              {creating ? getText(t, 'classrooms.create.creating', 'Đang tạo...') : getText(t, 'classrooms.create.submit', 'Tạo lớp')}
            </button>
          </form>
        ) : (
          <section className="classroom-panel classroom-empty">
            <LuSchool aria-hidden="true" />
            <h2>{getText(t, 'classrooms.teaching.teacherOnlyTitle', 'Cần tài khoản giảng viên')}</h2>
            <p>{getText(t, 'classrooms.teaching.teacherOnlyBody', 'Học viên có thể xem lớp đã tham gia hoặc nhập mã tham gia từ giảng viên.')}</p>
            <Link className="classroom-button primary" to="/classrooms/join">
              <LuDoorOpen aria-hidden="true" />
              {getText(t, 'classrooms.tabs.join', 'Nhập mã')}
            </Link>
          </section>
        )}

        <ClassroomList
          classrooms={classrooms}
          emptyBody={getText(t, 'classrooms.teaching.emptyBody', 'Tạo lớp đầu tiên để mời học viên bằng mã tham gia.')}
          emptyTitle={getText(t, 'classrooms.teaching.emptyTitle', 'Chưa có lớp đang dạy')}
          loading={loading}
          onRetry={loadClassrooms}
          retryLabel={getText(t, 'classrooms.actions.retry', 'Thử lại')}
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
      setError(getApiErrorMessage(err, getText(t, 'classrooms.errors.loadJoined', 'Không tải được danh sách lớp đã tham gia.')));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => {
    loadClassrooms();
  }, [loadClassrooms]);

  return (
    <ClassroomShell
      title={getText(t, 'classrooms.joined.title', 'Lớp đã tham gia')}
      subtitle={getText(t, 'classrooms.joined.subtitle', 'Xem các lớp học bạn đang là học viên.')}
    >
      <ClassroomTabs active="joined" />
      <MessageBar error={error} />
      <ClassroomList
        classrooms={classrooms}
        emptyBody={getText(t, 'classrooms.joined.emptyBody', 'Nhập mã tham gia giảng viên cung cấp để tham gia lớp đầu tiên.')}
        emptyTitle={getText(t, 'classrooms.joined.emptyTitle', 'Chưa tham gia lớp nào')}
        loading={loading}
        onRetry={loadClassrooms}
        retryLabel={getText(t, 'classrooms.actions.retry', 'Thử lại')}
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
      setError(getApiErrorMessage(err, getText(t, 'classrooms.errors.joinFailed', 'Không tham gia được lớp.')));
    } finally {
      setJoining(false);
    }
  };

  return (
    <ClassroomShell
      title={getText(t, 'classrooms.join.title', 'Tham gia lớp')}
      subtitle={getText(t, 'classrooms.join.subtitle', 'Nhập mã tham gia từ giảng viên để vào lớp học.')}
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
          {joining ? getText(t, 'classrooms.join.joining', 'Đang tham gia...') : getText(t, 'classrooms.join.submit', 'Tham gia lớp')}
        </button>
      </form>
    </ClassroomShell>
  );
}

export function ClassroomDetailPage({ membersOnly = false }) {
  const { classroomId } = useParams();
  const { t } = useLanguage();
  const { currentUser } = useAuth();
  const [classroom, setClassroom] = useState(null);
  const [members, setMembers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [membersLoading, setMembersLoading] = useState(false);
  const [error, setError] = useState('');
  const [membersError, setMembersError] = useState('');
  const [success, setSuccess] = useState('');
  const [creatingCode, setCreatingCode] = useState(false);
  const [disablingCodeId, setDisablingCodeId] = useState(null);

  const isTeacher = isClassroomTeacherRole(classroom?.currentUserRole) ||
                    sameId(classroom?.ownerUserId, currentUser?.id) ||
                    isSystemAdmin(currentUser);

  const loadDetail = useCallback(async () => {
    setLoading(true);
    setError('');

    try {
      const data = await classroomService.getClassroomDetail(classroomId);
      setClassroom(data);
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.errors.detailFailed', 'Không tải được chi tiết lớp.')));
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
        setMembersError(getText(t, 'classrooms.errors.membersForbidden', 'Chỉ giảng viên của lớp mới xem được danh sách thành viên.'));
      } else {
        setMembersError(getApiErrorMessage(err, getText(t, 'classrooms.errors.membersFailed', 'Không tải được danh sách thành viên.')));
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
      await refreshAfterCodeChange(getText(t, 'classrooms.feedback.codeCreated', 'Đã tạo mã tham gia.'));
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.errors.codeCreateFailed', 'Không tạo được mã tham gia.')));
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
      await refreshAfterCodeChange(getText(t, 'classrooms.feedback.codeDisabled', 'Đã tắt mã tham gia.'));
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.errors.codeDisableFailed', 'Không tắt được mã tham gia.')));
    } finally {
      setDisablingCodeId(null);
    }
  };

  const handleCopyCode = async (code) => {
    try {
      await navigator.clipboard.writeText(code);
      setSuccess(getText(t, 'classrooms.feedback.codeCopied', 'Đã sao chép mã tham gia.'));
    } catch {
      setError(getText(t, 'classrooms.errors.copyFailed', 'Không sao chép được mã. Hãy sao chép thủ công.'));
    }
  };

  const getRoleBadge = () => {
    if (sameId(classroom?.ownerUserId, currentUser?.id)) {
      return (
        <span className="classroom-role-badge owner">
          {getText(t, 'classrooms.detail.roles.owner', 'Chủ lớp')}
        </span>
      );
    }
    if (isClassroomTeacherRole(classroom?.currentUserRole)) {
      return (
        <span className="classroom-role-badge teacher">
          {getText(t, 'classrooms.detail.roles.teacher', 'Giảng viên')}
        </span>
      );
    }
    return (
      <span className="classroom-role-badge student">
        {getText(t, 'classrooms.detail.roles.student', 'Học viên')}
      </span>
    );
  };

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.detail.title', 'Chi tiết lớp')} subtitle="">
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Đang tải...')} />
      </ClassroomShell>
    );
  }

  if (error && !classroom) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.detail.title', 'Chi tiết lớp')} subtitle="">
        <MessageBar error={error} />
        <button className="classroom-button" type="button" onClick={loadDetail}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.actions.retry', 'Thử lại')}
        </button>
      </ClassroomShell>
    );
  }

  if (membersOnly && !isTeacher) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.members.title', 'Danh sách thành viên')} subtitle="">
        <MessageBar error={getText(t, 'classrooms.errors.membersForbidden', 'Chỉ giảng viên của lớp mới xem được danh sách thành viên.')} />
      </ClassroomShell>
    );
  }

  return (
    <ClassroomShell
      title={classroom?.name || getText(t, 'classrooms.detail.title', 'Chi tiết lớp')}
      subtitle={classroom?.description || getText(t, 'classrooms.detail.noDescription', 'Chưa có mô tả.')}
      roleBadge={classroom ? getRoleBadge() : null}
    >
      <MessageBar error={error} success={success} />

      {isTeacher ? (
        <section className="classroom-detail-grid">
          <div className="classroom-detail-main">
            {/* Metrics grid */}
            <div className="classroom-detail-metrics-grid">
              <div className="metric-card">
                <div className="metric-label">{getText(t, 'classrooms.metrics.members', 'Tổng thành viên')}</div>
                <div className="metric-value">{classroom?.memberCount || 0}</div>
              </div>
              <div className="metric-card">
                <div className="metric-label">{getText(t, 'classrooms.metrics.teachers', 'Giảng viên')}</div>
                <div className="metric-value">{classroom?.teacherCount || 0}</div>
              </div>
              <div className="metric-card">
                <div className="metric-label">{getText(t, 'classrooms.metrics.students', 'Học viên')}</div>
                <div className="metric-value">{classroom?.studentCount || 0}</div>
              </div>
              <div className="metric-card">
                <div className="metric-label">{getText(t, 'classrooms.detail.updatedCard', 'Cập nhật lúc')}</div>
                <div className="metric-value date-value">{formatDateTime(classroom?.updatedAt)}</div>
              </div>
            </div>

            {/* Quick Actions Grid */}
            <h2 style={{ marginBottom: '16px', fontSize: '18px', fontWeight: 800 }}>
              {getText(t, 'classrooms.detail.quickActions', 'Phím tắt nhanh')}
            </h2>
            <div className="classroom-actions-grid">
              <Link className="classroom-action-card" to={`/classrooms/${classroomId}/question-sets`}>
                <div className="classroom-action-card-icon"><LuListChecks aria-hidden="true" /></div>
                <h3>{getText(t, 'classrooms.detail.actions.questionSetsTitle', 'Bộ câu hỏi')}</h3>
                <p>{getText(t, 'classrooms.detail.actions.questionSetsDesc', 'Quản lý, tạo và biên soạn câu hỏi học tập.')}</p>
              </Link>
              <Link className="classroom-action-card" to={`/classrooms/${classroomId}/assignments`}>
                <div className="classroom-action-card-icon"><LuClipboard aria-hidden="true" /></div>
                <h3>{getText(t, 'classrooms.detail.actions.assignmentsTitle', 'Bài kiểm tra')}</h3>
                <p>{getText(t, 'classrooms.detail.actions.assignmentsDesc', 'Giao bài tập mới, theo dõi tiến độ làm bài.')}</p>
              </Link>
              <Link className="classroom-action-card" to={`/classrooms/${classroomId}/leaderboard`}>
                <div className="classroom-action-card-icon"><LuArrowUp aria-hidden="true" /></div>
                <h3>{getText(t, 'classrooms.detail.actions.leaderboardTitle', 'Bảng xếp hạng lớp')}</h3>
                <p>{getText(t, 'classrooms.detail.actions.leaderboardDesc', 'Xem thành tích học tập và xếp hạng điểm của học viên.')}</p>
              </Link>
              <Link className="classroom-action-card" to={`/classrooms/${classroomId}/analytics`}>
                <div className="classroom-action-card-icon"><LuChartBar aria-hidden="true" /></div>
                <h3>{getText(t, 'classrooms.detail.actions.analyticsTitle', 'Thống kê lớp học')}</h3>
                <p>{getText(t, 'classrooms.detail.actions.analyticsDesc', 'Phân tích hiệu suất học tập và câu hỏi khó.')}</p>
              </Link>
              <Link className="classroom-action-card" to={`/classrooms/${classroomId}/members`}>
                <div className="classroom-action-card-icon"><LuGraduationCap aria-hidden="true" /></div>
                <h3>{getText(t, 'classrooms.detail.actions.membersTitle', 'Thành viên lớp')}</h3>
                <p>{getText(t, 'classrooms.detail.actions.membersDesc', 'Xem danh sách và quản lý thành viên đang tham gia lớp.')}</p>
              </Link>
            </div>
          </div>

          <div className="classroom-detail-sidebar">
            <article className="classroom-panel classroom-code-panel">
              <div className="classroom-section-head">
                <div>
                  <span className="classroom-kicker">{getText(t, 'classrooms.codes.kicker', 'Join code')}</span>
                  <h2>{getText(t, 'classrooms.codes.title', 'Mời học viên')}</h2>
                </div>
                <button className="classroom-button primary" type="button" onClick={handleCreateCode} disabled={creatingCode}>
                  <LuPlus aria-hidden="true" />
                  {creatingCode ? getText(t, 'classrooms.codes.creating', 'Đang tạo...') : getText(t, 'classrooms.codes.create', 'Tạo code')}
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
          </div>
        </section>
      ) : (
        <section className="classroom-detail-grid">
          <div className="classroom-detail-main">
            {/* Student Note in main area */}
            <div className="classroom-panel classroom-student-note" style={{ marginBottom: '24px', display: 'flex', gap: '16px', alignItems: 'flex-start' }}>
              <LuGraduationCap aria-hidden="true" style={{ fontSize: '28px', flexShrink: 0, color: '#0f766e', marginTop: '2px' }} />
              <div>
                <h2 style={{ fontSize: '18px', fontWeight: 700, margin: '0 0 6px 0' }}>{getText(t, 'classrooms.student.title', 'Bạn đang là học viên')}</h2>
                <p style={{ margin: 0, color: '#4b5563', lineHeight: '1.5' }}>{getText(t, 'classrooms.student.body', 'Trang lớp học của học viên chỉ hiển thị thông tin lớp. Các luồng xử lý tài liệu vẫn nằm trong không gian làm việc cá nhân riêng của bạn.')}</p>
              </div>
            </div>

            {/* Quick Actions Grid */}
            <h2 style={{ marginBottom: '16px', fontSize: '18px', fontWeight: 800 }}>
              {getText(t, 'classrooms.detail.quickActions', 'Phím tắt nhanh')}
            </h2>
            <div className="classroom-actions-grid">
              <Link className="classroom-action-card" to={`/classrooms/${classroomId}/student/assignments`}>
                <div className="classroom-action-card-icon"><LuClipboard aria-hidden="true" /></div>
                <h3>{getText(t, 'classrooms.detail.actions.assignedTasksTitle', 'Bài kiểm tra được giao')}</h3>
                <p>{getText(t, 'classrooms.detail.actions.assignedTasksDesc', 'Xem danh sách bài tập cần làm và thời hạn.')}</p>
              </Link>
              <Link className="classroom-action-card" to={`/classrooms/${classroomId}/leaderboard`}>
                <div className="classroom-action-card-icon"><LuArrowUp aria-hidden="true" /></div>
                <h3>{getText(t, 'classrooms.detail.actions.leaderboardTitle', 'Bảng xếp hạng lớp')}</h3>
                <p>{getText(t, 'classrooms.detail.actions.leaderboardDesc', 'Xem thành tích học tập và xếp hạng điểm của bạn.')}</p>
              </Link>
              <Link className="classroom-action-card" to={`/classrooms/${classroomId}/student/analytics`}>
                <div className="classroom-action-card-icon"><LuChartBar aria-hidden="true" /></div>
                <h3>{getText(t, 'classrooms.detail.actions.myProgressTitle', 'Tiến độ của tôi')}</h3>
                <p>{getText(t, 'classrooms.detail.actions.myProgressDesc', 'Theo dõi điểm trung bình và thống kê cá nhân.')}</p>
              </Link>
              <Link className="classroom-action-card" to="/classroom-attempts/history">
                <div className="classroom-action-card-icon"><LuListChecks aria-hidden="true" /></div>
                <h3>{getText(t, 'classrooms.detail.actions.attemptHistoryTitle', 'Lịch sử làm bài')}</h3>
                <p>{getText(t, 'classrooms.detail.actions.attemptHistoryDesc', 'Xem lại kết quả các bài kiểm tra đã làm.')}</p>
              </Link>
            </div>
          </div>
        </section>
      )}

      {isTeacher && membersOnly && (
        <MembersPanel
          error={membersError}
          loading={membersLoading}
          members={members}
          onRetry={loadMembers}
          t={t}
        />
      )}
    </ClassroomShell>
  );
}

export function ClassroomQuestionSetsPage() {
  const { classroomId } = useParams();
  const { t } = useLanguage();
  const { currentUser } = useAuth();
  const [classroom, setClassroom] = useState(null);
  const [questionSets, setQuestionSets] = useState([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [form, setForm] = useState({ title: '', description: '', documentId: '' });
  const isTeacher = isClassroomTeacherRole(classroom?.currentUserRole) ||
                    sameId(classroom?.ownerUserId, currentUser?.id) ||
                    isSystemAdmin(currentUser);

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
      setError(getApiErrorMessage(err, getText(t, 'classrooms.questionSets.errors.load', 'Không tải được bộ câu hỏi.')));
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
      setError(getText(t, 'classrooms.questionSets.errors.titleRequired', 'Nhập tiêu đề bộ câu hỏi.'));
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
      setSuccess(getText(t, 'classrooms.questionSets.feedback.created', 'Đã tạo bộ câu hỏi.'));
      setQuestionSets((current) => [created, ...current]);
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.questionSets.errors.create', 'Không tạo được bộ câu hỏi.')));
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.questionSets.title', 'Bộ câu hỏi')} subtitle="">
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Đang tải...')} />
      </ClassroomShell>
    );
  }

  return (
    <ClassroomShell
      title={getText(t, 'classrooms.questionSets.title', 'Bộ câu hỏi')}
      subtitle={classroom?.name || getText(t, 'classrooms.detail.title', 'Chi tiết lớp')}
    >
      <MessageBar error={error} success={success} />

      <div className="classroom-page-actions">
        <Link className="classroom-button" to={`/classrooms/${classroomId}`}>
          <LuSchool aria-hidden="true" />
          {getText(t, 'classrooms.questionSets.backToClassroom', 'Về lớp học')}
        </Link>
        <button className="classroom-button" type="button" onClick={loadPage}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.actions.refresh', 'Làm mới')}
        </button>
      </div>

      <section className="classroom-layout">
        {isTeacher ? (
          <form className="classroom-panel classroom-form" onSubmit={handleCreate}>
            <div>
              <span className="classroom-kicker">{getText(t, 'classrooms.questionSets.teacherTools', 'Công cụ giảng viên')}</span>
              <h2>{getText(t, 'classrooms.questionSets.createTitle', 'Tạo bộ câu hỏi')}</h2>
            </div>
            <QuestionSetFields form={form} onChange={setForm} t={t} />
            <button className="classroom-button primary" type="submit" disabled={saving}>
              <LuPlus aria-hidden="true" />
              {saving ? getText(t, 'classrooms.questionSets.creating', 'Đang tạo...') : getText(t, 'classrooms.questionSets.create', 'Tạo bộ câu hỏi')}
            </button>
          </form>
        ) : (
          <section className="classroom-panel classroom-student-note">
            <LuGraduationCap aria-hidden="true" />
            <div>
              <h2>{getText(t, 'classrooms.questionSets.studentTitle', 'Bộ câu hỏi đã công bố')}</h2>
              <p>{getText(t, 'classrooms.questionSets.studentBody', 'Học sinh chỉ xem được bộ câu hỏi đã công bố và không có công cụ quản trị.')}</p>
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
  const { currentUser } = useAuth();
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
  const isTeacher = isClassroomTeacherRole(classroom?.currentUserRole) ||
                    sameId(classroom?.ownerUserId, currentUser?.id) ||
                    isSystemAdmin(currentUser);

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
      const isTeacherRole = isClassroomTeacherRole(classroomData?.currentUserRole) ||
                            sameId(classroomData?.ownerUserId, currentUser?.id) ||
                            isSystemAdmin(currentUser);
      if (isTeacherRole) {
        questionSetData = await classroomService.getClassroomQuestionSetDetail(questionSetId);
      } else {
        const visibleQuestionSets = await classroomService.getClassroomQuestionSets(classroomId);
        const visibleQuestionSet = (Array.isArray(visibleQuestionSets) ? visibleQuestionSets : [])
          .find((candidate) => String(candidate.id) === String(questionSetId));
        if (!visibleQuestionSet) {
          setQuestionSet(null);
          setError(getText(t, 'classrooms.questionSets.errors.forbidden', 'Bạn không có quyền xem hoặc quản lý bộ câu hỏi này.'));
          return;
        }

        questionSetData = await classroomService.getClassroomQuestionSetDetail(questionSetId);
      }

      setQuestionSet(questionSetData);
      syncEditForm(questionSetData);
    } catch (err) {
      const fallback = isApiForbidden(err)
        ? getText(t, 'classrooms.questionSets.errors.forbidden', 'Bạn không có quyền xem hoặc quản lý bộ câu hỏi này.')
        : getText(t, 'classrooms.questionSets.errors.detail', 'Không tải được chi tiết bộ câu hỏi.');
      setError(getApiErrorMessage(err, fallback));
    } finally {
      setLoading(false);
    }
  }, [classroomId, questionSetId, t, currentUser]);

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
      setError(getText(t, 'classrooms.questionSets.errors.titleRequired', 'Nhập tiêu đề bộ câu hỏi.'));
      return;
    }

    await runTeacherAction(
      () => classroomService.updateClassroomQuestionSet(questionSetId, {
        title: editForm.title.trim(),
        description: editForm.description.trim() || null,
        documentId: editForm.documentId ? Number(editForm.documentId) : null,
      }),
      getText(t, 'classrooms.questionSets.feedback.updated', 'Đã cập nhật bộ câu hỏi.'),
      getText(t, 'classrooms.questionSets.errors.update', 'Không cập nhật được bộ câu hỏi.')
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
      setError(getApiErrorMessage(err, getText(t, 'classrooms.questionSets.errors.delete', 'Không xóa được bộ câu hỏi.')));
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
      setError(getApiErrorMessage(err, getText(t, 'classrooms.questionSets.errors.available', 'Không tải được câu hỏi khả dụng.')));
    } finally {
      setAvailableLoading(false);
    }
  };

  const addQuestion = async (questionId) => {
    const resolvedQuestionId = questionId || Number(itemForm.questionId);
    if (!resolvedQuestionId) {
      setError(getText(t, 'classrooms.questionSets.errors.questionRequired', 'Nhập Question ID.'));
      return;
    }

    await runTeacherAction(
      () => classroomService.addQuestionToClassroomQuestionSet(questionSetId, {
        questionId: Number(resolvedQuestionId),
        pointWeight: Number(itemForm.pointWeight) || 1,
        sectionCode: itemForm.sectionCode.trim() || null,
      }),
      getText(t, 'classrooms.questionSets.feedback.questionAdded', 'Đã thêm câu hỏi.'),
      getText(t, 'classrooms.questionSets.errors.addQuestion', 'Không thêm được câu hỏi.')
    );
    setItemForm((current) => ({ ...current, questionId: '' }));
  };

  const removeQuestion = async (itemId) => {
    await runTeacherAction(
      () => classroomService.removeQuestionFromClassroomQuestionSet(questionSetId, itemId),
      getText(t, 'classrooms.questionSets.feedback.questionRemoved', 'Đã xóa câu hỏi khỏi bộ.'),
      getText(t, 'classrooms.questionSets.errors.removeQuestion', 'Không xóa được câu hỏi.')
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
      getText(t, 'classrooms.questionSets.feedback.reordered', 'Đã sắp xếp lại câu hỏi.'),
      getText(t, 'classrooms.questionSets.errors.reorder', 'Không sắp xếp được câu hỏi.')
    );
  };

  const publish = () => runTeacherAction(
    () => classroomService.publishClassroomQuestionSet(questionSetId),
    getText(t, 'classrooms.questionSets.feedback.published', 'Đã công bố bộ câu hỏi.'),
    getText(t, 'classrooms.questionSets.errors.publish', 'Không công bố được bộ câu hỏi.')
  );

  const unpublish = () => runTeacherAction(
    () => classroomService.unpublishClassroomQuestionSet(questionSetId),
    getText(t, 'classrooms.questionSets.feedback.unpublished', 'Đã đưa bộ câu hỏi về bản nháp.'),
    getText(t, 'classrooms.questionSets.errors.unpublish', 'Không gỡ công bố được bộ câu hỏi.')
  );

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.questionSets.detailTitle', 'Chi tiết bộ câu hỏi')} subtitle="">
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Đang tải...')} />
      </ClassroomShell>
    );
  }

  if (error && !questionSet) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.questionSets.detailTitle', 'Chi tiết bộ câu hỏi')} subtitle="">
        <MessageBar error={error} />
        <div className="classroom-page-actions">
          <Link className="classroom-button" to={`/classrooms/${classroomId}/question-sets`}>
            <LuListChecks aria-hidden="true" />
            {getText(t, 'classrooms.questionSets.backToList', 'Về danh sách')}
          </Link>
          <button className="classroom-button" type="button" onClick={loadDetail}>
            <LuRefreshCw aria-hidden="true" />
            {getText(t, 'classrooms.actions.retry', 'Thử lại')}
          </button>
        </div>
      </ClassroomShell>
    );
  }

  const orderedItems = [...(questionSet?.items || [])].sort(compareQuestionSetItems);
  const isPublished = questionSet?.visibility === 'Published';

  return (
    <ClassroomShell title={questionSet?.title || getText(t, 'classrooms.questionSets.detailTitle', 'Chi tiết bộ câu hỏi')} subtitle={classroom?.name || ''}>
      <MessageBar error={error} success={success} />

      <div className="classroom-page-actions">
        <Link className="classroom-button" to={`/classrooms/${classroomId}/question-sets`}>
          <LuListChecks aria-hidden="true" />
          {getText(t, 'classrooms.questionSets.backToList', 'Về danh sách')}
        </Link>
        <button className="classroom-button" type="button" onClick={loadDetail}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.actions.refresh', 'Làm mới')}
        </button>
      </div>

      <section className="classroom-detail-grid">
        <article className="classroom-panel classroom-summary">
          <span className={`classroom-badge ${isPublished ? '' : 'muted'}`}>{questionSet?.visibility || 'Draft'}</span>
          <h2>{questionSet?.title}</h2>
          <p>{questionSet?.description || getText(t, 'classrooms.questionSets.noDescription', 'Chưa có mô tả.')}</p>
          <div className="classroom-metrics">
            <Metric label={getText(t, 'classrooms.questionSets.itemCount', 'Câu hỏi')} value={questionSet?.itemCount || orderedItems.length} />
            <Metric label={getText(t, 'classrooms.questionSets.totalPoints', 'Điểm')} value={questionSet?.totalPoints || 0} />
            <Metric label="Document ID" value={questionSet?.documentId || '-'} />
          </div>
          {!isTeacher && (
            <p className="classroom-muted">{getText(t, 'classrooms.questionSets.readOnly', 'Bạn đang xem ở chế độ chỉ đọc.')}</p>
          )}
        </article>

        {isTeacher && (
          <form className="classroom-panel classroom-form" onSubmit={handleUpdate}>
            <div className="classroom-section-head">
              <div>
                <span className="classroom-kicker">{getText(t, 'classrooms.questionSets.teacherTools', 'Công cụ giảng viên')}</span>
                <h2>{getText(t, 'classrooms.questionSets.editTitle', 'Sửa bộ câu hỏi')}</h2>
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
                    {getText(t, 'classrooms.questionSets.publish', 'Công bố')}
                  </button>
                )}
                <button className="classroom-icon-button danger" type="button" onClick={handleDelete} disabled={working} title={getText(t, 'classrooms.questionSets.delete', 'Xóa')}>
                  <LuTrash2 aria-hidden="true" />
                </button>
              </div>
            </div>
            <QuestionSetFields form={editForm} onChange={setEditForm} t={t} />
            <button className="classroom-button primary" type="submit" disabled={working}>
              <LuSave aria-hidden="true" />
              {getText(t, 'classrooms.questionSets.save', 'Lưu')}
            </button>
          </form>
        )}
      </section>

      {isTeacher && (
        <section className="classroom-panel classroom-question-picker">
          <div className="classroom-section-head">
            <div>
              <span className="classroom-kicker">{getText(t, 'classrooms.questionSets.availableKicker', 'Question source')}</span>
              <h2>{getText(t, 'classrooms.questionSets.availableTitle', 'Câu hỏi khả dụng')}</h2>
            </div>
            <button className="classroom-button" type="button" onClick={loadAvailableQuestions} disabled={availableLoading}>
              <LuRefreshCw aria-hidden="true" />
              {availableLoading ? getText(t, 'classrooms.states.loading', 'Đang tải...') : getText(t, 'classrooms.questionSets.loadQuestions', 'Tải câu hỏi')}
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
                <span>{getText(t, 'classrooms.questionSets.pointWeight', 'Điểm')}</span>
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
                {getText(t, 'classrooms.questionSets.addById', 'Thêm bằng ID')}
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
            <h2>{getText(t, 'classrooms.questionSets.itemsTitle', 'Câu hỏi trong bộ')}</h2>
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
  const { currentUser } = useAuth();
  const [classroom, setClassroom] = useState(null);
  const [assignments, setAssignments] = useState([]);
  const [questionSets, setQuestionSets] = useState([]);
  const [form, setForm] = useState(emptyAssignmentForm);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const isTeacher = isClassroomTeacherRole(classroom?.currentUserRole) ||
                    sameId(classroom?.ownerUserId, currentUser?.id) ||
                    isSystemAdmin(currentUser);

  const loadPage = useCallback(async () => {
    setLoading(true);
    setError('');

    try {
      const classroomData = await classroomService.getClassroomDetail(classroomId);
      setClassroom(classroomData);
      const isTeacherRole = isClassroomTeacherRole(classroomData?.currentUserRole) ||
                            sameId(classroomData?.ownerUserId, currentUser?.id) ||
                            isSystemAdmin(currentUser);
      if (!isTeacherRole) {
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
      setError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.load', 'Không tải được bài kiểm tra.')));
    } finally {
      setLoading(false);
    }
  }, [classroomId, t, currentUser]);

  useEffect(() => {
    loadPage();
  }, [loadPage]);

  const handleCreate = async (event) => {
    event.preventDefault();
    if (!form.title.trim() || !form.questionSetId) {
      setError(getText(t, 'classrooms.assignments.errors.required', 'Nhập tiêu đề và chọn bộ câu hỏi.'));
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
      setSuccess(getText(t, 'classrooms.assignments.feedback.created', 'Đã tạo bài kiểm tra.'));
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.create', 'Không tạo được bài kiểm tra.')));
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.title', 'Bài kiểm tra')} subtitle="">
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Đang tải...')} />
      </ClassroomShell>
    );
  }

  if (!isTeacher) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.title', 'Bài kiểm tra')} subtitle={classroom?.name || ''}>
        <MessageBar error={getText(t, 'classrooms.assignments.errors.teacherOnly', 'Chỉ giảng viên của lớp mới quản lý bài kiểm tra.')} />
        <Link className="classroom-button" to={`/classrooms/${classroomId}/student/assignments`}>
          <LuGraduationCap aria-hidden="true" />
          {getText(t, 'classrooms.assignments.studentList', 'Bài kiểm tra của học viên')}
        </Link>
      </ClassroomShell>
    );
  }

  return (
    <ClassroomShell title={getText(t, 'classrooms.assignments.title', 'Bài kiểm tra')} subtitle={classroom?.name || ''}>
      <MessageBar error={error} success={success} />
      <ClassroomResourceLinks classroomId={classroomId} isTeacher={isTeacher} t={t} />

      <section className="classroom-layout">
        <form className="classroom-panel classroom-form" onSubmit={handleCreate}>
          <div>
            <span className="classroom-kicker">{getText(t, 'classrooms.assignments.teacherTools', 'Công cụ giảng viên')}</span>
            <h2>{getText(t, 'classrooms.assignments.createTitle', 'Tạo bài kiểm tra')}</h2>
          </div>
          <AssignmentFields form={form} onChange={setForm} questionSets={questionSets} t={t} />
          <button className="classroom-button primary" type="submit" disabled={saving}>
            <LuPlus aria-hidden="true" />
            {saving ? getText(t, 'classrooms.assignments.creating', 'Đang tạo...') : getText(t, 'classrooms.assignments.create', 'Tạo bài kiểm tra')}
          </button>
        </form>

        <AssignmentList
          assignments={assignments}
          classroomId={classroomId}
          emptyBody={getText(t, 'classrooms.assignments.emptyBody', 'Tạo bài kiểm tra từ bộ câu hỏi đã công bố.')}
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
  const { currentUser } = useAuth();
  const [classroom, setClassroom] = useState(null);
  const [assignment, setAssignment] = useState(null);
  const [questionSets, setQuestionSets] = useState([]);
  const [form, setForm] = useState(emptyAssignmentForm);
  const [loading, setLoading] = useState(true);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [questionStats, setQuestionStats] = useState([]);

  const isTeacher = isClassroomTeacherRole(classroom?.currentUserRole) ||
                    sameId(classroom?.ownerUserId, currentUser?.id) ||
                    isSystemAdmin(currentUser);

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
      const isTeacherRole = isClassroomTeacherRole(classroomData?.currentUserRole) ||
                            sameId(classroomData?.ownerUserId, currentUser?.id) ||
                            isSystemAdmin(currentUser);
      if (!isTeacherRole) {
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
      setError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.detail', 'Không tải được bài kiểm tra.')));
    } finally {
      setLoading(false);
    }
  }, [assignmentId, classroomId, t, currentUser]);

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
      setError(getText(t, 'classrooms.assignments.errors.titleRequired', 'Nhập tiêu đề bài kiểm tra.'));
      return;
    }

    const validationError = validateScoringForm(form, t);
    if (validationError) {
      setError(validationError);
      return;
    }

    await runAction(
      () => classroomService.updateClassroomAssignment(assignmentId, buildAssignmentPayload(form)),
      getText(t, 'classrooms.assignments.feedback.updated', 'Đã cập nhật bài kiểm tra.'),
      getText(t, 'classrooms.assignments.errors.update', 'Không cập nhật được bài kiểm tra.')
    );
  };

  const handleDelete = async () => {
    if (!window.confirm(getText(t, 'classrooms.assignments.confirmDelete', 'Xóa bài kiểm tra này?'))) {
      return;
    }

    setWorking(true);
    setError('');
    try {
      await classroomService.deleteClassroomAssignment(assignmentId);
      navigate(`/classrooms/${classroomId}/assignments`);
    } catch (err) {
      setError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.delete', 'Không xóa được bài kiểm tra.')));
      setWorking(false);
    }
  };

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.detailTitle', 'Chi tiết bài kiểm tra')} subtitle="">
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Đang tải...')} />
      </ClassroomShell>
    );
  }

  if (!isTeacher) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.detailTitle', 'Chi tiết bài kiểm tra')} subtitle={classroom?.name || ''}>
        <MessageBar error={getText(t, 'classrooms.assignments.errors.teacherOnly', 'Chỉ giảng viên của lớp mới quản lý bài kiểm tra.')} />
        <Link className="classroom-button" to={`/classrooms/${classroomId}/student/assignments/${assignmentId}`}>
          <LuGraduationCap aria-hidden="true" />
          {getText(t, 'classrooms.assignments.openStudentView', 'Mở trang học viên')}
        </Link>
      </ClassroomShell>
    );
  }

  if (error && !assignment) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.detailTitle', 'Chi tiết bài kiểm tra')} subtitle={classroom?.name || ''}>
        <MessageBar error={error} />
        <Link className="classroom-button" to={`/classrooms/${classroomId}/assignments`}>
          <LuClipboard aria-hidden="true" />
          {getText(t, 'classrooms.assignments.backToList', 'Về danh sách')}
        </Link>
      </ClassroomShell>
    );
  }

  const items = Array.isArray(assignment?.items) ? assignment.items : [];

  return (
    <ClassroomShell title={assignment?.title || getText(t, 'classrooms.assignments.detailTitle', 'Chi tiết bài kiểm tra')} subtitle={classroom?.name || ''}>
      <MessageBar error={error} success={success} />
      <div className="classroom-page-actions">
        <Link className="classroom-button" to={`/classrooms/${classroomId}/assignments`}>
          <LuClipboard aria-hidden="true" />
          {getText(t, 'classrooms.assignments.backToList', 'Về danh sách')}
        </Link>
        <Link className="classroom-button" to={`/classrooms/${classroomId}/assignments/${assignmentId}/attempts`}>
          <LuListChecks aria-hidden="true" />
          {getText(t, 'classrooms.assignments.viewAttempts', 'Lượt làm')}
        </Link>
        <Link className="classroom-button" to={`/classrooms/${classroomId}/assignments/${assignmentId}/leaderboard`}>
          <LuListChecks aria-hidden="true" />
          {getText(t, 'classrooms.leaderboard.assignmentTitle', 'Bảng xếp hạng bài kiểm tra')}
        </Link>
        <button className="classroom-button" type="button" onClick={loadDetail}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.actions.refresh', 'Làm mới')}
        </button>
      </div>

      <section className="classroom-detail-grid">
        <article className="classroom-panel classroom-summary">
          <span className={`classroom-badge ${assignment?.status === 'Published' ? '' : 'muted'}`}>{assignment?.status || 'Draft'}</span>
          <h2>{assignment?.title}</h2>
          <p>{assignment?.description || getText(t, 'classrooms.assignments.noDescription', 'Chưa có mô tả.')}</p>
          <div className="classroom-metrics">
            <Metric label={getText(t, 'classrooms.assignments.itemCount', 'Câu hỏi')} value={assignment?.itemCount || items.length} />
            <Metric label={getText(t, 'classrooms.assignments.totalPoints', 'Điểm')} value={assignment?.totalPoints || 0} />
            <Metric label={getText(t, 'classrooms.assignments.attemptLimit', 'Lượt làm')} value={assignment?.attemptLimit || 1} />
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
              <span className="classroom-kicker">{getText(t, 'classrooms.assignments.teacherTools', 'Công cụ giảng viên')}</span>
              <h2>{getText(t, 'classrooms.assignments.editTitle', 'Sửa bài kiểm tra')}</h2>
            </div>
            <div className="classroom-row-actions">
              {assignment?.status !== 'Published' && (
                <button className="classroom-button primary" type="button" onClick={() => runAction(
                  () => classroomService.publishClassroomAssignment(assignmentId),
                  getText(t, 'classrooms.assignments.feedback.published', 'Đã công bố bài kiểm tra.'),
                  getText(t, 'classrooms.assignments.errors.publish', 'Không công bố được bài kiểm tra.')
                )} disabled={working}>
                  <LuCheck aria-hidden="true" />
                  {getText(t, 'classrooms.assignments.publish', 'Công bố')}
                </button>
              )}
              {assignment?.status !== 'Closed' && (
                <button className="classroom-button" type="button" onClick={() => runAction(
                  () => classroomService.closeClassroomAssignment(assignmentId),
                  getText(t, 'classrooms.assignments.feedback.closed', 'Đã đóng bài kiểm tra.'),
                  getText(t, 'classrooms.assignments.errors.close', 'Không đóng được bài kiểm tra.')
                )} disabled={working}>
                  <LuBan aria-hidden="true" />
                  {getText(t, 'classrooms.assignments.close', 'Đóng')}
                </button>
              )}
              <button className="classroom-icon-button danger" type="button" onClick={handleDelete} disabled={working} title={getText(t, 'classrooms.assignments.delete', 'Xóa')}>
                <LuTrash2 aria-hidden="true" />
              </button>
            </div>
          </div>
          <AssignmentFields form={form} onChange={setForm} questionSets={questionSets} t={t} />
          <button className="classroom-button primary" type="submit" disabled={working}>
            <LuSave aria-hidden="true" />
            {getText(t, 'classrooms.assignments.save', 'Lưu')}
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
            <h2>{getText(t, 'classrooms.assignments.questionsInAssignment', 'Câu hỏi trong bài kiểm tra')}</h2>
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
  const { currentUser } = useAuth();
  const [classroom, setClassroom] = useState(null);
  const [assignment, setAssignment] = useState(null);
  const [attempts, setAttempts] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const isTeacher = isClassroomTeacherRole(classroom?.currentUserRole) ||
                    sameId(classroom?.ownerUserId, currentUser?.id) ||
                    isSystemAdmin(currentUser);

  const loadPage = useCallback(async () => {
    setLoading(true);
    setError('');

    try {
      const classroomData = await classroomService.getClassroomDetail(classroomId);
      setClassroom(classroomData);
      const isTeacherRole = isClassroomTeacherRole(classroomData?.currentUserRole) ||
                            sameId(classroomData?.ownerUserId, currentUser?.id) ||
                            isSystemAdmin(currentUser);
      if (!isTeacherRole) {
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
  }, [assignmentId, classroomId, t, currentUser]);

  useEffect(() => {
    loadPage();
  }, [loadPage]);

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.attemptsTitle', 'Lượt làm')} subtitle="">
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Đang tải...')} />
      </ClassroomShell>
    );
  }

  if (!isTeacher) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.attemptsTitle', 'Lượt làm')} subtitle={classroom?.name || ''}>
        <MessageBar error={getText(t, 'classrooms.assignments.errors.teacherOnly', 'Chỉ giảng viên của lớp mới xem lượt làm bài.')} />
      </ClassroomShell>
    );
  }

  return (
    <ClassroomShell title={getText(t, 'classrooms.assignments.attemptsTitle', 'Lượt làm')} subtitle={assignment?.title || classroom?.name || ''}>
      <MessageBar error={error} />
      <div className="classroom-page-actions">
        <Link className="classroom-button" to={`/classrooms/${classroomId}/assignments/${assignmentId}`}>
          <LuClipboard aria-hidden="true" />
          {getText(t, 'classrooms.assignments.backToDetail', 'Về bài kiểm tra')}
        </Link>
        <button className="classroom-button" type="button" onClick={loadPage}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.actions.refresh', 'Làm mới')}
        </button>
      </div>

      <section className="classroom-panel classroom-question-set-items">
        {!attempts.length ? (
          <p className="classroom-muted">{getText(t, 'classrooms.assignments.noAttempts', 'Chưa có lượt làm nào.')}</p>
        ) : (
          <div className="classroom-question-list">
            {attempts.map((attempt) => (
              <article className="classroom-question-row" key={attempt.id}>
                <div>
                  <strong>{attempt.user?.fullName || attempt.user?.email || `User ${attempt.userId}`}</strong>
                  <small>
                    Lượt làm #{attempt.attemptNumber || '-'} | {attempt.status}
                    {' | '}
                    Điểm: {attempt.rawScore ?? '-'} / {attempt.percentScore != null ? `${attempt.percentScore}%` : '-'}
                    {' | '}
                    Bắt đầu: {formatDateTime(attempt.startedAt)}
                    {' | '}
                    Đã nộp: {formatDateTime(attempt.submittedAt)}
                    {' | '}
                    Thời gian: {attempt.durationSeconds ?? 0} giây
                  </small>
                  <AttemptAnswers answers={attempt.answers || []} reveal />
                </div>
                <Link className="classroom-button" to={`/classroom-attempts/${attempt.id}/result`}>
                  {getText(t, 'classrooms.assignments.viewAttempt', 'Xem chi tiết')}
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
      setError(localizeBackendError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.studentLoad', 'Không tải được bài kiểm tra của học viên.')), t));
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
      const isExisting = attempt.startedAt && (new Date() - new Date(attempt.startedAt) > 10000);
      if (isExisting) {
        setSuccess(getText(t, 'classrooms.assignments.feedback.resumed', 'Bạn đang có lượt làm bài đang diễn ra. Hệ thống sẽ tiếp tục lượt làm hiện tại.'));
      } else {
        setSuccess(getText(t, 'classrooms.assignments.feedback.started', 'Đã mở lượt làm.'));
      }
      setTimeout(() => {
        navigate(`/classroom-attempts/${attempt.id}`);
      }, 1000);
    } catch (err) {
      setError(localizeBackendError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.start', 'Không bắt đầu được bài kiểm tra.')), t));
    } finally {
      setStartingId(null);
    }
  };

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.studentTitle', 'Bài kiểm tra của học viên')} subtitle="">
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Đang tải...')} />
      </ClassroomShell>
    );
  }

  return (
    <ClassroomShell title={getText(t, 'classrooms.assignments.studentTitle', 'Bài kiểm tra của học viên')} subtitle={classroom?.name || ''}>
      <MessageBar error={error} success={success} />
      <ClassroomResourceLinks classroomId={classroomId} isTeacher={false} t={t} />
      <div className="classroom-page-actions">
        <Link className="classroom-button" to="/classroom-attempts/history">
          <LuClipboard aria-hidden="true" />
          {getText(t, 'classrooms.assignments.history', 'Lịch sử làm bài')}
        </Link>
      </div>
      <AssignmentList
        assignments={assignments}
        attempts={attempts}
        classroomId={classroomId}
        emptyBody={getText(t, 'classrooms.assignments.studentEmpty', 'Lớp học chưa có bài kiểm tra nào được giao.')}
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
  const [success, setSuccess] = useState('');

  const loadPage = useCallback(async () => {
    setLoading(true);
    setError('');
    setSuccess('');

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
        setError(getText(t, 'classrooms.assignments.errors.studentForbidden', 'Bạn không có quyền truy cập bài kiểm tra này.'));
        return;
      }
      setAssignment(visibleAssignment);
    } catch (err) {
      setError(localizeBackendError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.studentDetail', 'Không tải được bài kiểm tra.')), t));
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
    setSuccess('');
    try {
      const attempt = await classroomService.startClassroomAssignmentAttempt(assignmentId);
      const isExisting = attempt.startedAt && (new Date() - new Date(attempt.startedAt) > 10000);
      if (isExisting) {
        setSuccess(getText(t, 'classrooms.assignments.feedback.resumed', 'Bạn đang có lượt làm bài đang diễn ra. Hệ thống sẽ tiếp tục lượt làm hiện tại.'));
      } else {
        setSuccess(getText(t, 'classrooms.assignments.feedback.started', 'Đã mở lượt làm.'));
      }
      setTimeout(() => {
        navigate(`/classroom-attempts/${attempt.id}`);
      }, 1000);
    } catch (err) {
      setError(localizeBackendError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.start', 'Không bắt đầu được bài kiểm tra.')), t));
    } finally {
      setStarting(false);
    }
  };

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.studentDetailTitle', 'Bài kiểm tra')} subtitle="">
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Đang tải...')} />
      </ClassroomShell>
    );
  }

  const attempt = findLatestAttemptForAssignment(attempts, assignmentId);
  const items = Array.isArray(assignment?.items) ? assignment.items : [];

  return (
    <ClassroomShell title={assignment?.title || getText(t, 'classrooms.assignments.studentDetailTitle', 'Bài kiểm tra')} subtitle={classroom?.name || ''}>
      <MessageBar error={error} success={success} />
      <div className="classroom-page-actions">
        <Link className="classroom-button" to={`/classrooms/${classroomId}/student/assignments`}>
          <LuClipboard aria-hidden="true" />
          {getText(t, 'classrooms.assignments.backToList', 'Về danh sách')}
        </Link>
        <Link className="classroom-button" to={`/classrooms/${classroomId}/assignments/${assignmentId}/leaderboard`}>
          <LuListChecks aria-hidden="true" />
          {getText(t, 'classrooms.leaderboard.assignmentTitle', 'Bảng xếp hạng bài kiểm tra')}
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
            <p>{assignment.description || getText(t, 'classrooms.assignments.noDescription', 'Chưa có mô tả.')}</p>
            <div className="classroom-metrics">
              <Metric label={getText(t, 'classrooms.assignments.itemCount', 'Câu hỏi')} value={assignment.itemCount || items.length} />
              <Metric label={getText(t, 'classrooms.assignments.totalPoints', 'Điểm')} value={assignment.totalPoints || 0} />
              <Metric label={getText(t, 'classrooms.assignments.attemptLimit', 'Lượt làm')} value={assignment.attemptLimit || 1} />
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
      setError(localizeBackendError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.attemptDetail', 'Khong tai duoc attempt.')), t));
    } finally {
      setLoading(false);
    }
  }, [attemptId, navigate, t]);

  useEffect(() => {
    loadAttempt();
  }, [loadAttempt]);

  const items = Array.isArray(attempt?.assignment?.items) ? attempt.assignment.items : [];
  const answeredCount = items.filter((item) => answers[item.questionId]).length;

  const isClosed = attempt?.assignment?.status === 'Closed';
  const isExpired = attempt?.status === 'Expired';
  const isPastDue = attempt?.assignment?.dueAt && new Date(attempt.assignment.dueAt) <= new Date();
  
  // Calculate if time limit exceeded
  const isTimeLimitExceeded = attempt?.assignment?.timeLimitMinutes && attempt?.startedAt &&
    (new Date(attempt.startedAt).getTime() + attempt.assignment.timeLimitMinutes * 60 * 1000) <= new Date().getTime();

  const isReadOnly = isClosed || isExpired || isPastDue || isTimeLimitExceeded;

  let readOnlyError = '';
  if (isClosed) {
    readOnlyError = getText(t, 'classrooms.assignments.errors.closed', 'Bài kiểm tra đã được đóng.');
  } else if (isExpired || isPastDue || isTimeLimitExceeded) {
    readOnlyError = getText(t, 'classrooms.assignments.errors.expired', 'Bài làm đã hết hạn.');
  }

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
      setError(localizeBackendError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.answer', 'Không lưu được câu trả lời. Vui lòng thử lại.')), t));
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
      setError(localizeBackendError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.submitAttempt', 'Không nộp được bài. Vui lòng thử lại.')), t));
    } finally {
      setSubmitting(false);
    }
  };

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.attemptTitle', 'Làm bài kiểm tra')} subtitle="">
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Đang tải...')} />
      </ClassroomShell>
    );
  }

  const displayedError = error || readOnlyError;

  return (
    <ClassroomShell title={attempt?.assignment?.title || getText(t, 'classrooms.assignments.attemptTitle', 'Làm bài kiểm tra')} subtitle={`${answeredCount}/${items.length} ${getText(t, 'classrooms.assignments.answered', 'đã trả lời')}`}>
      <MessageBar error={displayedError} success={success} />
      <section className="classroom-panel classroom-attempt-toolbar">
        <div>
          <span className="classroom-kicker">{attempt?.status || 'InProgress'}</span>
          <h2>{getText(t, 'classrooms.assignments.progress', 'Tiến độ')}: {answeredCount}/{items.length}</h2>
        </div>
        <button className="classroom-button primary" type="button" onClick={submitAttempt} disabled={submitting || !items.length || isReadOnly}>
          <LuCheck aria-hidden="true" />
          {submitting ? getText(t, 'classrooms.assignments.submitting', 'Đang nộp...') : getText(t, 'classrooms.assignments.submitAttempt', 'Nộp bài')}
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
            disabled={isReadOnly}
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
      setError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.result', 'Không tải được kết quả.')));
    } finally {
      setLoading(false);
    }
  }, [attemptId, t]);

  useEffect(() => {
    loadAttempt();
  }, [loadAttempt]);

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.assignments.resultTitle', 'Kết quả bài kiểm tra')} subtitle="">
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Đang tải...')} />
      </ClassroomShell>
    );
  }

  const answers = Array.isArray(attempt?.answers) ? attempt.answers : [];
  const reveal = answers.some((answer) => Object.prototype.hasOwnProperty.call(answer, 'isCorrect') || answer.question?.correctAnswer);

  return (
    <ClassroomShell title={getText(t, 'classrooms.assignments.resultTitle', 'Kết quả bài kiểm tra')} subtitle={attempt?.assignment?.title || ''}>
      <MessageBar error={error} />
      <div className="classroom-page-actions">
        <Link className="classroom-button" to="/classroom-attempts/history">
          <LuClipboard aria-hidden="true" />
          {getText(t, 'classrooms.assignments.history', 'Lịch sử làm bài')}
        </Link>
        {attempt?.assignment?.classroomWorkspaceId && (
          <Link className="classroom-button" to={`/classrooms/${attempt.assignment.classroomWorkspaceId}/assignments/${attempt.classroomAssignmentId || attempt.assignment.id}/leaderboard`}>
            <LuListChecks aria-hidden="true" />
            {getText(t, 'classrooms.leaderboard.assignmentTitle', 'Bảng xếp hạng bài kiểm tra')}
          </Link>
        )}
      </div>
      {attempt && (
        <>
          <section className="classroom-panel classroom-summary">
            <span className="classroom-badge">{attempt.status}</span>
            <div className="classroom-metrics">
              <Metric label={getText(t, 'classrooms.assignments.rawScore', 'Điểm')} value={attempt.rawScore ?? '-'} />
              <Metric label={getText(t, 'classrooms.assignments.percentScore', 'Phần trăm')} value={attempt.percentScore != null ? `${attempt.percentScore}%` : '-'} />
              <Metric label={getText(t, 'classrooms.assignments.answeredCount', 'Da tra loi')} value={answers.length} />
            </div>
            {attempt.assignment?.scoringMode === 'EmpiricalDifficulty' && (
              <div className="classroom-scoring-mode-notice text-primary" style={{ marginTop: '1rem', fontWeight: 500 }}>
                {getText(t, 'classrooms.assignments.empiricalScoringNote', 'Điểm được tính theo độ khó thực nghiệm của câu hỏi.')}
              </div>
            )}
            {!attempt.assignment?.showAnswerAfterSubmit && (
              <p className="classroom-muted" style={{ marginTop: '0.5rem' }}>{getText(t, 'classrooms.assignments.hiddenAnswers', 'Giảng viên đang ẩn đáp án đúng; trang này chỉ hiện tổng điểm.')}</p>
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
                  <span className="classroom-kicker">{getText(t, 'classrooms.assignments.review', 'Soát lại')}</span>
                  <h2>{getText(t, 'classrooms.assignments.answerReview', 'Xem lại câu trả lời')}</h2>
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
      setError(getApiErrorMessage(err, getText(t, 'classrooms.assignments.errors.history', 'Không tải được lịch sử làm bài kiểm tra.')));
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
      <ClassroomShell title={getText(t, 'classrooms.assignments.historyTitle', 'Lịch sử làm bài kiểm tra')} subtitle="">
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Đang tải...')} />
      </ClassroomShell>
    );
  }

  return (
    <ClassroomShell title={getText(t, 'classrooms.assignments.historyTitle', 'Lịch sử làm bài kiểm tra')} subtitle={getText(t, 'classrooms.assignments.historySubtitle', 'Tất cả lượt làm của bạn trong lớp học.')}>
      <MessageBar error={error} />
      <div className="classroom-page-actions">
        <button className="classroom-button" type="button" onClick={loadHistory}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.actions.refresh', 'Làm mới')}
        </button>
      </div>

      {!attempts.length ? (
        <section className="classroom-panel classroom-empty">
          <LuClipboard aria-hidden="true" />
          <h2>{getText(t, 'classrooms.assignments.historyEmptyTitle', 'Chưa có lượt làm nào')}</h2>
          <p>{getText(t, 'classrooms.assignments.historyEmptyBody', 'Bắt đầu một bài kiểm tra để lịch sử xuất hiện tại đây.')}</p>
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
                    Lượt làm #{attempt.attemptNumber || '-'} | Bắt đầu: {formatDateTime(attempt.startedAt)}
                    {' | '}
                    Đã nộp: {formatDateTime(attempt.submittedAt)}
                    {' | '}
                    Điểm: {attempt.rawScore ?? '-'} / {attempt.percentScore != null ? `${attempt.percentScore}%` : '-'}
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

function ClassroomShell({ children, subtitle, title, roleBadge }) {
  const { t } = useLanguage();
  return (
    <main className="classroom-page">
      <header className="classroom-hero">
        <div className="classroom-hero-content">
          <div className="classroom-hero-main">
            <span className="classroom-kicker">{getText(t, 'classrooms.detail.kicker', 'Lớp học')}</span>
            <div className="classroom-title-row" style={{ display: 'flex', alignItems: 'center', gap: '12px', flexWrap: 'wrap' }}>
              <h1 style={{ margin: 0 }}>{title}</h1>
              {roleBadge && <div className="classroom-role-badge-wrapper">{roleBadge}</div>}
            </div>
            {subtitle && <p style={{ margin: '8px 0 0' }}>{subtitle}</p>}
          </div>
        </div>
      </header>
      {children}
    </main>
  );
}

function ClassroomTabs({ active }) {
  const { t } = useLanguage();
  const tabs = [
    { id: 'teaching', to: '/classrooms/teaching', label: getText(t, 'classrooms.tabs.teaching', 'Đang dạy'), icon: <LuSchool aria-hidden="true" /> },
    { id: 'joined', to: '/classrooms/joined', label: getText(t, 'classrooms.tabs.joined', 'Đã tham gia'), icon: <LuGraduationCap aria-hidden="true" /> },
    { id: 'join', to: '/classrooms/join', label: getText(t, 'classrooms.tabs.join', 'Nhập mã'), icon: <LuDoorOpen aria-hidden="true" /> },
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
    return <LoadingCard label={getText(t, 'classrooms.states.loading', 'Đang tải...')} />;
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
            <p>{classroom.description || getText(t, 'classrooms.detail.noDescription', 'Chưa có mô tả.')}</p>
            <small>
              {getText(t, 'classrooms.metrics.members', 'Thành viên')}: {classroom.memberCount || 0}
              {' · '}
              {getText(t, 'classrooms.detail.updated', 'Cập nhật')}: {formatDateTime(classroom.updatedAt)}
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
        <span>{getText(t, 'classrooms.questionSets.fields.title', 'Tiêu đề')}</span>
        <input
          value={form.title}
          onChange={(event) => onChange((current) => ({ ...current, title: event.target.value }))}
          placeholder={getText(t, 'classrooms.questionSets.fields.titlePlaceholder', 'Ví dụ: Ôn tập từ vựng N5')}
        />
      </label>
      <label>
        <span>{getText(t, 'classrooms.questionSets.fields.description', 'Mô tả')}</span>
        <textarea
          rows={3}
          value={form.description}
          onChange={(event) => onChange((current) => ({ ...current, description: event.target.value }))}
          placeholder={getText(t, 'classrooms.questionSets.fields.descriptionPlaceholder', 'Ghi chú ngắn cho giảng viên')}
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
    return <LoadingCard label={getText(t, 'classrooms.states.loading', 'Đang tải...')} />;
  }

  if (!questionSets.length) {
    return (
      <section className="classroom-panel classroom-empty">
        <LuFileQuestion aria-hidden="true" />
        <h2>{getText(t, 'classrooms.questionSets.emptyTitle', 'Chưa có bộ câu hỏi')}</h2>
        <p>{getText(t, 'classrooms.questionSets.emptyBody', 'Tạo bộ câu hỏi đầu tiên để gom các câu hỏi đã sinh trong lớp học.')}</p>
        <button className="classroom-button" type="button" onClick={onRetry}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.actions.refresh', 'Làm mới')}
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
            <p>{questionSet.description || getText(t, 'classrooms.questionSets.noDescription', 'Chưa có mô tả.')}</p>
            <small>
              {getText(t, 'classrooms.questionSets.itemCount', 'Câu hỏi')}: {questionSet.itemCount || 0}
              {' | '}
              {getText(t, 'classrooms.detail.updated', 'Cập nhật')}: {formatDateTime(questionSet.updatedAt)}
            </small>
          </div>
        </Link>
      ))}
    </section>
  );
}

function AvailableQuestionList({ loading, onAdd, questions, t, working }) {
  if (loading) {
    return <LoadingCard label={getText(t, 'classrooms.states.loading', 'Đang tải...')} />;
  }

  if (!questions.length) {
    return <p className="classroom-muted">{getText(t, 'classrooms.questionSets.availableEmpty', 'Chưa có câu hỏi khả dụng cho mã tài liệu này.')}</p>;
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
            {getText(t, 'classrooms.questionSets.add', 'Thêm')}
          </button>
        </div>
      ))}
    </div>
  );
}

function QuestionSetItems({ isTeacher, items, onMove, onRemove, t, working }) {
  if (!items.length) {
    return <p className="classroom-muted">{getText(t, 'classrooms.questionSets.itemsEmpty', 'Bộ câu hỏi này chưa có câu hỏi nào.')}</p>;
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
              {getText(t, 'classrooms.questionSets.pointWeight', 'Điểm')}: {item.pointWeight}
              {item.sectionCode ? ` | ${item.sectionCode}` : ''}
            </small>
          </div>
          {isTeacher && (
            <div className="classroom-row-actions">
              <button className="classroom-icon-button" type="button" onClick={() => onMove(item.id, -1)} disabled={working || index === 0} title={getText(t, 'classrooms.questionSets.moveUp', 'Lên')}>
                <LuArrowUp aria-hidden="true" />
              </button>
              <button className="classroom-icon-button" type="button" onClick={() => onMove(item.id, 1)} disabled={working || index === items.length - 1} title={getText(t, 'classrooms.questionSets.moveDown', 'Xuống')}>
                <LuArrowDown aria-hidden="true" />
              </button>
              <button className="classroom-icon-button danger" type="button" onClick={() => onRemove(item.id)} disabled={working} title={getText(t, 'classrooms.questionSets.remove', 'Xóa')}>
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
        {getText(t, 'classrooms.assignments.classOverview', 'Tổng quan lớp')}
      </Link>
      <Link className="classroom-button" to={`/classrooms/${classroomId}/question-sets`}>
        <LuListChecks aria-hidden="true" />
        {getText(t, 'classrooms.questionSets.open', 'Bộ câu hỏi')}
      </Link>
      <Link className="classroom-button primary" to={isTeacher ? `/classrooms/${classroomId}/assignments` : `/classrooms/${classroomId}/student/assignments`}>
        <LuClipboard aria-hidden="true" />
        {getText(t, 'classrooms.assignments.open', 'Bài kiểm tra')}
      </Link>
      {isTeacher && (
        <Link className="classroom-button" to={`/classrooms/${classroomId}/members`}>
          <LuGraduationCap aria-hidden="true" />
          {getText(t, 'classrooms.members.title', 'Danh sách thành viên')}
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
        <span>{getText(t, 'classrooms.assignments.fields.title', 'Tiêu đề')}</span>
        <input
          value={form.title}
          onChange={(event) => update({ title: event.target.value })}
          placeholder={getText(t, 'classrooms.assignments.fields.titlePlaceholder', 'Vi du: N5 midterm quiz')}
        />
      </label>
      <label>
        <span>{getText(t, 'classrooms.assignments.fields.description', 'Mô tả')}</span>
        <textarea
          rows={3}
          value={form.description}
          onChange={(event) => update({ description: event.target.value })}
          placeholder={getText(t, 'classrooms.assignments.fields.descriptionPlaceholder', 'Hướng dẫn ngắn cho học viên')}
        />
      </label>
      <label>
        <span>{getText(t, 'classrooms.assignments.fields.questionSet', 'Bộ câu hỏi đã công bố')}</span>
        {questionSets.length ? (
          <select value={form.questionSetId} onChange={(event) => update({ questionSetId: event.target.value })}>
            <option value="">{getText(t, 'classrooms.assignments.fields.selectQuestionSet', 'Chọn bộ câu hỏi')}</option>
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
          <span>{getText(t, 'classrooms.assignments.fields.type', 'Loại bài')}</span>
          <select value={form.type} onChange={(event) => update({ type: event.target.value })}>
            {['Quiz', 'Test', 'Flashcard', 'Mixed'].map((type) => (
              <option key={type} value={type}>{type}</option>
            ))}
          </select>
        </label>
        <label>
          <span>{getText(t, 'classrooms.assignments.fields.attemptLimit', 'Giới hạn lượt nộp')}</span>
          <input
            inputMode="numeric"
            min="1"
            value={form.attemptLimit}
            onChange={(event) => update({ attemptLimit: event.target.value.replace(/\D/g, '') || '1' })}
          />
        </label>
        <label>
          <span>{getText(t, 'classrooms.assignments.fields.timeLimit', 'Thời gian làm bài')}</span>
          <input
            inputMode="numeric"
            value={form.timeLimitMinutes}
            onChange={(event) => update({ timeLimitMinutes: event.target.value.replace(/\D/g, '') })}
            placeholder={getText(t, 'classrooms.assignments.fields.optionalMinutes', 'Phút, không bắt buộc')}
          />
        </label>
      </div>
      <div className="classroom-form-grid">
        <label>
          <span>{getText(t, 'classrooms.assignments.fields.startAt', 'Thời gian bắt đầu')}</span>
          <input type="datetime-local" value={form.startAt} onChange={(event) => update({ startAt: event.target.value })} />
        </label>
        <label>
          <span>{getText(t, 'classrooms.assignments.fields.dueAt', 'Hạn nộp')}</span>
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
    return <LoadingCard label={getText(t, 'classrooms.states.loading', 'Đang tải...')} />;
  }

  if (!assignments.length) {
    return (
      <section className="classroom-panel classroom-empty">
        <LuClipboard aria-hidden="true" />
        <h2>{getText(t, 'classrooms.assignments.emptyTitle', 'Chưa có bài kiểm tra')}</h2>
        <p>{emptyBody}</p>
        <button className="classroom-button" type="button" onClick={onRetry}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.actions.refresh', 'Làm mới')}
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
                <span className={`classroom-badge ${assignment.status === 'Published' ? '' : 'muted'}`}>
                  {teacher ? translateAssignmentStatus(assignment.status, t) : translateAttemptStatus(studentStatus, t)}
                </span>
              </div>
              <p>{assignment.description || getText(t, 'classrooms.assignments.noDescription', 'Chưa có mô tả.')}</p>
              <small>
                {assignment.type} | {getText(t, 'classrooms.assignments.attemptLimit', 'Lượt làm')}: {assignment.attemptLimit || 1}
                {' | '}
                {getText(t, 'classrooms.assignments.fields.dueAt', 'Hạn nộp')}: {formatDateTime(assignment.dueAt)}
              </small>
              <div className="classroom-row-actions classroom-card-actions">
                {teacher ? (
                  <>
                    <Link className="classroom-button" to={`/classrooms/${classroomId}/assignments/${assignment.id}`}>
                      {getText(t, 'classrooms.assignments.openDetail', 'Chi tiết')}
                    </Link>
                    <Link className="classroom-button" to={`/classrooms/${classroomId}/assignments/${assignment.id}/attempts`}>
                      {getText(t, 'classrooms.assignments.viewAttempts', 'Lượt làm')}
                    </Link>
                  </>
                ) : (
                  <>
                    <Link className="classroom-button" to={`/classrooms/${classroomId}/student/assignments/${assignment.id}`}>
                      {getText(t, 'classrooms.assignments.openDetail', 'Chi tiết')}
                    </Link>
                    {attempt?.status === 'InProgress' ? (
                      <Link className="classroom-button primary" to={`/classroom-attempts/${attempt.id}`}>
                        {getText(t, 'classrooms.assignments.resume', 'Tiếp tục')}
                      </Link>
                    ) : attempt?.status === 'Submitted' ? (
                      <Link className="classroom-button primary" to={`/classroom-attempts/${attempt.id}/result`}>
                        {getText(t, 'classrooms.assignments.result', 'Kết quả')}
                      </Link>
                    ) : (
                      <button className="classroom-button primary" type="button" onClick={() => onStart?.(assignment.id)} disabled={startingId === assignment.id}>
                        {startingId === assignment.id ? getText(t, 'classrooms.assignments.starting', 'Đang mở...') : getText(t, 'classrooms.assignments.start', 'Bắt đầu')}
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
    return 'submitted';
  }
  if (attempt?.status === 'InProgress') {
    return 'inProgress';
  }
  if (attempt?.status === 'Expired') {
    return 'expired';
  }
  if (assignment?.dueAt && new Date(assignment.dueAt) < new Date()) {
    return 'expired';
  }
  if (attempts.length >= (Number(assignment?.attemptLimit) || 1)) {
    return 'noAttemptsLeft';
  }
  return 'notStarted';
}

function AssignmentQuestions({ items, showSensitive = false }) {
  const { t } = useLanguage();
  if (!items.length) {
    return <p className="classroom-muted">{getText(t, 'classrooms.questionSets.itemsEmpty', 'Chưa có câu hỏi nào.')}</p>;
  }

  return (
    <div className="classroom-question-list">
      {items.map((item, index) => (
        <div className="classroom-question-row" key={item.id || item.questionId}>
          <div>
            <strong>{index + 1}. {item.question?.questionText || `${getText(t, 'classrooms.assignments.questionKicker', 'Câu hỏi')} #${item.questionId}`}</strong>
            <small>
              {getText(t, 'classrooms.assignments.stats.questionId', 'Mã câu hỏi')}: {item.questionId} | {item.question?.questionType || '-'} | {getText(t, 'classrooms.assignments.pointLabel', 'Điểm:')} {item.pointWeight ?? '-'}
            </small>
            <OptionPreview options={item.question?.options} />
            {showSensitive && item.question?.correctAnswer && (
              <p className="classroom-answer-key">{getText(t, 'classrooms.assignments.correctAnswerLabel', 'Đáp án đúng:')} {item.question.correctAnswer}</p>
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

function QuestionAttemptCard({ answer, item, onAnswer, onSubmit, saving, t, index, disabled }) {
  const options = parseQuestionOptions(item.question?.options);
  return (
    <article className="classroom-panel classroom-attempt-question">
      <div className="classroom-section-head">
        <div>
          <span className="classroom-kicker">{getText(t, 'classrooms.assignments.questionKicker', 'Câu hỏi')} {index + 1}</span>
          <h2>{item.question?.questionText || `${getText(t, 'classrooms.assignments.questionKicker', 'Câu hỏi')} #${item.questionId}`}</h2>
          <p className="classroom-muted">{getText(t, 'classrooms.assignments.pointLabel', 'Điểm:')} {item.pointWeight ?? '-'}</p>
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
                  disabled={saving || disabled}
                />
                <span><strong>{value}.</strong> {getOptionText(option)}</span>
              </label>
            );
          })}
        </div>
      ) : (
        <label className="classroom-form">
          <span>{getText(t, 'classrooms.assignments.selectedAnswer', 'Câu trả lời')}</span>
          <input value={answer} onChange={(event) => onAnswer(event.target.value)} placeholder="A" disabled={saving || disabled} />
        </label>
      )}

      <button className="classroom-button" type="button" onClick={onSubmit} disabled={saving || !answer || disabled}>
        <LuSave aria-hidden="true" />
        {saving ? getText(t, 'classrooms.assignments.savingAnswer', 'Đang lưu...') : getText(t, 'classrooms.assignments.submitAnswer', 'Lưu câu trả lời')}
      </button>
    </article>
  );
}

function AttemptAnswers({ answers, reveal }) {
  const { t } = useLanguage();
  if (!answers.length) {
    return <p className="classroom-muted">{getText(t, 'classrooms.assignments.noAnswersYet', 'Chưa có câu trả lời.')}</p>;
  }

  return (
    <div className="classroom-answer-review">
      {answers.map((answer) => (
        <div className="classroom-answer-review-row" key={answer.id || answer.questionId}>
          <strong>{answer.question?.questionText || `${getText(t, 'classrooms.assignments.questionKicker', 'Câu hỏi')} #${answer.questionId}`}</strong>
          <small>{getText(t, 'classrooms.assignments.selectedLabel', 'Đã chọn:')} {answer.selectedAnswer || '-'}</small>
          {reveal && Object.prototype.hasOwnProperty.call(answer, 'isCorrect') && (
            <small>{answer.isCorrect ? 'Đúng' : 'Sai'} | {getText(t, 'classrooms.assignments.pointLabel', 'Điểm:')} {answer.pointEarned ?? 0}</small>
          )}
          {reveal && answer.question?.correctAnswer && (
            <small>{getText(t, 'classrooms.assignments.correctAnswerLabel', 'Đáp án đúng:')} {answer.question.correctAnswer}</small>
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
    return <p className="classroom-muted">{getText(t, 'classrooms.codes.empty', 'Chưa có mã tham gia nào.')}</p>;
  }

  return (
    <div className="classroom-code-list">
      {codes.map((code) => (
        <div className={`classroom-code-row${code.isActive ? '' : ' disabled'}`} key={code.id}>
          <div>
            <strong>{code.code}</strong>
            <small>
              {code.isActive ? getText(t, 'classrooms.codes.active', 'Đang bật') : getText(t, 'classrooms.codes.disabled', 'Đã tắt')}
              {' · '}
              {code.usedCount || 0}/{code.maxUses || getText(t, 'classrooms.codes.unlimited', 'không giới hạn')}
            </small>
          </div>
          <div className="classroom-row-actions">
            <button className="classroom-icon-button" type="button" onClick={() => onCopy(code.code)} title={getText(t, 'classrooms.actions.copy', 'Sao chép')}>
              <LuCopy aria-hidden="true" />
            </button>
            {code.isActive && (
              <button className="classroom-icon-button danger" type="button" onClick={() => onDisable(code.id)} disabled={disablingCodeId === code.id} title={getText(t, 'classrooms.actions.disable', 'Tắt code')}>
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
          <span className="classroom-kicker">{getText(t, 'classrooms.members.kicker', 'Thành viên')}</span>
          <h2>{getText(t, 'classrooms.members.title', 'Danh sách thành viên')}</h2>
        </div>
        <button className="classroom-button" type="button" onClick={onRetry}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.actions.refresh', 'Làm mới')}
        </button>
      </div>
      <MessageBar error={error} />
      {loading && <LoadingCard label={getText(t, 'classrooms.states.loadingMembers', 'Đang tải thành viên...')} />}
      {!loading && !members.length && !error && (
        <p className="classroom-muted">{getText(t, 'classrooms.members.empty', 'Chưa có học viên nào trong lớp.')}</p>
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

export function ClassroomLeaderboardPage() {
  const { classroomId } = useParams();
  const { t } = useLanguage();
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [isForbidden, setIsForbidden] = useState(false);

  const loadLeaderboard = useCallback(async () => {
    setLoading(true);
    setError('');
    setIsForbidden(false);
    try {
      const response = await classroomService.getClassroomLeaderboard(classroomId);
      setData(response);
    } catch (err) {
      if (isApiForbidden(err)) {
        setIsForbidden(true);
        setError(getText(t, 'classrooms.leaderboard.forbidden', 'Bạn không có quyền xem bảng xếp hạng này.'));
      } else if (err?.response?.status === 404) {
        setError(getText(t, 'classrooms.leaderboard.notFound', 'Không tìm thấy bảng xếp hạng.'));
      } else {
        setError(getApiErrorMessage(err, getText(t, 'classrooms.leaderboard.errors.load', 'Không tải được bảng xếp hạng.')));
      }
    } finally {
      setLoading(false);
    }
  }, [classroomId, t]);

  useEffect(() => {
    loadLeaderboard();
  }, [loadLeaderboard]);

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.leaderboard.classroomTitle', 'Bảng xếp hạng lớp học')} subtitle="">
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Đang tải...')} />
      </ClassroomShell>
    );
  }

  if (error || isForbidden) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.leaderboard.classroomTitle', 'Bảng xếp hạng lớp học')} subtitle="">
        <MessageBar error={error} />
        {!isForbidden && (
          <button className="classroom-button" type="button" onClick={loadLeaderboard}>
            <LuRefreshCw aria-hidden="true" />
            {getText(t, 'classrooms.actions.retry', 'Thử lại')}
          </button>
        )}
      </ClassroomShell>
    );
  }

  const rows = Array.isArray(data?.rows) ? data.rows : [];

  return (
    <ClassroomShell title={data?.classroomName || getText(t, 'classrooms.leaderboard.classroomTitle', 'Bảng xếp hạng lớp học')} subtitle={getText(t, 'classrooms.leaderboard.title', 'Bảng xếp hạng')}>
      <div className="classroom-leaderboard-header">
        <div className="classroom-leaderboard-metadata">
          <span>
            {getText(t, 'classrooms.leaderboard.assignmentsCount', 'Số bài kiểm tra')}: <strong>{data?.assignmentCount ?? 0}</strong>
          </span>
          <span>
            {getText(t, 'classrooms.leaderboard.activeStudents', 'Học sinh hoạt động')}: <strong>{data?.activeStudentCount ?? 0}</strong>
          </span>
          {data?.generatedAt && (
            <span>
              {getText(t, 'classrooms.leaderboard.generatedAt', 'Cập nhật lúc')}: <strong>{formatDateTime(data.generatedAt)}</strong>
            </span>
          )}
        </div>
      </div>

      <div className="classroom-page-actions" style={{ marginBottom: '16px' }}>
        <Link className="classroom-button" to={`/classrooms/${classroomId}`}>
          <LuSchool aria-hidden="true" />
          {getText(t, 'classrooms.questionSets.backToClassroom', 'Về lớp học')}
        </Link>
        <button className="classroom-button" type="button" onClick={loadLeaderboard}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.actions.refresh', 'Làm mới')}
        </button>
      </div>

      {rows.length === 0 ? (
        <section className="classroom-panel classroom-empty">
          <LuClipboard aria-hidden="true" />
          <h2>{getText(t, 'classrooms.leaderboard.empty', 'Chưa có dữ liệu xếp hạng.')}</h2>
        </section>
      ) : (
        <div className="classroom-table-wrapper">
          <table className="classroom-stats-table">
            <thead>
              <tr>
                <th>{getText(t, 'classrooms.leaderboard.rank', 'Hạng')}</th>
                <th>{getText(t, 'classrooms.leaderboard.student', 'Học sinh')}</th>
                <th>{getText(t, 'classrooms.leaderboard.email', 'Email')}</th>
                <th style={{ textAlign: 'center' }}>{getText(t, 'classrooms.leaderboard.completedAssignments', 'Số bài đã hoàn thành')}</th>
                <th style={{ textAlign: 'center' }}>{getText(t, 'classrooms.leaderboard.submittedAttempts', 'Số lượt nộp')}</th>
                <th style={{ textAlign: 'right' }}>{getText(t, 'classrooms.leaderboard.averageScore', 'Điểm trung bình')}</th>
                <th style={{ textAlign: 'right' }}>{getText(t, 'classrooms.leaderboard.totalScore', 'Tổng điểm quy đổi')}</th>
                <th style={{ textAlign: 'right' }}>{getText(t, 'classrooms.leaderboard.bestScore', 'Điểm cao nhất')}</th>
                <th>{getText(t, 'classrooms.leaderboard.latestSubmitted', 'Nộp bài gần nhất')}</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row, idx) => {
                const rankVal = row.rank ?? (idx + 1);
                let rankBadgeClass = 'rank-other';
                if (rankVal === 1) rankBadgeClass = 'rank-1';
                else if (rankVal === 2) rankBadgeClass = 'rank-2';
                else if (rankVal === 3) rankBadgeClass = 'rank-3';

                return (
                  <tr key={row.userId}>
                    <td>
                      <span className={`rank-badge ${rankBadgeClass}`}>{rankVal}</span>
                    </td>
                    <td>
                      <strong>{row.displayName || '-'}</strong>
                    </td>
                    <td>{row.email || '-'}</td>
                    <td style={{ textAlign: 'center' }}>{row.completedAssignments}</td>
                    <td style={{ textAlign: 'center' }}>{row.submittedAttempts}</td>
                    <td style={{ textAlign: 'right' }}>
                      {row.averagePercentScore != null ? `${Number(row.averagePercentScore).toFixed(2)}%` : '0.00%'}
                    </td>
                    <td style={{ textAlign: 'right' }}>
                      {row.totalPercentScore != null ? Number(row.totalPercentScore).toFixed(2) : '0.00'}
                    </td>
                    <td style={{ textAlign: 'right' }}>
                      {row.bestPercentScore != null ? `${Number(row.bestPercentScore).toFixed(2)}%` : '0.00%'}
                    </td>
                    <td>{row.latestSubmittedAt ? formatDateTime(row.latestSubmittedAt) : '-'}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </ClassroomShell>
  );
}

export function ClassroomAssignmentLeaderboardPage() {
  const { classroomId, assignmentId } = useParams();
  const { t } = useLanguage();
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [isForbidden, setIsForbidden] = useState(false);

  const loadLeaderboard = useCallback(async () => {
    setLoading(true);
    setError('');
    setIsForbidden(false);
    try {
      const response = await classroomService.getClassroomAssignmentLeaderboard(assignmentId);
      setData(response);
    } catch (err) {
      if (isApiForbidden(err)) {
        setIsForbidden(true);
        setError(getText(t, 'classrooms.leaderboard.forbidden', 'Bạn không có quyền xem bảng xếp hạng này.'));
      } else if (err?.response?.status === 404) {
        setError(getText(t, 'classrooms.leaderboard.notFound', 'Không tìm thấy bảng xếp hạng.'));
      } else {
        setError(getApiErrorMessage(err, getText(t, 'classrooms.leaderboard.errors.load', 'Không tải được bảng xếp hạng.')));
      }
    } finally {
      setLoading(false);
    }
  }, [assignmentId, t]);

  useEffect(() => {
    loadLeaderboard();
  }, [loadLeaderboard]);

  const formatDuration = (seconds) => {
    if (seconds == null) return '-';
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
  };

  if (loading) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.leaderboard.assignmentTitle', 'Bảng xếp hạng bài kiểm tra')} subtitle="">
        <LoadingCard label={getText(t, 'classrooms.states.loading', 'Đang tải...')} />
      </ClassroomShell>
    );
  }

  if (error || isForbidden) {
    return (
      <ClassroomShell title={getText(t, 'classrooms.leaderboard.assignmentTitle', 'Bảng xếp hạng bài kiểm tra')} subtitle="">
        <MessageBar error={error} />
        {!isForbidden && (
          <button className="classroom-button" type="button" onClick={loadLeaderboard}>
            <LuRefreshCw aria-hidden="true" />
            {getText(t, 'classrooms.actions.retry', 'Thử lại')}
          </button>
        )}
      </ClassroomShell>
    );
  }

  const rows = Array.isArray(data?.rows) ? data.rows : [];

  return (
    <ClassroomShell title={data?.assignmentTitle || getText(t, 'classrooms.leaderboard.assignmentTitle', 'Bảng xếp hạng bài kiểm tra')} subtitle={getText(t, 'classrooms.leaderboard.title', 'Bảng xếp hạng')}>
      
      <div className="classroom-leaderboard-header">
        <div className="classroom-leaderboard-metadata">
          <span>
            {getText(t, 'classrooms.leaderboard.scoringMode', 'Chế độ tính điểm')}:{' '}
            <strong>
              {data?.scoringMode === 'EmpiricalDifficulty'
                ? getText(t, 'classrooms.assignments.empiricalScoring', 'Chấm theo độ khó thực nghiệm')
                : getText(t, 'classrooms.assignments.percentScoring', 'Chấm theo phần trăm')}
            </strong>
          </span>
          {data?.scoreFinality && (
            <span>
              {getText(t, 'classrooms.leaderboard.finality', 'Trạng thái điểm')}:{' '}
              <span className={`scoring-badge-pill ${data.scoreFinality === 'Final' ? 'final' : 'temporary'}`}>
                {data.scoreFinality === 'Final'
                  ? getText(t, 'classrooms.leaderboard.finalScore', 'Điểm chính thức')
                  : getText(t, 'classrooms.leaderboard.temporaryScore', 'Điểm tạm thời')}
              </span>
            </span>
          )}
          {data?.generatedAt && (
            <span>
              {getText(t, 'classrooms.leaderboard.generatedAt', 'Cập nhật lúc')}: <strong>{formatDateTime(data.generatedAt)}</strong>
            </span>
          )}
        </div>
      </div>

      <div className="classroom-leaderboard-cards">
        <div className="classroom-leaderboard-card">
          <h3>{getText(t, 'classrooms.leaderboard.totalStudents', 'Tổng số học sinh')}</h3>
          <div className="card-value">{data?.totalStudents ?? 0}</div>
        </div>
        <div className="classroom-leaderboard-card">
          <h3>{getText(t, 'classrooms.leaderboard.submittedStudents', 'Học sinh đã nộp')}</h3>
          <div className="card-value">{data?.submittedStudents ?? 0}</div>
        </div>
        <div className="classroom-leaderboard-card">
          <h3>{getText(t, 'classrooms.leaderboard.inProgressStudents', 'Học sinh đang làm')}</h3>
          <div className="card-value">{data?.inProgressStudents ?? 0}</div>
        </div>
        <div className="classroom-leaderboard-card">
          <h3>{getText(t, 'classrooms.leaderboard.notStartedStudents', 'Học sinh chưa bắt đầu')}</h3>
          <div className="card-value">{data?.notStartedStudents ?? 0}</div>
        </div>
      </div>

      {data?.scoringMode === 'EmpiricalDifficulty' && data?.scoreFinality === 'Temporary' && (
        <div className="classroom-info-banner warning" style={{ marginBottom: '16px' }}>
          <p>{getText(t, 'classrooms.leaderboard.empiricalNotice', 'Điểm có thể thay đổi sau khi giảng viên đóng assignment.')}</p>
        </div>
      )}

      <div className="classroom-page-actions" style={{ marginBottom: '16px' }}>
        <Link className="classroom-button" to={`/classrooms/${classroomId}/student/assignments`}>
          <LuClipboard aria-hidden="true" />
          {getText(t, 'classrooms.assignments.backToList', 'Về danh sách')}
        </Link>
        <button className="classroom-button" type="button" onClick={loadLeaderboard}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.actions.refresh', 'Làm mới')}
        </button>
      </div>

      {rows.length === 0 ? (
        <section className="classroom-panel classroom-empty">
          <LuClipboard aria-hidden="true" />
          <h2>{getText(t, 'classrooms.leaderboard.empty', 'Chưa có dữ liệu xếp hạng.')}</h2>
        </section>
      ) : (
        <div className="classroom-table-wrapper">
          <table className="classroom-stats-table">
            <thead>
              <tr>
                <th>{getText(t, 'classrooms.leaderboard.rank', 'Hạng')}</th>
                <th>{getText(t, 'classrooms.leaderboard.student', 'Học sinh')}</th>
                <th>{getText(t, 'classrooms.leaderboard.email', 'Email')}</th>
                <th style={{ textAlign: 'center' }}>{getText(t, 'classrooms.leaderboard.attempts', 'Lượt làm')}</th>
                <th style={{ textAlign: 'right' }}>{getText(t, 'classrooms.leaderboard.rawScore', 'Điểm số')}</th>
                <th style={{ textAlign: 'right' }}>{getText(t, 'classrooms.leaderboard.averageScore', 'Phần trăm')}</th>
                <th style={{ textAlign: 'center' }}>{getText(t, 'classrooms.leaderboard.duration', 'Thời gian')}</th>
                <th>{getText(t, 'classrooms.leaderboard.submittedAt', 'Thời gian nộp')}</th>
                <th style={{ textAlign: 'center' }}>{getText(t, 'classrooms.leaderboard.status', 'Trạng thái')}</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row, idx) => {
                const isSubmitted = row.statusLabel === 'Submitted';
                const rankVal = isSubmitted ? (row.rank ?? (idx + 1)) : '-';
                
                let rankBadgeClass = 'rank-other';
                if (rankVal === 1) rankBadgeClass = 'rank-1';
                else if (rankVal === 2) rankBadgeClass = 'rank-2';
                else if (rankVal === 3) rankBadgeClass = 'rank-3';

                let statusBadgeClass = 'badge-warning'; // InProgress
                let statusText = getText(t, 'classrooms.leaderboard.inProgress', 'Đang làm');
                if (row.statusLabel === 'Submitted') {
                  statusBadgeClass = 'badge-success';
                  statusText = getText(t, 'classrooms.leaderboard.submitted', 'Đã nộp');
                } else if (row.statusLabel === 'NotStarted') {
                  statusBadgeClass = 'badge-danger';
                  statusText = getText(t, 'classrooms.leaderboard.notStarted', 'Chưa bắt đầu');
                }

                return (
                  <tr key={row.userId}>
                    <td>
                      {isSubmitted ? (
                        <span className={`rank-badge ${rankBadgeClass}`}>{rankVal}</span>
                      ) : (
                        '-'
                      )}
                    </td>
                    <td>
                      <strong>{row.displayName || '-'}</strong>
                    </td>
                    <td>{row.email || '-'}</td>
                    <td style={{ textAlign: 'center' }}>
                      {row.attemptNumber != null ? `#${row.attemptNumber}` : '-'}
                      {row.attemptCount > 1 ? ` (${row.attemptCount} ${getText(t, 'classrooms.leaderboard.attempts', 'lượt')})` : ''}
                    </td>
                    <td style={{ textAlign: 'right' }}>
                      {row.rawScore != null ? Number(row.rawScore).toFixed(2) : '-'}
                    </td>
                    <td style={{ textAlign: 'right' }}>
                      {row.percentScore != null ? `${Number(row.percentScore).toFixed(2)}%` : '-'}
                    </td>
                    <td style={{ textAlign: 'center' }}>{formatDuration(row.durationSeconds)}</td>
                    <td>{row.submittedAt ? formatDateTime(row.submittedAt) : '-'}</td>
                    <td style={{ textAlign: 'center' }}>
                      <span className={`classroom-stat-badge ${statusBadgeClass}`}>{statusText}</span>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </ClassroomShell>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 5C: Classroom Analytics Pages
// ─────────────────────────────────────────────────────────────────────────────

// ─────────────────────────────── Teacher Analytics ───────────────────────────

export function ClassroomAnalyticsPage() {
  const { classroomId } = useParams();
  const { t } = useLanguage();
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const loadAnalytics = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const result = await classroomService.getClassroomAnalytics(classroomId);
      setData(result);
    } catch (err) {
      if (isApiForbidden(err)) {
        setError(getText(t, 'classrooms.analytics.forbidden', 'Bạn không có quyền xem analytics này.'));
      } else {
        setError(getApiErrorMessage(err, getText(t, 'classrooms.analytics.notFound', 'Không tải được analytics.')));
      }
    } finally {
      setLoading(false);
    }
  }, [classroomId, t]);

  useEffect(() => {
    loadAnalytics();
  }, [loadAnalytics]);

  if (loading) {
    return (
      <ClassroomShell
        title={getText(t, 'classrooms.analytics.teacherTitle', 'Analytics lớp học')}
        subtitle=""
      >
        <LoadingCard label={getText(t, 'classrooms.analytics.loading', 'Đang tải analytics...')} />
      </ClassroomShell>
    );
  }

  if (error) {
    return (
      <ClassroomShell
        title={getText(t, 'classrooms.analytics.teacherTitle', 'Analytics lớp học')}
        subtitle=""
      >
        <MessageBar error={error} />
        <button className="classroom-button" type="button" onClick={loadAnalytics}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.analytics.retry', 'Thử lại')}
        </button>
      </ClassroomShell>
    );
  }

  const overview = data?.overview || {};
  const assignments = Array.isArray(data?.assignmentSummaries) ? data.assignmentSummaries : [];
  const insights = data?.questionInsights || {};
  const atRisk = Array.isArray(data?.atRiskStudents) ? data.atRiskStudents : [];

  return (
    <ClassroomShell
      title={data?.classroomName || getText(t, 'classrooms.analytics.teacherTitle', 'Analytics lớp học')}
      subtitle={getText(t, 'classrooms.analytics.teacherSubtitle', 'Tổng quan bài tập, học sinh và câu hỏi khó.')}
    >

      <div className="classroom-page-actions" style={{ marginBottom: '16px' }}>
        <Link className="classroom-button" to={`/classrooms/${classroomId}`}>
          <LuSchool aria-hidden="true" />
          {getText(t, 'classrooms.analytics.backToClassroom', 'Về lớp học')}
        </Link>
        <button className="classroom-button" type="button" onClick={loadAnalytics}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.analytics.refresh', 'Làm mới')}
        </button>
      </div>

      {data?.generatedAt && (
        <p className="classroom-muted" style={{ marginBottom: '16px' }}>
          {getText(t, 'classrooms.analytics.generatedAt', 'Cập nhật lúc')}: {formatDateTime(data.generatedAt)}
        </p>
      )}

      {/* Overview cards */}
      <section style={{ marginBottom: '24px' }}>
        <h2 style={{ marginBottom: '12px' }}>{getText(t, 'classrooms.analytics.overview.title', 'Tổng quan')}</h2>
        <div className="classroom-leaderboard-stats">
          <div className="classroom-leaderboard-card">
            <h3>{getText(t, 'classrooms.analytics.overview.activeStudents', 'Học sinh đang học')}</h3>
            <div className="card-value">{overview.activeStudentCount ?? 0}</div>
          </div>
          <div className="classroom-leaderboard-card">
            <h3>{getText(t, 'classrooms.analytics.overview.totalAssignments', 'Tổng bài tập')}</h3>
            <div className="card-value">{overview.assignmentCount ?? 0}</div>
          </div>
          <div className="classroom-leaderboard-card">
            <h3>{getText(t, 'classrooms.analytics.overview.published', 'Đã công bố')}</h3>
            <div className="card-value">{overview.publishedAssignmentCount ?? 0}</div>
          </div>
          <div className="classroom-leaderboard-card">
            <h3>{getText(t, 'classrooms.analytics.overview.closed', 'Đã đóng')}</h3>
            <div className="card-value">{overview.closedAssignmentCount ?? 0}</div>
          </div>
          <div className="classroom-leaderboard-card">
            <h3>{getText(t, 'classrooms.analytics.overview.submittedAttempts', 'Lượt nộp bài')}</h3>
            <div className="card-value">{overview.submittedAttemptCount ?? 0}</div>
          </div>
          <div className="classroom-leaderboard-card">
            <h3>{getText(t, 'classrooms.analytics.overview.averageScore', 'Điểm trung bình')}</h3>
            <div className="card-value">
              {overview.averageScore != null ? `${Number(overview.averageScore).toFixed(2)}%` : '-'}
            </div>
          </div>
          <div className="classroom-leaderboard-card">
            <h3>{getText(t, 'classrooms.analytics.overview.completionRate', 'Tỷ lệ hoàn thành')}</h3>
            <div className="card-value">
              {overview.completionRate != null ? `${Number(overview.completionRate).toFixed(2)}%` : '-'}
            </div>
          </div>
        </div>
      </section>

      {/* Assignment summary table */}
      <section style={{ marginBottom: '24px' }}>
        <h2 style={{ marginBottom: '12px' }}>{getText(t, 'classrooms.analytics.assignments.title', 'Tóm tắt bài tập')}</h2>
        {assignments.length === 0 ? (
          <section className="classroom-panel classroom-empty">
            <LuClipboard aria-hidden="true" />
            <h2>{getText(t, 'classrooms.analytics.assignments.emptyTitle', 'Chưa có bài tập nào được công bố.')}</h2>
          </section>
        ) : (
          <div className="classroom-table-wrapper">
            <table className="classroom-stats-table">
              <thead>
                <tr>
                  <th>{getText(t, 'classrooms.analytics.assignments.colTitle', 'Tiêu đề')}</th>
                  <th style={{ textAlign: 'center' }}>{getText(t, 'classrooms.analytics.assignments.colStatus', 'Trạng thái')}</th>
                  <th style={{ textAlign: 'center' }}>{getText(t, 'classrooms.analytics.assignments.colTotal', 'HS')}</th>
                  <th style={{ textAlign: 'center' }}>{getText(t, 'classrooms.analytics.assignments.colSubmitted', 'Đã nộp')}</th>
                  <th style={{ textAlign: 'center' }}>{getText(t, 'classrooms.analytics.assignments.colInProgress', 'Đang làm')}</th>
                  <th style={{ textAlign: 'center' }}>{getText(t, 'classrooms.analytics.assignments.colNotStarted', 'Chưa bắt đầu')}</th>
                  <th style={{ textAlign: 'right' }}>{getText(t, 'classrooms.analytics.assignments.colCompletion', 'Hoàn thành')}</th>
                  <th style={{ textAlign: 'right' }}>{getText(t, 'classrooms.analytics.assignments.colAvg', 'TB %')}</th>
                  <th style={{ textAlign: 'right' }}>{getText(t, 'classrooms.analytics.assignments.colBest', 'Cao nhất')}</th>
                  <th style={{ textAlign: 'right' }}>{getText(t, 'classrooms.analytics.assignments.colLowest', 'Thấp nhất')}</th>
                </tr>
              </thead>
              <tbody>
                {assignments.map((a) => (
                  <tr key={a.assignmentId}>
                    <td>
                      <Link to={`/classrooms/${classroomId}/assignments/${a.assignmentId}`}>
                        <strong>{a.title}</strong>
                      </Link>
                    </td>
                    <td style={{ textAlign: 'center' }}>
                      <span className={`classroom-stat-badge badge-${a.status === 'Closed' ? 'success' : a.status === 'Published' ? 'warning' : 'danger'}`}>
                        {a.status}
                      </span>
                    </td>
                    <td style={{ textAlign: 'center' }}>{a.totalStudents}</td>
                    <td style={{ textAlign: 'center' }}>{a.submittedStudents}</td>
                    <td style={{ textAlign: 'center' }}>{a.inProgressStudents}</td>
                    <td style={{ textAlign: 'center' }}>{a.notStartedStudents}</td>
                    <td style={{ textAlign: 'right' }}>{Number(a.completionRate).toFixed(1)}%</td>
                    <td style={{ textAlign: 'right' }}>{Number(a.averagePercentScore).toFixed(2)}%</td>
                    <td style={{ textAlign: 'right' }}>{Number(a.bestPercentScore).toFixed(2)}%</td>
                    <td style={{ textAlign: 'right' }}>{Number(a.lowestPercentScore).toFixed(2)}%</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {/* Question difficulty */}
      <section style={{ marginBottom: '24px' }}>
        <h2 style={{ marginBottom: '12px' }}>{getText(t, 'classrooms.analytics.questions.title', 'Câu hỏi khó và nghi vấn')}</h2>
        <h3 style={{ marginBottom: '8px', fontSize: '1rem' }}>{getText(t, 'classrooms.analytics.questions.hardestTitle', 'Khó nhất (top 5)')}</h3>
        {(insights.hardestQuestions || []).length === 0 ? (
          <p className="classroom-muted">{getText(t, 'classrooms.analytics.questions.emptyHardest', 'Chưa có đủ dữ liệu.')}</p>
        ) : (
          <div className="classroom-table-wrapper" style={{ marginBottom: '16px' }}>
            <table className="classroom-stats-table">
              <thead>
                <tr>
                  <th>{getText(t, 'classrooms.analytics.questions.colAssignment', 'Bài tập')}</th>
                  <th>{getText(t, 'classrooms.analytics.questions.colQuestion', 'Nội dung câu hỏi')}</th>
                  <th style={{ textAlign: 'center' }}>{getText(t, 'classrooms.analytics.questions.colAnswered', 'Đã trả lời')}</th>
                  <th style={{ textAlign: 'center' }}>{getText(t, 'classrooms.analytics.questions.colCorrect', 'Đúng')}</th>
                  <th style={{ textAlign: 'right' }}>{getText(t, 'classrooms.analytics.questions.colWeight', 'Trọng số')}</th>
                  <th>{getText(t, 'classrooms.analytics.questions.colFlag', 'Cờ chất lượng')}</th>
                </tr>
              </thead>
              <tbody>
                {(insights.hardestQuestions || []).map((q) => (
                  <tr key={`${q.assignmentId}-${q.questionId}`}>
                    <td><small>{q.assignmentTitle}</small></td>
                    <td>{q.questionText ? q.questionText.substring(0, 80) + (q.questionText.length > 80 ? '…' : '') : `#${q.questionId}`}</td>
                    <td style={{ textAlign: 'center' }}>{q.answeredCount}</td>
                    <td style={{ textAlign: 'center' }}>{q.correctCount}</td>
                    <td style={{ textAlign: 'right' }}>{Number(q.difficultyWeight).toFixed(2)}</td>
                    <td>{q.qualityFlag ? <span className="classroom-stat-badge badge-warning">{q.qualityFlag}</span> : '-'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {(insights.suspiciousQuestions || []).length > 0 && (
          <>
            <h3 style={{ marginBottom: '8px', fontSize: '1rem', display: 'flex', alignItems: 'center', gap: '6px' }}>
              <LuTriangleAlert aria-hidden="true" style={{ color: 'var(--classroom-warning, #f59e0b)' }} />
              {getText(t, 'classrooms.analytics.questions.suspiciousTitle', 'Câu hỏi nghi vấn')}
            </h3>
            <div className="classroom-table-wrapper">
              <table className="classroom-stats-table">
                <thead>
                  <tr>
                    <th>{getText(t, 'classrooms.analytics.questions.colAssignment', 'Bài tập')}</th>
                    <th>{getText(t, 'classrooms.analytics.questions.colQuestion', 'Nội dung câu hỏi')}</th>
                    <th style={{ textAlign: 'right' }}>{getText(t, 'classrooms.analytics.questions.colWeight', 'Trọng số')}</th>
                    <th>{getText(t, 'classrooms.analytics.questions.colFlag', 'Cờ chất lượng')}</th>
                  </tr>
                </thead>
                <tbody>
                  {(insights.suspiciousQuestions || []).map((q) => (
                    <tr key={`sus-${q.assignmentId}-${q.questionId}`}>
                      <td><small>{q.assignmentTitle}</small></td>
                      <td>{q.questionText ? q.questionText.substring(0, 80) + (q.questionText.length > 80 ? '…' : '') : `#${q.questionId}`}</td>
                      <td style={{ textAlign: 'right' }}>{Number(q.difficultyWeight).toFixed(2)}</td>
                      <td><span className="classroom-stat-badge badge-danger">{q.qualityFlag}</span></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </section>

      {/* At-risk students */}
      <section style={{ marginBottom: '24px' }}>
        <h2 style={{ marginBottom: '12px' }}>{getText(t, 'classrooms.analytics.atRisk.title', 'Học sinh cần hỗ trợ')}</h2>
        {atRisk.length === 0 ? (
          <section className="classroom-panel classroom-empty">
            <LuGraduationCap aria-hidden="true" />
            <h2>{getText(t, 'classrooms.analytics.atRisk.empty', 'Không có học sinh nào cần chú ý đặc biệt.')}</h2>
          </section>
        ) : (
          <div className="classroom-table-wrapper">
            <table className="classroom-stats-table">
              <thead>
                <tr>
                  <th>{getText(t, 'classrooms.analytics.atRisk.colStudent', 'Học sinh')}</th>
                  <th>{getText(t, 'classrooms.analytics.atRisk.colEmail', 'Email')}</th>
                  <th style={{ textAlign: 'center' }}>{getText(t, 'classrooms.analytics.atRisk.colCompleted', 'Bài hoàn thành')}</th>
                  <th style={{ textAlign: 'right' }}>{getText(t, 'classrooms.analytics.atRisk.colAvgScore', 'Điểm TB')}</th>
                  <th>{getText(t, 'classrooms.analytics.atRisk.colLastSubmit', 'Nộp gần nhất')}</th>
                </tr>
              </thead>
              <tbody>
                {atRisk.map((s) => (
                  <tr key={s.userId}>
                    <td><strong>{s.displayName}</strong></td>
                    <td>{s.email}</td>
                    <td style={{ textAlign: 'center' }}>{s.completedAssignments}</td>
                    <td style={{ textAlign: 'right' }}>{Number(s.averagePercentScore).toFixed(2)}%</td>
                    <td>{s.lastSubmittedAt ? formatDateTime(s.lastSubmittedAt) : '-'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </ClassroomShell>
  );
}

// ─────────────────────────────── Student Analytics ───────────────────────────

export function StudentClassroomAnalyticsPage() {
  const { classroomId } = useParams();
  const { t } = useLanguage();
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const loadAnalytics = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const result = await classroomService.getStudentClassroomAnalytics(classroomId);
      setData(result);
    } catch (err) {
      if (isApiForbidden(err)) {
        setError(getText(t, 'classrooms.analytics.forbidden', 'Bạn không có quyền xem analytics này.'));
      } else {
        setError(getApiErrorMessage(err, getText(t, 'classrooms.analytics.notFound', 'Không tải được analytics.')));
      }
    } finally {
      setLoading(false);
    }
  }, [classroomId, t]);

  useEffect(() => {
    loadAnalytics();
  }, [loadAnalytics]);

  if (loading) {
    return (
      <ClassroomShell
        title={getText(t, 'classrooms.analytics.studentTitle', 'Tiến độ của tôi')}
        subtitle=""
      >
        <LoadingCard label={getText(t, 'classrooms.analytics.loading', 'Đang tải analytics...')} />
      </ClassroomShell>
    );
  }

  if (error) {
    return (
      <ClassroomShell
        title={getText(t, 'classrooms.analytics.studentTitle', 'Tiến độ của tôi')}
        subtitle=""
      >
        <MessageBar error={error} />
        <button className="classroom-button" type="button" onClick={loadAnalytics}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.analytics.retry', 'Thử lại')}
        </button>
      </ClassroomShell>
    );
  }

  const summary = data?.summary || {};
  const attempts = Array.isArray(data?.recentAttempts) ? data.recentAttempts : [];

  return (
    <ClassroomShell
      title={data?.classroomName
        ? `${getText(t, 'classrooms.analytics.studentTitle', 'Tiến độ của tôi')} – ${data.classroomName}`
        : getText(t, 'classrooms.analytics.studentTitle', 'Tiến độ của tôi')}
      subtitle={getText(t, 'classrooms.analytics.studentSubtitle', 'Xem tóm tắt cá nhân và lịch sử làm bài.')}
    >

      <div className="classroom-page-actions" style={{ marginBottom: '16px' }}>
        <Link className="classroom-button" to={`/classrooms/${classroomId}`}>
          <LuSchool aria-hidden="true" />
          {getText(t, 'classrooms.analytics.student.backToClassroom', 'Về lớp học')}
        </Link>
        <button className="classroom-button" type="button" onClick={loadAnalytics}>
          <LuRefreshCw aria-hidden="true" />
          {getText(t, 'classrooms.analytics.refresh', 'Làm mới')}
        </button>
      </div>

      {/* Hint banners */}
      {data?.needsPractice && (
        <div className="classroom-info-banner warning" style={{ marginBottom: '12px' }}>
          <p>{getText(t, 'classrooms.analytics.student.needsPractice', 'Điểm trung bình của bạn dưới 50%. Hãy ôn tập thêm!')}</p>
        </div>
      )}
      {data?.hasPendingAssignments && (
        <div className="classroom-info-banner" style={{ marginBottom: '12px' }}>
          <p>{getText(t, 'classrooms.analytics.student.hasPending', 'Bạn còn bài tập chưa hoàn thành trong lớp này.')}</p>
        </div>
      )}

      {/* Summary cards */}
      <section style={{ marginBottom: '24px' }}>
        <h2 style={{ marginBottom: '12px' }}>{getText(t, 'classrooms.analytics.student.summaryTitle', 'Tổng quan cá nhân')}</h2>
        <div className="classroom-leaderboard-stats">
          <div className="classroom-leaderboard-card">
            <h3>{getText(t, 'classrooms.analytics.student.completedAssignments', 'Bài đã hoàn thành')}</h3>
            <div className="card-value">{summary.completedAssignments ?? 0} / {summary.totalAssignments ?? 0}</div>
          </div>
          <div className="classroom-leaderboard-card">
            <h3>{getText(t, 'classrooms.analytics.student.averageScore', 'Điểm trung bình')}</h3>
            <div className="card-value">
              {summary.averagePercentScore != null ? `${Number(summary.averagePercentScore).toFixed(2)}%` : '-'}
            </div>
          </div>
          <div className="classroom-leaderboard-card">
            <h3>{getText(t, 'classrooms.analytics.student.bestScore', 'Điểm cao nhất')}</h3>
            <div className="card-value">
              {summary.bestPercentScore != null ? `${Number(summary.bestPercentScore).toFixed(2)}%` : '-'}
            </div>
          </div>
          <div className="classroom-leaderboard-card">
            <h3>{getText(t, 'classrooms.analytics.student.latestSubmit', 'Nộp gần nhất')}</h3>
            <div className="card-value" style={{ fontSize: '0.85rem' }}>
              {summary.latestSubmittedAt ? formatDateTime(summary.latestSubmittedAt) : '-'}
            </div>
          </div>
        </div>
      </section>

      {/* Recent attempts table */}
      <section>
        <h2 style={{ marginBottom: '12px' }}>{getText(t, 'classrooms.analytics.student.attemptsTitle', 'Lịch sử làm bài gần đây')}</h2>
        {attempts.length === 0 ? (
          <section className="classroom-panel classroom-empty">
            <LuClipboard aria-hidden="true" />
            <h2>{getText(t, 'classrooms.analytics.student.attemptsEmpty', 'Bạn chưa nộp bài nào.')}</h2>
          </section>
        ) : (
          <div className="classroom-table-wrapper">
            <table className="classroom-stats-table">
              <thead>
                <tr>
                  <th>{getText(t, 'classrooms.analytics.student.colAssignment', 'Bài tập')}</th>
                  <th style={{ textAlign: 'center' }}>{getText(t, 'classrooms.analytics.student.colAttempt', 'Lần thử')}</th>
                  <th style={{ textAlign: 'center' }}>{getText(t, 'classrooms.analytics.student.colStatus', 'Trạng thái')}</th>
                  <th style={{ textAlign: 'right' }}>{getText(t, 'classrooms.analytics.student.colScore', 'Điểm')}</th>
                  <th style={{ textAlign: 'right' }}>{getText(t, 'classrooms.analytics.student.colPercent', 'Phần trăm')}</th>
                  <th style={{ textAlign: 'center' }}>{getText(t, 'classrooms.analytics.student.colFinality', 'Tính chính thức')}</th>
                  <th>{getText(t, 'classrooms.analytics.student.colSubmitted', 'Thời gian nộp')}</th>
                </tr>
              </thead>
              <tbody>
                {attempts.map((a) => {
                  const isSubmitted = a.status === 'Submitted';
                  return (
                    <tr key={a.attemptId}>
                      <td>
                        <Link to={`/classrooms/${classroomId}/student/assignments/${a.assignmentId}`}>
                          {a.assignmentTitle || `Assignment #${a.assignmentId}`}
                        </Link>
                      </td>
                      <td style={{ textAlign: 'center' }}>#{a.attemptNumber}</td>
                      <td style={{ textAlign: 'center' }}>
                        <span className={`classroom-stat-badge ${isSubmitted ? 'badge-success' : 'badge-warning'}`}>
                          {a.status}
                        </span>
                      </td>
                      <td style={{ textAlign: 'right' }}>
                        {isSubmitted ? Number(a.rawScore).toFixed(2) : '-'}
                      </td>
                      <td style={{ textAlign: 'right' }}>
                        {isSubmitted ? `${Number(a.percentScore).toFixed(2)}%` : '-'}
                      </td>
                      <td style={{ textAlign: 'center' }}>
                        {isSubmitted ? (
                          <span className={`classroom-stat-badge ${a.scoreFinality === 'Final' ? 'badge-success' : 'badge-warning'}`}>
                            {a.scoreFinality === 'Final'
                              ? getText(t, 'classrooms.analytics.student.finalityFinal', 'Chính thức')
                              : getText(t, 'classrooms.analytics.student.finalityTemporary', 'Tạm thời')}
                          </span>
                        ) : '-'}
                      </td>
                      <td>{a.submittedAt ? formatDateTime(a.submittedAt) : '-'}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </ClassroomShell>
  );
}
