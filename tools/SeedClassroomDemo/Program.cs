using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Enums;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SeedClassroomDemo
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("=== ELearnGamePlatform Dev Classroom Seed Tool ===");

            // 1. Find API project folder
            var currentDir = AppContext.BaseDirectory;
            string? apiRoot = null;
            var dir = new DirectoryInfo(currentDir);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "src", "ELearnGamePlatform.API");
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "appsettings.json")))
                {
                    apiRoot = candidate;
                    break;
                }
                dir = dir.Parent;
            }

            if (apiRoot == null)
            {
                var candidate = Path.Combine(Directory.GetCurrentDirectory(), "src", "ELearnGamePlatform.API");
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "appsettings.json")))
                {
                    apiRoot = candidate;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: Could not find ELearnGamePlatform.API root directory containing appsettings.json.");
                    Console.ResetColor();
                    return;
                }
            }

            Console.WriteLine($"Found API project root: {apiRoot}");

            // 2. Load Configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(apiRoot)
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

            // 3. Register Services
            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "HH:mm:ss ";
                });
                builder.SetMinimumLevel(LogLevel.Information);
            });

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(connectionString));

            services.AddScoped<IClassroomPermissionService, ClassroomPermissionService>();
            services.AddScoped<IClassroomAssignmentService, ClassroomAssignmentService>();
            services.AddScoped<IPasswordService, PasswordService>();

            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var assignmentService = scope.ServiceProvider.GetRequiredService<IClassroomAssignmentService>();
            var passwordService = scope.ServiceProvider.GetRequiredService<IPasswordService>();

            // 4. Seeding Data
            try
            {
                // Verify DB can connect
                if (!await db.Database.CanConnectAsync())
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: Cannot connect to the database. Please ensure PostgreSQL is running.");
                    Console.ResetColor();
                    return;
                }

                Console.WriteLine("Connected to database successfully. Seeding data...");

                // A. Seed Users (Teacher & 12 Students)
                var teacherEmail = "teacher.demo@elearn.local";
                var teacher = await db.AppUsers.FirstOrDefaultAsync(u => u.Email == teacherEmail);
                if (teacher == null)
                {
                    teacher = new AppUser
                    {
                        FullName = "Demo Teacher",
                        Email = teacherEmail,
                        PasswordHash = string.Empty,
                        Role = UserRole.Instructor,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    teacher.PasswordHash = passwordService.HashPassword(teacher, "Password123!");
                    db.AppUsers.Add(teacher);
                    await db.SaveChangesAsync();
                    Console.WriteLine($"Seeded Teacher: {teacher.FullName} ({teacher.Email})");
                }
                else
                {
                    Console.WriteLine($"Reused existing Teacher: {teacher.FullName} ({teacher.Email})");
                }

                var students = new List<AppUser>();
                for (int i = 1; i <= 12; i++)
                {
                    var studentEmail = $"student{i:00}@elearn.local";
                    var student = await db.AppUsers.FirstOrDefaultAsync(u => u.Email == studentEmail);
                    if (student == null)
                    {
                        student = new AppUser
                        {
                            FullName = $"Demo Student {i:00}",
                            Email = studentEmail,
                            PasswordHash = string.Empty,
                            Role = UserRole.Learner,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        };
                        student.PasswordHash = passwordService.HashPassword(student, "Password123!");
                        db.AppUsers.Add(student);
                        await db.SaveChangesAsync();
                        Console.WriteLine($"Seeded Student {i:00}: {student.FullName}");
                    }
                    else
                    {
                        Console.WriteLine($"Reused existing Student {i:00}: {student.FullName}");
                    }
                    students.Add(student);
                }

                // B. Seed Classroom Workspace
                var classroomName = "Lớp Demo Leaderboard";
                var classroom = await db.ClassroomWorkspaces.FirstOrDefaultAsync(w => w.Name == classroomName && w.OwnerUserId == teacher.Id);
                if (classroom == null)
                {
                    classroom = new ClassroomWorkspace
                    {
                        Name = classroomName,
                        Description = "Dữ liệu demo cho leaderboard, analytics và chấm điểm theo độ khó thực nghiệm.",
                        OwnerUserId = teacher.Id,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    db.ClassroomWorkspaces.Add(classroom);
                    await db.SaveChangesAsync();
                    Console.WriteLine($"Seeded Classroom: {classroom.Name} (ID: {classroom.Id})");
                }
                else
                {
                    Console.WriteLine($"Reused existing Classroom: {classroom.Name} (ID: {classroom.Id})");
                }

                // Add members to classroom
                var existingMembers = await db.ClassroomMembers.Where(m => m.ClassroomWorkspaceId == classroom.Id).ToListAsync();
                var existingMemberUserIds = existingMembers.Select(m => m.UserId).ToHashSet();

                // Add Teacher member
                if (!existingMemberUserIds.Contains(teacher.Id))
                {
                    db.ClassroomMembers.Add(new ClassroomMember
                    {
                        ClassroomWorkspaceId = classroom.Id,
                        UserId = teacher.Id,
                        Role = ClassroomRole.Teacher,
                        Status = ClassroomMemberStatus.Active,
                        JoinedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                    Console.WriteLine("Added Teacher member to ClassroomWorkspace.");
                }

                // Add Student members
                foreach (var student in students)
                {
                    if (!existingMemberUserIds.Contains(student.Id))
                    {
                        db.ClassroomMembers.Add(new ClassroomMember
                        {
                            ClassroomWorkspaceId = classroom.Id,
                            UserId = student.Id,
                            Role = ClassroomRole.Student,
                            Status = ClassroomMemberStatus.Active,
                            JoinedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        });
                        Console.WriteLine($"Added student {student.FullName} member to ClassroomWorkspace.");
                    }
                }
                await db.SaveChangesAsync();

                // C. Seed Document
                var docName = "Demo Leaderboard Knowledge Base";
                var document = await db.Documents.FirstOrDefaultAsync(d => d.FileName == docName && d.UploadedBy == teacher.Id.ToString());
                if (document == null)
                {
                    document = new Document
                    {
                        FileName = docName,
                        FileType = "PDF",
                        FilePath = "/uploads/demo_leaderboard.pdf",
                        FileSize = 102400,
                        ExtractedText = "Demo Leaderboard Knowledge Base extracted text. This contains terms like machine learning, neural networks, scoring metrics, and leaderboard difficulty. Easy questions cover machine learning basics. Medium questions cover neural network architectures. Hard questions cover hyperparameter tuning and empirical difficulty evaluations.",
                        Language = "vi",
                        Status = DocumentStatus.Completed,
                        UploadedBy = teacher.Id.ToString(),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        MainTopicsJson = JsonSerializer.Serialize(new List<string> { "Machine Learning", "Neural Networks", "Evaluation Metrics" }),
                        KeyPointsJson = JsonSerializer.Serialize(new List<string> { "Empirical scoring uses student performance to adjust question weights.", "Leaderboards encourage healthy competition.", "Smoothed correction rate prevents division by zero." })
                    };
                    db.Documents.Add(document);
                    await db.SaveChangesAsync();
                    Console.WriteLine($"Seeded Document: {document.FileName} (ID: {document.Id})");
                }
                else
                {
                    Console.WriteLine($"Reused existing Document: {document.FileName} (ID: {document.Id})");
                }

                // D. Seed Questions (Q1-Q10)
                var existingQuestions = await db.Questions.Where(q => q.DocumentId == document.Id && !q.IsArchived).ToListAsync();
                var questions = new List<Question>();

                if (existingQuestions.Count < 10)
                {
                    // Remove old questions to avoid mix-up
                    db.Questions.RemoveRange(existingQuestions);
                    await db.SaveChangesAsync();

                    var questionData = new List<(string text, DifficultyLevel diff, string explanation)>
                    {
                        ("Câu hỏi 1 (Dễ): Ai là người sáng tạo ra ngôn ngữ lập trình C#?", DifficultyLevel.Easy, "Microsoft (Anders Hejlsberg) phát triển C#."),
                        ("Câu hỏi 2 (Dễ): Cú pháp kết thúc một câu lệnh trong C# là gì?", DifficultyLevel.Easy, "Dấu chấm phẩy ';' kết thúc một câu lệnh."),
                        ("Câu hỏi 3 (Dễ): Từ khóa nào dùng để khai báo hằng số trong C#?", DifficultyLevel.Easy, "Từ khóa 'const' dùng khai báo hằng số."),
                        ("Câu hỏi 4 (Trung bình): Npgsql dùng để làm gì trong ứng dụng .NET?", DifficultyLevel.Medium, "Npgsql là open-source ADO.NET Data Provider cho PostgreSQL."),
                        ("Câu hỏi 5 (Trung bình): DbContext Lifetime mặc định trong ASP.NET Core DI là gì?", DifficultyLevel.Medium, "Mặc định là Scoped."),
                        ("Câu hỏi 6 (Trung bình): Sự khác biệt chính giữa IEnumerable và IQueryable là gì?", DifficultyLevel.Medium, "IQueryable thực thi truy vấn phía server database còn IEnumerable thực thi phía client memory."),
                        ("Câu hỏi 7 (Trung bình): Từ khóa 'virtual' trong C# dùng để làm gì?", DifficultyLevel.Medium, "Cho phép ghi đè phương thức ở lớp kế thừa."),
                        ("Câu hỏi 8 (Khó): Thuật toán Empirical Difficulty Scoring điều chỉnh trọng số dựa trên yếu tố nào?", DifficultyLevel.Hard, "Điều chỉnh trọng số dựa trên tỷ lệ làm đúng thực tế của học sinh (Smoothed Correct Rate)."),
                        ("Câu hỏi 9 (Khó): Mục đích của Smoothing Alpha và Smoothing Beta trong công thức chấm điểm thực nghiệm là gì?", DifficultyLevel.Hard, "Tránh hiện tượng chia cho 0 và làm mượt trọng số câu hỏi khi số lượng học sinh làm bài ít."),
                        ("Câu hỏi 10 (Khó): Discrimination Index âm của một câu hỏi chỉ ra điều gì?", DifficultyLevel.Hard, "Chỉ ra rằng nhóm học sinh yếu làm đúng câu này nhiều hơn nhóm học sinh giỏi, câu hỏi có thể bị lỗi.")
                    };

                    for (int i = 0; i < 10; i++)
                    {
                        var data = questionData[i];
                        var q = new Question
                        {
                            DocumentId = document.Id,
                            QuestionText = data.text,
                            QuestionType = QuestionType.MultipleChoice,
                            OptionsJson = JsonSerializer.Serialize(new List<QuestionOption>
                            {
                                new QuestionOption { Key = "A", Text = "Đáp án A (Đúng)", IsCorrect = true },
                                new QuestionOption { Key = "B", Text = "Đáp án B (Sai)", IsCorrect = false },
                                new QuestionOption { Key = "C", Text = "Đáp án C (Sai)", IsCorrect = false },
                                new QuestionOption { Key = "D", Text = "Đáp án D (Sai)", IsCorrect = false }
                            }),
                            CorrectAnswer = "A",
                            Explanation = data.explanation,
                            Difficulty = data.diff,
                            Topic = data.diff.ToString(),
                            IsArchived = false,
                            CreatedAt = DateTime.UtcNow
                        };
                        db.Questions.Add(q);
                        await db.SaveChangesAsync();
                        questions.Add(q);
                        Console.WriteLine($"Seeded Question {i + 1}: {q.QuestionText} (ID: {q.Id})");
                    }
                }
                else
                {
                    questions = existingQuestions.OrderBy(q => q.Id).Take(10).ToList();
                    Console.WriteLine("Reused 10 existing Questions.");
                }

                // E. Seed Classroom Question Set
                var questionSetTitle = "Bộ câu hỏi Demo Leaderboard";
                var questionSet = await db.ClassroomQuestionSets.FirstOrDefaultAsync(qs => qs.Title == questionSetTitle && qs.ClassroomWorkspaceId == classroom.Id);
                if (questionSet == null)
                {
                    questionSet = new ClassroomQuestionSet
                    {
                        ClassroomWorkspaceId = classroom.Id,
                        DocumentId = document.Id,
                        Title = questionSetTitle,
                        Description = "Bộ câu hỏi dùng cho dữ liệu demo leaderboard.",
                        CreatedByUserId = teacher.Id,
                        Visibility = ClassroomQuestionSetVisibility.Published,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    db.ClassroomQuestionSets.Add(questionSet);
                    await db.SaveChangesAsync();
                    Console.WriteLine($"Seeded ClassroomQuestionSet: {questionSet.Title} (ID: {questionSet.Id})");
                }
                else
                {
                    Console.WriteLine($"Reused existing ClassroomQuestionSet: {questionSet.Title} (ID: {questionSet.Id})");
                }

                // Question Set Items
                var existingItems = await db.ClassroomQuestionSetItems.Where(item => item.ClassroomQuestionSetId == questionSet.Id).ToListAsync();
                if (existingItems.Count == 0)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        string sectionCode = i < 3 ? "Knowledge" : (i < 7 ? "Understanding" : "Application");
                        db.ClassroomQuestionSetItems.Add(new ClassroomQuestionSetItem
                        {
                            ClassroomQuestionSetId = questionSet.Id,
                            QuestionId = questions[i].Id,
                            OrderIndex = i,
                            PointWeight = 1,
                            SectionCode = sectionCode,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                    await db.SaveChangesAsync();
                    Console.WriteLine("Added 10 items to ClassroomQuestionSet.");
                }
                else
                {
                    Console.WriteLine("Reused existing ClassroomQuestionSetItems.");
                }

                // F. Seed Assignments
                var assignmentATitle = "Bài kiểm tra Percent Demo";
                var assignmentBTitle = "Bài kiểm tra Độ khó thực nghiệm";
                var assignmentCTitle = "Bài kiểm tra Đang mở";

                var assignmentA = await db.ClassroomAssignments.FirstOrDefaultAsync(a => a.Title == assignmentATitle && a.ClassroomWorkspaceId == classroom.Id);
                if (assignmentA == null)
                {
                    assignmentA = new ClassroomAssignment
                    {
                        ClassroomWorkspaceId = classroom.Id,
                        QuestionSetId = questionSet.Id,
                        Title = assignmentATitle,
                        Description = "Bài kiểm tra theo chế độ tính điểm phần trăm cơ bản.",
                        Type = ClassroomAssignmentType.Quiz,
                        Status = ClassroomAssignmentStatus.Published,
                        AttemptLimit = 2,
                        ShowAnswerAfterSubmit = true,
                        ScoringMode = ClassroomScoringMode.Percent,
                        CreatedByUserId = teacher.Id,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    db.ClassroomAssignments.Add(assignmentA);
                    await db.SaveChangesAsync();
                    Console.WriteLine($"Seeded Assignment A: {assignmentA.Title} (ID: {assignmentA.Id})");
                }
                else
                {
                    // Ensure it is Published so we can add attempts
                    assignmentA.Status = ClassroomAssignmentStatus.Published;
                    await db.SaveChangesAsync();
                    Console.WriteLine($"Reused existing Assignment A: {assignmentA.Title} (ID: {assignmentA.Id})");
                }

                var assignmentB = await db.ClassroomAssignments.FirstOrDefaultAsync(a => a.Title == assignmentBTitle && a.ClassroomWorkspaceId == classroom.Id);
                if (assignmentB == null)
                {
                    assignmentB = new ClassroomAssignment
                    {
                        ClassroomWorkspaceId = classroom.Id,
                        QuestionSetId = questionSet.Id,
                        Title = assignmentBTitle,
                        Description = "Bài kiểm tra theo trọng số độ khó thực nghiệm. Điểm số sẽ được tính toán sau khi đóng.",
                        Type = ClassroomAssignmentType.Quiz,
                        Status = ClassroomAssignmentStatus.Published, // Start as Published so we can Close it using service
                        AttemptLimit = 1,
                        ShowAnswerAfterSubmit = true,
                        ScoringMode = ClassroomScoringMode.EmpiricalDifficulty,
                        MinQuestionWeight = 0.3m,
                        MaxQuestionWeight = 2.0m,
                        SmoothingAlpha = 1m,
                        SmoothingBeta = 1m,
                        CreatedByUserId = teacher.Id,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    db.ClassroomAssignments.Add(assignmentB);
                    await db.SaveChangesAsync();
                    Console.WriteLine($"Seeded Assignment B: {assignmentB.Title} (ID: {assignmentB.Id})");
                }
                else
                {
                    // Ensure it is Published so we can Close it again and recalculate
                    assignmentB.Status = ClassroomAssignmentStatus.Published;
                    await db.SaveChangesAsync();
                    Console.WriteLine($"Reused existing Assignment B: {assignmentB.Title} (ID: {assignmentB.Id})");
                }

                var assignmentC = await db.ClassroomAssignments.FirstOrDefaultAsync(a => a.Title == assignmentCTitle && a.ClassroomWorkspaceId == classroom.Id);
                if (assignmentC == null)
                {
                    assignmentC = new ClassroomAssignment
                    {
                        ClassroomWorkspaceId = classroom.Id,
                        QuestionSetId = questionSet.Id,
                        Title = assignmentCTitle,
                        Description = "Bài kiểm tra đang mở để học sinh vào làm.",
                        Type = ClassroomAssignmentType.Quiz,
                        Status = ClassroomAssignmentStatus.Published,
                        AttemptLimit = 2,
                        ShowAnswerAfterSubmit = false,
                        ScoringMode = ClassroomScoringMode.Percent,
                        CreatedByUserId = teacher.Id,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    db.ClassroomAssignments.Add(assignmentC);
                    await db.SaveChangesAsync();
                    Console.WriteLine($"Seeded Assignment C: {assignmentC.Title} (ID: {assignmentC.Id})");
                }
                else
                {
                    Console.WriteLine($"Reused existing Assignment C: {assignmentC.Title} (ID: {assignmentC.Id})");
                }

                // G. Reset attempts for specific demo assignments to be Idempotent
                Console.WriteLine("Resetting old attempts and question stats for the seeded assignments...");
                var seededAssignmentIds = new List<int> { assignmentA.Id, assignmentB.Id, assignmentC.Id };

                var oldAttempts = await db.ClassroomAssignmentAttempts
                    .Where(att => seededAssignmentIds.Contains(att.ClassroomAssignmentId))
                    .ToListAsync();
                db.ClassroomAssignmentAttempts.RemoveRange(oldAttempts);

                var oldStats = await db.ClassroomAssignmentQuestionStats
                    .Where(stat => seededAssignmentIds.Contains(stat.ClassroomAssignmentId))
                    .ToListAsync();
                db.ClassroomAssignmentQuestionStats.RemoveRange(oldStats);

                await db.SaveChangesAsync();
                Console.WriteLine("Old attempts/stats cleaned.");

                // H. Seed Attempts for Assignment B (Empirical Difficulty - 12 students submitted)
                Console.WriteLine("Seeding attempts for Assignment B (Empirical Difficulty)...");

                // Question correctness mapping: key is question index (0-9), value is hashset of student indexes (1-12) who answered correctly
                var correctStudentsMap = new Dictionary<int, HashSet<int>>
                {
                    { 0, new HashSet<int> { 1, 2, 3, 4, 5, 6, 7, 9, 10, 11, 12 } }, // Q1: 11/12 correct (student08 wrong)
                    { 1, new HashSet<int> { 1, 2, 3, 4, 5, 6, 7, 9, 11, 12 } },    // Q2: 10/12 correct (student08, student10 wrong)
                    { 2, new HashSet<int> { 1, 2, 3, 4, 5, 6, 9, 11, 12 } },       // Q3: 9/12 correct (student08, student10, student07 wrong)
                    { 3, new HashSet<int> { 1, 2, 3, 4, 9, 11, 12 } },             // Q4: 7/12 correct (1, 2, 3, 4, 9, 11, 12)
                    { 4, new HashSet<int> { 1, 2, 3, 5, 11, 12 } },                // Q5: 6/12 correct (1, 2, 3, 5, 11, 12)
                    { 5, new HashSet<int> { 1, 2, 3, 11, 12 } },                   // Q6: 5/12 correct (1, 2, 3, 11, 12)
                    { 6, new HashSet<int> { 1, 2, 3, 5 } },                        // Q7: 4/12 correct (1, 2, 3, 5)
                    { 7, new HashSet<int> { 1, 2, 5 } },                           // Q8: 3/12 correct (1, 2, 5)
                    { 8, new HashSet<int> { 1, 5 } },                              // Q9: 2/12 correct (1, 5)
                    { 9, new HashSet<int> { 1 } }                                  // Q10: 1/12 correct (1)
                };

                for (int sIdx = 1; sIdx <= 12; sIdx++)
                {
                    var student = students[sIdx - 1];
                    var attempt = new ClassroomAssignmentAttempt
                    {
                        ClassroomAssignmentId = assignmentB.Id,
                        UserId = student.Id,
                        StartedAt = DateTime.UtcNow.AddMinutes(-30),
                        SubmittedAt = DateTime.UtcNow.AddMinutes(-10),
                        Status = ClassroomAttemptStatus.Submitted,
                        DurationSeconds = 1200,
                        AttemptNumber = 1,
                        RawScore = 0, // Will be calculated by CloseAssignmentAsync
                        PercentScore = 0 // Will be calculated by CloseAssignmentAsync
                    };
                    db.ClassroomAssignmentAttempts.Add(attempt);
                    await db.SaveChangesAsync();

                    // Seed answers
                    for (int qIdx = 0; qIdx < 10; qIdx++)
                    {
                        var isCorrect = correctStudentsMap[qIdx].Contains(sIdx);
                        var answer = new ClassroomAssignmentAnswer
                        {
                            AttemptId = attempt.Id,
                            QuestionId = questions[qIdx].Id,
                            SelectedAnswer = isCorrect ? "A" : "B",
                            IsCorrect = isCorrect,
                            PointEarned = isCorrect ? 1.00m : 0.00m,
                            TimeSpentSeconds = 120,
                            AnsweredAt = DateTime.UtcNow.AddMinutes(-20)
                        };
                        db.ClassroomAssignmentAnswers.Add(answer);
                    }
                    await db.SaveChangesAsync();
                }

                // Call ClassroomAssignmentService to Close Assignment B (triggers scoring calculations)
                Console.WriteLine("Closing Assignment B using ClassroomAssignmentService to run scoring calculations...");
                await assignmentService.CloseAssignmentAsync(assignmentB.Id, teacher.Id);
                Console.WriteLine("Assignment B closed successfully.");

                // I. Seed Attempts for Assignment A (Percent-based - 10 students with some doing 2 attempts)
                Console.WriteLine("Seeding attempts for Assignment A (Percent scoring)...");
                var assignmentAAttemptsConfig = new List<(int studentIdx, int attemptNum, int correctCount)>
                {
                    (1, 1, 10), // student01: 1 attempt, 10/10
                    (2, 1, 5),  // student02: 2 attempts, attempt 1: 5/10
                    (2, 2, 8),  // student02: attempt 2: 8/10
                    (3, 1, 7),  // student03: 1 attempt, 7/10
                    (4, 1, 6),  // student04: 1 attempt, 6/10
                    (5, 1, 4),  // student05: 1 attempt, 4/10
                    (6, 1, 2),  // student06: 2 attempts, attempt 1: 2/10
                    (6, 2, 5),  // student06: attempt 2: 5/10
                    (7, 1, 3),  // student07: 1 attempt, 3/10
                    (8, 1, 1),  // student08: 1 attempt, 1/10
                    (9, 1, 5),  // student09: 1 attempt, 5/10
                    (10, 1, 2)  // student10: 1 attempt, 2/10
                };

                foreach (var config in assignmentAAttemptsConfig)
                {
                    var student = students[config.studentIdx - 1];
                    var attempt = new ClassroomAssignmentAttempt
                    {
                        ClassroomAssignmentId = assignmentA.Id,
                        UserId = student.Id,
                        StartedAt = DateTime.UtcNow.AddHours(-2).AddMinutes(config.attemptNum * 10),
                        SubmittedAt = DateTime.UtcNow.AddHours(-2).AddMinutes(config.attemptNum * 10 + 15),
                        Status = ClassroomAttemptStatus.Submitted,
                        DurationSeconds = 900,
                        AttemptNumber = config.attemptNum,
                        RawScore = config.correctCount,
                        PercentScore = config.correctCount * 10.0m // 10 questions, each point weight 1. Max total is 10
                    };
                    db.ClassroomAssignmentAttempts.Add(attempt);
                    await db.SaveChangesAsync();

                    // Seed answers: first config.correctCount questions are correct (A), others incorrect (B)
                    for (int qIdx = 0; qIdx < 10; qIdx++)
                    {
                        var isCorrect = qIdx < config.correctCount;
                        var answer = new ClassroomAssignmentAnswer
                        {
                            AttemptId = attempt.Id,
                            QuestionId = questions[qIdx].Id,
                            SelectedAnswer = isCorrect ? "A" : "B",
                            IsCorrect = isCorrect,
                            PointEarned = isCorrect ? 1.00m : 0.00m,
                            TimeSpentSeconds = 90,
                            AnsweredAt = DateTime.UtcNow.AddHours(-2).AddMinutes(config.attemptNum * 10 + 5)
                        };
                        db.ClassroomAssignmentAnswers.Add(answer);
                    }
                    await db.SaveChangesAsync();
                }
                Console.WriteLine("Seeded Assignment A attempts.");

                // J. Seed Attempts for Assignment C (Open - mixture of Submitted, InProgress, and NotStarted)
                Console.WriteLine("Seeding attempts for Assignment C (Open)...");
                // student01: Submitted (9/10)
                var attC1 = new ClassroomAssignmentAttempt
                {
                    ClassroomAssignmentId = assignmentC.Id,
                    UserId = students[0].Id,
                    StartedAt = DateTime.UtcNow.AddMinutes(-40),
                    SubmittedAt = DateTime.UtcNow.AddMinutes(-20),
                    Status = ClassroomAttemptStatus.Submitted,
                    DurationSeconds = 1200,
                    AttemptNumber = 1,
                    RawScore = 9m,
                    PercentScore = 90.0m
                };
                db.ClassroomAssignmentAttempts.Add(attC1);
                await db.SaveChangesAsync();
                for (int qIdx = 0; qIdx < 10; qIdx++)
                {
                    var isCorrect = qIdx < 9;
                    db.ClassroomAssignmentAnswers.Add(new ClassroomAssignmentAnswer
                    {
                        AttemptId = attC1.Id,
                        QuestionId = questions[qIdx].Id,
                        SelectedAnswer = isCorrect ? "A" : "B",
                        IsCorrect = isCorrect,
                        PointEarned = isCorrect ? 1.00m : 0.00m,
                        TimeSpentSeconds = 100,
                        AnsweredAt = DateTime.UtcNow.AddMinutes(-30)
                    });
                }

                // student02: Submitted (7/10)
                var attC2 = new ClassroomAssignmentAttempt
                {
                    ClassroomAssignmentId = assignmentC.Id,
                    UserId = students[1].Id,
                    StartedAt = DateTime.UtcNow.AddMinutes(-35),
                    SubmittedAt = DateTime.UtcNow.AddMinutes(-15),
                    Status = ClassroomAttemptStatus.Submitted,
                    DurationSeconds = 1200,
                    AttemptNumber = 1,
                    RawScore = 7m,
                    PercentScore = 70.0m
                };
                db.ClassroomAssignmentAttempts.Add(attC2);
                await db.SaveChangesAsync();
                for (int qIdx = 0; qIdx < 10; qIdx++)
                {
                    var isCorrect = qIdx < 7;
                    db.ClassroomAssignmentAnswers.Add(new ClassroomAssignmentAnswer
                    {
                        AttemptId = attC2.Id,
                        QuestionId = questions[qIdx].Id,
                        SelectedAnswer = isCorrect ? "A" : "B",
                        IsCorrect = isCorrect,
                        PointEarned = isCorrect ? 1.00m : 0.00m,
                        TimeSpentSeconds = 100,
                        AnsweredAt = DateTime.UtcNow.AddMinutes(-25)
                    });
                }

                // student03: InProgress, answered 3 questions (correctly)
                var attC3 = new ClassroomAssignmentAttempt
                {
                    ClassroomAssignmentId = assignmentC.Id,
                    UserId = students[2].Id,
                    StartedAt = DateTime.UtcNow.AddMinutes(-10),
                    SubmittedAt = null,
                    Status = ClassroomAttemptStatus.InProgress,
                    AttemptNumber = 1,
                    RawScore = 0,
                    PercentScore = 0
                };
                db.ClassroomAssignmentAttempts.Add(attC3);
                await db.SaveChangesAsync();
                for (int qIdx = 0; qIdx < 3; qIdx++)
                {
                    db.ClassroomAssignmentAnswers.Add(new ClassroomAssignmentAnswer
                    {
                        AttemptId = attC3.Id,
                        QuestionId = questions[qIdx].Id,
                        SelectedAnswer = "A",
                        IsCorrect = true,
                        PointEarned = 1m,
                        TimeSpentSeconds = 120,
                        AnsweredAt = DateTime.UtcNow.AddMinutes(-5)
                    });
                }

                // student04: InProgress, answered 2 questions (1 correct, 1 wrong)
                var attC4 = new ClassroomAssignmentAttempt
                {
                    ClassroomAssignmentId = assignmentC.Id,
                    UserId = students[3].Id,
                    StartedAt = DateTime.UtcNow.AddMinutes(-8),
                    SubmittedAt = null,
                    Status = ClassroomAttemptStatus.InProgress,
                    AttemptNumber = 1,
                    RawScore = 0,
                    PercentScore = 0
                };
                db.ClassroomAssignmentAttempts.Add(attC4);
                await db.SaveChangesAsync();
                db.ClassroomAssignmentAnswers.Add(new ClassroomAssignmentAnswer
                {
                    AttemptId = attC4.Id,
                    QuestionId = questions[0].Id,
                    SelectedAnswer = "A",
                    IsCorrect = true,
                    PointEarned = 1m,
                    TimeSpentSeconds = 90,
                    AnsweredAt = DateTime.UtcNow.AddMinutes(-6)
                });
                db.ClassroomAssignmentAnswers.Add(new ClassroomAssignmentAnswer
                {
                    AttemptId = attC4.Id,
                    QuestionId = questions[1].Id,
                    SelectedAnswer = "B",
                    IsCorrect = false,
                    PointEarned = 0m,
                    TimeSpentSeconds = 90,
                    AnsweredAt = DateTime.UtcNow.AddMinutes(-4)
                });

                await db.SaveChangesAsync();
                Console.WriteLine("Seeded Assignment C attempts.");

                // student05, student06 have not started (no attempts created).

                Console.WriteLine("\nSeed completed successfully!");

                // K. Summarize Results & Print to Console
                Console.WriteLine("\n====================================================");
                Console.WriteLine("SUMMARY REPORT:");
                Console.WriteLine("====================================================");
                Console.WriteLine($"Teacher Account: {teacherEmail} / Password123! (ID: {teacher.Id})");
                Console.WriteLine("Student Accounts: student01@elearn.local -> student12@elearn.local / Password123!");
                Console.WriteLine($"Classroom ID: {classroom.Id}");
                Console.WriteLine($"Document ID: {document.Id}");
                Console.WriteLine($"Question Set ID: {questionSet.Id}");
                Console.WriteLine($"Assignment A (Percent) ID: {assignmentA.Id}");
                Console.WriteLine($"Assignment B (Empirical Difficulty - Closed) ID: {assignmentB.Id}");
                Console.WriteLine($"Assignment C (Open) ID: {assignmentC.Id}");
                Console.WriteLine($"Number of Students Registered: {students.Count}");

                var countA = await db.ClassroomAssignmentAttempts.CountAsync(att => att.ClassroomAssignmentId == assignmentA.Id);
                var countB = await db.ClassroomAssignmentAttempts.CountAsync(att => att.ClassroomAssignmentId == assignmentB.Id);
                var countC = await db.ClassroomAssignmentAttempts.CountAsync(att => att.ClassroomAssignmentId == assignmentC.Id);

                Console.WriteLine($"Attempts seeded for Assignment A: {countA}");
                Console.WriteLine($"Attempts seeded for Assignment B: {countB}");
                Console.WriteLine($"Attempts seeded for Assignment C: {countC}");

                var statsCount = await db.ClassroomAssignmentQuestionStats
                    .CountAsync(s => s.ClassroomAssignmentId == assignmentB.Id);
                Console.WriteLine($"Question stats generated for Assignment B: {statsCount}");

                // Top 3 students for Assignment B
                var topStudents = await db.ClassroomAssignmentAttempts
                    .Include(att => att.User)
                    .Where(att => att.ClassroomAssignmentId == assignmentB.Id && att.Status == ClassroomAttemptStatus.Submitted)
                    .OrderByDescending(att => att.PercentScore)
                    .Take(3)
                    .ToListAsync();

                Console.WriteLine("\nTop 3 Students for Assignment B (Empirical Mode):");
                int rank = 1;
                foreach (var top in topStudents)
                {
                    Console.WriteLine($"{rank}. Student: {top.User?.FullName} | RawScore: {top.RawScore:F2} | PercentScore: {top.PercentScore:F2}%");
                    rank++;
                }

                // Question weights for Assignment B
                var questionStats = await db.ClassroomAssignmentQuestionStats
                    .Include(s => s.Question)
                    .Where(s => s.ClassroomAssignmentId == assignmentB.Id)
                    .OrderBy(s => s.QuestionId)
                    .ToListAsync();

                Console.WriteLine("\nQuestion Weights and Statistics for Assignment B:");
                Console.WriteLine("---------------------------------------------------------------------------------------------");
                Console.WriteLine($"{"Q.ID",-6} | {"Correct",-7} | {"Total",-5} | {"CorrectRate",-11} | {"DifficultyWeight",-16} | {"Question Text Snippet"}");
                Console.WriteLine("---------------------------------------------------------------------------------------------");
                foreach (var qStat in questionStats)
                {
                    var textSnippet = qStat.Question?.QuestionText ?? "";
                    if (textSnippet.Length > 45) textSnippet = textSnippet.Substring(0, 42) + "...";

                    Console.WriteLine($"{qStat.QuestionId,-6} | {qStat.CorrectCount,-7} | {qStat.AnsweredCount,-5} | {qStat.SmoothedCorrectRate,-11:F4} | {qStat.DifficultyWeight,-16:F4} | {textSnippet}");
                }
                Console.WriteLine("---------------------------------------------------------------------------------------------");

            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"An error occurred during seeding: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
            }
        }
    }
}
