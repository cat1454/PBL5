# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: smoke.spec.js >> Empirical Difficulty Weighted Scoring Smoke Test >> Complete Flow: Teacher creates/closes, Student joins/attempts/views
- Location: smoke.spec.js:21:3

# Error details

```
Test timeout of 30000ms exceeded.
```

```
Error: page.selectOption: Test timeout of 30000ms exceeded.
Call log:
  - waiting for locator('select:has-text("Chon bo cau hoi"), select:has-text("Chọn bộ câu hỏi"), select:has-text("QuestionSet")')

```

# Page snapshot

```yaml
- generic [ref=e3]:
  - banner [ref=e4]:
    - generic [ref=e5]:
      - generic [ref=e6]:
        - button "Mở menu điều hướng" [ref=e7] [cursor=pointer]:
          - img [ref=e8]
        - link "AI Teaching Không gian học tập với AI" [ref=e9] [cursor=pointer]:
          - /url: /
          - img [ref=e11]
          - generic [ref=e14]:
            - strong [ref=e15]: AI Teaching
            - generic [ref=e16]: Không gian học tập với AI
        - navigation "Menu điều hướng chính" [ref=e17]:
          - link "Bảng điều khiển" [ref=e18] [cursor=pointer]:
            - /url: /
            - img [ref=e19]
            - text: Bảng điều khiển
          - link "Không gian làm việc" [ref=e22] [cursor=pointer]:
            - /url: /workspaces
            - img [ref=e23]
            - text: Không gian làm việc
          - link "Lớp học" [ref=e25] [cursor=pointer]:
            - /url: /classrooms
            - img [ref=e26]
            - text: Lớp học
          - link "Thống kê" [ref=e31] [cursor=pointer]:
            - /url: /analytics
            - img [ref=e32]
            - text: Thống kê
          - button "Hướng dẫn" [ref=e35] [cursor=pointer]:
            - img [ref=e36]
            - text: Hướng dẫn
      - generic [ref=e39]:
        - generic "Ngôn ngữ" [ref=e40]:
          - button "VI" [ref=e41] [cursor=pointer]
          - button "EN" [ref=e42] [cursor=pointer]
        - button "S Smoke Teacher Giảng viên" [ref=e44] [cursor=pointer]:
          - generic [ref=e45]: S
          - generic [ref=e46]:
            - generic [ref=e47]: Smoke Teacher
            - generic [ref=e48]: Giảng viên
          - img [ref=e49]
  - main [ref=e52]:
    - main [ref=e54]:
      - generic [ref=e56]:
        - generic [ref=e57]: Classroom
        - heading "Assignments" [level=1] [ref=e58]
        - paragraph [ref=e59]: Smoke Workspace
      - navigation "Classroom navigation" [ref=e60]:
        - link "Đang dạy" [ref=e61] [cursor=pointer]:
          - /url: /classrooms/teaching
          - img [ref=e62]
          - text: Đang dạy
        - link "Đã tham gia" [ref=e67] [cursor=pointer]:
          - /url: /classrooms/joined
          - img [ref=e68]
          - text: Đã tham gia
        - link "Nhập code" [ref=e71] [cursor=pointer]:
          - /url: /classrooms/join
          - img [ref=e72]
          - text: Nhập code
      - alert [ref=e75]: Network Error
      - generic [ref=e76]:
        - link "Tong quan lop" [ref=e77] [cursor=pointer]:
          - /url: /classrooms/7
          - img [ref=e78]
          - text: Tong quan lop
        - link "Bo cau hoi" [ref=e83] [cursor=pointer]:
          - /url: /classrooms/7/question-sets
          - img [ref=e84]
          - text: Bo cau hoi
        - link "Assignments" [ref=e87] [cursor=pointer]:
          - /url: /classrooms/7/assignments
          - img [ref=e88]
          - text: Assignments
        - link "Danh sách thành viên" [ref=e91] [cursor=pointer]:
          - /url: /classrooms/7/members
          - img [ref=e92]
          - text: Danh sách thành viên
      - generic [ref=e95]:
        - generic [ref=e96]:
          - generic [ref=e97]:
            - generic [ref=e98]: Teacher tools
            - heading "Tao assignment" [level=2] [ref=e99]
          - generic [ref=e100]:
            - generic [ref=e101]: Tieu de
            - textbox "Tieu de" [active] [ref=e102]:
              - /placeholder: "Vi du: N5 midterm quiz"
              - text: Percent Assignment
          - generic [ref=e103]:
            - generic [ref=e104]: Mo ta
            - textbox "Mo ta" [ref=e105]:
              - /placeholder: Huong dan ngan cho hoc vien
          - generic [ref=e106]:
            - generic [ref=e107]: Published QuestionSet
            - textbox "Published QuestionSet" [ref=e108]:
              - /placeholder: QuestionSet ID
          - generic [ref=e109]:
            - generic [ref=e110]:
              - generic [ref=e111]: Type
              - combobox "Type" [ref=e112]:
                - option "Quiz" [selected]
                - option "Test"
                - option "Flashcard"
                - option "Mixed"
            - generic [ref=e113]:
              - generic [ref=e114]: Attempt limit
              - textbox "Attempt limit" [ref=e115]: "1"
            - generic [ref=e116]:
              - generic [ref=e117]: Time limit
              - textbox "Time limit" [ref=e118]:
                - /placeholder: Phut, optional
          - generic [ref=e119]:
            - generic [ref=e120]:
              - generic [ref=e121]: Start at
              - textbox "Start at" [ref=e122]
            - generic [ref=e123]:
              - generic [ref=e124]: Due at
              - textbox "Due at" [ref=e125]
          - generic [ref=e126]:
            - generic [ref=e127]: Cách chấm điểm
            - combobox "Cách chấm điểm" [ref=e128]:
              - option "Chấm theo phần trăm" [selected]
              - option "Chấm theo độ khó thực nghiệm"
          - generic [ref=e129]:
            - checkbox "Shuffle questions" [ref=e130]
            - generic [ref=e131]: Shuffle questions
          - generic [ref=e132]:
            - checkbox "Shuffle options" [ref=e133]
            - generic [ref=e134]: Shuffle options
          - generic [ref=e135]:
            - checkbox "Show answer after submit" [checked] [ref=e136]
            - generic [ref=e137]: Show answer after submit
          - button "Tao assignment" [ref=e138] [cursor=pointer]:
            - img [ref=e139]
            - text: Tao assignment
        - generic [ref=e140]:
          - img [ref=e141]
          - heading "Chua co assignment" [level=2] [ref=e144]
          - paragraph [ref=e145]: Tao assignment tu bo cau hoi da Published.
          - button "Làm mới" [ref=e146] [cursor=pointer]:
            - img [ref=e147]
            - text: Làm mới
```

# Test source

```ts
  1   | const { test, expect } = require('@playwright/test');
  2   | 
  3   | test.describe('Empirical Difficulty Weighted Scoring Smoke Test', () => {
  4   |   let classroomId = '';
  5   |   let inviteCode = '';
  6   |   let questionSetId = '';
  7   |   let assignmentIdPercent = '';
  8   |   let assignmentIdEmpirical = '';
  9   | 
  10  |   test.beforeAll(async ({ request }) => {
  11  |     // Seed the test data
  12  |     const response = await request.post('http://localhost:5000/api/auth/smoke-seed');
  13  |     expect(response.ok()).toBeTruthy();
  14  |     const data = await response.json();
  15  |     classroomId = data.classroomId;
  16  |     inviteCode = data.joinCode;
  17  |     questionSetId = data.questionSetId;
  18  |     console.log(`[Seed Success] classroomId: ${classroomId}, inviteCode: ${inviteCode}`);
  19  |   });
  20  | 
  21  |   test('Complete Flow: Teacher creates/closes, Student joins/attempts/views', async ({ page }) => {
  22  |     // === 1. LOGIN AS TEACHER ===
  23  |     console.log('Logging in as Teacher...');
  24  |     await page.goto('http://localhost:3000/login');
  25  |     await page.fill('input[type="email"]', 'teacher_smoke@t.com');
  26  |     await page.fill('input[type="password"]', 'Password123!');
  27  |     await page.click('button[type="submit"]');
  28  |     await expect(page).toHaveURL('http://localhost:3000/');
  29  | 
  30  |     // Check workspaces page (backward compatibility check)
  31  |     console.log('Checking /workspaces backward compatibility...');
  32  |     await page.goto('http://localhost:3000/workspaces');
  33  |     await expect(page.locator('#workspaces-hub-title')).toBeVisible();
  34  | 
  35  |     // Check teaching classrooms page
  36  |     console.log('Checking /classrooms/teaching...');
  37  |     await page.goto('http://localhost:3000/classrooms/teaching');
  38  |     await expect(page.locator('text=Smoke Workspace')).toBeVisible();
  39  | 
  40  |     // Navigate to teaching classroom detail
  41  |     await page.click('text=Smoke Workspace');
  42  |     await expect(page).toHaveURL(`http://localhost:3000/classrooms/${classroomId}`);
  43  | 
  44  |     // Navigate to assignments tab
  45  |     await page.goto(`http://localhost:3000/classrooms/${classroomId}/assignments`);
  46  | 
  47  |     // --- Create Percent Assignment ---
  48  |     console.log('Creating Percent Assignment...');
  49  |     await page.fill('input[placeholder*="N5 midterm quiz"], input[placeholder*="Vi du"]', 'Percent Assignment');
> 50  |     await page.selectOption('select:has-text("Chon bo cau hoi"), select:has-text("Chọn bộ câu hỏi"), select:has-text("QuestionSet")', { index: 1 });
      |                ^ Error: page.selectOption: Test timeout of 30000ms exceeded.
  51  |     await page.selectOption('select:has-text("Chấm theo phần trăm"), select:has-text("Chấm theo"), select:has-text("Percent")', 'Percent');
  52  |     // Min/max weights should not be visible
  53  |     await expect(page.locator('text=Trọng số tối thiểu')).not.toBeVisible();
  54  |     await page.click('button[type="submit"]');
  55  |     
  56  |     // Wait for the new assignment to appear in list
  57  |     const percentItem = page.locator('text=Percent Assignment');
  58  |     await expect(percentItem).toBeVisible();
  59  | 
  60  |     // --- Create Empirical Difficulty Assignment ---
  61  |     console.log('Creating Empirical Assignment...');
  62  |     await page.fill('input[placeholder*="N5 midterm quiz"], input[placeholder*="Vi du"]', 'Empirical Assignment');
  63  |     await page.selectOption('select:has-text("Chon bo cau hoi"), select:has-text("Chọn bộ câu hỏi"), select:has-text("QuestionSet")', { index: 1 });
  64  |     // Select Empirical Difficulty
  65  |     await page.selectOption('select:has-text("Chấm theo phần trăm"), select:has-text("Chấm theo"), select:has-text("Percent")', 'EmpiricalDifficulty');
  66  | 
  67  |     // Min/max weights and alpha/beta should now be visible
  68  |     await expect(page.locator('text=Trọng số tối thiểu')).toBeVisible();
  69  | 
  70  |     // Test validation: input invalid configs
  71  |     console.log('Testing client-side validation...');
  72  |     await page.fill('label:has-text("Trọng số tối thiểu") input', '0'); // Invalid Min
  73  |     await page.click('button[type="submit"]');
  74  |     // Error message should appear
  75  |     await expect(page.locator('.classroom-message.error')).toBeVisible();
  76  | 
  77  |     // Input valid configs
  78  |     await page.fill('label:has-text("Trọng số tối thiểu") input', '0.5');
  79  |     await page.fill('label:has-text("Trọng số tối đa") input', '2.5');
  80  |     await page.fill('label:has-text("Smoothing alpha") input', '1');
  81  |     await page.fill('label:has-text("Smoothing beta") input', '1');
  82  | 
  83  |     await page.click('button[type="submit"]');
  84  | 
  85  |     // Verify created successfully
  86  |     const empiricalItem = page.locator('text=Empirical Assignment');
  87  |     await expect(empiricalItem).toBeVisible();
  88  | 
  89  |     // Click details of Empirical Assignment to publish it
  90  |     console.log('Publishing Empirical Assignment...');
  91  |     await page.click('text=Empirical Assignment');
  92  |     
  93  |     // Extract assignmentId from URL
  94  |     const currentUrl = page.url();
  95  |     const match = currentUrl.match(/\/assignments\/(\d+)/);
  96  |     expect(match).not.toBeNull();
  97  |     assignmentIdEmpirical = match[1];
  98  | 
  99  |     // Verify scoring config details rendered
  100 |     await expect(page.locator('text=Chấm theo độ khó thực nghiệm')).toBeVisible();
  101 |     await expect(page.locator('text=Trọng số tối thiểu: 0.5')).toBeVisible();
  102 | 
  103 |     // Publish
  104 |     await page.click('button:has-text("Publish")');
  105 |     await expect(page.locator('.classroom-badge:has-text("Published")')).toBeVisible();
  106 | 
  107 |     // === 2. LOGIN AS STUDENT ===
  108 |     console.log('Logging out teacher and logging in as Student...');
  109 |     // Clear tokens by going to login and typing student info
  110 |     await page.goto('http://localhost:3000/login');
  111 |     await page.fill('input[type="email"]', 'student_smoke@t.com');
  112 |     await page.fill('input[type="password"]', 'Password123!');
  113 |     await page.click('button[type="submit"]');
  114 |     await expect(page).toHaveURL('http://localhost:3000/');
  115 | 
  116 |     // Check classrooms/joined and join
  117 |     console.log('Checking student classrooms and joining...');
  118 |     await page.goto('http://localhost:3000/classrooms/join');
  119 |     await page.fill('input[placeholder*="invite code"], input[placeholder*="mã tham gia"]', inviteCode);
  120 |     await page.click('button[type="submit"]');
  121 | 
  122 |     // Go to student assignments list
  123 |     await page.goto(`http://localhost:3000/classrooms/${classroomId}/student/assignments`);
  124 |     await expect(page.locator('text=Empirical Assignment')).toBeVisible();
  125 | 
  126 |     // Go to assignment details
  127 |     await page.click('text=Empirical Assignment');
  128 |     await expect(page).toHaveURL(`http://localhost:3000/classrooms/${classroomId}/student/assignments/${assignmentIdEmpirical}`);
  129 | 
  130 |     // Verify warning banner and empirical notice are visible
  131 |     await expect(page.locator('.classroom-info-banner.warning')).toBeVisible();
  132 |     await expect(page.locator('text=Điểm được tính theo độ khó thực nghiệm')).toBeVisible();
  133 |     // Verify teacher controls are hidden
  134 |     await expect(page.locator('button:has-text("Publish")')).not.toBeVisible();
  135 |     await expect(page.locator('button:has-text("Lưu")')).not.toBeVisible();
  136 | 
  137 |     // Start attempt
  138 |     console.log('Starting attempt...');
  139 |     await page.click('button:has-text("Start")');
  140 |     const attemptUrl = page.url();
  141 |     const attemptMatch = attemptUrl.match(/\/classroom-attempts\/(\d+)/);
  142 |     expect(attemptMatch).not.toBeNull();
  143 |     const attemptId = attemptMatch[1];
  144 | 
  145 |     // Verify correct answers are NOT exposed
  146 |     await expect(page.locator('text=correctAnswer')).not.toBeVisible();
  147 |     await expect(page.locator('.classroom-answer-key')).not.toBeVisible();
  148 | 
  149 |     // Answer questions (Question 1 -> choice A, Question 2 -> choice B)
  150 |     console.log('Answering questions...');
```