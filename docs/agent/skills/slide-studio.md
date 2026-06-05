# slide-studio

## Khi nao dung

- Task bat dau o slide generation, slide deck persistence, image sourcing, preview HTML, hoac Workspace Studio slide editor.
- Can kiem tra su khop nhau giua backend deck/item shape va frontend preview/editor.
- Can lan theo flow tu generate request den rendered slide deck.

## File/thu muc lien quan

- `src/ELearnGamePlatform.API/Controllers/SlidesController.cs`
- `src/ELearnGamePlatform.API/Services/SlideImageService.cs`
- `src/ELearnGamePlatform.Infrastructure/Repositories/SlideDeckRepository.cs`
- `src/ELearnGamePlatform.Core/Entities/SlideDeck.cs`
- `src/ELearnGamePlatform.Core/Entities/SlideEditorState.cs`
- `src/ELearnGamePlatform.Services/AI/SlideGeneratorService.cs`
- `client/src/components/FolderStudio.js`
- `client/src/components/slide-studio/*`
- `client/src/services/api.js`

## Dieu cam

- Khong sua rieng backend hoac frontend cua slide neu chua doc phia con lai.
- Khong doi deck/item shape ma khong kiem tra preview/editor va API consumer.
- Khong nhap pattern slide tu external repo neu trai voi persisted shape hoac UX hien tai cua PBL5.
- Khong bo qua image candidate flow, stale deck behavior, hoac HTML preview khi task cham slide pipeline.

## Checklist truoc khi sua

- Current routing note: slide editing is owned by Workspace Studio (`FolderStudio` plus shared `components/slide-studio/*`). The document-level `/slides/:documentId` frontend route has been retired.
- Xac dinh task nam o generation, persistence, image pipeline, preview, hay editor.
- Doc `SlidesController` entrypoint tuong ung.
- Doc `SlideGeneratorService` hoac `SlideImageService` neu logic nam o generation/image.
- Doc `SlideDeckRepository` va entity neu task co persisted shape.
- Doc `FolderStudio` de thay frontend consume payload nhu the nao.

## Checklist sau khi sua

- Xac nhan deck/item shape van khop giua backend va frontend.
- Xac nhan progress/state/image candidate/select flow van nhat quan.
- Xac nhan HTML preview va editor khong drift nhau o field quan trong.
- Neu task cham folder/workspace deck, kiem tra them stale deck logic va source selection impact.

## Lenh kiem tra phu hop

```powershell
dotnet build ELearnGamePlatform.sln
cd client; npm run build
rg -n "SlidesController|SlideGeneratorService|SlideImageService|SlideDeck|SlideItem|FolderStudio|imageCandidates|editorState|html" src client/src
```
