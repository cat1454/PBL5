# Hướng dẫn Development và Best Practices

## Code Structure

### Naming Conventions

#### C# (.NET)
- **Classes/Interfaces**: PascalCase (e.g., `DocumentProcessor`, `IContentAnalyzer`)
- **Methods**: PascalCase (e.g., `ExtractTextAsync()`)
- **Variables/Parameters**: camelCase (e.g., `documentId`, `filePath`)
- **Constants**: UPPER_CASE (e.g., `MAX_FILE_SIZE`)
- **Private fields**: _camelCase (e.g., `_logger`, `_repository`)

#### JavaScript/React
- **Components**: PascalCase (e.g., `DocumentUpload`, `QuizGame`)
- **Functions/Variables**: camelCase (e.g., `loadDocuments`, `currentIndex`)
- **Constants**: UPPER_CASE (e.g., `API_BASE_URL`)

### Async/Await Pattern

Luôn sử dụng async/await cho I/O operations:

```csharp
// ✅ Good
public async Task<Document> GetDocumentAsync(string id)
{
    return await _repository.GetByIdAsync(id);
}

// ❌ Bad
public Document GetDocument(string id)
{
    return _repository.GetByIdAsync(id).Result; // Blocking!
}
```

### Error Handling

```csharp
// API Controller
try
{
    var result = await _service.ProcessAsync(data);
    return Ok(result);
}
catch (NotFoundException ex)
{
    _logger.LogWarning(ex, "Resource not found: {Id}", id);
    return NotFound(ex.Message);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Unexpected error");
    return StatusCode(500, "Internal server error");
}
```

### Dependency Injection

Register services trong `Program.cs`:

```csharp
// Singleton - Shared across all requests
builder.Services.AddSingleton<IDocumentRepository, DocumentRepository>();

// Scoped - One instance per request
builder.Services.AddScoped<IContentAnalyzer, ContentAnalyzerService>();

// Transient - New instance every time
builder.Services.AddTransient<IDocumentProcessor, PdfProcessor>();
```

## Testing

### Unit Tests

Tạo thư mục `tests/` và thêm test projects:

```powershell
cd H:\PBL5
mkdir tests
cd tests

# Create test projects
dotnet new xunit -n ELearnGamePlatform.Services.Tests
dotnet new xunit -n ELearnGamePlatform.API.Tests

# Add to solution
dotnet sln ../ELearnGamePlatform.sln add ELearnGamePlatform.Services.Tests/ELearnGamePlatform.Services.Tests.csproj
```

Example test:

```csharp
public class ContentAnalyzerServiceTests
{
    [Fact]
    public async Task AnalyzeContentAsync_ShouldReturnProcessedContent()
    {
        // Arrange
        var mockOllama = new Mock<IOllamaService>();
        var service = new ContentAnalyzerService(mockOllama.Object, Mock.Of<ILogger>());
        
        // Act
        var result = await service.AnalyzeContentAsync("test content");
        
        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.MainTopics);
    }
}
```

### Integration Tests

```csharp
public class DocumentsControllerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    
    public DocumentsControllerIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }
    
    [Fact]
    public async Task Upload_ValidFile_ReturnsOk()
    {
        // Arrange
        var content = new MultipartFormDataContent();
        // Add file and userId
        
        // Act
        var response = await _client.PostAsync("/api/documents/upload", content);
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

## Logging

### Sử dụng ILogger

```csharp
public class DocumentProcessor
{
    private readonly ILogger<DocumentProcessor> _logger;
    
    public DocumentProcessor(ILogger<DocumentProcessor> logger)
    {
        _logger = logger;
    }
    
    public async Task ProcessAsync(string filePath)
    {
        _logger.LogInformation("Starting to process file: {FilePath}", filePath);
        
        try
        {
            // Process
            _logger.LogDebug("File size: {Size} bytes", fileSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process file: {FilePath}", filePath);
            throw;
        }
    }
}
```

### Log Levels
- **Trace**: Very detailed diagnostic info
- **Debug**: Internal application state
- **Information**: General flow of application
- **Warning**: Unusual events
- **Error**: Error events that still allow app to continue
- **Critical**: Critical failures

## Database Operations

### MongoDB Best Practices

```csharp
// ✅ Use indexes for frequent queries
var indexKeys = Builders<Document>.IndexKeys
    .Ascending(d => d.UploadedBy)
    .Descending(d => d.CreatedAt);
await collection.Indexes.CreateOneAsync(new CreateIndexModel<Document>(indexKeys));

// ✅ Use projection to limit data transfer
var projection = Builders<Document>.Projection
    .Include(d => d.FileName)
    .Include(d => d.Status)
    .Exclude(d => d.ExtractedText); // Don't load large text

// ✅ Use aggregation for complex queries
var pipeline = new[]
{
    new BsonDocument("$match", new BsonDocument("status", 3)),
    new BsonDocument("$group", new BsonDocument
    {
        { "_id", "$uploadedBy" },
        { "count", new BsonDocument("$sum", 1) }
    })
};
```

## API Design

### RESTful Conventions

```
GET    /api/documents           - List all
GET    /api/documents/{id}      - Get one
POST   /api/documents           - Create
PUT    /api/documents/{id}      - Update (full)
PATCH  /api/documents/{id}      - Update (partial)
DELETE /api/documents/{id}      - Delete

GET    /api/documents/{id}/questions  - Nested resource
POST   /api/documents/{id}/process    - Action
```

### Response Format

```csharp
// Success with data
return Ok(new
{
    success = true,
    data = document,
    message = "Document created successfully"
});

// Error
return BadRequest(new
{
    success = false,
    error = "Invalid file type",
    code = "INVALID_FILE_TYPE"
});

// Pagination
return Ok(new
{
    data = documents,
    pagination = new
    {
        page = 1,
        pageSize = 20,
        totalItems = 100,
        totalPages = 5
    }
});
```

## Frontend Best Practices

### React Component Structure

```jsx
// ✅ Good: Separate concerns
function DocumentList() {
    // State
    const [documents, setDocuments] = useState([]);
    const [loading, setLoading] = useState(true);
    
    // Effects
    useEffect(() => {
        loadDocuments();
    }, []);
    
    // Handlers
    const handleDelete = async (id) => {
        // Handle delete
    };
    
    // Render helpers
    const renderDocument = (doc) => {
        return <DocumentItem key={doc.id} document={doc} />;
    };
    
    // Main render
    if (loading) return <Loading />;
    
    return (
        <div>
            {documents.map(renderDocument)}
        </div>
    );
}
```

### API Service Pattern

```javascript
// services/api.js
export const documentService = {
    upload: (file) => axios.post('/api/documents/upload', file),
    getAll: () => axios.get('/api/documents'),
    delete: (id) => axios.delete(`/api/documents/${id}`)
};

// Component
import { documentService } from '../services/api';

const handleUpload = async (file) => {
    try {
        const result = await documentService.upload(file);
        setMessage('Upload successful');
    } catch (error) {
        setError(error.message);
    }
};
```

## Performance Tips

### Backend
1. **Use async/await** cho tất cả I/O operations
2. **Cache** frequently accessed data
3. **Pagination** cho large datasets
4. **Background processing** cho heavy tasks
5. **Connection pooling** cho database
6. **Compression** cho API responses

### Frontend
1. **Lazy loading** components
2. **Memoization** với useMemo/useCallback
3. **Virtual scrolling** cho large lists
4. **Image optimization**
5. **Code splitting**

## Security Checklist

- [ ] Validate all user inputs
- [ ] Sanitize file names and paths
- [ ] Limit file upload sizes
- [ ] Check file types by content, not just extension
- [ ] Use HTTPS in production
- [ ] Implement authentication/authorization
- [ ] Rate limiting on APIs
- [ ] CORS configuration
- [ ] SQL injection prevention (use parameterized queries)
- [ ] XSS prevention (sanitize output)
- [ ] Store sensitive config in environment variables

## Git Workflow

### Branch Strategy
```
main (production)
  ├── develop (latest development)
      ├── feature/document-upload
      ├── feature/question-generation
      └── bugfix/ocr-error
```

### Commit Messages
```
feat: Add PDF processing support
fix: Resolve OCR text encoding issue
docs: Update setup instructions
refactor: Improve question generation logic
test: Add unit tests for ContentAnalyzer
```

### Pull Request Process
1. Create feature branch from `develop`
2. Implement changes
3. Write tests
4. Update documentation
5. Create PR with description
6. Code review
7. Merge to `develop`

## Monitoring và Debugging

### Application Insights (Azure)
```csharp
builder.Services.AddApplicationInsightsTelemetry();
```

### Health Checks
```csharp
builder.Services.AddHealthChecks()
    .AddMongoDb(mongoConnectionString)
    .AddUrlGroup(new Uri("http://localhost:11434"), "ollama");

app.MapHealthChecks("/health");
```

### Debug Tips
1. Set breakpoints trong VS Code
2. Use `dotnet watch run` cho hot reload
3. Check logs: `dotnet run --verbosity detailed`
4. MongoDB Compass để xem data
5. Postman/Swagger để test APIs
6. React DevTools cho component debugging

## Code Review Checklist

- [ ] Code follows naming conventions
- [ ] No hardcoded values (use config)
- [ ] Error handling implemented
- [ ] Logging added where appropriate
- [ ] Tests written and passing
- [ ] Documentation updated
- [ ] No sensitive data in code
- [ ] Performance considered
- [ ] SOLID principles followed
- [ ] DRY principle applied

## Useful Commands

```powershell
# Backend
dotnet clean
dotnet restore
dotnet build
dotnet test
dotnet run --project src/ELearnGamePlatform.API
dotnet watch run

# Frontend
npm install
npm start
npm run build
npm test

# MongoDB
mongosh
use ELearnGameDB
db.documents.find().pretty()
db.questions.countDocuments()

# Git
git status
git add .
git commit -m "feat: add new feature"
git push origin feature-branch
```
