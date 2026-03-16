# Huong dan chay ELearn Game Platform

## 1. Yeu cau truoc khi chay

- .NET SDK 8.0+
- Node.js 18+
- PostgreSQL 14+
- Ollama
- Model `llama3.2`
- Tesseract tessdata:
  - `eng.traineddata` bat buoc
  - `vie.traineddata` khuyen nghi neu OCR tai lieu tieng Viet

## 2. Cau hinh backend

File cau hinh chinh:

- `src/ELearnGamePlatform.API/appsettings.json`

Can kiem tra:

- connection string PostgreSQL
- `OllamaSettings`
- `FileUpload`

Vi du:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=ELearnGameDB;Username=postgres;Password=..."
  },
  "OllamaSettings": {
    "BaseUrl": "http://localhost:11434",
    "Model": "llama3.2",
    "TimeoutSeconds": 60,
    "Temperature": 0.3
  },
  "FileUpload": {
    "MaxFileSizeInMB": 50,
    "AllowedExtensions": [".pdf", ".docx", ".png", ".jpg", ".jpeg"]
  }
}
```

## 3. Chay Ollama

```powershell
ollama pull llama3.2
ollama list
```

Neu `ollama list` khong tra ve model, he thong sinh cau hoi va phan tich AI se khong chay dung.

## 4. Chay backend

```powershell
cd H:\pbl5\src\ELearnGamePlatform.API
dotnet restore
dotnet run
```

Backend mac dinh:

- `http://localhost:5000`

Swagger co san khi chay development.

## 5. Chay frontend

Mo terminal moi:

```powershell
cd H:\pbl5\client
npm install
npm start
```

Frontend mac dinh:

- `http://localhost:3000`

## 6. Luong su dung

### Upload tai lieu

- Ho tro: `PDF`, `DOCX`, `PNG`, `JPG`, `JPEG`
- Server-side se validate:
  - file rong / khong co file
  - file vuot qua gioi han dung luong
  - extension khong nam trong `FileUpload.AllowedExtensions`

### Xu ly tai lieu

Sau khi upload:

1. Luu file vao `uploads/`
2. Tao `Document`
3. Chay background processing
4. Trich xuat text
5. Phan tich AI
6. Cap nhat status

### Tao cau hoi

Frontend hien tai dung luong:

- `POST /api/questions/generate/start`
- `GET /api/questions/generate/progress/{jobId}`

Khi xong, lay cau hoi theo document:

- `GET /api/questions/document/{documentId}`

### Hoc tap

- Quiz: `GET /api/games/quiz/{documentId}`
- Flashcards: `GET /api/games/flashcards/{documentId}`

## 7. OCR va PDF scan

### Tesseract

He thong se doc tessdata trong:

- `src/ELearnGamePlatform.API/tessdata/` khi chay local
- hoac `bin/.../tessdata/` sau build

Hanh vi hien tai:

- neu co `eng.traineddata` va `vie.traineddata`: dung `eng+vie`
- neu thieu `vie.traineddata`: fallback sang `eng`
- neu thieu ca hai: OCR co the fail

### Poppler / pdftoppm

De OCR PDF scan, he thong can `pdftoppm`.

Service hien tai se thu theo thu tu:

1. Poppler bundled trong repo: `poppler-25.12.0/Library/bin/pdftoppm.exe`
2. `pdftoppm` trong PATH

Neu ca hai khong co, OCR PDF scan se that bai.

## 8. Cac han che hien tai

- Chua co auth that su
- Frontend dang hardcode `demo-user`
- `local-store` chi la du lieu mau, khong phai runtime source
- Job progress dang o RAM cua process
- Neu restart backend trong luc generate question, progress co the mat

## 9. Build kiem tra

Backend:

```powershell
dotnet build H:\pbl5\ELearnGamePlatform.sln
```

Frontend:

```powershell
cd H:\pbl5\client
npm run build
```

## 10. Loi thuong gap

### Khong upload duoc file

- Kiem tra extension co nam trong `FileUpload.AllowedExtensions`
- Kiem tra file co vuot qua `FileUpload.MaxFileSizeInMB`

### OCR tieng Viet ra ket qua xau

- Them `vie.traineddata` vao `tessdata`
- Dung anh ro net hon
- Neu la PDF scan, kiem tra `pdftoppm`

### Generate question ra fallback/default

- Kiem tra Ollama dang chay
- Kiem tra model `llama3.2` da pull
- Kiem tra document da o status `Completed`

### Frontend khong goi duoc backend

- Kiem tra backend dang chay o `http://localhost:5000`
- Kiem tra `client/src/services/api.js`
- Kiem tra CORS trong `Program.cs`
