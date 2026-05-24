const overrides = {
  vi: {
    app: {
      nav: {
        admin: 'Quản trị',
      },
      roles: {
        LEARNER: 'Người học',
        INSTRUCTOR: 'Giảng viên',
        ADMIN: 'Quản trị viên',
      },
      account: {
        demoRole: 'Tài khoản đang hoạt động',
        logoutHint: 'Kết thúc phiên hiện tại',
      },
      dashboard: {
        workspaceTitle: 'Workspace học tập chính',
        workspaceSubtitle: 'Theo dõi nguồn học, tạo câu hỏi và dựng slide trong một luồng làm việc rõ ràng.',
        emptySourceHint: 'Nguồn mới nhất sẽ xuất hiện tại đây sau khi tải lên.',
        guide: {
          title: 'Trợ lý học tập',
          subtitle: 'Gợi ý bước tiếp theo dựa trên trạng thái nguồn học và question bank hiện tại.',
        },
        titleByRole: {
          LEARNER: 'Học từ tài liệu của bạn bằng quiz, flashcards, streak và slide.',
          INSTRUCTOR: 'Tạo học liệu từ tài liệu của bạn rồi chuyển nhanh sang question bank và slide deck.',
          ADMIN: 'Quản trị và kiểm tra các luồng học liệu từ một tài khoản hệ thống.',
        },
        subtitleByRole: {
          LEARNER: 'Tải tài liệu, để AI phân tích, rồi học lại ngay trong cùng một workspace.',
          INSTRUCTOR: 'Tải tài liệu giảng dạy, gom nguồn trong workspace và tạo bộ học liệu ngắn gọn, có thể chỉnh sửa.',
          ADMIN: 'Bạn vẫn dùng được toàn bộ core flow, đồng thời có thể mở khu vực quản trị khi cần.',
        },
      },
    },
    auth: {
      common: {
        kicker: 'ELearn Account',
        fullName: 'Họ và tên',
        email: 'Email',
        password: 'Mật khẩu',
        confirmPassword: 'Xác nhận mật khẩu',
        role: 'Vai trò',
      },
      roles: {
        LEARNER: 'Người học',
        INSTRUCTOR: 'Giáo viên / Giảng viên',
      },
      login: {
        title: 'Đăng nhập',
        subtitle: 'Dùng tài khoản thật để lưu workspace, tài liệu, question bank và slide deck của bạn.',
        submit: 'Đăng nhập',
        submitting: 'Đang đăng nhập...',
        switchPrompt: 'Chưa có tài khoản?',
        switchAction: 'Đăng ký',
        errors: {
          failed: 'Không đăng nhập được. Vui lòng kiểm tra email và mật khẩu.',
        },
      },
      register: {
        title: 'Đăng ký',
        subtitle: 'Tạo tài khoản học tập hoặc giảng dạy. Vai trò quản trị không xuất hiện trong form đăng ký.',
        submit: 'Tạo tài khoản',
        submitting: 'Đang tạo tài khoản...',
        switchPrompt: 'Đã có tài khoản?',
        switchAction: 'Đăng nhập',
        errors: {
          failed: 'Không tạo được tài khoản.',
          passwordMismatch: 'Mật khẩu xác nhận chưa khớp.',
        },
      },
    },
    workspaces: {
      createNameLabel: 'Tên workspace',
      createDescriptionLabel: 'Mô tả workspace',
      filters: {
        label: 'Bộ lọc workspace',
        searchLabel: 'Tìm workspace',
        searchPlaceholder: 'Tìm theo tên, mô tả hoặc deck',
        statusLabel: 'Trạng thái deck',
        statusAll: 'Tất cả',
        statusReady: 'Deck sẵn sàng',
        statusStale: 'Cần tạo lại',
        statusGenerating: 'Đang tạo',
        statusFailed: 'Thất bại',
        statusNone: 'Chưa có deck',
        sortLabel: 'Sắp xếp',
        sortUpdated: 'Mới cập nhật',
        sortName: 'Tên A-Z',
        sortSources: 'Nhiều nguồn nhất',
        resultCount: '{{count}} workspace',
        emptyTitle: 'Không tìm thấy workspace phù hợp',
        emptyBody: 'Thử đổi từ khóa hoặc bộ lọc trạng thái deck.',
      },
    },
    admin: {
      title: 'Khu vực quản trị',
      subtitle: 'Bề mặt quản trị tối thiểu để xem người dùng và tài liệu gần đây.',
      users: 'Người dùng',
      documents: 'Tài liệu',
      cards: {
        users: 'Tổng người dùng',
        documents: 'Tài liệu gần đây',
      },
      columns: {
        name: 'Tên',
        email: 'Email',
        role: 'Vai trò',
        fileName: 'Tài liệu',
        status: 'Trạng thái',
        owner: 'Chủ sở hữu',
      },
      errors: {
        loadFailed: 'Không tải được dữ liệu quản trị.',
      },
    },
  },
  en: {
    app: {
      nav: {
        admin: 'Admin',
      },
      roles: {
        LEARNER: 'Learner',
        INSTRUCTOR: 'Instructor',
        ADMIN: 'Administrator',
      },
      account: {
        demoRole: 'Active account',
        logoutHint: 'End the current session',
      },
      dashboard: {
        workspaceTitle: 'Primary learning workspace',
        workspaceSubtitle: 'Track sources, generate questions, and build slides in one clear workflow.',
        emptySourceHint: 'The latest source will appear here after upload.',
        guide: {
          title: 'Learning assistant',
          subtitle: 'Suggests the next step from the current source and question-bank status.',
        },
        titleByRole: {
          LEARNER: 'Learn from your documents with quiz, flashcards, streak, and slides.',
          INSTRUCTOR: 'Turn your documents into teaching materials, then move quickly into question banks and slide decks.',
          ADMIN: 'Manage the system and still use the core study flows from one account.',
        },
        subtitleByRole: {
          LEARNER: 'Upload a document, let AI analyze it, then study inside the same workspace.',
          INSTRUCTOR: 'Upload teaching sources, group them in a workspace, and produce editable learning materials.',
          ADMIN: 'You can still use the core app and open the admin area whenever needed.',
        },
      },
    },
    auth: {
      common: {
        kicker: 'ELearn Account',
        fullName: 'Full name',
        email: 'Email',
        password: 'Password',
        confirmPassword: 'Confirm password',
        role: 'Role',
      },
      roles: {
        LEARNER: 'Learner',
        INSTRUCTOR: 'Teacher / Instructor',
      },
      login: {
        title: 'Sign in',
        subtitle: 'Use a real account to keep your workspaces, documents, question banks, and slide decks.',
        submit: 'Sign in',
        submitting: 'Signing in...',
        switchPrompt: 'Need an account?',
        switchAction: 'Register',
        errors: {
          failed: 'Unable to sign in. Check your email and password.',
        },
      },
      register: {
        title: 'Create account',
        subtitle: 'Create a learner or instructor account. Admin is intentionally excluded from self-registration.',
        submit: 'Create account',
        submitting: 'Creating account...',
        switchPrompt: 'Already have an account?',
        switchAction: 'Sign in',
        errors: {
          failed: 'Unable to create account.',
          passwordMismatch: 'Password confirmation does not match.',
        },
      },
    },
    workspaces: {
      createNameLabel: 'Workspace name',
      createDescriptionLabel: 'Workspace description',
      filters: {
        label: 'Workspace filters',
        searchLabel: 'Search workspaces',
        searchPlaceholder: 'Search by name, description, or deck',
        statusLabel: 'Deck status',
        statusAll: 'All',
        statusReady: 'Deck ready',
        statusStale: 'Needs regeneration',
        statusGenerating: 'Generating',
        statusFailed: 'Failed',
        statusNone: 'No deck',
        sortLabel: 'Sort',
        sortUpdated: 'Recently updated',
        sortName: 'Name A-Z',
        sortSources: 'Most sources',
        resultCount: '{{count}} workspaces',
        emptyTitle: 'No matching workspaces',
        emptyBody: 'Try a different keyword or deck status filter.',
      },
    },
    admin: {
      title: 'Admin area',
      subtitle: 'A minimal admin surface for viewing users and recent documents.',
      users: 'Users',
      documents: 'Documents',
      cards: {
        users: 'Total users',
        documents: 'Recent documents',
      },
      columns: {
        name: 'Name',
        email: 'Email',
        role: 'Role',
        fileName: 'Document',
        status: 'Status',
        owner: 'Owner',
      },
      errors: {
        loadFailed: 'Unable to load admin data.',
      },
    },
  },
};

export default overrides;
