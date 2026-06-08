const { test, expect } = require('@playwright/test');

test.describe('Empirical Difficulty Weighted Scoring Smoke Test', () => {
  let classroomId = '';
  let inviteCode = '';
  let questionSetId = '';
  let assignmentIdPercent = '';
  let assignmentIdEmpirical = '';

  test.beforeAll(async ({ request }) => {
    // Seed the test data
    const response = await request.post('http://localhost:5000/api/auth/smoke-seed');
    expect(response.ok()).toBeTruthy();
    const data = await response.json();
    classroomId = data.classroomId;
    inviteCode = data.joinCode;
    questionSetId = data.questionSetId;
    console.log(`[Seed Success] classroomId: ${classroomId}, inviteCode: ${inviteCode}`);
  });

  test('Complete Flow: Teacher creates/closes, Student joins/attempts/views', async ({ page }) => {
    // === 1. LOGIN AS TEACHER ===
    console.log('Logging in as Teacher...');
    await page.goto('http://localhost:3000/login');
    await page.fill('input[type="email"]', 'teacher_smoke@t.com');
    await page.fill('input[type="password"]', 'Password123!');
    await page.click('button[type="submit"]');
    await expect(page).toHaveURL('http://localhost:3000/');

    // Check workspaces page (backward compatibility check)
    console.log('Checking /workspaces backward compatibility...');
    await page.goto('http://localhost:3000/workspaces');
    await expect(page.locator('#workspaces-hub-title')).toBeVisible();

    // Check teaching classrooms page
    console.log('Checking /classrooms/teaching...');
    await page.goto('http://localhost:3000/classrooms/teaching');
    await expect(page.locator('text=Smoke Workspace')).toBeVisible();

    // Navigate to teaching classroom detail
    await page.click('text=Smoke Workspace');
    await expect(page).toHaveURL(`http://localhost:3000/classrooms/${classroomId}`);

    // Navigate to assignments tab
    await page.goto(`http://localhost:3000/classrooms/${classroomId}/assignments`);

    // --- Create Percent Assignment ---
    console.log('Creating Percent Assignment...');
    await page.fill('input[placeholder*="N5 midterm quiz"], input[placeholder*="Vi du"]', 'Percent Assignment');
    await page.selectOption('select:has-text("Chon bo cau hoi"), select:has-text("Chọn bộ câu hỏi"), select:has-text("QuestionSet")', { index: 1 });
    await page.selectOption('select:has-text("Chấm theo phần trăm"), select:has-text("Chấm theo"), select:has-text("Percent")', 'Percent');
    // Min/max weights should not be visible
    await expect(page.locator('text=Trọng số tối thiểu')).not.toBeVisible();
    await page.click('button[type="submit"]');
    
    // Wait for the new assignment to appear in list
    const percentItem = page.locator('text=Percent Assignment');
    await expect(percentItem).toBeVisible();

    // --- Create Empirical Difficulty Assignment ---
    console.log('Creating Empirical Assignment...');
    await page.fill('input[placeholder*="N5 midterm quiz"], input[placeholder*="Vi du"]', 'Empirical Assignment');
    await page.selectOption('select:has-text("Chon bo cau hoi"), select:has-text("Chọn bộ câu hỏi"), select:has-text("QuestionSet")', { index: 1 });
    // Select Empirical Difficulty
    await page.selectOption('select:has-text("Chấm theo phần trăm"), select:has-text("Chấm theo"), select:has-text("Percent")', 'EmpiricalDifficulty');

    // Min/max weights and alpha/beta should now be visible
    await expect(page.locator('text=Trọng số tối thiểu')).toBeVisible();

    // Test validation: input invalid configs
    console.log('Testing client-side validation...');
    await page.fill('label:has-text("Trọng số tối thiểu") input', '0'); // Invalid Min
    await page.click('button[type="submit"]');
    // Error message should appear
    await expect(page.locator('.classroom-message.error')).toBeVisible();

    // Input valid configs
    await page.fill('label:has-text("Trọng số tối thiểu") input', '0.5');
    await page.fill('label:has-text("Trọng số tối đa") input', '2.5');
    await page.fill('label:has-text("Smoothing alpha") input', '1');
    await page.fill('label:has-text("Smoothing beta") input', '1');

    await page.click('button[type="submit"]');

    // Verify created successfully
    const empiricalItem = page.locator('text=Empirical Assignment');
    await expect(empiricalItem).toBeVisible();

    // Click details of Empirical Assignment to publish it
    console.log('Publishing Empirical Assignment...');
    await page.click('text=Empirical Assignment');
    
    // Extract assignmentId from URL
    const currentUrl = page.url();
    const match = currentUrl.match(/\/assignments\/(\d+)/);
    expect(match).not.toBeNull();
    assignmentIdEmpirical = match[1];

    // Verify scoring config details rendered
    await expect(page.locator('text=Chấm theo độ khó thực nghiệm')).toBeVisible();
    await expect(page.locator('text=Trọng số tối thiểu: 0.5')).toBeVisible();

    // Publish
    await page.click('button:has-text("Publish")');
    await expect(page.locator('.classroom-badge:has-text("Published")')).toBeVisible();

    // === 2. LOGIN AS STUDENT ===
    console.log('Logging out teacher and logging in as Student...');
    // Clear tokens by going to login and typing student info
    await page.goto('http://localhost:3000/login');
    await page.fill('input[type="email"]', 'student_smoke@t.com');
    await page.fill('input[type="password"]', 'Password123!');
    await page.click('button[type="submit"]');
    await expect(page).toHaveURL('http://localhost:3000/');

    // Check classrooms/joined and join
    console.log('Checking student classrooms and joining...');
    await page.goto('http://localhost:3000/classrooms/join');
    await page.fill('input[placeholder*="invite code"], input[placeholder*="mã tham gia"]', inviteCode);
    await page.click('button[type="submit"]');

    // Go to student assignments list
    await page.goto(`http://localhost:3000/classrooms/${classroomId}/student/assignments`);
    await expect(page.locator('text=Empirical Assignment')).toBeVisible();

    // Go to assignment details
    await page.click('text=Empirical Assignment');
    await expect(page).toHaveURL(`http://localhost:3000/classrooms/${classroomId}/student/assignments/${assignmentIdEmpirical}`);

    // Verify warning banner and empirical notice are visible
    await expect(page.locator('.classroom-info-banner.warning')).toBeVisible();
    await expect(page.locator('text=Điểm được tính theo độ khó thực nghiệm')).toBeVisible();
    // Verify teacher controls are hidden
    await expect(page.locator('button:has-text("Publish")')).not.toBeVisible();
    await expect(page.locator('button:has-text("Lưu")')).not.toBeVisible();

    // Start attempt
    console.log('Starting attempt...');
    await page.click('button:has-text("Start")');
    const attemptUrl = page.url();
    const attemptMatch = attemptUrl.match(/\/classroom-attempts\/(\d+)/);
    expect(attemptMatch).not.toBeNull();
    const attemptId = attemptMatch[1];

    // Verify correct answers are NOT exposed
    await expect(page.locator('text=correctAnswer')).not.toBeVisible();
    await expect(page.locator('.classroom-answer-key')).not.toBeVisible();

    // Answer questions (Question 1 -> choice A, Question 2 -> choice B)
    console.log('Answering questions...');
    await page.click('text=Paris'); // choice A for Q1
    await page.click('button:has-text("Next"), button:has-text("Tiếp theo"), button:has-text("Submit Answer")');
    await page.click('text=4'); // choice B for Q2
    await page.click('button:has-text("Next"), button:has-text("Tiếp theo"), button:has-text("Submit Answer")');

    // Submit attempt
    console.log('Submitting attempt...');
    await page.click('button:has-text("Submit Attempt"), button:has-text("Nộp bài")');
    await expect(page).toHaveURL(`http://localhost:3000/classroom-attempts/${attemptId}/result`);

    // Verify result page displays temporary score warnings
    await expect(page.locator('.classroom-info-banner.warning')).toContainText('Điểm chính thức được xác định khi giảng viên đóng assignment.');

    // Go to history
    await page.goto('http://localhost:3000/classroom-attempts/history');
    await expect(page.locator('.scoring-badge-pill')).toContainText('(Điểm tạm thời)');

    // === 3. TEACHER CLOSES ASSIGNMENT ===
    console.log('Logging in as Teacher to close assignment...');
    await page.goto('http://localhost:3000/login');
    await page.fill('input[type="email"]', 'teacher_smoke@t.com');
    await page.fill('input[type="password"]', 'Password123!');
    await page.click('button[type="submit"]');

    await page.goto(`http://localhost:3000/classrooms/${classroomId}/assignments/${assignmentIdEmpirical}`);
    // Click Close
    await page.click('button:has-text("Close")');
    await expect(page.locator('.classroom-badge:has-text("Closed")')).toBeVisible();

    // Verify Question Stats table rendered
    console.log('Verifying Question Statistics table...');
    await expect(page.locator('.classroom-stats-table')).toBeVisible();
    await expect(page.locator('.classroom-stats-table th:has-text("Question ID")')).toBeVisible();
    await expect(page.locator('.classroom-stats-table th:has-text("Trọng số độ khó")')).toBeVisible();
    
    // Verify quality flags
    await expect(page.locator('.classroom-stat-badge')).toBeVisible();

    // === 4. STUDENT VIEWS FINALIZED SCORE ===
    console.log('Logging in as Student to check finalized score...');
    await page.goto('http://localhost:3000/login');
    await page.fill('input[type="email"]', 'student_smoke@t.com');
    await page.fill('input[type="password"]', 'Password123!');
    await page.click('button[type="submit"]');

    // Go to history
    await page.goto('http://localhost:3000/classroom-attempts/history');
    await expect(page.locator('.scoring-badge-pill')).toContainText('(Chính thức)');

    // Go to result
    await page.goto(`http://localhost:3000/classroom-attempts/${attemptId}/result`);
    await expect(page.locator('.classroom-info-banner.warning')).toContainText('Giảng viên đã đóng bài thi, điểm số này đã được tính toán chính thức.');
    
    // Verify student cannot see stats table
    await expect(page.locator('.classroom-stats-table')).not.toBeVisible();

    console.log('--- ALL SMOKE TESTS COMPLETED SUCCESSFULLY ---');
  });
});
