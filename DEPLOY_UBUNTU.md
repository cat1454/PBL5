# Deploy public test nhe PBL5 tren Ubuntu

Day la huong dan public test/demo nhe, khong phai production that.

## Mo hinh

- `pbl5.danangtoiiu.live` -> frontend React container port `8080`
- `pbl5-api.danangtoiiu.live` -> backend .NET container port `5000`
- PostgreSQL chay bang Docker
- Ollama chay truc tiep tren Ubuntu host
- Backend container goi Ollama qua `http://host.docker.internal:11434`

## 1. Cai Docker

```bash
sudo apt update
sudo apt install -y git docker.io docker-compose-plugin
sudo systemctl enable --now docker
```

## 2. Clone repo

```bash
mkdir -p ~/projects
cd ~/projects
git clone <repo-url> pbl5
cd pbl5
```

## 3. Tao file .env

```bash
cp .env.example .env
nano .env
```

Sua `POSTGRES_PASSWORD`, `JWT_SECRET`, va `ADMIN_PASSWORD` thanh gia tri manh. Khong commit `.env`.

## 4. Cai va chay Ollama model nhe

```bash
ollama pull qwen3:4b
ollama list
curl http://localhost:11434/api/tags
```

Khong dung mac dinh `qwen2.5:7b` hoac `qwen3:8b` cho server/laptop 8GB RAM. Neu may van cham hoac OOM, dung model nhe hon:

```bash
ollama pull qwen3:1.7b
```

Sau do sua cac bien `OLLAMA_*` trong `.env` hoac gia tri model trong `docker-compose.yml` tu `qwen3:4b` thanh `qwen3:1.7b`.

## 5. Chay app bang Docker Compose

```bash
docker compose up -d --build
```

## 6. Test local tren server

```bash
curl http://localhost:8080
curl http://localhost:5000
curl http://localhost:11434/api/tags
```

## 7. Cloudflare Tunnel routes

Them routes:

```text
pbl5.danangtoiiu.live -> http://localhost:8080
pbl5-api.danangtoiiu.live -> http://localhost:5000
```

Vi API chay `ASPNETCORE_ENVIRONMENT=Production`, Swagger khong public.

## 8. Mo web

```text
https://pbl5.danangtoiiu.live
```

Frontend production build goi API public:

```text
https://pbl5-api.danangtoiiu.live/api
```

## 9. Update code sau nay

```bash
cd ~/projects/pbl5
git pull
docker compose up -d --build
docker compose logs -f
```

## 10. Tat sleep Ubuntu laptop

```bash
sudo systemctl mask sleep.target suspend.target hibernate.target hybrid-sleep.target
```

## 11. Luu y vi day chi la public test nhe

- Chua dung cho nhieu nguoi dung that.
- Khong upload file qua lon.
- Khong chay nhieu job AI cung luc.
- Neu AI cham, giam model xuong `qwen3:1.7b`.
- Upload files duoc giu trong Docker volume `uploads_data`.
- PostgreSQL duoc giu trong Docker volume `postgres_data`.
- Neu backend restart, job progress dang chay co the mat neu project van dung in-memory progress store.
