# ELearn Game Platform

ELearn Game Platform は、学習資料をインタラクティブな学習体験に変換する `.NET + React` の MVP です。資料のアップロード、OCR/テキスト抽出、ローカル AI 分析、問題生成、クイズ/フラッシュカード/ストリーク/模擬テスト、進捗管理、ワークスペース/フォルダー管理、スライドデッキ生成、プレビュー、編集、エクスポートを扱います。

このリポジトリは現在、**PBL デモ向けの MVP+** です。主要なフローはエンドツーエンドでデモできますが、本番運用向けではありません。ジョブ進捗はまだメモリ上にあり、バックグラウンド処理はデモ向けで、自動テストとセキュリティ強化もまだ十分ではありません。

## 現在の機能

- **JWT 認証**: 登録、ログイン、現在のユーザー取得、基本ロール `ADMIN`、`INSTRUCTOR`、`LEARNER`。
- **ドキュメント処理**: `PDF`、`DOCX`、`PNG`、`JPG`、`JPEG` のアップロード、ファイル検証、PdfPig/OpenXML/Tesseract によるテキスト抽出、OCR クリーンアップ、Ollama による summary、topics、key points、language、structure、coverage metadata の分析。
- **問題生成**: ドキュメントから問題を生成し、ジョブ進捗を追跡し、検証と 1 回の auto-repair を行い、PostgreSQL に保存します。
- **Question Studio**: 生成 run の作成、draft のレビュー、編集、accept/reject/quarantine/restore、承認済み draft の question bank への import。
- **学習フロー**: quiz、flashcards、streak mode、game session、practice test、learning attempts、review queue、document 単位の progress summary。
- **Analytics**: 実データに基づく personal dashboard、heatmap、skill/radar、checklist、recent activity。
- **Workspace/Folder**: workspace/folder の作成、複数 source のアップロード、slide 用 source/section の選択、学習資料の管理。
- **Slide Studio**: document または workspace/folder から slide を生成し、content scope を選択し、HTML preview、slide item 編集、image candidate の refresh/select、HTML export、ブラウザー Print/Save as PDF、basic PPTX export を行います。

## 技術スタック

### Backend

- ASP.NET Core Web API、project target は `net8.0`
- `global.json` で固定された .NET SDK: `9.0.306`
- Entity Framework Core 8
- PostgreSQL + Npgsql
- JWT Bearer Authentication
- Tesseract OCR、PdfPig、OpenXML、ImageSharp
- Ollama による local AI、デフォルト model は `qwen2.5:7b`

### Frontend

- React 18
- React Router DOM 6
- Axios
- React Scripts
- React Icons

### Runtime

- Backend: `http://localhost:5000`
- Swagger: `http://localhost:5000/swagger`
- Frontend dev server: `http://localhost:3000`
- Frontend proxy: `http://127.0.0.1:5000`
- Database: EF Core migrations を使う PostgreSQL

## リポジトリ構成

```text
src/
  ELearnGamePlatform.API/             Web API, controllers, DI, config, auth, runtime uploads
  ELearnGamePlatform.Core/            Entities, enums, interfaces, shared contracts
  ELearnGamePlatform.Infrastructure/  DbContext, repositories, migrations, Ollama integration
  ELearnGamePlatform.Services/        OCR, document processing, AI, question, slide services

client/
  public/                             Static frontend entrypoint
  src/                                React frontend source

tests/                                Automated test projects
docs/                                 Guides, research, working notes, agent context
benchmarks/                           OCR/document benchmark harness
scripts/                              Repo helper scripts
```

`artifacts/`、`.artifacts/`、`.tmp/`、`commit-history/`、`src/ELearnGamePlatform.API/uploads/`、`src/ELearnGamePlatform.API/logs/`、`src/ELearnGamePlatform.API/tessdata/*.traineddata`、`poppler-25.12.0/` などは local/generated data または runtime asset であり、主要な source file ではありません。

## 環境要件

事前にインストールしてください:

- .NET SDK `9.0.306`
- Node.js 18+
- PostgreSQL 14+
- Ollama
- Tesseract OCR
- Git

OCR service は次の場所で tessdata を探します:

```text
src/ELearnGamePlatform.API/tessdata
```

最低限、次のファイルを推奨します:

```text
eng.traineddata
vie.traineddata
```

スキャン PDF では、`poppler-25.12.0/Library/bin/pdftoppm.exe` が存在する場合は local Poppler を使い、存在しない場合は `PATH` 上の `pdftoppm` に fallback します。

## ローカル設定

Backend の主な設定ファイル:

```text
src/ELearnGamePlatform.API/appsettings.json
```

placeholder のみを使った local 設定例:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=ELearnGameDB;Username=postgres;Password=YOUR_LOCAL_PASSWORD;SslMode=disable"
  },
  "JwtSettings": {
    "Issuer": "ELearnGamePlatform",
    "Audience": "ELearnGamePlatform.Client",
    "SecretKey": "CHANGE_THIS_TO_A_LONG_LOCAL_DEV_SECRET",
    "ExpirationMinutes": 10080
  },
  "AdminSeed": {
    "Enabled": true,
    "Email": "admin@example.local",
    "Password": "CHANGE_THIS_ADMIN_PASSWORD",
    "FullName": "System Admin"
  },
  "OllamaSettings": {
    "BaseUrl": "http://localhost:11434",
    "Model": "qwen2.5:7b",
    "AnalysisModel": "qwen2.5:7b",
    "GenerationModel": "qwen2.5:7b",
    "VerificationModel": "qwen2.5:7b"
  }
}
```

本物の password、API key、個人設定を repository に commit しないでください。

## ローカル実行

リポジトリを clone します:

```powershell
git clone https://github.com/cat1454/PBL5.git
cd PBL5
```

PostgreSQL database を作成します:

```sql
CREATE DATABASE "ELearnGameDB";
```

Ollama model を準備します:

```powershell
ollama pull qwen2.5:7b
ollama list
```

Backend を起動します:

```powershell
cd src\ELearnGamePlatform.API
dotnet restore
dotnet run
```

別の terminal で frontend を起動します:

```powershell
cd client
npm install
npm start
```

アプリを開きます:

```text
http://localhost:3000
```

## 推奨デモフロー

1. 登録またはログインします。
2. workspace を作成するか default workspace を使います。
3. PDF/DOCX/画像 document をアップロードします。
4. document processing の完了を待ちます。
5. analysis、structure、OCR text を確認します。
6. Question Studio を開くか、直接 questions を生成します。
7. Question Studio を使う場合は draft を review/import します。
8. quiz、flashcards、streak、practice test で学習します。
9. analytics と learning progress を確認します。
10. Slide Studio を開き、scope を選び、deck を生成し、preview/edit します。
11. HTML、browser Print/Save as PDF、basic PPTX として export します。

## 主な API グループ

login/register を除き、ほとんどの endpoint は JWT Bearer token が必要です。

```text
Auth
POST   /api/auth/register
POST   /api/auth/login
GET    /api/auth/me

Admin
GET    /api/admin/overview

Documents
POST   /api/documents/upload
GET    /api/documents/{id}
GET    /api/documents/{id}/progress
GET    /api/documents/{id}/structure
GET    /api/documents/{id}/understanding/latest
POST   /api/documents/{id}/analyze-structure
GET    /api/documents/user/{userId}
DELETE /api/documents/{id}

Workspaces / Folders
POST   /api/workspaces
GET    /api/workspaces/user/{userId}
GET    /api/workspaces/default/user/{userId}
GET    /api/workspaces/{id}
DELETE /api/workspaces/{id}
POST   /api/workspaces/{id}/sources/upload
GET    /api/workspaces/{id}/sources
PUT    /api/workspaces/{id}/sources/{sourceId}/slide-selection
POST   /api/folders
GET    /api/folders/user/{userId}
GET    /api/folders/{id}
DELETE /api/folders/{id}
POST   /api/folders/{id}/sources/upload
GET    /api/folders/{id}/sources
PUT    /api/folders/{id}/sources/{sourceId}/slide-selection

Questions
POST   /api/questions/generate/start
GET    /api/questions/generate/progress/{jobId}
POST   /api/questions/generate
GET    /api/questions/document/{documentId}
GET    /api/questions/document/{documentId}/metrics
GET    /api/questions/{id}
PUT    /api/questions/{id}
DELETE /api/questions/{id}

Question Studio
POST   /api/question-studio/runs/start
GET    /api/question-studio/runs/{runId}
GET    /api/question-studio/drafts
PUT    /api/question-studio/drafts/{draftId}
POST   /api/question-studio/drafts/{draftId}/accept
POST   /api/question-studio/drafts/{draftId}/reject
POST   /api/question-studio/drafts/{draftId}/quarantine
POST   /api/question-studio/drafts/{draftId}/restore
POST   /api/question-studio/import

Games / Learning
POST   /api/games/sessions
GET    /api/games/sessions/{sessionId}
POST   /api/games/sessions/{sessionId}/start
POST   /api/games/sessions/{sessionId}/submit
GET    /api/games/quiz/{documentId}
POST   /api/games/quiz/{documentId}/answers
GET    /api/games/flashcards/{documentId}
GET    /api/games/user/{userId}
POST   /api/learning/attempts
POST   /api/learning/tests/start
POST   /api/learning/tests/submit
GET    /api/learning/tests/document/{documentId}
GET    /api/learning/tests/summary/{documentId}
GET    /api/learning/progress/document/{documentId}
GET    /api/learning/review-queue/{documentId}
GET    /api/learning/progress/summary/{documentId}
GET    /api/learning/export/attempts.csv
GET    /api/learning/export/progress.csv
GET    /api/learning/export/test-results.csv

Analytics
GET    /api/analytics/personal
POST   /api/analytics/events

Slides
POST   /api/slides/generate/start
POST   /api/slides/folders/{folderId}/generate/start
GET    /api/slides/generate/progress/{jobId}
GET    /api/slides/document/{documentId}
GET    /api/slides/document/{documentId}/html
GET    /api/slides/folders/{folderId}
GET    /api/slides/folders/{folderId}/html
GET    /api/slides/{deckId}/export/html
GET    /api/slides/{deckId}/export/print
GET    /api/slides/{deckId}/export/pptx
PUT    /api/slides/{deckId}/items/{itemId}
POST   /api/slides/{deckId}/items/{itemId}/images/refresh
POST   /api/slides/{deckId}/items/{itemId}/images/select
```

## merge/push 前の確認

Backend:

```powershell
dotnet build ELearnGamePlatform.sln
```

Frontend:

```powershell
cd client
npm run build
```

必要に応じて frontend test:

```powershell
cd client
npm test -- --watchAll=false
```

## 現在の制限

- 本番運用向けではありません。
- Authentication は local/demo JWT レベルで、refresh token、password reset、email verification、rate limit、完全な audit log は未実装です。
- Job progress は in-memory のため、backend restart で実行中 job の状態が失われる可能性があります。
- Background processing はまだ簡易 runtime task ベースで、durable queue ではありません。
- AI 品質は local Ollama model、マシン性能、入力 document の品質に依存します。
- Slide PDF export は browser Print/Save as PDF flow を使い、backend の binary PDF rendering ではありません。
- PPTX export は basic で、HTML preview と pixel-perfect ではありません。

## 関連ドキュメント

- [Docs Index](./docs/README.md)
- [Architecture](./docs/guides/ARCHITECTURE.md)
- [Run Guide](./docs/guides/RUN_GUIDE.md)
- [Frontend Handoff](./docs/guides/FRONTEND_HANDOFF.md)
- [Roadmap](./docs/guides/ROADMAP.md)
- [Agent Context](./docs/agent/PROJECT_CONTEXT.md)
