# CSharpApp — a proxy API over the Platzi Store

A .NET 10 service that fronts the public [Platzi fake-store API](https://api.escuelajs.co/api/v1/), authenticating to it with JWT and re-publishing products and categories behind its own versioned, validated, observable contract. It started as the provided scaffold; the original exercise statement is preserved at [docs/TASK.md](docs/TASK.md).

**What the exercise asked for, and where it lives**

| Asked | Code | Explained in |
|---|---|---|
| Refactor the HTTP client | `CSharpApp.Infrastructure/Configuration/HttpConfiguration.cs` | HTTP and resilience |
| Products `getOne` and `create` | `CSharpApp.Api/Endpoints/ProductEndpoints.cs` | Endpoints |
| Categories | `CSharpApp.Api/Endpoints/CategoryEndpoints.cs` | Endpoints |
| JWT auth to the upstream | `Infrastructure/Http/AuthTokenHandler.cs`, `AccessTokenProvider.cs` | Authentication |
| Performance middleware | `CSharpApp.Api/Middleware/RequestTimingMiddleware.cs` | Observability |
| Health checks | `Api/Endpoints/HealthEndpoints.cs`, `Infrastructure/HealthChecks/` | Observability |
| CQRS | `CSharpApp.Application/{Products,Categories}/` | API shape |

**Jump to:** [How to run](#how-to-run) · [Configuration](#configuration) · [Architecture](#architecture) · [Design decisions](#design-decisions) · [Security notes](#security-notes) · [Performance notes](#performance-notes) · [AI usage](#ai-usage)

---

## How to run

### Prerequisites

- .NET 10 SDK. Built and tested here with 10.0.102; `global.json` sets a 10.0.100 floor and rolls forward, so a newer SDK is fine and an older one fails with a clear toolchain error rather than a confusing project error.
- Docker (optional, only for the container route)

### Locally

```bash
dotnet run --project CSharpApp.Api
```

Listens on `http://localhost:5225`. If that port is taken, pass `--urls http://localhost:5300`; the `ASPNETCORE_URLS` variable on its own is overridden by the launch profile. The OpenAPI document is served at `/openapi/v1.json` in Development.

The console is human-readable in Development and compact JSON everywhere else. To watch the slow-request warning fire, lower the threshold:

```bash
PerformanceLoggingSettings__SlowRequestThresholdMs=50 dotnet run --project CSharpApp.Api
```

`CSharpApp.Api/CSharpApp.Api.http` holds a ready request per endpoint for Rider or Visual Studio.

### Tests

```bash
dotnet test CSharpApp.slnx
```

289 tests, all offline. xUnit, with hand-rolled test doubles rather than a mocking framework, so the assertions are about behaviour rather than about a mock. No test touches the internet: unit tests drive a scripted message handler, and integration tests boot the real application with an in-memory upstream behind every HTTP client.

### In Docker

```bash
docker compose up --build
```

Listens on `http://127.0.0.1:8080`. Credentials come from the environment, so a different account needs no rebuild:

```bash
PLATZI_USERNAME=someone@example.com PLATZI_PASSWORD=secret docker compose up
```

Without compose, keep the loopback binding, because the service is an unauthenticated proxy holding the upstream credentials:

```bash
docker build -t csharpapp . && docker run --rm -p 127.0.0.1:8080:8080 csharpapp
```

### Endpoints

| Method | Route | Notes |
|---|---|---|
| `GET` | `/api/v1/products` | `limit` and `offset` must be supplied together |
| `GET` | `/api/v1/products/{id}` | `404` when the upstream does not know the id |
| `POST` | `/api/v1/products` | `201` with a `Location` header |
| `GET` | `/api/v1/categories` | |
| `GET` | `/api/v1/categories/{id}` | `404` when the upstream does not know the id |
| `POST` | `/api/v1/categories` | `201` with a `Location` header |
| `GET` | `/health/live` | Liveness: this process, no dependencies |
| `GET` | `/health/ready` | Readiness: also probes the upstream |
| `GET` | `/openapi/v1.json` | Development only |

The two resource groups are rate-limited per caller; the health endpoints are not, so an orchestrator probe is never throttled. Every response carries a `Server-Timing` header with the measured duration, and every failure is an RFC 9457 problem details document.

### Configuration

Every key below is bound and validated at startup, and an unknown key under any of these sections fails the process rather than being ignored, so a misspelled override is caught instead of silently disabling a retry. As environment variables the separator is a double underscore, for example `RestApiSettings__BaseUrl`.

| Section and key | Meaning | Default |
|---|---|---|
| `RestApiSettings:BaseUrl` | Upstream root. Must be https unless the host is loopback | the public sandbox |
| `RestApiSettings:Products` `:Categories` `:Auth` | Resource paths, resolved against the root | `products`, `/categories`, `/auth/login` |
| `RestApiSettings:Username` `:Password` | Upstream credentials | the sandbox's published demo account |
| `HttpClientSettings:LifeTime` | Pooled handler lifetime, minutes | 10 |
| `HttpClientSettings:RetryCount` | Retries per upstream call; 0 disables them | 2 |
| `HttpClientSettings:SleepDuration` | Base retry delay, milliseconds | 100 |
| `PerformanceLoggingSettings:SlowRequestThresholdMs` | Above this a request logs as a warning | 1000 |
| `RateLimitingSettings:PermitLimit` | Requests per caller per window | 100 |
| `RateLimitingSettings:WindowSeconds` | Length of the window | 60 |

---

## Architecture

Four projects, and the dependency arrows match the description rather than the other way round.

- **Core** — DTOs, settings with their validation attributes, the interfaces the other layers talk through. Depends on nothing.
- **Application** — one folder per business capability, one file per operation holding the request, its FluentValidation rules and its MediatR handler. Depends on Core.
- **Infrastructure** — the typed HTTP client, the JWT delegating handler and token cache, the resilience pipelines, the health check, the DI wiring for all of it. Depends on Core.
- **Api** — minimal API endpoints, versioning, the timing middleware, the exception handler, the OpenAPI transformer. Depends on Application and Core, and on Infrastructure as well, because it is the composition root and the one place allowed to name concrete wiring. Beyond registration it touches exactly two things from there: the health check's name and the serialization context. Nothing but review currently stops an endpoint injecting the store client directly and bypassing the pipeline; an architecture test asserting that is the first guard I would add.

A request flows: endpoint → MediatR pipeline (validation behaviour) → handler → typed client (auth handler, then the resilience pipeline) → upstream.

The upstream's shapes and the shapes this service publishes are separate types. `CSharpApp.Infrastructure/Http/Contracts/` holds the wire format as it actually arrives, all of it optional because that is the truth about a schema nobody here controls; `CSharpApp.Core/Dtos/` holds what this service publishes. Nothing outside the infrastructure layer ever sees an upstream type.

Build configuration is centralised rather than repeated: `Directory.Build.props` carries the properties every project shares, and `Directory.Packages.props` carries every package version, so a version moves in one place and six project files stop restating the same properties. The Core project is now a single SDK declaration with nothing else in it. The container copies both files before restoring, which was verified by removing that line and watching the restore fail.

---

## What the scaffold got wrong

Found by reading the code and by running it, and fixed in the commit history rather than rewritten away:

- `Product.Images` was a get-only `List<string>`. System.Text.Json silently skips get-only collections when reading, so every product came back with an empty image array. A serializer test failed before the fix and passes after it.
- `Price` was `int?` against an upstream schema that says number, on a sandbox anyone can write to. One fractional price would have broken the whole catalogue. It is `decimal?` now.
- The provided configuration mixes `products` and `/categories`. Resolved against a base address, the leading slash escapes the `/api/v1/` path entirely, and the live sandbox answers 404 for the escaped URL. Paths are now validated for shape at startup and normalised once.
- The service logged one interpolated line per item returned, so a single list request wrote dozens of near-identical lines.
- `BuildServiceProvider()` was called inside service registration, which builds a second container: duplicated singletons, configuration captured before later sources are added, and failure moved from startup to the first request.

## Design decisions

### HTTP and resilience

- The scaffold created a `new HttpClient` per service instance and assigned `BaseAddress` on every call, so it served one request per process: `HttpClient` forbids changing `BaseAddress` after the first send. The singleton registration is what kept it from the classic per-call socket exhaustion, but it bought the opposite failure: a handler that is never recycled, so a DNS change is never observed. It was also one word from the first problem, because changing that lifetime to scoped produces socket exhaustion with nothing in the code to stop it. Replaced with `IHttpClientFactory`: a typed client with pooled handlers recycled on the configured lifetime, plus the standard resilience pipeline driven by the settings the exercise already provided.
- Retries never repeat a write: on the store client only idempotent methods are retried, because a retried `POST` whose first attempt was processed but whose answer was lost creates a duplicate entity. The login call may retry its `POST`; it has no side effects. Both behaviours are pinned by tests that count attempts through the real pipeline.
- The retry budget is stated in code rather than inherited from library defaults: 30 s total per call, 10 s per attempt, no single back-off above 5 s. `RetryCount: 0` genuinely disables retries instead of being silently clamped to one.
- Three named clients, deliberately: the store client carries the token and the full pipeline, the login client carries neither the token nor recursion into itself, and the health probe carries no token, no retries and a three-second timeout.
- JSON is System.Text.Json with source generation: compile-time serializers, no reflection on the hot path.

### Authentication to the upstream

- The exercise states the upstream supports JWT and that we must support it, so every store call carries a bearer token, including the endpoints the sandbox happens to answer anonymously. It is a stated requirement rather than an optimisation.
- Authentication is a `DelegatingHandler`, so no caller, handler or endpoint ever sees a token. It sits outside the resilience pipeline, so its single 401 refresh-and-resend stays one deliberate retry instead of being multiplied by transient retries.
- The token is cached process-wide until its own `exp` claim minus a 60-second safety window, and acquisition is single-flight: a cold start with ten concurrent requests performs exactly one login. An integration test proves it by holding the login open and reading the counter while it is still in flight.
- On a 401 the handler drops the cached token and replays the request once with a fresh one, from a copy made before the first send, so bodies and content headers survive. A second 401 reaches the caller instead of looping.
- The clock is injected as `TimeProvider` rather than read from `DateTimeOffset.UtcNow`, so the safety window is testable directly: a test caches a token, moves the clock across the window and asserts that exactly one further login happens. Before that, the only way to exercise the window was to mint a nearly expired token and hope the wall clock cooperated.
- A refresh-token flow was rejected on purpose: this service owns the credentials, so a second secret to store and rotate buys nothing. It is listed under future work for the day the service no longer holds the password.

### API shape

- CQRS through MediatR — the packages were referenced by the original author but never wired, so the direction was clearly intended. Endpoints translate HTTP into a query or a command and back; every decision about data lives in a handler that knows nothing about HTTP.
- Validation runs in a MediatR pipeline behaviour **before** any handler, so an invalid request never spends an upstream call or upstream quota. Failures are keyed by the JSON names the caller actually sent, so a 400 points at the request body rather than at our internal command shape.
- The Application layer is organised by capability, not by CQRS half: a folder is a capability, a file is an operation, and which side of CQRS a request is on is carried by its name and return type. A `Commands`/`Queries` split would cut one capability across two trees and enforce nothing, because MediatR discovers handlers from assembly metadata and never sees a folder. The rule for revisiting it is written down: when two files in one capability pass roughly 120 lines, that capability is promoted to a folder per operation.
- Responses carry the versions the service supports, so a client discovers the version surface from the answer rather than from this document, which is what makes a deprecation window possible later.
- Routes moved from the RPC-style `/api/v1/getproducts` to resource-style `/api/v1/products`. A breaking change made deliberately, while there are no consumers and inside a versioned URL space.
- The scaffold referenced the deprecated Polly v7 integration package and never wired it up. It was replaced with the first-party v8 resilience pipeline, which is where the retry, timeout and circuit-breaker configuration now lives.
- The store client is one interface with six methods, which is a real interface-segregation cost: a category handler depends on a type that also exposes three product methods. It is paid for one transport client, one resilience pipeline and one set of wire tests. A catalogue interface per resource over the same implementation is the next step.
- Paging is validated rather than forwarded blindly, because probing the upstream showed it ignores `limit` without `offset` and would silently return the whole catalogue to a caller who asked for a page. Categories publish no paging at all: the upstream honours a `limit` there but ignores `offset`, and a limit without a working offset is truncation, not pagination.

### Mapping across the upstream boundary

- The scaffold used one set of types for two jobs: deserializing the upstream and serving this service's own responses. That is why every published field was optional, and it meant an upstream rename would have reached clients directly. The two are now separate types with an explicit mapping between them.
- The mapping is Mapster by convention: the names match on both sides, so writing each member out by hand would be work that buys nothing and rots the moment either side changes. The register declares the two pairs and nothing else, and the call site is the `Adapt` extension rather than an injected mapper, because a pure shape transformation is not a collaborator worth threading through constructors.
- The usual objection to convention is that adding a property becomes a silent no-op. A test answers it: it builds the real registrations under strict mode, where every published member must have an upstream source, and fails if one does not. A second test feeds that same guard a deliberately unmappable pair, so the first cannot pass vacuously.
- That check is a test rather than a startup assertion on purpose. Configuration validation belongs at startup because configuration differs per environment and is only knowable there; a mapping is the same in every environment, so a test catches the mistake earlier, cheaper, and without a class of production boot failure that has no environmental cause. The startup step compiles the mapping so the first request does not pay to build it.
- Note what this check is not: it compares two C# types, so an upstream that renames a JSON field does not trip it. That shows up as a null in a response, and the integration tests that assert on real upstream payload shapes are what guard it.
- What this does not yet buy: the published shapes are still as nullable as the upstream's, so the published schema still guarantees a client little. The seam is what makes tightening them a local change, and that is listed under future improvements.

### Errors

- Every failure becomes an RFC 9457 problem details response from a single exception handler, and the status says whose fault it was: 400 for validation, 422 for a well-formed request the upstream refused, 502 for an unreachable or misbehaving upstream, 504 for a timeout, 503 for an open circuit or a rate limit.
- The 422-versus-502 distinction came from probing the upstream, which answers 400 with a not-found envelope when a create references a missing category. Fault is decided where it is known — in the HTTP client — rather than guessed from a status code, so only a create's 4xx can become the caller's 422 while a 4xx on a read stays the gateway's problem.
- Nothing internal reaches a caller: no exception message, no stack trace, no upstream body. Those go to the log, with the upstream body truncated to 500 characters. Every problem response carries the trace id, so a support conversation can find the log line without exposing it.

### Rate limiting

- The service is rate-limited with the built-in fixed-window limiter, because it multiplies inbound traffic onto a third party: an unthrottled caller spends someone else's quota, and the upstream's opinion of that arrives as a 429 for everybody.
- The allowance is counted per caller rather than once for the whole service, so one noisy client cannot lock everyone out. Callers are told apart by remote address, normalised so that a dual-stack listener keys the same caller the same way and an IPv6 caller is counted per network allocation rather than per address, since a single caller holds 2^64 of those. Anything that rewrites the source address collapses every caller into one allowance: a reverse proxy, a load balancer, and the published port of the compose file in this repository. Such a deployment has to configure forwarded headers first.
- A refused request is a problem details document with a `Retry-After` header, the same contract as every other failure, so no client has to special-case it. Nothing is queued: a caller already over budget learns it now instead of paying in latency.
- The window is fixed rather than sliding, which is the cheapest option and has a known edge: the counter resets whole, so a caller can spend the allowance at the end of one window and again at the start of the next, and up to twice the allowance lands in a span shorter than one window. Measured here with an allowance of three per two seconds: three requests crossed the boundary in 0.84 s, about 2.4 times the nominal rate. A sliding window removes it and is listed under future work.
- Health endpoints are deliberately exempt. A throttled readiness probe would take an instance out of rotation for the sin of being probed.
- Counters are per instance. Behind more than one replica the effective allowance multiplies by the replica count; a shared store is the follow-up and is listed under future work.
- Known limits, stated rather than implied: one allowance covers both resource groups, so a caller that exhausts it on categories is also refused on products; there is no exemption for a trusted caller; nothing outside the two groups is limited at all, which is deliberate, because an unmatched path costs the upstream nothing; and `Retry-After` reports the full window rather than the time left in it, because the limiter does not expose the remainder. It errs long, never short.

### Observability

- A dedicated timing middleware sits outermost among the application's own middleware — outside the exception handler, so the measurement and the logged status include error handling. Placed inside, every failed request would be logged as a 200.
- One structured event per request with method, route pattern, status, elapsed milliseconds and path as separate fields. The **route pattern** is the aggregation key because its cardinality is bounded; unmatched requests share one key rather than giving every scanner probe its own series. The query string is never logged: it is where callers put secrets.
- The duration also returns to the caller in a `Server-Timing` header — the standard header browser developer tools already render — rather than a custom `X-` header, which RFC 6648 deprecated.
- The statement asks for a health check for the api and self. Self is `/health/live`, this process, which evaluates no dependency checks; the api is the upstream, checked under the name `upstream_api` on `/health/ready`.
- Two health endpoints for two questions. `/health/live` evaluates no checks, so a restart is only ever triggered by the process itself being stuck. `/health/ready` also probes the upstream. An unreachable upstream makes readiness `Degraded`, and `Degraded` still answers 200: this service without its upstream is impaired, not dead, and taking every instance out of rotation for a third party's outage would turn a partial failure into a total one.
- Readiness proves the upstream is reachable, not that our credentials still work. The probe is deliberately anonymous, because depending on the login would turn a login outage into a login storm, so a rotated credential shows as 502s behind a green probe. Surfacing the token provider's last login outcome as a degraded reason is listed under future work.
- Outbound calls are deliberately not timed a second time: the framework's own HTTP client category already records per-call durations and is held at warning here, so the request log is not doubled.
- Logs are shaped for whoever reads them: a plain timestamped line in Development, compact JSON in production for a log pipeline. Each console sink is declared in exactly one environment file, because configuration sections merge rather than replace and a sink declared twice would leave the logging library's overload selection deciding the format. The consequence is that an environment with no settings file would have no sink, so the application refuses to start instead of serving traffic silently.

### What .NET 10 buys this service

New in 10 and used here: the OpenAPI document is produced at version 3.1.1 through the consolidated `Microsoft.OpenApi` 2.x model, which is what the version transformer rewrites paths in; the solution is in the SDK's `.slnx` format; and the compiler-generated entry point is public, so the partial declaration in `Program.cs` documents the integration tests' dependency rather than unlocking it.

Current-platform rather than new in 10, and load-bearing: source-generated JSON and configuration binding, `TypedResults`, the built-in rate limiter, `IExceptionHandler`, `TimeProvider`, source-generated options validators, and the first-party resilience pipeline.

One .NET 10 feature was declined: built-in minimal-API validation. Validation has to run inside the MediatR pipeline so an invalid request is refused before it spends an upstream call, and endpoint-level validation would have run in the wrong place.

### Testing

- Unit tests use a hand-rolled scripted message handler rather than a mocking framework, so the real `HttpClient` pipeline — URI resolution, content, headers — is exercised and recorded.
- Integration tests boot the real application through `WebApplicationFactory` and replace exactly one thing: the socket. Everything else runs for real, so the suite proves the claims in this document end to end.
- Determinism has three layers: the in-memory upstream owns every named client, the configured host is a reserved domain that cannot resolve, and the whole suite was additionally run under a sandbox denying outbound HTTP and HTTPS, with a control request failing.

### Docker

- Multi-stage build; the runtime is the ASP.NET image and the service runs as the base image's **numeric** non-root user id, because an orchestrator asked to enforce a non-root container can only verify a numeric one.
- The image carries an init process. The kernel applies no default action to PID 1 for the signal .NET raises on an unhandled startup exception, so without one a misconfigured container did not exit: it spun on a full core, kept reporting itself running, and no restart policy ever fired. This belongs in the image rather than the compose file, because a plain `docker run` has no such flag and Kubernetes has no equivalent at all. Graceful shutdown was then measured rather than assumed: a stop request ends the container in half a second with a clean exit code.
- Compose publishes on loopback only, runs with a read-only root filesystem, every capability dropped and no new privileges — all verified not to break the upstream round-trip — and caps processor and memory.
- No `HEALTHCHECK` instruction: an orchestrator ignores it and has the liveness and readiness endpoints instead. A readiness probe must be given a timeout **above** the probe's own three-to-four-second budget, or the orchestrator counts every slow probe as a failure regardless of the `Degraded`-still-200 policy.

---

## Security notes

- The upstream address must be https unless it is loopback, enforced at startup. That address is where the login request carries the username and password, and one character in configuration would otherwise have put them on the wire in cleartext with nothing in the stack objecting.
- Transport into this service is terminated outside it. The container listens on plain 8080 and expects TLS at the ingress, which is why there is no redirection middleware: in this deployment it would log a startup warning and redirect nowhere.
- Credentials: the upstream demo credentials live in `appsettings.json` for the exercise. In production they belong in a secret store or the environment; compose demonstrates the override path, and it was verified by running with a deliberately wrong password — the service answered 502 and the wrong password appeared nowhere in the logs.
- Tokens are held in memory only, never logged, never visible to callers. The only auth log line records an expiry time. A failed login throws with the status code alone and never quotes the response body, because that exchange carried the credentials. Types that carry secrets override the record-generated `ToString`. `IHttpClientFactory` redacts every header value in its own logs by default, and that deny-all was deliberately left alone rather than replaced with an allow-list.
- This proxy is anonymous by design here. It rate-limits callers, but before real exposure it still needs its own authentication and per-client authorization. The health endpoints in particular are unauthenticated and each readiness hit costs one upstream request, so they belong off a public ingress.
- Input is validated before it spends upstream quota, which also protects the third party: an empty create body makes the sandbox answer 500 of its own.
- Nothing from upstream is reflected to callers: not bodies, not error text, not status codes this service did not choose.
- The Postman and Insomnia collections that came with the exercise are kept as provided, and they embed bearer tokens. Every one of them expired in 2022 or 2023, so nothing live is exposed here, but the habit is worth naming: a collection that carries a token eventually carries a valid one. They are kept byte for byte as supplied because they are part of the exercise; in a repository I owned, the token would move to a collection variable fed from the environment and a lint step would fail the build on a literal one.
- `dotnet list package --vulnerable --include-transitive` runs in CI on every push and fails the build on any finding.

## Performance notes

- The scaffold created one `HttpClient` per service instance; pooled handlers with a bounded lifetime replaced it.
- Bounded worst-case latency: explicit total and per-attempt timeouts, retries with exponential backoff and jitter, and a circuit breaker. One limit worth stating: the breaker needs 100 calls in a 30-second window before it can open, so on a low-traffic instance it effectively never trips. The per-attempt and total timeouts are the real guard there, and the standard thresholds were left alone rather than tuned against a sandbox's traffic profile, which would have been fitting to noise.
- Source-generated JSON on both directions of every call; no reflection on the hot path.
- `CancellationToken` flows from the inbound request through MediatR into the upstream call, so an abandoned browser tab stops costing upstream calls.
- Timing uses `Stopwatch.GetTimestamp`, so the measurement itself allocates nothing.
- List responses are buffered rather than streamed, so the response size cap and the per-attempt timeout cover the body as well as the headers.
- Known limitation: the console sink writes synchronously, so a slow consumer of standard output can push back into request threads under load. Wrapping it in the asynchronous sink is a one-line change when traffic justifies it.

---

## AI usage

AI (Claude) was used throughout, the way I use it at work.

**What I decided.** The architecture and every decision in the sections above, including:

- keeping MediatR on 12.5.0, the last Apache-2.0 release, rather than adopting a licence and a runtime key in a take-home;
- migrating the solution to the `.slnx` format;
- organising the Application layer by capability rather than by CQRS half, and writing down the measurable trigger for revisiting it;
- separating the upstream's shapes from the ones this service publishes, after noticing that types named as this service's DTOs were in fact the third party's models, which is why every published field was optional;
- putting extension methods in their own folder per project, and requiring reusable test setup to live in base classes with strict AAA;
- the comment policy: a comment answers *why* or warns about a trap, never restates code;
- centralising the build, with shared properties and every package version in the two root files rather than repeated across six project files;
- the working process itself — one commit per task, every diff reviewed before it lands, nothing committed without me reading it first.

**Where I overruled the AI.**

- It proposed choosing the log format in code, branching on the environment. I rejected that: environment-specific configuration belongs in `appsettings.{Environment}.json`, which is what the framework provides. The trap it was working around only exists if the same sink is declared in two files, so the real fix was to declare it once per environment. Configuration stayed configuration.
- It then put a startup guard as an `if` block in `Program.cs`. I rejected that too: the composition root should read as a flat list of what the application is made of, not as control flow. The guard moved into a logging extension and `Program.cs` went back to one line per concern.
- A review pass recommended replacing the container's numeric user id with a user name. It had verified that claim against a different base image, and the change would have broken `runAsNonRoot` in Kubernetes. The premise was checked against our own image and the change was reverted.
- The first mapping it produced wrote out every member by hand, on the argument that convention makes a new property a silent no-op. I rejected the premise: the names match on both sides, a library exists precisely so that work is not done by hand, and a hand-written list rots the moment either side changes. The answer to the silent no-op belongs in configuration and a test, not in typing.
- It then injected a mapper through the constructor. A pure shape transformation is not a collaborator worth threading through a class, so the call site is the `Adapt` extension instead, and the client lost a dependency.
- It put the strict check that every published member has an upstream source into application startup. I asked what an upstream contract change would do to that, and the answer exposed the mistake: the check compares two local types, so an upstream change cannot trip it, and the thing it does catch is a developer's own edit. A mapping is identical in every environment, unlike configuration, so the check belongs in a test that fails earlier and cheaper rather than in a class of boot failure with no environmental cause. It moved, with a second test proving the first cannot pass vacuously.
- Two private wrappers survived that refactor to hold a null check the null-conditional operator already does. They are gone and the mapping sits inline where the response is read.

**What AI wrote.** Test boilerplate and the scripted test doubles, the container files, the first draft of this README, and repetitive mechanical work such as mirroring the products capability into categories. It also ran an automated review pass over every task's diff before I read it, which is where a number of real defects surfaced: a JWT expiry parser that threw on malformed input, a timeout this document claimed that was silently not in effect, a health check that reported a caller's own cancellation as an upstream failure, a flaky assertion that would have failed roughly one continuous-integration run in a hundred, and a container that hung on a core instead of exiting when misconfigured.

AI accelerated the mechanical work and widened the search for edge cases. The decisions recorded in this document are mine, including the ones that reversed it.

## Time spent

About **8.5 hours** of focused work, tracked per task as it went. It was done alongside a working week and in the evenings, so the elapsed time in the commit history is considerably longer than the time spent: the work was picked up and put down around a day job rather than run end to end. The breakdown:

| Phase | Time |
|---|---|
| Baseline, dependency upgrade, process and CI | 1.25 h |
| Configuration, HTTP client, resilience, JWT | 2 h |
| CQRS, endpoints, error handling | 2 h |
| Observability and health checks | 1 h |
| Integration tests and logging shape | 1 h |
| Container and rate limiting | 1.25 h |

The stated frame was four to six hours. The extra went into verification rather than features: claims in this document were checked against a running process or a test rather than left as intent, and several defects surfaced that way, two of them in work already called done.

## Future improvements

- Authentication and per-client authorization on this API itself.
- A sliding window in place of the fixed one, so the published allowance holds across a window boundary, at the cost of per-caller memory proportional to the number of segments.
- A shared store behind the limiter so the allowance holds across replicas, at the cost of a round trip on the request path and a new dependency to keep available.
- Tighten the published shapes now that they are separate types from the upstream's: required members and non-nullable properties where the contract really is guaranteed, so the published schema promises a client something. The cost is deciding what this service guarantees when the upstream omits a field, which is a product question rather than a technical one.
- Mapster's compile-time generator in place of the runtime configuration, which removes the startup compile step and makes the generated mapping readable in the repository; it needs a build-time tool in the pipeline.
- Refresh-token flow, for the day this service no longer holds the upstream password.
- `PUT` and `DELETE` for both resources; the upstream supports them, so the work is additive rather than structural.
- Response caching with an explicit invalidation policy, and coalescing concurrent readiness probes behind a short TTL.
- OpenTelemetry traces and metrics — the natural next step for the timing middleware, whose aggregation key is already `http.route`.
- Contract tests against the upstream schema, run on a schedule rather than in the main build.
- Pagination metadata headers, once the upstream exposes a total count.
- The asynchronous logging sink, and source-generated `LoggerMessage` delegates, if request volume ever makes either matter.
