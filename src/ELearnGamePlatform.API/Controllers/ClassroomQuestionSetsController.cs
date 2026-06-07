using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ELearnGamePlatform.API.Controllers;

[ApiController]
[Authorize]
[Route("api/classroom-question-sets")]
public sealed class ClassroomQuestionSetsController : AuthenticatedControllerBase
{
    private readonly IClassroomQuestionSetService _questionSetService;

    public ClassroomQuestionSetsController(IClassroomQuestionSetService questionSetService)
    {
        _questionSetService = questionSetService;
    }

    [HttpPost("/api/classroom-workspaces/{classroomId:int}/question-sets")]
    public async Task<IActionResult> Create(
        int classroomId,
        [FromBody] CreateClassroomQuestionSetRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ApiBadRequest("request_required", "Request body is required.");
        }

        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var questionSet = await _questionSetService.CreateQuestionSetAsync(
                classroomId,
                CurrentUserId.Value,
                new CreateClassroomQuestionSetInput(request.Title, request.Description, request.DocumentId),
                cancellationToken);

            return Ok(MapQuestionSet(questionSet));
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_question_set_manage_required", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_question_set_invalid", ex.Message);
        }
    }

    [HttpGet("/api/classroom-workspaces/{classroomId:int}/question-sets")]
    public async Task<IActionResult> GetForClassroom(int classroomId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var questionSets = await _questionSetService.GetQuestionSetsForClassroomAsync(
                classroomId,
                CurrentUserId.Value,
                cancellationToken);

            return Ok(questionSets.Select(questionSet => MapQuestionSet(questionSet)));
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_question_set_view_required", ex.Message);
        }
    }

    [HttpGet("/api/classroom-workspaces/{classroomId:int}/available-questions")]
    public async Task<IActionResult> GetAvailableQuestions(
        int classroomId,
        [FromQuery] int? documentId,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var questions = await _questionSetService.GetAvailableQuestionsAsync(
                classroomId,
                CurrentUserId.Value,
                documentId,
                cancellationToken);

            return Ok(questions.Select(MapQuestion));
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_question_source_forbidden", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_question_source_invalid", ex.Message);
        }
    }

    [HttpGet("{questionSetId:int}")]
    public async Task<IActionResult> GetById(int questionSetId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        var questionSet = await _questionSetService.GetQuestionSetDetailAsync(
            questionSetId,
            CurrentUserId.Value,
            cancellationToken);
        if (questionSet == null)
        {
            return ApiNotFound("classroom_question_set_not_found", "Question set was not found or is not available to this user.");
        }

        return Ok(MapQuestionSet(questionSet, includeItems: true));
    }

    [HttpPut("{questionSetId:int}")]
    public async Task<IActionResult> Update(
        int questionSetId,
        [FromBody] UpdateClassroomQuestionSetRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ApiBadRequest("request_required", "Request body is required.");
        }

        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var questionSet = await _questionSetService.UpdateQuestionSetAsync(
                questionSetId,
                CurrentUserId.Value,
                new UpdateClassroomQuestionSetInput(request.Title, request.Description, request.DocumentId),
                cancellationToken);

            return Ok(MapQuestionSet(questionSet, includeItems: true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_question_set_manage_required", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_question_set_invalid", ex.Message);
        }
    }

    [HttpDelete("{questionSetId:int}")]
    public async Task<IActionResult> Delete(int questionSetId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            await _questionSetService.DeleteQuestionSetAsync(questionSetId, CurrentUserId.Value, cancellationToken);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_question_set_manage_required", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_question_set_invalid", ex.Message);
        }
    }

    [HttpPost("{questionSetId:int}/items")]
    public async Task<IActionResult> AddItem(
        int questionSetId,
        [FromBody] AddClassroomQuestionSetItemRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ApiBadRequest("request_required", "Request body is required.");
        }

        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var item = await _questionSetService.AddQuestionToSetAsync(
                questionSetId,
                CurrentUserId.Value,
                new AddClassroomQuestionSetItemInput(request.QuestionId, request.OrderIndex, request.PointWeight, request.SectionCode),
                cancellationToken);

            return Ok(MapItem(item));
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_question_set_manage_required", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_question_set_item_invalid", ex.Message);
        }
    }

    [HttpDelete("{questionSetId:int}/items/{itemId:int}")]
    public async Task<IActionResult> RemoveItem(int questionSetId, int itemId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var removed = await _questionSetService.RemoveQuestionFromSetAsync(
                questionSetId,
                itemId,
                CurrentUserId.Value,
                cancellationToken);
            if (!removed)
            {
                return ApiNotFound("classroom_question_set_item_not_found", "Question set item was not found.");
            }

            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_question_set_manage_required", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_question_set_invalid", ex.Message);
        }
    }

    [HttpPut("{questionSetId:int}/items/reorder")]
    public async Task<IActionResult> ReorderItems(
        int questionSetId,
        [FromBody] ReorderClassroomQuestionSetItemsRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ApiBadRequest("request_required", "Request body is required.");
        }

        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var questionSet = await _questionSetService.ReorderQuestionSetItemsAsync(
                questionSetId,
                CurrentUserId.Value,
                request.Items.Select(item => new ReorderClassroomQuestionSetItemInput(item.ItemId, item.OrderIndex)).ToList(),
                cancellationToken);

            return Ok(MapQuestionSet(questionSet, includeItems: true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_question_set_manage_required", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_question_set_item_invalid", ex.Message);
        }
    }

    [HttpPost("{questionSetId:int}/publish")]
    public async Task<IActionResult> Publish(int questionSetId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var questionSet = await _questionSetService.PublishQuestionSetAsync(questionSetId, CurrentUserId.Value, cancellationToken);
            return Ok(MapQuestionSet(questionSet, includeItems: true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_question_set_manage_required", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_question_set_invalid", ex.Message);
        }
    }

    [HttpPost("{questionSetId:int}/unpublish")]
    public async Task<IActionResult> Unpublish(int questionSetId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var questionSet = await _questionSetService.UnpublishQuestionSetAsync(questionSetId, CurrentUserId.Value, cancellationToken);
            return Ok(MapQuestionSet(questionSet, includeItems: true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_question_set_manage_required", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_question_set_invalid", ex.Message);
        }
    }

    private static object MapQuestionSet(ClassroomQuestionSet questionSet, bool includeItems = false)
    {
        var orderedItems = questionSet.Items
            .OrderBy(item => item.OrderIndex)
            .ThenBy(item => item.Id)
            .ToList();

        return new
        {
            id = questionSet.Id,
            classroomWorkspaceId = questionSet.ClassroomWorkspaceId,
            documentId = questionSet.DocumentId,
            title = questionSet.Title,
            description = questionSet.Description,
            createdByUserId = questionSet.CreatedByUserId,
            visibility = questionSet.Visibility.ToString(),
            createdAt = questionSet.CreatedAt,
            updatedAt = questionSet.UpdatedAt,
            itemCount = orderedItems.Count,
            totalPoints = orderedItems.Sum(item => item.PointWeight),
            items = includeItems ? orderedItems.Select(MapItem) : null
        };
    }

    private static object MapItem(ClassroomQuestionSetItem item)
    {
        return new
        {
            id = item.Id,
            classroomQuestionSetId = item.ClassroomQuestionSetId,
            questionId = item.QuestionId,
            orderIndex = item.OrderIndex,
            pointWeight = item.PointWeight,
            sectionCode = item.SectionCode,
            createdAt = item.CreatedAt,
            question = item.Question == null
                ? null
                : new
                {
                    id = item.Question.Id,
                    documentId = item.Question.DocumentId,
                    questionText = item.Question.QuestionText,
                    questionType = item.Question.QuestionType.ToString(),
                    correctAnswer = item.Question.CorrectAnswer,
                    difficulty = item.Question.Difficulty.ToString(),
                    topic = item.Question.Topic
                }
        };
    }

    private static object MapQuestion(Question question)
    {
        return new
        {
            id = question.Id,
            documentId = question.DocumentId,
            questionText = question.QuestionText,
            questionType = question.QuestionType.ToString(),
            correctAnswer = question.CorrectAnswer,
            explanation = question.Explanation,
            difficulty = question.Difficulty.ToString(),
            topic = question.Topic,
            createdAt = question.CreatedAt
        };
    }
}

public sealed class CreateClassroomQuestionSetRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? DocumentId { get; set; }
}

public sealed class UpdateClassroomQuestionSetRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? DocumentId { get; set; }
}

public sealed class AddClassroomQuestionSetItemRequest
{
    public int QuestionId { get; set; }
    public int? OrderIndex { get; set; }
    public double PointWeight { get; set; } = 1;
    public string? SectionCode { get; set; }
}

public sealed class ReorderClassroomQuestionSetItemsRequest
{
    public List<ReorderClassroomQuestionSetItemRequest> Items { get; set; } = new();
}

public sealed class ReorderClassroomQuestionSetItemRequest
{
    public int ItemId { get; set; }
    public int OrderIndex { get; set; }
}
