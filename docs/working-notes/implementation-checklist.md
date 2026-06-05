# Implementation Checklist: Slide System

Historical note: this working note predates the workspace-first route pivot. Treat `/documents` and `/slides/:documentId` references below as legacy design context; the active public surfaces are `/workspaces` and `/workspaces/:workspaceId`.

Tai lieu nay bien `design.md` thanh checklist co the implement theo pha. Muc tieu la giao hang tung buoc, tranh mo rong pham vi qua som, va giu frontend/backend di cung mot huong.

## Phase 1 - UI scaffolding an toan voi backend hien tai

Muc tieu:

- Tao shared image view model cho frontend
- Chen media placeholders vao legacy `/documents` va retired `/slides/:documentId` design surfaces
- Chuan bi cho payload anh trong tuong lai ma khong lam vo contract hien tai

Checklist:

- [x] Tao checklist implementation tach rieng khoi `design.md`
- [x] Tao utility normalize `slide image state`, `selected image`, `image candidates`
- [x] Hien image badge + media placeholder trong legacy document-list screen
- [x] Hien media block + candidate tray scaffold trong retired `SlideStudio.js`; active editor now lives in `FolderStudio.js`
- [x] Them CSS cho image shell, source badge, attribution, candidate tray
- [x] Chay sanity check frontend sau khi sua

Exit criteria:

- Legacy `/documents` va retired `/slides/:documentId` design surfaces deu co vung media ro rang
- Frontend khong phu thuoc vao backend image contract moi de render duoc

Ghi chu sanity check:

- `cmd /c npm --prefix client run build` da pass
- Chi con warning cu trong repo, khong co loi build moi tu Phase 1 scaffold

## Phase 2 - Backend image contract va luu tru du lieu

Muc tieu:

- Mo rong `SlideItem` de luu image plan, candidate list, selected image
- Bat dau tra payload anh tu backend

Checklist:

- [x] Them field image vao `SlideItem` va `SlideDeck` payload
- [x] Tao migration cho `image_plan`, `image_candidates`, `selected_image_key`
- [x] Cap nhat `SlidesController` de tra `imageState`, `selectedImage`, `imageCandidates`
- [x] Tao helper serialize/deserialize image data
- [x] Dam bao `GetDeckByDocument` van backward-compatible

Exit criteria:

- Frontend nhan duoc du lieu anh that tu backend
- Deck payload da co du thong tin de render selected image va attribution

Ghi chu:

- `SlideItem` da co them `image_plan`, `image_candidates`, `selected_image_key`
- `SlidesController` da scaffold `imageState`, `selectedImage`, `imageCandidates` ngay trong `BuildSlideItemPayload`
- Migration duoc tao thu cong vi repo dang pin `global.json` vao SDK `9.0.306`, trong khi may hien tai chua co dung version de chay `dotnet ef`

## Phase 3 - Document tab quick replace va image sourcing status

Muc tieu:

- Bien `/documents` thanh noi review deck va doi anh nhanh

Checklist:

- [ ] Them image sourcing progress tach rieng voi slide generation progress
- [ ] Hien candidate tray that va cho phep chon anh
- [ ] Tao optimistic update / rollback cho thao tac doi anh
- [ ] Hien `no-image-needed`, `no-license-safe-image`, `generated-only`
- [ ] Hien attribution compact trong document card

Exit criteria:

- User co the review va doi anh nhanh ngay trong document card

## Phase 4 - Slide Studio media inspector va export-ready canvas

Muc tieu:

- Dua image workflow day du vao Studio

Checklist:

- [ ] Them inspector rieng cho image / source / attribution / quality
- [ ] Cho phep doi anh, bo anh, retry sourcing trong Studio
- [ ] Render selected image theo layout mode trong canvas
- [ ] Hien attribution dung o preview va export
- [ ] Disable/export guard neu deck chua san sang

Exit criteria:

- Studio tro thanh noi chinh sua text + image cap slide

## Phase 5 - Image worker integration va serving assets

Muc tieu:

- Noi local AI voi image worker internet trong ranh gioi bao mat da chot

Checklist:

- [ ] Tao service / endpoint populate image candidates
- [ ] Mirror asset ve local storage de tranh hotlink
- [ ] Serve asset qua API route on dinh
- [ ] Luu license / attribution / origin metadata
- [ ] Dung redacted prompt thay vi raw document content

Exit criteria:

- Anh web/generated duoc lay, luu, va phuc vu on dinh trong app

## Phase 6 - Polish va hardening

Muc tieu:

- Lam tron UX, responsive, va tinh on dinh

Checklist:

- [ ] Tablet/mobile pass cho `/documents` va `/slides/:documentId`
- [ ] Loading skeleton text/media tach biet ro
- [ ] QA voi deck text-only, hero image, side accent, background image
- [ ] QA voi image doc, image crop xau, attribution dai
- [ ] QA voi export HTML/PDF va delete document cleanup

Exit criteria:

- Flow slide co anh on dinh, de duyet, de sua, de export

## Thu tu khuyen nghi

1. Phase 1
2. Phase 2
3. Phase 3
4. Phase 4
5. Phase 5
6. Phase 6

## Ghi chu

- Khong skip Phase 2 neu muon doi anh that su luu duoc
- `/documents` la diem vao chinh, nen Phase 3 co gia tri UX cao nhat sau khi co backend contract
- Export chi nen polish sau khi selected image va attribution da on dinh

## Model stack de xuat theo 3 muc may

Ghi chu:

- Repo hien tai da dung `qwen2.5:7b` trong `src/ELearnGamePlatform.API/appsettings.json`, nen day la diem bat dau an toan
- Trong session nay khong doc duoc thong tin hardware qua WMI vi bi sandbox chan, nen stack duoc chot theo tier `RAM/VRAM`
- Muc tieu khong phai tim 1 model lam tat ca, ma tach theo 4 vai tro:
  - local LLM tao query/prompt da redacted
  - image-text encoder rerank anh tim duoc
  - local VLM review anh co khop slide hay khong
  - image generation fallback khi web khong du anh hop le

### May yeu

Dieu kien tham khao:

- CPU-only hoac GPU <= 8GB VRAM
- RAM tong 16GB-32GB

Stack:

- Query/prompt redaction:
  - `qwen2.5:3b-instruct` neu uu tien toc do
  - fallback len `qwen2.5:7b` neu prompt anh qua chung chung
- Image rerank:
  - `google/siglip2-base-patch16-224`
- Local image review:
  - bo qua o v1, hoac dung `Qwen2.5-VL-3B-Instruct` chi cho top 1-2 anh
- Fallback image generation:
  - uu tien provider internet nhe/chi khi that can
  - neu dung OpenAI thi chon `gpt-image-1-mini`

Khuyen nghi:

- Day la stack de chay duoc, khong phai stack chat luong cao nhat
- Neu may yeu, nen doi local VLM thanh buoc tuy chon de tranh nghen latency

Checklist:

- [ ] Tao cau hinh tier `low`
- [ ] Ho tro tat local image review trong tier `low`
- [ ] Giam so candidate rerank xuong top 8 truoc khi shortlist top 4

### May vua

Dieu kien tham khao:

- GPU 10GB-16GB VRAM hoac may co RAM 32GB-64GB
- Co the chay on 1 LLM 7B va 1 encoder nhe

Stack:

- Query/prompt redaction:
  - `qwen2.5:7b`
- Image rerank:
  - `google/siglip2-so400m-patch14-384` neu tai duoc
  - fallback `google/siglip2-base-patch16-224`
- Local image review:
  - `Qwen2.5-VL-7B-Instruct`
- Fallback image generation:
  - `gpt-image-1-mini` khi uu tien chi phi
  - `gpt-image-1.5` cho slide quan trong, hero image, hoac deck xuat final

Khuyen nghi:

- Day la tier can bang nhat cho project nay
- Nen xem day la stack mac dinh khi bat dau Phase 5

Checklist:

- [ ] Tao cau hinh tier `medium`
- [ ] Dung `qwen2.5:7b` lam default cho image prompt planning
- [ ] Bat local image review cho top 4 candidate
- [ ] Cho phep route hero slide sang `gpt-image-1.5`

### May manh

Dieu kien tham khao:

- GPU >= 24GB VRAM, hoac nhieu GPU, hoac RAM >= 64GB
- Co the chay dong thoi LLM/VLM lon hon va batch reranking

Stack:

- Query/prompt redaction:
  - `qwen2.5:14b`
  - co the thu `qwen2.5:32b` neu ha tang local that su on
- Image rerank:
  - `google/siglip2-so400m-patch14-384` batch mode
  - co the bo sung ensemble voi `openclip` neu can nghien cuu sau
- Local image review:
  - `Qwen2.5-VL-7B-Instruct`
  - hoac `Qwen2.5-VL-14B-Instruct` neu latency chap nhan duoc
- Fallback image generation:
  - `gpt-image-1.5` lam default

Khuyen nghi:

- Tier nay hop cho batch deck generation va QA nghiem hon
- Khong can nhay len model lon hon neu phase dau chua co metrics cho thay 7B that bai

Checklist:

- [ ] Tao cau hinh tier `high`
- [ ] Ho tro batch rerank va batch local review
- [ ] Chi bat model > 7B sau khi co benchmark latency/chi phi

## Default stack de xuat cho project nay

Neu chua biet may thuoc tier nao, lay stack mac dinh sau:

- Local query/prompt redaction: `qwen2.5:7b`
- Image rerank: `google/siglip2-base-patch16-224` hoac `google/siglip2-so400m-patch14-384`
- Local image review: `Qwen2.5-VL-7B-Instruct`
- Fallback generation: `gpt-image-1.5`

Ly do:

- Khop voi stack local da co san trong repo
- De nang cap dan ma khong pha architecture
- Phan tach ro local privacy boundary va internet generation boundary

## Checklist bo sung cho Phase 5

- [ ] Them `ModelTier` vao config: `low | medium | high`
- [ ] Them `ImagePipelineSettings` gom `planningModel`, `rerankModel`, `reviewModel`, `generationModel`
- [ ] Them routing rule theo slide importance: `hero`, `content`, `text-only`
- [ ] Them flag tat/mo local image review
- [ ] Them benchmark nho ghi lai latency cho 3 buoc: planning, rerank, review
- [ ] Chot default tier ban dau la `medium` tru khi benchmark tren may that cho thay khong du tai nguyen

## Ket qua kiem tra may hien tai

Thong tin doc duoc trong session nay:

- GPU: `NVIDIA GeForce RTX 4060 Laptop GPU`
- VRAM: xap xi `8188 MB`
- RAM: xap xi `16 GB`

Ket luan thuc dung:

- May nay khong nen coi la `medium` day du
- Nen xep vao nhom `low+ / medium-lite`
- Model local hop ly nhat de bat dau:
  - planning: `qwen2.5:7b`
  - rerank: `google/siglip2-base-patch16-224`
  - local review: `qwen2.5-vl:3b` hoac tat hinh review local trong v1
  - fallback generation: `gpt-image-1.5`

Khong khuyen nghi lam default tren may nay:

- `qwen2.5:14b`
- `qwen2.5-vl:7b` cho workflow moi slide moi anh
- rerank + review batch lon cung luc

Default tier de ghi vao backend config luc nay:

- `ModelTier = "low"`
- bat dau voi `EnableLocalImageReview = false`
- chi bat local review sau khi benchmark thuc te cho thay latency van chap nhan duoc

## Config schema cu the cho backend

Da chot schema co the dua thang vao backend:

- File options class:
  - `src/ELearnGamePlatform.API/Configuration/ImagePipelineSettings.cs`
- Noi bind config:
  - `src/ELearnGamePlatform.API/Program.cs`
- Noi dat gia tri mac dinh:
  - `src/ELearnGamePlatform.API/appsettings.json`

### Section name

```json
"ImagePipeline": { }
```

### Muc tieu cua schema

- Tach ro config cho 5 nhom:
  - pipeline-level controls
  - planning model
  - rerank model
  - review model
  - generation model
  - web source policy

- Dam bao co the route model theo tier ma khong phai sua business logic

### Top-level fields

```json
{
  "Enabled": false,
  "ModelTier": "low",
  "EnableLocalImageReview": false,
  "DownloadAssetsLocally": true,
  "AssetStorageRoot": "uploads/slide-assets",
  "MaxCandidatesToRerank": 8,
  "MaxCandidatesToPersist": 4,
  "PreferredAspectRatio": "16:9",
  "LicensePolicy": "license-safe"
}
```

Y nghia:

- `Enabled`: bat/tat toan bo image pipeline
- `ModelTier`: route mac dinh `low | medium | high`
- `EnableLocalImageReview`: bat/tat buoc VLM review local
- `DownloadAssetsLocally`: mirror asset ve local thay vi hotlink
- `AssetStorageRoot`: thu muc luu anh da chon/da crawl
- `MaxCandidatesToRerank`: so anh dau vao cho encoder rerank
- `MaxCandidatesToPersist`: so anh giu lai de frontend doi nhanh
- `PreferredAspectRatio`: goc render mac dinh cho slide
- `LicensePolicy`: rang buoc nguon anh web

### Nested schema

#### `Planning`

```json
{
  "Provider": "ollama",
  "Model": "qwen2.5:7b",
  "Temperature": 0.2,
  "MaxPromptChars": 800,
  "TimeoutSeconds": 90
}
```

#### `Rerank`

```json
{
  "Provider": "siglip2",
  "Model": "google/siglip2-base-patch16-224",
  "TopKBeforeReview": 8,
  "FinalShortlistCount": 4,
  "PreferGpu": true
}
```

#### `Review`

```json
{
  "Provider": "ollama-vl",
  "Model": "qwen2.5-vl:3b",
  "TimeoutSeconds": 120,
  "MaxImagesPerSlide": 4,
  "MaxParallelSlides": 1
}
```

#### `Generation`

```json
{
  "Provider": "openai",
  "Model": "gpt-image-1.5",
  "UseOnlyAsFallback": true,
  "TimeoutSeconds": 120,
  "Quality": "high",
  "Size": "1536x1024"
}
```

#### `WebSources`

```json
{
  "Enabled": true,
  "MaxResultsPerQuery": 20,
  "MaxDownloadsPerSlide": 8,
  "AllowedDomains": [
    "commons.wikimedia.org",
    "unsplash.com",
    "pexels.com"
  ]
}
```

### C# options shape

Da duoc tao thanh nested options class, gom:

- `ImagePipelineSettings`
- `ImagePlanningSettings`
- `ImageRerankSettings`
- `ImageReviewSettings`
- `ImageGenerationSettings`
- `ImageWebSourceSettings`

Muc tieu:

- Strongly typed config binding
- De validate hoac map sang service sau nay
- De route default theo tier ma van cho phep override tung model

### Rule de ap dung ngay cho may hien tai

Khi `ModelTier = "low"`:

- `Planning.Model = "qwen2.5:7b"`
- `Rerank.Model = "google/siglip2-base-patch16-224"`
- `EnableLocalImageReview = false`
- `Review.Model = "qwen2.5-vl:3b"` chi de du phong
- `Generation.Model = "gpt-image-1.5"`

Khi benchmark on hon moi nang len:

- `EnableLocalImageReview = true`
- hoac doi `Rerank.Model` len `google/siglip2-so400m-patch14-384`

## Checklist tiep theo de bien schema thanh code chay duoc

- [ ] Them validation cho `ImagePipelineSettings` de chan gia tri sai
- [ ] Them service `IImagePipelineProfileResolver` map `ModelTier` -> effective config
- [ ] Them endpoint health check cho planning / rerank / review / generation providers
- [ ] Them benchmark command nho de do latency tren may hien tai
- [ ] Chi bat `Review` local sau khi benchmark top-4 candidates tren RTX 4060 Laptop + 16GB RAM
