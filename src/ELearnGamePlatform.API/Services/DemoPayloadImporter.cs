using System.Text.Json;
using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Enums;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Models;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ELearnGamePlatform.API.Services;

public class DemoPayloadImporter
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordService _passwordService;
    private readonly ILogger<DemoPayloadImporter> _logger;

    public DemoPayloadImporter(
        ApplicationDbContext context,
        IPasswordService passwordService,
        ILogger<DemoPayloadImporter> _logger)
    {
        _context = context;
        _passwordService = passwordService;
        this._logger = _logger;
    }

    public async Task<DemoImportResult> ImportAsync(string filePath, string? userParam, bool replace)
    {
        _logger.LogInformation("Starting import of DemoLearningPayload from file: {FilePath}, userParam: {UserParam}, replace: {Replace}", filePath, userParam, replace);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Markdown file not found at: {filePath}");
        }

        // 1. Resolve or create target user
        var user = await ResolveOrCreateUserAsync(userParam);
        _logger.LogInformation("Resolved target user: {Email} (Id: {Id})", user.Email, user.Id);

        // 2. Read and parse Markdown
        var markdownContent = await File.ReadAllTextAsync(filePath);
        var jsonContent = ExtractJsonFromMarkdown(markdownContent);
        
        var payload = JsonSerializer.Deserialize<DemoLearningPayload>(jsonContent)
            ?? throw new InvalidOperationException("Failed to deserialize JSON payload.");

        // 3. Validate payload
        ValidatePayload(payload);

        int? deletedOldDocId = null;
        
        // Use single database transaction for safety
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // 4. Handle replacement mechanism
            var existingDoc = await _context.Documents
                .FirstOrDefaultAsync(d => d.UploadedBy == user.Id.ToString() && d.FileName == payload.DocumentAnalysis.Title);

            if (existingDoc != null)
            {
                if (!replace)
                {
                    _logger.LogInformation("Document with title '{Title}' already exists for user '{User}'. Skipping import.", payload.DocumentAnalysis.Title, user.Email);
                    return new DemoImportResult
                    {
                        UserId = user.Id.ToString(),
                        DocumentTitle = payload.DocumentAnalysis.Title,
                        Message = "Document already exists. Import skipped (replace=false)."
                    };
                }

                _logger.LogInformation("Document with title '{Title}' already exists. Deleting old document and related data.", payload.DocumentAnalysis.Title);
                deletedOldDocId = existingDoc.Id;
                
                var oldWorkspaceId = existingDoc.FolderProjectId;
                if (oldWorkspaceId.HasValue)
                {
                    var oldWorkspace = await _context.FolderProjects
                        .Include(w => w.Documents)
                        .Include(w => w.SlideDecks)
                        .FirstOrDefaultAsync(w => w.Id == oldWorkspaceId.Value);

                    if (oldWorkspace != null)
                    {
                        // Clean up all documents inside this workspace
                        var docIds = oldWorkspace.Documents.Select(d => d.Id).ToList();
                        foreach (var docId in docIds)
                        {
                            await CleanUpDocumentDataAsync(docId);
                        }

                        // Clean up any remaining slide decks in the workspace
                        foreach (var deck in oldWorkspace.SlideDecks)
                        {
                            var slideItems = await _context.SlideItems
                                .Where(item => item.SlideDeckId == deck.Id)
                                .ToListAsync();
                            _context.SlideItems.RemoveRange(slideItems);
                        }
                        _context.SlideDecks.RemoveRange(oldWorkspace.SlideDecks);

                        _context.FolderProjects.Remove(oldWorkspace);
                        await _context.SaveChangesAsync();
                        _logger.LogInformation("Deleted old workspace associated with document: {WorkspaceId}", oldWorkspaceId.Value);
                    }
                    else
                    {
                        await CleanUpDocumentDataAsync(existingDoc.Id);
                    }
                }
                else
                {
                    await CleanUpDocumentDataAsync(existingDoc.Id);
                }
            }

            // Create new workspace for this document
            var workspace = new FolderProject
            {
                Name = payload.DocumentAnalysis.Title,
                Description = $"Workspace for demo topic: {payload.DocumentAnalysis.Title}",
                UploadedBy = user.Id.ToString(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.FolderProjects.Add(workspace);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Created new workspace: {WorkspaceName} (Id: {Id})", workspace.Name, workspace.Id);

            // 5. Map and Save Document
            var document = new Document
            {
                FileName = payload.DocumentAnalysis.Title,
                FileType = "PDF",
                FilePath = $"/uploads/demo_{Guid.NewGuid()}.pdf",
                FileSize = 1024,
                Status = DocumentStatus.Completed,
                UploadedBy = user.Id.ToString(),
                FolderProjectId = workspace.Id, // Link to the new workspace!
                Summary = payload.DocumentAnalysis.Summary,
                Language = payload.DocumentAnalysis.Language,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Combine text elements to make extracted text non-empty
            var extractedTextParts = new List<string>
            {
                payload.DocumentAnalysis.Summary,
                string.Join("\n", payload.DocumentAnalysis.KeyPoints)
            };
            extractedTextParts.AddRange(payload.SlideDeck.Slides.Select(s => s.EvidenceFromText).Where(t => !string.IsNullOrEmpty(t)));
            document.ExtractedText = string.Join("\n\n", extractedTextParts);

            document.SetMainTopics(payload.DocumentAnalysis.MainTopics);
            document.SetKeyPoints(payload.DocumentAnalysis.KeyPoints);
            
            var processingMetadata = new DocumentProcessingMetadata
            {
                DocumentType = payload.DocumentAnalysis.DocumentType,
                Language = payload.DocumentAnalysis.Language,
                Title = payload.DocumentAnalysis.Title
            };
            document.SetProcessingMetadata(processingMetadata);

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Created new document: {FileName} (Id: {Id})", document.FileName, document.Id);

            // 6. Map and Save Questions
            var questionsImported = 0;
            foreach (var q in payload.Questions)
            {
                var question = new Question
                {
                    DocumentId = document.Id,
                    QuestionText = q.QuestionText,
                    QuestionType = Enum.Parse<QuestionType>(q.QuestionType, ignoreCase: true),
                    CorrectAnswer = q.CorrectAnswer,
                    Explanation = q.Explanation,
                    Difficulty = Enum.Parse<DifficultyLevel>(q.Difficulty, ignoreCase: true),
                    Topic = q.Topic,
                    CreatedAt = DateTime.UtcNow
                };

                if (q.Options != null && q.Options.Count > 0)
                {
                    var options = q.Options.Select(o => new QuestionOption
                    {
                        Key = o.Key,
                        Text = o.Text,
                        IsCorrect = o.IsCorrect
                    }).ToList();
                    question.SetOptions(options);
                }

                _context.Questions.Add(question);
                questionsImported++;
            }
            await _context.SaveChangesAsync();
            _logger.LogInformation("Imported {Count} questions.", questionsImported);

            // 7. Map and Save SlideDeck
            var slideDeck = new SlideDeck
            {
                DocumentId = document.Id,
                FolderProjectId = workspace.Id, // Link to workspace!
                Title = payload.SlideDeck.Title,
                Subtitle = payload.SlideDeck.Subtitle,
                ThemeKey = payload.SlideDeck.ThemeKey,
                Status = SlideDeckStatus.Completed,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.SlideDecks.Add(slideDeck);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Created new slide deck: {Title} (Id: {Id})", slideDeck.Title, slideDeck.Id);

            // 8. Map and Save SlideItems
            var slidesImported = 0;
            foreach (var s in payload.SlideDeck.Slides)
            {
                var slideItem = new SlideItem
                {
                    SlideDeckId = slideDeck.Id,
                    SlideIndex = s.SlideIndex,
                    SlideType = Enum.Parse<SlideItemType>(s.SlideType, ignoreCase: true),
                    Status = SlideItemStatus.Completed,
                    Heading = s.Heading,
                    Subheading = s.Subheading,
                    Goal = s.Goal,
                    KeyMessage = s.KeyMessage,
                    EvidenceFromText = s.EvidenceFromText,
                    SpeakerNotes = s.SpeakerNotes,
                    AccentTone = s.AccentTone,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                if (s.ImagePlan.HasValue)
                {
                    slideItem.ImagePlanJson = s.ImagePlan.Value.GetRawText();
                }

                slideItem.SetBodyBlocks(s.Body);
                slideItem.SetEditorState(slideItem.BuildDefaultEditorState());

                _context.SlideItems.Add(slideItem);
                slidesImported++;
            }
            await _context.SaveChangesAsync();
            _logger.LogInformation("Imported {Count} slides.", slidesImported);

            await transaction.CommitAsync();

            _logger.LogInformation("Import completed successfully.");
            return new DemoImportResult
            {
                UserId = user.Id.ToString(),
                DocumentId = document.Id,
                DocumentTitle = document.FileName,
                QuestionsImported = questionsImported,
                SlideDeckId = slideDeck.Id,
                SlidesImported = slidesImported,
                ReplaceMode = replace,
                DeletedOldDocumentId = deletedOldDocId,
                Message = "Import completed successfully."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during import transaction. Rolling back.");
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task<AppUser> ResolveOrCreateUserAsync(string? userParam)
    {
        if (!string.IsNullOrWhiteSpace(userParam))
        {
            if (int.TryParse(userParam, out var userId))
            {
                var userById = await _context.AppUsers.FirstOrDefaultAsync(u => u.Id == userId);
                if (userById != null) return userById;
            }

            var normalizedEmail = userParam.Trim().ToLowerInvariant();
            var userByEmail = await _context.AppUsers.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
            if (userByEmail != null) return userByEmail;

            throw new InvalidOperationException($"User specified by '{userParam}' was not found in database.");
        }

        // Default: teacher.demo@elearn.local
        var defaultEmail = "teacher.demo@elearn.local";
        var defaultUser = await _context.AppUsers.FirstOrDefaultAsync(u => u.Email == defaultEmail);
        if (defaultUser != null)
        {
            return defaultUser;
        }

        // Create default user
        _logger.LogInformation("Default user '{Email}' not found. Automatically creating.", defaultEmail);
        var newUser = new AppUser
        {
            FullName = "Demo Teacher",
            Email = defaultEmail,
            PasswordHash = string.Empty,
            Role = UserRole.Instructor,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        newUser.PasswordHash = _passwordService.HashPassword(newUser, "Password123!");

        _context.AppUsers.Add(newUser);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Successfully created default user: {Email} (Id: {Id})", newUser.Email, newUser.Id);

        return newUser;
    }

    private string ExtractJsonFromMarkdown(string markdown)
    {
        var match = Regex.Match(markdown, @"```json\s*(.*?)\s*```", RegexOptions.Singleline);
        if (!match.Success)
        {
            throw new InvalidOperationException("Could not find any JSON code block (```json ... ```) in the Markdown file.");
        }
        return match.Groups[1].Value;
    }

    private void ValidatePayload(DemoLearningPayload payload)
    {
        if (payload.DocumentAnalysis == null)
            throw new ArgumentException("Payload is missing 'document_analysis'.");
        if (string.IsNullOrWhiteSpace(payload.DocumentAnalysis.Title))
            throw new ArgumentException("Document title is empty.");

        if (payload.Questions == null || payload.Questions.Count == 0)
            throw new ArgumentException("Payload is missing 'questions' or has no questions.");

        if (payload.SlideDeck == null)
            throw new ArgumentException("Payload is missing 'slide_deck'.");
        if (payload.SlideDeck.Slides == null || payload.SlideDeck.Slides.Count == 0)
            throw new ArgumentException("Slide deck is missing 'slides' or has no slides.");

        // Validate Questions
        var validQuestionTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MultipleChoice", "TrueFalse", "ShortAnswer", "FillInTheBlank" };
        var validDifficulties = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Easy", "Medium", "Hard" };

        foreach (var q in payload.Questions)
        {
            if (string.IsNullOrWhiteSpace(q.QuestionText))
                throw new ArgumentException("Question text cannot be empty.");
            if (!validQuestionTypes.Contains(q.QuestionType))
                throw new ArgumentException($"Invalid question_type '{q.QuestionType}'. Must be one of: MultipleChoice, TrueFalse, ShortAnswer, FillInTheBlank.");
            if (!validDifficulties.Contains(q.Difficulty))
                throw new ArgumentException($"Invalid difficulty '{q.Difficulty}'. Must be one of: Easy, Medium, Hard.");
        }

        // Validate Slides
        var validSlideTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Title", "SectionDivider", "Content", "Quote", "Highlight", "Stat" };
        var validThemeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "editorial-sunrise", "midnight", "ocean", "forest", "minimal" };
        var validAccentTones = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "emerald", "amber", "rose", "indigo", "slate" };

        if (!validThemeKeys.Contains(payload.SlideDeck.ThemeKey))
            throw new ArgumentException($"Invalid theme_key '{payload.SlideDeck.ThemeKey}'. Must be one of: editorial-sunrise, midnight, ocean, forest, minimal.");

        foreach (var s in payload.SlideDeck.Slides)
        {
            if (s.SlideIndex <= 0)
                throw new ArgumentException("slide_index must be greater than 0.");
            if (!validSlideTypes.Contains(s.SlideType))
                throw new ArgumentException($"Invalid slide_type '{s.SlideType}' at index {s.SlideIndex}. Must be one of: Title, SectionDivider, Content, Quote, Highlight, Stat.");
            if (!string.IsNullOrWhiteSpace(s.AccentTone) && !validAccentTones.Contains(s.AccentTone))
                throw new ArgumentException($"Invalid accent_tone '{s.AccentTone}' at index {s.SlideIndex}. Must be one of: emerald, amber, rose, indigo, slate.");
        }
    }

    private async Task CleanUpDocumentDataAsync(int documentId)
    {
        // Find question IDs
        var questionIds = await _context.Questions
            .Where(q => q.DocumentId == documentId)
            .Select(q => q.Id)
            .ToListAsync();

        // 1. ClassroomQuestionSetItem
        if (questionIds.Count > 0)
        {
            var classroomQuestionSetItems = await _context.ClassroomQuestionSetItems
                .Where(item => questionIds.Contains(item.QuestionId))
                .ToListAsync();
            _context.ClassroomQuestionSetItems.RemoveRange(classroomQuestionSetItems);
        }

        // 2. ClassroomAssignmentAnswer
        if (questionIds.Count > 0)
        {
            var classroomAssignmentAnswers = await _context.ClassroomAssignmentAnswers
                .Where(answer => questionIds.Contains(answer.QuestionId))
                .ToListAsync();
            _context.ClassroomAssignmentAnswers.RemoveRange(classroomAssignmentAnswers);
        }

        // 2b. ClassroomAssignmentQuestionStat
        if (questionIds.Count > 0)
        {
            var classroomAssignmentQuestionStats = await _context.ClassroomAssignmentQuestionStats
                .Where(stat => questionIds.Contains(stat.QuestionId))
                .ToListAsync();
            _context.ClassroomAssignmentQuestionStats.RemoveRange(classroomAssignmentQuestionStats);
        }

        // 3. LearningAttempt
        var learningAttempts = await _context.LearningAttempts
            .Where(la => la.DocumentId == documentId || questionIds.Contains(la.QuestionId))
            .ToListAsync();
        _context.LearningAttempts.RemoveRange(learningAttempts);

        // 4. LearningProgress
        var learningProgresses = await _context.LearningProgresses
            .Where(lp => lp.DocumentId == documentId || questionIds.Contains(lp.QuestionId))
            .ToListAsync();
        _context.LearningProgresses.RemoveRange(learningProgresses);

        // 4b. LearningTestResult
        var learningTestResults = await _context.LearningTestResults
            .Where(r => r.DocumentId == documentId)
            .ToListAsync();
        _context.LearningTestResults.RemoveRange(learningTestResults);

        // 5. GameSession
        var gameSessions = await _context.GameSessions
            .Where(gs => gs.DocumentId == documentId)
            .ToListAsync();
        _context.GameSessions.RemoveRange(gameSessions);

        // 6. QuestionGenerationRun
        var questionGenRuns = await _context.QuestionGenerationRuns
            .Where(run => run.DocumentId == documentId)
            .ToListAsync();
        _context.QuestionGenerationRuns.RemoveRange(questionGenRuns);

        // 7. QuestionSourceUnit
        var questionSourceUnits = await _context.QuestionSourceUnits
            .Where(unit => unit.DocumentId == documentId)
            .ToListAsync();
        _context.QuestionSourceUnits.RemoveRange(questionSourceUnits);

        // 8. QuestionDraft (Cascade will delete QuestionReviewEvents)
        var questionDrafts = await _context.QuestionDrafts
            .Where(draft => draft.DocumentId == documentId)
            .ToListAsync();
        _context.QuestionDrafts.RemoveRange(questionDrafts);

        // 9. DocumentUnderstandingRun
        var understandingRuns = await _context.DocumentUnderstandingRuns
            .Where(run => run.DocumentId == documentId)
            .ToListAsync();
        _context.DocumentUnderstandingRuns.RemoveRange(understandingRuns);

        // 10. SlideItem & SlideDeck
        var slideDecks = await _context.SlideDecks
            .Where(sd => sd.DocumentId == documentId)
            .ToListAsync();
        
        foreach (var deck in slideDecks)
        {
            var slideItems = await _context.SlideItems
                .Where(item => item.SlideDeckId == deck.Id)
                .ToListAsync();
            _context.SlideItems.RemoveRange(slideItems);
        }
        _context.SlideDecks.RemoveRange(slideDecks);

        // Save dependencies before deleting parent Questions and Documents
        await _context.SaveChangesAsync();
        _logger.LogInformation("Cleaned up child dependency tables for document {DocumentId}.", documentId);

        // 11. Question
        if (questionIds.Count > 0)
        {
            var questions = await _context.Questions
                .Where(q => questionIds.Contains(q.Id))
                .ToListAsync();
            _context.Questions.RemoveRange(questions);
        }

        // 12. Document
        var document = await _context.Documents.FindAsync(documentId);
        if (document != null)
        {
            _context.Documents.Remove(document);
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Cleaned up all existing data for document {DocumentId} successfully.", documentId);
    }
}

public class DemoImportResult
{
    public string UserId { get; set; } = string.Empty;
    public int? DocumentId { get; set; }
    public string DocumentTitle { get; set; } = string.Empty;
    public int QuestionsImported { get; set; }
    public int? SlideDeckId { get; set; }
    public int SlidesImported { get; set; }
    public bool ReplaceMode { get; set; }
    public int? DeletedOldDocumentId { get; set; }
    public string Message { get; set; } = string.Empty;
}
