# slide-studio

## Khi nào dùng

- Task bắt đầu ở slide generation, slide deck persistence, image sourcing, preview HTML, hoặc Slide Studio editor.
- Cần kiểm tra sự khớp nhau giữa backend deck/item shape và frontend preview/editor.
- Cần lần theo flow từ generate request đến rendered slide deck.

## File/thư mục liên quan

- `src/ELearnGamePlatform.API/Controllers/SlidesController.cs`
- `src/ELearnGamePlatform.API/Services/SlideImageService.cs`
- `src/ELearnGamePlatform.Infrastructure/Repositories/SlideDeckRepository.cs`
- `src/ELearnGamePlatform.Core/Entities/SlideDeck.cs`
- `src/ELearnGamePlatform.Core/Entities/SlideEditorState.cs`
- `src/ELearnGamePlatform.Services/AI/SlideGeneratorService.cs`
- `client/src/components/SlideStudio.js`
- `client/src/services/api.js`

## Điều cấm

- Không sửa riêng backend hoặc frontend của slide nếu chưa đọc phía còn lại.
- Không đổi deck/item shape mà không kiểm tra preview/editor và API consumer.
- Không nhập pattern slide từ external repo nếu trái với persisted shape hoặc UX hiện tại của PBL5.
- Không bỏ qua image candidate flow, stale deck behavior, hoặc HTML preview khi task chạm slide pipeline.

## Checklist trước khi sửa

- Xác định task nằm ở generation, persistence, image pipeline, preview, hay editor.
- Đọc `SlidesController` entrypoint tương ứng.
- Đọc `SlideGeneratorService` hoặc `SlideImageService` nếu logic nằm ở generation/image.
- Đọc `SlideDeckRepository` và entity nếu task có persisted shape.
- Đọc `SlideStudio` để thấy frontend consume payload như thế nào. Legacy slide screen đã retire.

## Checklist sau khi sửa

- Xác nhận deck/item shape vẫn khớp giữa backend và frontend.
- Xác nhận progress/state/image candidate/select flow vẫn nhất quán.
- Xác nhận HTML preview và editor không drift nhau ở field quan trọng.
- Nếu task chạm folder/workspace deck, kiểm tra thêm stale deck logic và source selection impact.

## Lệnh kiểm tra phù hợp

```powershell
dotnet build ELearnGamePlatform.sln
cd client; npm run build
rg -n "SlidesController|SlideGeneratorService|SlideImageService|SlideDeck|SlideItem|SlideStudio|imageCandidates|editorState|html" src client/src
```
