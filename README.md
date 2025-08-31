# MatchMaking System

Distributed matchmaking built with .NET 9, Kafka and Redis. The solution contains three projects (two runtime processes and one shared library):

* **MatchMaking.Service** — ASP.NET Core Web API that accepts player search requests and exposes query endpoints.
* **MatchMaking.Worker** — background worker that forms matches from a Redis-backed queue and publishes results to Kafka.
* **MatchMaking.Infrastructure** — shared library with Kafka/Redis DI extensions, common constants and other infrastructure glue (not a standalone process).

## Requirements

* .NET SDK 9.0+
* Docker + Docker Compose
* (optional) HTTP client (e.g., cURL/Postman) — Swagger UI is included

## Project structure

```
MatchMaking/
├─ MatchMaking.Service/        # Web API
├─ MatchMaking.Worker/         # Background worker
├─ MatchMaking.Infrastructure/ # Shared DI/extensions/constants library
├─ docker-compose.yml          # Kafka, Redis, API, two Worker instances
└─ README.md
```

## How it works (runtime flow)

1. **POST** `api/matchmaking/search` with JSON `{ "playerId": "..." }`.
2. API enforces a per-user **rate limit** (default **100 ms**) via Redis NX key `ratelimit:{playerId}`.

   * If within the window → **429 Too Many Requests**.
   * Otherwise → produce message `{ "userId": "..." }` to Kafka topic **`matchmaking.request`**.
3. Worker consumes **`matchmaking.request`**, pushes players to Redis list **`matchmaking_queue`**, and when it collects **N players** (N = `PlayersPerMatch`, default 3), it creates a match under a new GUID.

   * Uses a short Redis lock to avoid races: key **`matchmaking:lock`**, TTL **5 s**.
4. When a match is formed, Worker:

   * writes `user:{userId}:match` → `matchId` (**TTL 10 min**) for each participant;
   * writes `match:{matchId}` → JSON array of `userIds`;
   * appends JSON entry to `match:history:{userId}` (internal history);
   * publishes `{ matchId, userIds[] }` to Kafka **`matchmaking.complete`**.
5. Service subscribes to **`matchmaking.complete`** and ensures state for fast reads:

   * `user:{userId}:match` → `matchId` (TTL **10 min**);
   * `match:{matchId}:users` — Redis **list** of participants;
   * `match:{matchId}` — Redis **hash** with `createdAt` (ISO 8601).
6. **GET** `api/matchmaking/match/{userId}` returns the current match:

   * resolve `matchId` from `user:{userId}:match`;
   * return `userIds` from `match:{matchId}:users`; if missing, fall back to JSON in `match:{matchId}`.

## Quick start

```bash
# from repository root
docker-compose up --build
```

* API: [http://localhost:5000](http://localhost:5000) (Swagger UI available at `/swagger` in Development)
* Redis: `localhost:6379`
* Kafka: `localhost:9092`

## API

### POST /api/matchmaking/search

Enqueue a player into the matchmaking process.

Request body:

```json
{ "playerId": "player-123" }
```

Responses:

* 204 No Content — enqueued
* 400 Bad Request — invalid/missing `playerId`
* 429 Too Many Requests — rate limit window not elapsed
* 500 Internal Server Error — infrastructure failure

### GET /api/matchmaking/match/{userId}

Return the current match for a given user.

200 OK example:

```json
{ "matchId": "45ae548ed72f438dbf1af1692a699a81", "userIds": ["user-1", "user-2", "user-3"] }
```

Other responses: 400 Bad Request, 404 Not Found

## Configuration

### MatchMaking.Service (appsettings.json)

```json
{
  "Kafka": {
    "BootstrapServers": "kafka:9092",
    "RequestTopic": "matchmaking.request",
    "CompleteTopic": "matchmaking.complete"
  },
  "ConnectionStrings": {
    "Redis": "redis:6379"
  },
  "RateLimiting": {
    "Enabled": true,
    "KeyPrefix": "ratelimit:",
    "WindowMilliseconds": 100
  }
}
```

Notes:

* Rate limit is enforced per `playerId` using Redis NX key `ratelimit:{playerId}`.
* Health check: `/health`. Swagger UI at `/swagger` in Development.

### MatchMaking.Worker (appsettings.json)

```json
{
  "Kafka": {
    "BootstrapServers": "kafka:9092",
    "RequestTopic": "matchmaking.request",
    "CompleteTopic": "matchmaking.complete"
  },
  "ConnectionStrings": {
    "Redis": "redis:6379"
  },
  "Matchmaking": {
    "PlayersPerMatch": 3,
    "LockKey": "matchmaking:lock",
    "LockTtlSeconds": 5
  }
}
```

### Environment overrides (Docker)

```
ASPNETCORE_URLS=http://+:5000
ASPNETCORE_ENVIRONMENT=Development
Kafka__BootstrapServers=kafka:9092
Kafka__RequestTopic=matchmaking.request
Kafka__CompleteTopic=matchmaking.complete
ConnectionStrings__Redis=redis:6379
RateLimiting__Enabled=true
RateLimiting__KeyPrefix=ratelimit:
RateLimiting__WindowMilliseconds=100
Matchmaking__PlayersPerMatch=3
Matchmaking__LockKey=matchmaking:lock
Matchmaking__LockTtlSeconds=5
```

## Testing (via Swagger)

1. Start the stack from the repository root:

```
docker-compose up --build
```

Wait until the containers are healthy. The API runs with `ASPNETCORE_ENVIRONMENT=Development`, so Swagger is enabled.

2. Open the browser at:

```
http://localhost:5000/swagger
```

3. Send a search request:

* Open **POST /api/matchmaking/search** → Try it out → request body:

```
{ "playerId": "user-1" }
```

* Click Execute. Expected response: **204 No Content**.
* Repeat for other players (e.g., `user-2`, `user-3`). By default a match is formed when **3 players** are collected.
* If you resend the same `playerId` within \~100 ms, you will get **429 Too Many Requests** (rate limit).

4. Retrieve the formed match:

* Open **GET /api/matchmaking/match/{userId}** → Try it out → set `userId` to one of the players (e.g., `user-1`).
* Click Execute. Expected response: **200 OK** with `{ matchId, userIds }`. If the match is not formed yet — **404 Not Found**.

Note about history: there is no public endpoint for history in this build. The worker writes history to Redis (`match:history:{userId}`), but the API does not expose it.

## Implementation notes

* .NET 9, records for POCOs, `Nullable` enabled, `WarningsAsErrors` for nullable.
* Logging via Serilog; two Worker instances by default.
* Public request DTO contains **only** `playerId`.
* Matches are formed strictly by count (`PlayersPerMatch`).
* Redis TTL for `user:{userId}:match` is **10 minutes**.
