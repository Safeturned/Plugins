# Safeturned Module Design (Draft)

Intent: a single Unturned module (no extra libs) that watches plugin folders, hashes DLLs, and uploads to Safeturned automatically. Target environments: Vanilla module load (earliest), but watches Rocket/Plugins, OpenMod/plugins, Modules/** by default.

## Core responsibilities
- Config (file-based): API key, API base URL, scan interval, watched paths (defaults), include/exclude patterns, `forceAnalyze` flag, log level.
- Discovery: hash DLLs, persist cache to disk, skip unchanged; detect/flag external dependencies.
- Upload: UnityWebRequest (no HttpClient) to `/v1.0/files`; add `X-API-Key` + `X-Client: safeturned-module`; honor `Retry-After`.
- Rate limit: local token bucket per API key seeded from `users/me/usage/rate-limit`; adjust from `X-RateLimit-*`; queue when low; persist bucket+cache to disk.
- Auto-update: fetch loader metadata from API (`/v1.0/loaders?framework=module&configuration=Release`); compare version/hash; optional auto-download/apply.
- UX: console logs; no webhook config here (notifications handled by backend/site).

## CI/CD (planned)
- GitHub Actions (public runners) on tag: build Release, zip `Safeturned.Module.zip`, publish release asset. Hook GitHub webhook to API to ingest metadata and mark latest.

## Open items before coding
- Confirm target runtime/framework for the module (Unity/Unturned-compatible, netstandard2.1).
- Cache file location/format: JSON under module folder (hash cache + rate-limit cache).
- Confirm default watched paths and exclusions for each framework variant.
- Finalize auto-update flow (self-replace vs staged download).

## Next steps
- Scaffold module project structure and config model. ✅ (initial)
- Implement cache + bucket utilities (Unity-friendly). ✅ (initial)
- Add CI workflow and wire API loader metadata endpoint usage.
