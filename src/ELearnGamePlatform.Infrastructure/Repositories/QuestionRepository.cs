using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ELearnGamePlatform.Infrastructure.Repositories;

public class QuestionRepository : IQuestionRepository
{
    private readonly ApplicationDbContext _context;

    public QuestionRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Question> CreateAsync(Question question)
    {
        _context.Questions.Add(question);
        await _context.SaveChangesAsync();
        return question;
    }

    public async Task<IEnumerable<Question>> ReplaceByDocumentIdAsync(int documentId, IEnumerable<Question> questions)
    {
        var questionList = questions.ToList();

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var existingQuestions = await _context.Questions
            .Where(q => q.DocumentId == documentId)
            .ToListAsync();

        _context.Questions.RemoveRange(existingQuestions);

        if (questionList.Any())
        {
            _context.Questions.AddRange(questionList);
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return questionList;
    }

    public async Task<Question?> GetByIdAsync(int id)
    {
        return await _context.Questions
            .Include(q => q.Document)
            .FirstOrDefaultAsync(q => q.Id == id);
    }

    public async Task<IEnumerable<Question>> GetByDocumentIdAsync(int documentId)
    {
        return await _context.Questions
            .Where(q => q.DocumentId == documentId)
            .ToListAsync();
    }

    public async Task<IEnumerable<Question>> GetByDocumentIdAndTypeAsync(int documentId, QuestionType type)
    {
        return await _context.Questions
            .Where(q => q.DocumentId == documentId && q.QuestionType == type)
            .ToListAsync();
    }

    public async Task<bool> UpdateAsync(int id, Question question)
    {
        var existing = await _context.Questions.FindAsync(id);
        if (existing == null)
            return false;

        _context.Entry(existing).CurrentValues.SetValues(question);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var question = await _context.Questions.FindAsync(id);
        if (question == null)
            return false;

        _context.Questions.Remove(question);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteByDocumentIdAsync(int documentId)
    {
        var questions = await _context.Questions
            .Where(q => q.DocumentId == documentId)
            .ToListAsync();

        _context.Questions.RemoveRange(questions);
        await _context.SaveChangesAsync();
        return true;
    }
}
