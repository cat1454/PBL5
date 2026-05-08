using System.Text;
using System.Text.Json.Serialization;
using System.Data;
using System.Data.Common;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Enums;
using ELearnGamePlatform.Core.Configuration;
using ELearnGamePlatform.Infrastructure.Configuration;
using ELearnGamePlatform.Infrastructure.Data;
using ELearnGamePlatform.Infrastructure.Repositories;
using ELearnGamePlatform.Infrastructure.Services;
using ELearnGamePlatform.API.Configuration;
using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Services.AI;
using ELearnGamePlatform.Services.DocumentProcessing;
using ELearnGamePlatform.Services.OCR;
using ELearnGamePlatform.Services.Slides;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = ResolveContentRoot()
});
builder.WebHost.UseUrls("http://localhost:5000");
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
builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.Configure<AdminSeedSettings>(
    builder.Configuration.GetSection(AdminSeedSettings.SectionName));
builder.Services.Configure<LocalLlmSettings>(
    builder.Configuration.GetSection(LocalLlmSettings.SectionName));

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? throw new InvalidOperationException("JwtSettings configuration is required.");
if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey))
{
    throw new InvalidOperationException("JwtSettings.SecretKey must be configured.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();

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
builder.Services.AddScoped<ITokenEstimator, TokenEstimator>();
builder.Services.AddScoped<IDocumentInputQualityGate, DocumentInputQualityGate>();
builder.Services.AddScoped<ITokenBudgetPlanner, TokenBudgetPlanner>();
builder.Services.AddScoped<IPromptAssembler, PromptAssembler>();
builder.Services.AddScoped<IQuestionGenerator, QuestionGeneratorService>();
builder.Services.AddScoped<ISlideGenerator, SlideGeneratorService>();
builder.Services.AddScoped<ISlideExportService, SlideExportService>();
builder.Services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();
builder.Services.AddScoped<IWorkspaceService, WorkspaceService>();
builder.Services.AddScoped<IPasswordService, PasswordService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<ILearningProgressService, LearningProgressService>();
builder.Services.AddScoped<IQuestionMetricsService, QuestionMetricsService>();
builder.Services.AddHttpClient<ISlideImageService, SlideImageService>(client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ELearnGamePlatform/1.0");
    client.Timeout = TimeSpan.FromSeconds(45);
});
builder.Services.AddSingleton<IDocumentProcessingJobStore, DocumentProcessingJobStore>();
builder.Services.AddSingleton<IQuestionGenerationJobStore, QuestionGenerationJobStore>();
builder.Services.AddSingleton<ISlideGenerationJobStore, SlideGenerationJobStore>();

// Configure CORS
// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy
            .WithOrigins("http://localhost:3000", "http://localhost:5173")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
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
        await SeedAdminUserAsync(scope.ServiceProvider, dbContext, logger);
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
app.UseAuthentication();
app.UseAuthorization();
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

    EnsureColumnExists(connection, "documents", "processed_metadata");

    EnsureColumnExists(connection, "slide_items", "verifier_score");
    EnsureColumnExists(connection, "slide_items", "verifier_issues");
    EnsureColumnExists(connection, "slide_items", "key_message");
    EnsureColumnExists(connection, "slide_items", "evidence_from_text");
    EnsureColumnExists(connection, "slide_items", "evidence_debug");

    EnsureColumnExists(connection, "slide_items", "image_candidates");
    EnsureColumnExists(connection, "slide_items", "image_plan");
    EnsureColumnExists(connection, "slide_items", "editor_state");
    EnsureColumnExists(connection, "slide_items", "selected_image_key");
    }
    finally
    {
        if (shouldClose && connection.State == ConnectionState.Open)
        {
            connection.Close();
        }
    }
}

static async Task SeedAdminUserAsync(
    IServiceProvider serviceProvider,
    ApplicationDbContext dbContext,
    ILogger logger)
{
    var settings = serviceProvider.GetRequiredService<IOptions<AdminSeedSettings>>().Value;
    if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Email) || string.IsNullOrWhiteSpace(settings.Password))
    {
        return;
    }

    var normalizedEmail = settings.Email.Trim().ToLowerInvariant();
    var existingAdmin = await dbContext.AppUsers.FirstOrDefaultAsync(user => user.Email == normalizedEmail);
    if (existingAdmin != null)
    {
        if (existingAdmin.Role != UserRole.Admin || !existingAdmin.IsActive)
        {
            existingAdmin.Role = UserRole.Admin;
            existingAdmin.IsActive = true;
            existingAdmin.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();
        }

        return;
    }

    var passwordService = serviceProvider.GetRequiredService<IPasswordService>();
    var admin = new AppUser
    {
        FullName = string.IsNullOrWhiteSpace(settings.FullName) ? "System Admin" : settings.FullName.Trim(),
        Email = normalizedEmail,
        PasswordHash = string.Empty,
        Role = UserRole.Admin,
        IsActive = true,
        CreatedAt = DateTime.UtcNow
    };
    admin.PasswordHash = passwordService.HashPassword(admin, settings.Password);

    dbContext.AppUsers.Add(admin);
    await dbContext.SaveChangesAsync();
    logger.LogInformation("Seeded admin user {Email}", normalizedEmail);
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
