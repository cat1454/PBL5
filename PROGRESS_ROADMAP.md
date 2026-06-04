# Progress Roadmap - ELearn Game Platform

Cap nhat: 2026-04-18

## 1. Ket luan nhanh

- Repo hien tai da dat muc `MVP+ / demoable` cho flow cot loi:
  - upload tai lieu
  - OCR / extraction / analysis
  - generate question
  - quiz / flashcards
  - generate va edit slide
  - folder project + folder deck
- Muc do hoan thanh tong quan uoc tinh: `65-75%` so voi huong "ban demo thuyet phuc" trong `ROADMAP.md`.
- Frontend build pass.
- Backend build hien tai chua xac nhan duoc do blocker moi truong SDK:
  - `MSB4276`
  - missing workload SDK locator trong `C:\Program Files\dotnet\sdk\9.0.306\...`

## 2. Muc do hoan thanh theo module

| Module | Muc do | Danh gia |
|---|---:|---|
| Document upload + processing | 85% | Da co pipeline, progress, OCR/analyze, dashboard document |
| Question generation | 80% | Da co start job, polling, question bank, verifier score |
| Quiz / Flashcards | 75% | Da choi duoc voi du lieu that, co quality filter |
| Slide Studio theo document | 80% | Da generate, preview, edit, export HTML/PDF |
| Folder Projects / Folder Studio | 70% | Da co folder, source selection, deck cap folder, editor |
| UI/UX polish toan he thong | 65% | Da lam lai nhieu man hinh chinh, nhung van chua dong bo 100% |
| Slide media pipeline | 55% | Da co image scaffold, refresh/select candidate, nhung thong diep va code van cho phase tiep theo |
| Game modes mo rong | 25% | Moi co quiz + flashcards, chua co streak / match pairs / weakness mode |
| Slide templates san de demo nhanh | 45% | Da co theme/brief picker, nhung chua thanh bo template demo dung nghia |
| Reliability / production hardening | 35% | Van in-memory job store, `Task.Run`, legacy hardcoded identity copy, chua co test he thong |

## 3. Trang thai chi tiet

### A. Da hoan thanh hoac gan hoan thanh

- Document pipeline:
  - upload file
  - OCR / extract text
  - analysis summary / topics / key points
  - progress payload da duoc chuan hoa
- Question flow:
  - start generation job
  - poll progress
  - luu question vao DB
  - surface verifier score / low-confidence
- Study flow:
  - quiz route chay duoc
  - flashcards route chay duoc
  - quality filter da co
- Slide flow:
  - tao outline
  - tao tung slide
  - luu deck + item
  - mo Slide Studio
  - edit slide item
  - export HTML/PDF
- Folder flow:
  - tao folder project
  - upload nhieu source
  - chon source dua vao folder deck
  - generate deck cap folder
  - edit slide trong folder studio

### B. Da co scaffold nhung chua xong

- Slide media:
  - da co `refresh` / `select` image candidate
  - UI media tray da ton tai
  - nhung trong code va copy van the hien day la workflow dang noi tiep theo phase sau
- Slide templates:
  - da co `themeKey`, `audience`, `tone`, `languageStyle`, `narrativeGoal`
  - nhung chua co bo template ro rang kieu `On Tap Nhanh`, `Bai Giang 10 Phut`, `PBL Defense`
  - chua thay acceptance layer de xac nhan template tac dong xuyen suot outline -> preview theo preset
- Folder future actions:
  - quiz cap folder
  - flashcard cap folder
  - summary cap folder
  - mindmap
  - export PPTX
  - share link
  - hien dang la nut `Soon`

### C. Chua lam hoac moi o muc placeholder

- Game mode moi theo roadmap:
  - `Streak Mode`
  - `Match Pairs`
  - `Weakness Mode`
- Auth that su:
  - runtime da co auth/JWT; mot so copy/docs cu van can duoc chuan hoa khoi hardcoded identity wording
  - settings page moi la placeholder
- Reliability:
  - job store van in-memory
  - background job van dua tren `Task.Run`
  - restart app co nguy co mat state dang chay
- Test / CI hardening:
  - chua thay bo test tu dong co y nghia cho core flows
- Backend local build verification:
  - dang bi chan boi moi truong SDK, nen chua xac nhan "green build" backend trong may hien tai

## 4. Roadmap tien trinh de xuat

### Phase 1 - Chot ban demo on dinh

Muc tieu: khoa core demo flow thanh mot luong thuyet phuc va it loi.

- Fix blocker build backend SDK de co the build/run/verify on-demand
- Polish lai copy, empty state, loading, error cho `Documents`, `Slides`, `Folders`
- Ra soat lai encoding text giao dien dang bi loi ky tu o mot so component game
- Chot 1 demo script chuan:
  - upload
  - analysis
  - generate question
  - quiz / flashcards
  - generate slide
  - folder studio

### Phase 2 - Day dung roadmap co gia tri demo cao

Muc tieu: hoan thanh phan dang thieu lon nhat cua roadmap hien tai.

- Them `Streak Mode`
- Them `Match Pairs`
- Chuyen theme picker thanh `template picker` that su
- Tao it nhat 3 template demo ro rang:
  - On Tap Nhanh
  - Bai Giang 10 Phut
  - PBL Defense
- Chot image/media workflow de tranh trang thai nua that nua scaffold

### Phase 3 - Bien scaffold thanh feature

Muc tieu: noi backend cho cac nut da dat san.

- Folder-level quiz / flashcards
- Folder summary / synthesis
- Export PPTX
- Share review link
- Neu khong lam kip, an cac nut `Soon` khoi UI demo de tranh tao ky vong sai

### Phase 4 - Hardening

Muc tieu: giam no ky thuat va san sang mo rong.

- Chuyen job state sang persistent store
- Hoan thien auth / ownership va hardcoded identity cleanup
- Them logging va health checks ro rang
- Them smoke test / integration test cho:
  - upload -> processed
  - generate questions
  - generate slides
  - folder deck

## 5. Thu tu uu tien 7 ngay tiep theo

1. Xu ly blocker build backend va xac nhan app chay end-to-end.
2. Sua cac chuoi text/encoding bi vo trong `QuizGame` va `FlashcardGame`.
3. Chot 2 game mode moi hoac neu chua lam kip thi an khoi roadmap demo gan nhat.
4. Nang cap theme picker thanh template picker co preset dung nghia.
5. Quyết dinh ro cho media pipeline:
   - hoan thien that
   - hoac ghi ro la beta/scaffold trong UI
6. Loai bot cac nut `Soon` khoi man hinh demo neu chua noi backend.
7. Viet smoke checklist de truoc demo co the test nhanh trong 5-10 phut.

## 6. Danh sach blocker hien tai

- Blocker 1: khong co thu muc `task` rieng trong repo de doi chieu theo task list goc.
  - Hien tai tien do phai suy ra tu code + docs + UI scaffold.
- Blocker 2: backend `dotnet build` dang fail do moi truong SDK/workload:
  - `MSB4276`
  - missing `Microsoft.NET.SDK.WorkloadAutoImportPropsLocator`
  - missing `Microsoft.NET.SDK.WorkloadManifestTargetsLocator`
- Blocker 3: mot so man hinh game van co text encoding loi, lam giam chat luong demo.

## 7. Ket luan thuc thi

- Phan "lam duoc" cua san pham da kha day cho demo hoc tap AI.
- Phan "thuyet phuc va nhat quan" chua theo kip backend.
- Viec can lam ngay khong phai mo rong kien truc lon, ma la:
  - chot UX
  - bo sung 2 game mode moi
  - bien template thanh gia tri thay duoc
  - giam scaffold lo ra tren UI demo
