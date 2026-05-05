# ai-ocr

## Khi nào dùng

- Task bắt đầu ở OCR, document processing, prompt, chunking, Ollama config, analysis, hoặc question generation.
- Cần kiểm tra progress payload hoặc UI coupling của các flow AI/OCR.
- Cần xác nhận runtime asset như `tessdata`, Poppler fallback, hoặc model settings.

## File/thư mục liên quan

- `src/ELearnGamePlatform.Services/OCR/TesseractOcrService.cs`
- `src/ELearnGamePlatform.Services/DocumentProcessing/`
- `src/ELearnGamePlatform.Services/AI/`
- `src/ELearnGamePlatform.Infrastructure/Services/OllamaService.cs`
- `src/ELearnGamePlatform.API/appsettings.json`
- `src/ELearnGamePlatform.API/Controllers/DocumentsController.cs`
- `src/ELearnGamePlatform.API/Controllers/QuestionsController.cs`
- `src/ELearnGamePlatform.API/Contracts/JobProgressPayload.cs`

## Điều cấm

- Không nhập workflow SaaS hoặc MCP external như Nutrient/install flow vào PBL5.
- Không đổi model/config thật chỉ vì external skill gợi ý khác.
- Không sửa prompt hoặc processor mà bỏ qua progress payload, persisted metadata, hoặc UI polling liên quan.
- Không patch fallback bề mặt nếu chưa hiểu root cause ở processor/service/config.

## Checklist trước khi sửa

- Xác định flow bắt đầu từ upload, OCR, analysis, hay question generation.
- Đọc processor hoặc AI service chịu trách nhiệm chính.
- Đọc config trong `appsettings.json` và binding trong `Program.cs` nếu task chạm setting/runtime.
- Đọc progress payload hoặc frontend consumer nếu flow có polling/status.
- Kiểm tra asset/runtime assumption như `tessdata`, Poppler, allowed file types.

## Checklist sau khi sửa

- Xác nhận processor/service/config vẫn khớp nhau.
- Xác nhận metadata hoặc progress payload không drift với UI.
- Xác nhận không thêm dependency hay external runtime trái với stack hiện tại.
- Nếu sửa prompt/chunking, đọc lại flow verification hoặc fallback liên quan.

## Lệnh kiểm tra phù hợp

```powershell
dotnet build ELearnGamePlatform.sln
dotnet run --project src/ELearnGamePlatform.API
rg -n "Tesseract|Ollama|Prompt|Chunk|Coverage|QuestionGenerator|ContentAnalyzer|progress" src/ELearnGamePlatform.Services src/ELearnGamePlatform.API src/ELearnGamePlatform.Infrastructure
```
