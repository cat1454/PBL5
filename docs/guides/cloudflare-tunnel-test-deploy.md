# Cloudflare Tunnel Test Deploy

This guide is for a light public test/demo, not a production deployment.

## Topology

- Frontend: `https://pbl5.danangtoiiu.live`
- API: `https://pbl5-api.danangtoiiu.live`
- Cloudflare Tunnel routes both hostnames to local Docker services on the Ubuntu host.
- PostgreSQL runs in Docker.
- Ollama runs directly on the Ubuntu host.
- The API container reaches Ollama at `http://host.docker.internal:11434`.

## Host Prerequisites

Install Docker Engine, Docker Compose, Cloudflare Tunnel, and Ollama on the Ubuntu host.

Pull the default light model:

```bash
ollama pull qwen3:4b
```

On an 8 GB RAM machine, do not use `qwen2.5:7b` or `qwen3:8b` as defaults. If the host still runs out of memory, lower all `OLLAMA_*` values in `.env` to `qwen3:1.7b` and run:

```bash
ollama pull qwen3:1.7b
```

## Environment

Create a local `.env` from the example and change all placeholder secrets:

```bash
cp .env.cloudflare.example .env
```

Keep `.env` private. It is intentionally ignored by git.

Important defaults:

- `REACT_APP_API_BASE_URL=https://pbl5-api.danangtoiiu.live`
- `OLLAMA_MODEL=qwen3:4b`
- `OLLAMA_ANALYSIS_MODEL=qwen3:4b`
- `OLLAMA_GENERATION_MODEL=qwen3:4b`
- `OLLAMA_VERIFICATION_MODEL=qwen3:4b`

## Start Docker Services

```bash
docker compose -f docker-compose.cloudflare.yml up -d --build
```

Local service ports:

- Frontend: `http://127.0.0.1:8080`
- API: `http://127.0.0.1:5000`
- PostgreSQL: `127.0.0.1:5432`

Uploads are persisted in the `api_uploads` Docker volume. PostgreSQL data is persisted in the `postgres_data` Docker volume.

## Cloudflare Tunnel Routes

Configure the tunnel to route:

```yaml
ingress:
  - hostname: pbl5.danangtoiiu.live
    service: http://localhost:8080
  - hostname: pbl5-api.danangtoiiu.live
    service: http://localhost:5000
  - service: http_status:404
```

Then run or restart the tunnel service.

## Notes

- Swagger is only enabled in `Development`, so it is not public when the API runs with `ASPNETCORE_ENVIRONMENT=Production`.
- CORS allows `https://pbl5.danangtoiiu.live` in the Cloudflare Compose profile.
- The frontend production build uses `REACT_APP_API_BASE_URL`; it does not rely on localhost.
- The API applies EF Core migrations at startup, matching the current project behavior.
- This setup is sized for light demo traffic. Avoid multiple concurrent AI/OCR jobs on an 8 GB host.
