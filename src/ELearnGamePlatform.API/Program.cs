using System.Text.Json.Serialization;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Infrastructure.Configuration;
using ELearnGamePlatform.Infrastructure.Data;
using ELearnGamePlatform.Infrastructure.Repositories;
using ELearnGamePlatform.Infrastructure.Services;
using ELearnGamePlatform.API.Configuration;
using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Services.AI;
using ELearnGamePlatform.Services.DocumentProcessing;
using ELearnGamePlatform.Services.OCR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure PostgreSQL with EF Core
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// Configure Ollama
builder.Services.Configure<OllamaSettings>(
    builder.Configuration.GetSection("OllamaSettings"));
builder.Services.Configure<FileUploadSettings>(
    builder.Configuration.GetSection(FileUploadSettings.SectionName));

// Register HttpClient for Ollama
builder.Services.AddHttpClient<IOllamaService, OllamaService>();

// Register Repositories
builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
builder.Services.AddScoped<IQuestionRepository, QuestionRepository>();
builder.Services.AddScoped<IGameSessionRepository, GameSessionRepository>();

// Register Services
builder.Services.AddScoped<IOcrService, TesseractOcrService>();
builder.Services.AddScoped<IDocumentProcessor, PdfProcessor>();
builder.Services.AddScoped<IDocumentProcessor, DocxProcessor>();
builder.Services.AddScoped<IDocumentProcessor, ImageProcessor>();
builder.Services.AddScoped<IContentAnalyzer, ContentAnalyzerService>();
builder.Services.AddScoped<IQuestionGenerator, QuestionGeneratorService>();
builder.Services.AddSingleton<IQuestionGenerationJobStore, QuestionGenerationJobStore>();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp",
        builder => builder
            .WithOrigins("http://localhost:3000", "http://localhost:5173")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

var app = builder.Build();

// Run migrations automatically
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        dbContext.Database.Migrate();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating the database.");
    }
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowReactApp");
app.UseAuthorization();
app.MapControllers();

// Create uploads directory if it doesn't exist
var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
if (!Directory.Exists(uploadsPath))
{
    Directory.CreateDirectory(uploadsPath);
}

app.Run();
