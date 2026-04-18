using System.Text.Json.Serialization;
using System.Data;
using System.Data.Common;
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
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = ResolveContentRoot()
});
builder.WebHost.UseUrls("http://localhost:5001");
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

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
builder.Services.Configure<ImagePipelineSettings>(
    builder.Configuration.GetSection(ImagePipelineSettings.SectionName));

// Register HttpClient for Ollama
builder.Services.AddHttpClient<IOllamaService, OllamaService>();

// Register Repositories
builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
builder.Services.AddScoped<IFolderProjectRepository, FolderProjectRepository>();
builder.Services.AddScoped<IQuestionRepository, QuestionRepository>();
builder.Services.AddScoped<IGameSessionRepository, GameSessionRepository>();
builder.Services.AddScoped<ISlideDeckRepository, SlideDeckRepository>();

// Register Services
builder.Services.AddScoped<IOcrService, TesseractOcrService>();
builder.Services.AddScoped<IDocumentProcessor, PdfProcessor>();
builder.Services.AddScoped<IDocumentProcessor, DocxProcessor>();
builder.Services.AddScoped<IDocumentProcessor, ImageProcessor>();
builder.Services.AddScoped<IContentAnalyzer, ContentAnalyzerService>();
builder.Services.AddScoped<IQuestionGenerator, QuestionGeneratorService>();
builder.Services.AddScoped<ISlideGenerator, SlideGeneratorService>();
builder.Services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();
builder.Services.AddHttpClient<ISlideImageService, SlideImageService>(client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ELearnGamePlatform/1.0");
    client.Timeout = TimeSpan.FromSeconds(45);
});
builder.Services.AddSingleton<IDocumentProcessingJobStore, DocumentProcessingJobStore>();
builder.Services.AddSingleton<IQuestionGenerationJobStore, QuestionGenerationJobStore>();
builder.Services.AddSingleton<ISlideGenerationJobStore, SlideGenerationJobStore>();

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

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});
var app = builder.Build();

// Run migrations automatically
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var pendingMigrations = dbContext.Database.GetPendingMigrations().ToList();
        if (pendingMigrations.Count > 0)
        {
            logger.LogInformation(
                "Applying {Count} pending database migrations: {Migrations}",
                pendingMigrations.Count,
                string.Join(", ", pendingMigrations));
        }

        dbContext.Database.Migrate();
        ValidateCriticalSchema(dbContext);
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Database migration failed. The API cannot start with a schema mismatch.");
        throw;
    }
}

// Configure the HTTP request pipeline
//if (app.Environment.IsDevelopment())
//{
    app.UseSwagger();
    app.UseSwaggerUI();
//}

//app.UseHttpsRedirection();
app.UseCors("AllowReactApp");
app.UseCors("AllowAll");
var uploadsPath = Path.Combine(app.Environment.ContentRootPath, "uploads");
if (!Directory.Exists(uploadsPath))
{
    Directory.CreateDirectory(uploadsPath);
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});
app.UseAuthorization();
app.UseCors("AllowAll");
app.MapControllers();

app.Run();

static void ValidateCriticalSchema(ApplicationDbContext dbContext)
{
    using var connection = dbContext.Database.GetDbConnection();
    var shouldClose = connection.State != ConnectionState.Open;

    if (shouldClose)
    {
        connection.Open();
    }

    try
    {
        EnsureColumnExists(connection, "questions", "verifier_score");
        EnsureColumnExists(connection, "questions", "verifier_issues");
      //  EnsureColumnExists(connection, "slide_items", "verifier_score");
       // EnsureColumnExists(connection, "slide_items", "verifier_issues");
    }
    finally
    {
        if (shouldClose && connection.State == ConnectionState.Open)
        {
            connection.Close();
        }
    }
}

static void EnsureColumnExists(DbConnection connection, string tableName, string columnName)
{
    using var command = connection.CreateCommand();
    command.CommandText = @"
select 1
from information_schema.columns
where table_schema = 'public'
  and table_name = @tableName
  and column_name = @columnName
limit 1;";

    var tableParameter = command.CreateParameter();
    tableParameter.ParameterName = "@tableName";
    tableParameter.Value = tableName;
    command.Parameters.Add(tableParameter);

    var columnParameter = command.CreateParameter();
    columnParameter.ParameterName = "@columnName";
    columnParameter.Value = columnName;
    command.Parameters.Add(columnParameter);

    var exists = command.ExecuteScalar() != null;
    if (!exists)
    {
        throw new InvalidOperationException(
            $"Database schema mismatch: missing column public.{tableName}.{columnName}. Run migrations before starting the API.");
    }
}

static string ResolveContentRoot()
{
    var currentDirectory = Directory.GetCurrentDirectory();
    if (File.Exists(Path.Combine(currentDirectory, "appsettings.json")))
    {
        return currentDirectory;
    }

    var baseDirectory = AppContext.BaseDirectory;
    if (File.Exists(Path.Combine(baseDirectory, "appsettings.json")))
    {
        return baseDirectory;
    }

    var directory = new DirectoryInfo(baseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "appsettings.json")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return currentDirectory;
}
