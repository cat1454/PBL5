# AUTH UI/UX UPGRADE — Agent Brief

## Mục tiêu
Nâng cấp toàn bộ trải nghiệm đăng nhập/đăng ký hiện tại của ELearn Game Platform dựa trên file mẫu `ai_teaching_auth_final.html`, đồng thời nghiên cứu và áp dụng thêm UI/UX best practices cho authentication screen.

Không copy nguyên HTML/CSS/JS thuần vào app. Chuyển ý tưởng thiết kế thành React components, CSS module/page CSS, state rõ ràng, responsive, accessibility tốt và nối được với auth backend hiện tại hoặc auth API mới nếu repo chưa có.

## File tham chiếu phải đưa vào repo
Đặt file mẫu vào:

```text
docs/design/auth/ai_teaching_auth_final.html
```

Tạo thêm spec ngắn:

```text
docs/design/auth/auth-ui-upgrade-spec.md
```

Trong spec ghi rõ file HTML là visual reference, không phải production implementation.

## Bối cảnh repo
Repo là React 18 frontend + ASP.NET Core 8 Web API + PostgreSQL. Hệ thống hiện có luồng upload tài liệu, OCR, AI analysis, quiz, flashcard, slide studio. Theo docs hiện tại, auth thật chưa hoàn chỉnh và frontend còn dùng `demo-user`, nên khi làm auth phải kiểm tra thực tế source code trước khi sửa.

## Việc agent phải làm trước khi code
1. Inspect repo hiện tại:
   - Tìm file login/register hiện có trong `client/src`.
   - Tìm route auth trong React Router.
   - Tìm API service liên quan auth trong frontend.
   - Tìm controller/service/entity user/auth trong backend.
   - Tìm nơi đang hardcode `demo-user`, `userId`, `UploadedBy`, ownership.
2. Ghi lại ngắn trong changelog:
   - Auth hiện tại là mock, partial hay real.
   - Những file sẽ sửa.
   - Có thay đổi DB migration hay không.
3. Không phá các flow hiện có: upload, workspace, study, quiz, flashcard, slide studio.

## Hướng UI cần chuyển từ file mẫu
Từ `ai_teaching_auth_final.html`, lấy các ý chính sau:

### Layout
- Auth page chia 2 cột trên desktop:
  - Cột trái: brand, nền gradient xanh teal, AI assistant/illustration, nút “Xem cách hoạt động”.
  - Cột phải: form login/register dạng tab, nội dung gọn, trường nhập rõ ràng.
- Trên mobile/tablet:
  - Ưu tiên form trước.
  - Cột minh họa chuyển lên top dạng compact hoặc ẩn bớt animation nặng.
  - Không overflow ngang.

### Login/Register tab
- Dùng state React, không dùng `onclick` DOM thuần.
- Có animated tab indicator nhưng phải đơn giản, không gây layout shift.
- Login gồm email, password, show/hide password, submit, link qua đăng ký.
- Register gồm full name, email, password, confirm password, role selector, submit, link qua đăng nhập.

### Role selector
- 2 role:
  - `student` / Người học: vào lớp, làm bài, xem kết quả.
  - `teacher` / Giảng viên: tạo học liệu, giao bài, theo dõi lớp.
- Dùng button/radio semantics, keyboard accessible.
- Role selected phải rõ bằng border, background, check icon và aria state.

### Modal “Cách nền tảng hoạt động”
- Chuyển modal flow trong file mẫu thành React component.
- Nội dung flow:
  - Giảng viên: tải tài liệu → tạo bài kiểm tra → theo dõi lớp học.
  - Lớp học: bộ câu hỏi, bài kiểm tra, xếp hạng, tiến độ.
  - Người học: vào lớp học → làm bài được giao → xem kết quả.
- Modal phải có:
  - `role="dialog"`, `aria-modal="true"`.
  - Đóng bằng ESC.
  - Đóng khi click backdrop.
  - Focus trap hoặc ít nhất focus quay lại nút mở modal.

### AI assistant illustration
- Có thể chuyển canvas animation thành component `AiAssistantCanvas.jsx`.
- Animation phải optional và tôn trọng `prefers-reduced-motion`.
- Nếu canvas làm phức tạp hoặc ảnh hưởng hiệu năng, thay bằng SVG/illustration tĩnh nhưng vẫn giữ cảm giác “AI Teaching”.
- Không dùng global event listener không cleanup. Mọi listener phải cleanup trong `useEffect`.

## UI/UX nâng cấp thêm sau khi research
Áp dụng thêm các điểm sau:

### Form UX
- Label thật luôn hiển thị, không chỉ dùng placeholder.
- Placeholder chỉ là ví dụ, không thay thế label.
- Có helper/error text dưới input, tránh toast cho lỗi form đơn giản.
- Giữ chiều cao khu vực error ổn định để tránh layout nhảy.
- Submit button có loading state, disabled state, success/error state.

### Password UX
- Password field có show/hide button với `aria-label` thay đổi theo trạng thái.
- Register nên có password strength/help text ngắn:
  - Tối thiểu 8 ký tự.
  - Nên có chữ và số.
- Không ép password rule quá phức tạp nếu backend chưa hỗ trợ.
- Thêm `autoComplete` chuẩn:
  - Login email: `autoComplete="username"` hoặc `email`.
  - Login password: `autoComplete="current-password"`.
  - Register password: `autoComplete="new-password"`.
  - Confirm password: `autoComplete="new-password"`.

### Accessibility
- Tất cả nút/icon button có accessible name.
- Target click tối thiểu 40px; ưu tiên 44px cho mobile.
- Focus ring rõ, không remove outline nếu không thay bằng focus style tốt hơn.
- Màu chữ phải đủ contrast.
- Tab login/register dùng button, không dùng anchor giả.
- Role card dùng radio group hoặc button group có keyboard support.

### Security UX
- Login error không nói rõ “email không tồn tại” hay “mật khẩu sai”; dùng message chung: “Email hoặc mật khẩu không đúng.”
- Register có kiểm tra confirm password ở client, nhưng backend vẫn phải validate lại.
- Không log password/token.
- Token/session lưu theo pattern hiện có của repo; ưu tiên HTTP-only cookie nếu backend hỗ trợ, nếu không thì dùng Bearer token tạm thời nhưng phải ghi rõ trade-off.

## Backend/auth scope
Agent phải chọn theo thực trạng repo:

### Trường hợp A — Backend đã có auth endpoint
- Không tạo hệ auth mới.
- Wire UI vào endpoint hiện có.
- Chuẩn hóa response/error/loading.
- Sau login, lưu auth state và redirect về dashboard/workspace.
- Sau register, tự login hoặc chuyển sang login tùy endpoint hiện có.

### Trường hợp B — Backend chưa có auth thật
Triển khai minimal auth vừa đủ, không over-engineer:
- Entity `ApplicationUser` hoặc `User` gồm:
  - Id
  - FullName
  - Email normalized/unique
  - PasswordHash
  - Role: Student/Teacher/Admin nếu đã có admin
  - CreatedAt
- Dùng password hasher an toàn của .NET, không tự hash thủ công.
- Thêm endpoints:
  - `POST /api/auth/register`
  - `POST /api/auth/login`
  - `GET /api/auth/me`
  - `POST /api/auth/logout` nếu dùng cookie/session
- Thêm JWT/cookie auth tùy cấu trúc hiện tại.
- Thêm migration PostgreSQL nếu có entity mới.
- Thay dần `demo-user` bằng user thật ở frontend.

## File gợi ý frontend
Agent phải điều chỉnh theo cấu trúc thực tế, nhưng hướng tách nên là:

```text
client/src/pages/AuthPage.jsx
client/src/components/auth/AuthLayout.jsx
client/src/components/auth/AuthTabs.jsx
client/src/components/auth/LoginForm.jsx
client/src/components/auth/RegisterForm.jsx
client/src/components/auth/RoleSelector.jsx
client/src/components/auth/PasswordField.jsx
client/src/components/auth/AuthFlowModal.jsx
client/src/components/auth/AiAssistantCanvas.jsx
client/src/services/authService.js
client/src/context/AuthContext.jsx
client/src/styles/pages/auth.css
```

Nếu repo đang dùng cấu trúc khác, giữ style nhất quán với repo thay vì tạo trùng lặp lung tung.

## Route/redirect mong muốn
- `/login`: mở AuthPage tab login.
- `/register`: mở AuthPage tab register.
- Nếu chưa login mà vào protected pages: redirect `/login`.
- Sau login:
  - Teacher/Giảng viên → workspace/dashboard tạo học liệu.
  - Student/Người học → study/class dashboard nếu đã có; nếu chưa có thì dashboard chung.
- Nếu đang login rồi vào `/login` hoặc `/register`: redirect dashboard.

## Acceptance criteria
- UI mới bám visual direction của file HTML mẫu nhưng là React sạch, không dùng inline onclick/script DOM thuần.
- Login/register hoạt động với auth hiện tại hoặc minimal auth mới.
- Không còn hardcode `demo-user` trong các flow đã cần ownership thật, hoặc có TODO rõ nếu chưa chuyển hết.
- Role được lưu đúng và dùng được để điều hướng/ẩn hiện tính năng.
- Mobile không vỡ layout, không overflow ngang.
- Keyboard navigation dùng được.
- `npm run build` pass.
- Backend `dotnet build` pass nếu có sửa backend.
- Có cập nhật docs/changelog ngắn về auth UI upgrade.

## Lệnh kiểm tra sau khi làm

```powershell
cd H:\pbl5\client
npm run build

cd H:\pbl5
dotnet build
```

Nếu có migration:

```powershell
cd H:\pbl5\src\ELearnGamePlatform.API
dotnet ef migrations list --project ../ELearnGamePlatform.Infrastructure
dotnet ef database update --project ../ELearnGamePlatform.Infrastructure
```

## Không được làm
- Không iframe file HTML mẫu vào React.
- Không copy nguyên `<script>` và global functions vào app.
- Không lưu password plaintext.
- Không để thông báo lỗi login làm lộ user/email tồn tại.
- Không làm scope quá lớn như social login/passkey/MFA trong phase này, trừ khi repo đã có sẵn nền tảng.
- Không phá dashboard/workspace/study/slide hiện có.
