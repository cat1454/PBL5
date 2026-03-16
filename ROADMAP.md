# ROADMAP - ELearn Game Platform

## 1. Tong quan san pham hien tai

- San pham hien tai dang o muc MVP: upload tai lieu -> OCR/trich xuat text -> AI phan tich noi dung -> sinh cau hoi -> hoc bang quiz/flashcards.
- Backend hien tai: .NET 8 Web API + EF Core + PostgreSQL + Ollama + Tesseract.
- Frontend hien tai: React 18.
- Trang thai ky thuat hien tai:
  - `dotnet build ELearnGamePlatform.sln` da build duoc.
  - `npm run build` da build duoc, nhung con warning React hooks.
- Cac su that quan trong can bam theo khi mo rong:
  - Docs trong repo con lech voi code runtime o mot so diem, dac biet la MongoDB vs PostgreSQL.
  - `DemoMode.UseLocalFileStore` co trong config nhung chua thay wiring that su vao runtime.
  - Frontend dang hardcode `demo-user`, chua co auth/user that.
  - OCR tieng Viet chua day du: repo hien tai moi co `eng.traineddata`, chua co `vie.traineddata`.
  - OCR PDF scan con phu thuoc `pdftoppm` trong PATH.

## 2. He thong dang lam duoc gi

- Upload va luu file PDF, DOCX, PNG, JPG.
- Xu ly nen sau upload de trich xuat noi dung.
- OCR cho image va PDF scan.
- Phan tich noi dung bang AI de lay:
  - main topics
  - key points
  - summary
  - language
- Sinh cau hoi tu dong bang Ollama, co progress polling va fallback khi AI tra ve du lieu khong hop le.
- Luu documents, questions, game sessions tren PostgreSQL qua EF Core.
- Frontend da co luong nguoi dung MVP:
  - upload tai lieu
  - xem danh sach tai lieu
  - theo doi trang thai xu ly
  - xem phan tich noi dung
  - tao bo cau hoi
  - choi quiz
  - hoc flashcards

## 3. He thong chua lam duoc gi / rui ro hien tai

- Chua co auth, login, phan quyen, ownership that su.
- Chua co demo mode that su du config co goi y.
- Chua co test tu dong cho backend/frontend.
- Job xu ly hien tai chua ben vung cho production:
  - dung `Task.Run`
  - progress store in-memory
  - restart app se mat job state
- Frontend chua mo ra het nang luc backend:
  - chua cho chon `QuestionType`
  - chua co `Test` mode
  - chua dung game session flow mot cach day du
- OCR va pipeline AI con nhay cam voi moi truong chay:
  - thieu du lieu OCR tieng Viet
  - phu thuoc Ollama local
  - phu thuoc tool PDF scan OCR ben ngoai
- Tai lieu huong dan trong repo can dong bo lai de nguoi moi khong hieu sai he thong.

## 4. Roadmap + backlog

### P0 - On dinh MVP

- [ ] Muc tieu: Dong bo lai hien trang repo de MVP chay on dinh, de debug, de demo, va de nguoi moi co the setup dung.
- [ ] Gia tri cho nguoi dung: Giam tinh trang upload duoc nhung OCR/AI loi ngam; giam nham lan do docs cu va config ao.
- [ ] Hang muc can lam:
  - [ ] Dong bo `README.md`, `ARCHITECTURE.md`, `RUN_GUIDE.md`, `HUONG_DAN_CHAY.md` voi code runtime hien tai.
  - [ ] Quyet dinh ro `DemoMode.UseLocalFileStore`: wiring that su, hoac xoa neu khong dung.
  - [ ] Enforce config upload that su tu `appsettings.json` thay vi hardcode scattered.
  - [ ] Bo sung `vie.traineddata` va tai lieu hoa dung OCR tieng Viet.
  - [ ] Lam ro yeu cau `pdftoppm`/Poppler trong setup va runtime.
  - [ ] Review va sua cac warning build frontend lien quan `useEffect` dependencies.
- [ ] Done when:
  - [ ] Docs khop voi runtime PostgreSQL + EF Core + Ollama + Tesseract.
  - [ ] Khong con config/dependency "gia" gay hieu nham trong onboarding.
  - [ ] Upload + OCR + AI + generate question co huong dan setup ro rang va lap lai duoc.

### P1 - San pham cot loi

- [ ] Muc tieu: Dua MVP len muc san pham co nguoi dung that, du lieu that, va xu ly loi ro rang hon.
- [ ] Gia tri cho nguoi dung: Moi nguoi co kho tai lieu rieng, tien trinh xu ly dang tin hon, va co the quay lai du lieu cua minh.
- [ ] Hang muc can lam:
  - [ ] Them auth co ban: login, user identity, ownership tai lieu.
  - [ ] Bo hardcode `demo-user` trong frontend.
  - [ ] Them retry/fail handling cho upload, OCR, content analysis, question generation.
  - [ ] Doi job progress store in-memory sang co the song sot qua restart hoac co job table/queue toi thieu.
  - [ ] Hien thi loi than thien voi nguoi dung thay vi chi log backend.
  - [ ] Bo sung validation ve file size, file type, va trang thai document truoc khi cho generate question.
- [ ] Done when:
  - [ ] Moi document gan voi user that su.
  - [ ] User khong thay hoac sua du lieu cua user khac.
  - [ ] Neu OCR/AI loi, he thong tra ve trang thai va huong xu ly ro rang.
  - [ ] Job generate question co the theo doi duoc ma khong phu thuoc hoan toan vao RAM cua process.

### P2 - Hoc tap tuong tac

- [ ] Muc tieu: Mo rong trai nghiem hoc tap tu MVP demo sang mot bo cong cu hoc tap day du hon.
- [ ] Gia tri cho nguoi dung: Nguoi hoc duoc chon cach on tap phu hop thay vi chi co 1 flow mac dinh.
- [ ] Hang muc can lam:
  - [ ] Cho phep chon `QuestionType` tren UI khi sinh cau hoi.
  - [ ] Them `Test` mode tren frontend thay vi chi co Quiz va Flashcards.
  - [ ] Dung game session API de luu mot lan hoc that su.
  - [ ] Luu lich su hoc, diem, so cau dung/sai, va thong ke co ban theo document.
  - [ ] Them man hinh xem ket qua hoc truoc do.
  - [ ] Nghien cuu ranking/topic weakness de goi y on tap theo lo hong kien thuc.
- [ ] Done when:
  - [ ] User co the tao va choi nhieu che do hoc tu cung mot document.
  - [ ] Ket qua moi lan hoc duoc luu va xem lai duoc.
  - [ ] UI da khai thac duoc phan lon API hoc tap co san.

### P3 - Chat luong van hanh

- [ ] Muc tieu: Tang do tin cay ky thuat, kha nang quan sat, va kha nang dua len moi truong that.
- [ ] Gia tri cho nguoi dung: He thong it loi hon, de bao tri hon, va it phu thuoc vao test tay.
- [ ] Hang muc can lam:
  - [ ] Them test tu dong cho backend services, controllers, va repositories quan trong.
  - [ ] Them test frontend cho luong upload, document list, quiz/flashcards.
  - [ ] Them health checks cho DB, Ollama, OCR dependencies.
  - [ ] Chuan hoa logging va error codes de debug production.
  - [ ] Tao deployment checklist: env vars, database migration, OCR assets, Ollama model, storage path.
  - [ ] Xem xet metrics toi thieu cho upload time, OCR time, AI time, failed jobs.
- [ ] Done when:
  - [ ] Co bo test co y nghia cho core flows.
  - [ ] Co cach biet he thong hong o dau ma khong can doc log thu cong qua nhieu.
  - [ ] Co checklist dua he thong len moi truong moi ma khong doan mo.

### P4 - Auto slide tu tai lieu

- [ ] Muc tieu: Tu document da upload, tao deck bai giang de preview tren web va export PDF.
- [ ] Gia tri cho nguoi dung: Giam thoi gian bien tai lieu thanh bai giang; dung cho hoc, demo, va thuyet trinh nhanh.
- [ ] Hang muc can lam:
  - [ ] Chi cho tao slide khi document da co `ExtractedText + ProcessedContent`.
  - [ ] Dinh nghia output v1:
    - [ ] 5-12 slide
    - [ ] moi slide co 1 tieu de
    - [ ] moi slide co 3-5 bullet
    - [ ] co the co speaker notes
  - [ ] Tach ro 2 lop:
    - [ ] slide schema/doc lap voi renderer
    - [ ] renderer HTML de preview va print/export PDF
  - [ ] Du kien them domain/types:
    - [ ] `SlideDeck`
    - [ ] `SlideItem`
    - [ ] `SlideDeckStatus`
  - [ ] Du kien them service:
    - [ ] `ISlideGenerator`
  - [ ] Du kien them repository:
    - [ ] `ISlideRepository`
  - [ ] Du kien them API:
    - [ ] `POST /api/slides/generate/start`
    - [ ] `GET /api/slides/generate/progress/{jobId}`
    - [ ] `GET /api/slides/document/{documentId}`
    - [ ] `GET /api/slides/document/{documentId}/html`
  - [ ] Du kien them pipeline:
    - [ ] tai su dung `ExtractedText`, `MainTopics`, `KeyPoints`, `Summary`
    - [ ] AI tao outline slide
    - [ ] AI/renderer map outline thanh slide items
    - [ ] backend render HTML theo template co dinh
    - [ ] frontend co man hinh preview slide deck
    - [ ] export PDF di theo huong print/export tu HTML preview
  - [ ] Chot ro rang v1 khong lam PPTX.
  - [ ] Giu schema slide trung lap thap voi renderer de sau nay co the them renderer PPTX ma khong pha lai pipeline.
- [ ] Done when:
  - [ ] Document da xu ly xong moi duoc phep generate slide.
  - [ ] Progress endpoint co `queued`, `running`, `completed`, `failed`.
  - [ ] Slide deck duoc luu theo `documentId` va mo lai duoc.
  - [ ] HTML preview doc duoc tren desktop/mobile.
  - [ ] Export PDF giu dung thu tu slide va noi dung bullet.
  - [ ] OCR/LLM loi thi tra ve trang thai loi ro rang, khong im lang.

## 5. Kiem tra / acceptance checklist

- [ ] `ROADMAP.md` de doc, de sua, de mo rong them bang tay.
- [ ] Moi phase co du 4 dong co dinh:
  - [ ] `Muc tieu`
  - [ ] `Gia tri cho nguoi dung`
  - [ ] `Hang muc can lam`
  - [ ] `Done when`
- [ ] Noi dung roadmap bam sat hien trang repo hien tai, khong lap lai docs cu da lech.
- [ ] Phase slide ghi ro v1 la `HTML/PDF`, khong mo ta nhu da co san PPTX.
- [ ] Cac interface/API cho slide duoc liet ke de lam moc implementation sau nay.

## 6. Open notes / decisions

- [ ] Co giu `DemoMode.UseLocalFileStore` nhu mot che do demo that su, hay bo han de giam do phuc tap?
- [ ] Auth v1 uu tien muc nao: session cookie noi bo hay JWT?
- [ ] Slide HTML preview nen dung:
  - [ ] template server-side don gian
  - [ ] route frontend rieng cho preview
  - [ ] hoac ket hop ca hai
- [ ] PDF export v1 nen theo huong print CSS trong browser hay renderer backend?
- [ ] Co can speaker notes trong v1 hay de sang v1.1?
- [ ] Khi them PPTX sau nay, co can chot truoc slide schema versioning khong?
