# ScoramAPI – observability and health (non-functional additions)

## Endpoints
| Path | Meaning | Touches DB | Use it for |
|---|---|---|---|
| `GET /health` (unchanged) | Liveness: process is up | No | Render `healthCheckPath` – a slow DB must never restart the container |
| `GET /health/ready` (new) | Readiness: DB reachable | Yes, cached 5 s, 3 s probe timeout | Deploy gates, uptime monitors, manual diagnosis. Returns 200 `{"status":"ready","database":"ok"}` or 503 `{"status":"unavailable","database":"unreachable"|"timeout"}`. No connection details are ever returned. |

## Response headers on every request
* `X-Trace-Id` – matches the `traceId` in error bodies and the log lines for that request.
* `Server-Timing: app;dur=<ms>` – server time. Compare with the client-side duration to separate network from server time.

## Configuration (all optional; absent = defaults)
* `Observability:SlowRequestMs` (default 1500) – requests slower than this log a warning with route template, status, duration. `/hubs` and WebSockets are skipped.
* `Observability:SlowQueryMs` (default 500) – EF commands slower than this log kind, duration, parameter count and the first 300 chars of SQL. **Parameter values are never logged.**

## Hosting notes
* Render free/starter instances sleep when idle, so the first request after idle is slow. The only real fixes are an always-on plan or a paid keep-warm monitor. This change does **not** add a self-ping loop (it would burn instance hours and is unreliable). If you want keep-warm, point an external uptime monitor at `/health`, not `/health/ready`.
* `Scripts/OptionalPerformanceIndexes.sql` is an optional, idempotent, manually-run script. Test on staging first.
