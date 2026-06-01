# SignalEire — Phase 5 Build Brief
**For:** Claude Code  
**Prerequisite:** Phase 4 complete and passing; Azure VM provisioned; domain name pointed at VM  
**Scope:** Production deployment + ASP.NET Core Identity + PWA + VAPID Web Push + rate limiting + GDPR + RAG summaries  
**Primary goal:** Get the platform in front of real users.

---

## What Phase 5 adds

1. **Production deployment** — nginx reverse proxy, systemd service, Let's Encrypt, GitHub Actions CI/CD
2. **ASP.NET Core Identity** — registration, email confirmation, login, password reset
3. **Per-user personalisation** — favourite stops, per-user alert rules and device profiles
4. **PWA** — web app manifest, service worker, mobile-responsive CSS overhaul, install prompt
5. **VAPID Web Push** — browser push notifications replacing/supplementing Phase 2 email alerts
6. **Rate limiting** — per-IP on all public API endpoints
7. **GDPR compliance** — cookie consent, privacy policy, data attribution
8. **RAG summaries** — nightly job indexing reliability and grid summaries into existing Qdrant pipeline *(parallel track — does not block going live)*

---

## Part 1 — Production deployment

### Directory layout on VM

```
/opt/ireland-live-signals/          ← application binaries (overwritten on each deploy)
/var/lib/ireland-live-signals/
    data/
        signals.db                  ← SQLite database (never overwritten by deploy)
        raw/                        ← raw snapshot archive (never overwritten by deploy)
/etc/ireland-live-signals/
    environment                     ← production secrets (gitignored, env vars)
/var/log/ireland-live-signals/      ← log files
```

The database and raw archive paths must be outside the deploy directory. Update `appsettings.Production.json`:

```json
{
  "ConnectionStrings": {
    "Sqlite": "Data Source=/var/lib/ireland-live-signals/data/signals.db"
  },
  "RawSnapshotPath": "/var/lib/ireland-live-signals/data/raw"
}
```

### Secrets in production

Do not store production secrets in `appsettings.json` or any file committed to the repository.

All secrets are injected as environment variables from `/etc/ireland-live-signals/environment`:

```bash
# /etc/ireland-live-signals/environment  (chmod 600, owned by service user)
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__Sqlite=Data Source=/var/lib/ireland-live-signals/data/signals.db
GoogleMaps__BrowserKey=...
GoogleMaps__ServerKey=...
TransitPoller__VehiclePositionsUrl=...
TransitPoller__TripUpdatesUrl=...
TransitPoller__ServiceAlertsUrl=...
Alerts__Email__SmtpHost=...
Alerts__Email__Password=...
Alerts__Telegram__BotToken=...
WebPush__VapidPublicKey=...
WebPush__VapidPrivateKey=...
Qdrant__Endpoint=...
```

ASP.NET Core reads environment variables automatically. Double-underscore `__` maps to nested config keys.

### systemd service

```ini
# /etc/systemd/system/ireland-live-signals.service
[Unit]
Description=Ireland Live Signals
After=network.target

[Service]
Type=notify
User=signaleire
WorkingDirectory=/opt/ireland-live-signals
ExecStart=/opt/ireland-live-signals/IrelandLiveSignals.Web
Restart=on-failure
RestartSec=5
EnvironmentFile=/etc/ireland-live-signals/environment
StandardOutput=append:/var/log/ireland-live-signals/app.log
StandardError=append:/var/log/ireland-live-signals/app.log

[Install]
WantedBy=multi-user.target
```

Create a dedicated non-root service user: `useradd -r -s /bin/false signaleire`

### nginx config

```nginx
server {
    listen 80;
    server_name yourdomain.ie;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl;
    server_name yourdomain.ie;

    ssl_certificate     /etc/letsencrypt/live/yourdomain.ie/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/yourdomain.ie/privkey.pem;

    # Forward real client IP for rate limiting
    set_real_ip_from 0.0.0.0/0;
    real_ip_header X-Forwarded-For;

    location / {
        proxy_pass         http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header   Upgrade $http_upgrade;
        proxy_set_header   Connection keep-alive;
        proxy_set_header   Host $host;
        proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;
        proxy_read_timeout 90;
    }
}
```

Let's Encrypt: `certbot --nginx -d yourdomain.ie`  
Auto-renewal via existing certbot systemd timer (standard Ubuntu setup).

### GitHub Actions deployment workflow

```yaml
# .github/workflows/deploy.yml
name: Deploy to Production

on:
  push:
    branches: [main]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Build and publish
        run: dotnet publish src/IrelandLiveSignals.Web/IrelandLiveSignals.Web.csproj
             -c Release -o ./publish --no-self-contained

      - name: Deploy to VM
        uses: appleboy/scp-action@v0.1.7
        with:
          host: ${{ secrets.SSH_HOST }}
          username: ${{ secrets.SSH_USER }}
          key: ${{ secrets.SSH_PRIVATE_KEY }}
          source: "./publish/*"
          target: "/opt/ireland-live-signals"
          rm: true

      - name: Restart service
        uses: appleboy/ssh-action@v1.0.3
        with:
          host: ${{ secrets.SSH_HOST }}
          username: ${{ secrets.SSH_USER }}
          key: ${{ secrets.SSH_PRIVATE_KEY }}
          script: sudo systemctl restart ireland-live-signals
```

Required GitHub repository secrets: `SSH_HOST`, `SSH_USER`, `SSH_PRIVATE_KEY`.

The service user needs `sudo systemctl restart ireland-live-signals` without a password:  
Add to `/etc/sudoers.d/signaleire`: `signaleire ALL=(ALL) NOPASSWD: /bin/systemctl restart ireland-live-signals`

### Database backup

Cron job as root, daily at 02:00:

```bash
# /etc/cron.d/signaleire-backup
0 2 * * * root sqlite3 /var/lib/ireland-live-signals/data/signals.db ".backup /var/lib/ireland-live-signals/data/signals.db.bak" && \
  cp /var/lib/ireland-live-signals/data/signals.db.bak \
     /var/lib/ireland-live-signals/data/signals-$(date +\%Y\%m\%d).db.bak && \
  find /var/lib/ireland-live-signals/data -name "signals-*.db.bak" -mtime +7 -delete
```

---

## Part 2 — ASP.NET Core Identity

### Setup

Add to `IrelandLiveSignals.Infrastructure`:
```
Microsoft.AspNetCore.Identity.EntityFrameworkCore
```

### ApplicationUser

```csharp
public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public string PreferredRegion { get; set; } = "ROI";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool PushNotificationsEnabled { get; set; } = false;
}
```

### DbContext

Merge Identity into the existing `SignalEireDbContext`:

```csharp
public class SignalEireDbContext : IdentityDbContext<ApplicationUser>
{
    // ... all existing DbSet properties ...
    public DbSet<FavouriteStop> FavouriteStops { get; set; }
    public DbSet<PushSubscription> PushSubscriptions { get; set; }
}
```

### Identity pages

Use ASP.NET Core Identity Razor Pages (scaffolded). Required pages:
- Register (with email confirmation)
- Login / Logout
- Password reset (forgot password → email link → reset form)
- Manage account (change email, change password)

Email confirmation uses the existing Phase 2 SMTP configuration. If SMTP is not configured, registration still works but email confirmation is skipped with a log warning (so development works without email setup).

### Backward compatibility for existing single-tenant data

`AlertRule` and `DeviceProfile` get a nullable `UserId` added:

```csharp
public string? UserId { get; set; }   // null = legacy single-tenant / admin-owned
```

Existing rows with `UserId = null` remain visible only to authenticated admin users. This is a non-breaking migration — `EnsureCreated()` handles new nullable columns.

The first registered user is automatically assigned the `Admin` role. Subsequent users are `User` role.

---

## Part 3 — Per-user personalisation

### FavouriteStop

```csharp
public record FavouriteStop
{
    public string Id { get; init; }
    public string UserId { get; init; }
    public string StopId { get; init; }
    public string? DisplayLabel { get; init; }   // user's custom name, e.g. "Work stop"
    public int SortOrder { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}
```

### API endpoints

```
GET    /api/me/favourites               → list user's favourite stops
POST   /api/me/favourites               → add a stop ({ stopId, displayLabel })
DELETE /api/me/favourites/{stopId}      → remove

GET    /api/me/alerts                   → list user's alert rules
POST   /api/me/alerts                   → create
DELETE /api/me/alerts/{id}              → delete

GET    /api/me/devices                  → list user's device profiles
POST   /api/me/devices                  → create
DELETE /api/me/devices/{id}             → delete
```

All `/api/me/*` endpoints require authentication. Return `401` if not authenticated.

### Dashboard personalisation

- After login, dashboard home shows favourite stops' arrival boards first
- "Add to favourites" button on each stop board page
- "My alerts" section in the dashboard — filtered to current user's rules
- "My devices" section — EV and appliance profiles

---

## Part 4 — PWA

### Web app manifest

```json
// wwwroot/manifest.json
{
  "name": "Ireland Live Signals",
  "short_name": "SignalEire",
  "description": "Live Irish grid and transit intelligence",
  "start_url": "/",
  "display": "standalone",
  "background_color": "#ffffff",
  "theme_color": "#1a6b3c",
  "icons": [
    { "src": "/icons/icon-192.png", "sizes": "192x192", "type": "image/png" },
    { "src": "/icons/icon-512.png", "sizes": "512x512", "type": "image/png" },
    { "src": "/icons/icon-512.png", "sizes": "512x512", "type": "image/png", "purpose": "maskable" }
  ]
}
```

Add to `_Layout.cshtml`:
```html
<link rel="manifest" href="/manifest.json">
<meta name="theme-color" content="#1a6b3c">
<meta name="apple-mobile-web-app-capable" content="yes">
<meta name="apple-mobile-web-app-status-bar-style" content="default">
<link rel="apple-touch-icon" href="/icons/icon-192.png">
```

### Service worker

```javascript
// wwwroot/sw.js
const SHELL_CACHE = 'signaleire-shell-v1';
const SHELL_ASSETS = ['/', '/css/site.css', '/js/site.js', '/manifest.json', '/offline.html'];

// Install: cache app shell
self.addEventListener('install', event => {
    event.waitUntil(
        caches.open(SHELL_CACHE).then(cache => cache.addAll(SHELL_ASSETS))
    );
    self.skipWaiting();
});

// Activate: clean old caches
self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys().then(keys =>
            Promise.all(keys.filter(k => k !== SHELL_CACHE).map(k => caches.delete(k)))
        )
    );
    self.clients.claim();
});

// Fetch strategy:
// - Shell assets → cache first
// - /api/* → network first, no cache (live data must be fresh)
// - Everything else → network first, fallback to cache
self.addEventListener('fetch', event => {
    const url = new URL(event.request.url);

    if (event.request.method !== 'GET') return;

    if (url.pathname.startsWith('/api/')) {
        // Network only for API — never serve stale live data
        event.respondWith(
            fetch(event.request).catch(() =>
                new Response(JSON.stringify({ error: 'offline' }), {
                    headers: { 'Content-Type': 'application/json' }
                })
            )
        );
        return;
    }

    // Network first, cache fallback for pages
    event.respondWith(
        fetch(event.request)
            .then(response => {
                const clone = response.clone();
                caches.open(SHELL_CACHE).then(cache => cache.put(event.request, clone));
                return response;
            })
            .catch(() => caches.match(event.request).then(r => r || caches.match('/offline.html')))
    );
});

// Push notification handler
self.addEventListener('push', event => {
    const data = event.data?.json() ?? {};
    event.waitUntil(
        self.registration.showNotification(data.title ?? 'Ireland Live Signals', {
            body: data.body ?? '',
            icon: '/icons/icon-192.png',
            badge: '/icons/badge-72.png',
            data: { url: data.url ?? '/' }
        })
    );
});

self.addEventListener('notificationclick', event => {
    event.notification.close();
    event.waitUntil(clients.openWindow(event.notification.data.url));
});
```

Register service worker in `_Layout.cshtml`:
```html
<script>
  if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('/sw.js');
  }
</script>
```

### Offline page

Create `wwwroot/offline.html` — simple branded page: "You're offline. Check your connection and try again."

### Mobile-responsive CSS

Overhaul `site.css` with a mobile-first breakpoint system:

- Base styles: mobile (≤ 480px) — single column, large touch targets (min 44px), no horizontal scroll
- `@media (min-width: 768px)` — tablet layout, side-by-side cards
- `@media (min-width: 1024px)` — desktop layout, existing multi-column grid

Critical responsive requirements:
- Stop board table → collapses to card stack on mobile (route + destination + confidence + ghost risk per card)
- Grid dashboard cards → full width on mobile, 2-up on tablet, 3-up on desktop
- Google Maps → full viewport height on mobile
- Alert management form → stacked inputs on mobile
- Navigation → hamburger menu on mobile (`<details>` element, no JS required)

### Install prompt

```javascript
// wwwroot/js/pwa-install.js
let deferredPrompt;

window.addEventListener('beforeinstallprompt', e => {
    e.preventDefault();
    deferredPrompt = e;
    document.getElementById('pwa-install-banner')?.removeAttribute('hidden');
});

document.getElementById('pwa-install-btn')?.addEventListener('click', async () => {
    if (!deferredPrompt) return;
    deferredPrompt.prompt();
    const { outcome } = await deferredPrompt.userChoice;
    deferredPrompt = null;
    document.getElementById('pwa-install-banner')?.setAttribute('hidden', '');
});
```

Add a dismissible install banner to `_Layout.cshtml`:
```html
<div id="pwa-install-banner" hidden class="install-banner">
    <span>Install Ireland Live Signals for quick access</span>
    <button id="pwa-install-btn">Install</button>
    <button onclick="this.closest('.install-banner').hidden=true">✕</button>
</div>
```

---

## Part 5 — VAPID Web Push

### NuGet package

Add to `IrelandLiveSignals.Infrastructure`:
```
WebPush
```

### VAPID key generation

One-time setup — generate and store permanently:
```csharp
// Run once: dotnet run -- generate-vapid-keys
var vapidKeys = VapidHelper.GenerateVapidKeys();
Console.WriteLine($"Public: {vapidKeys.PublicKey}");
Console.WriteLine($"Private: {vapidKeys.PrivateKey}");
```

Store public and private keys in `/etc/ireland-live-signals/environment`. The **public key is safe to embed in client JS**.

### Configuration

```json
"WebPush": {
  "VapidPublicKey": "",
  "VapidPrivateKey": "",
  "VapidSubject": "mailto:admin@yourdomain.ie"
}
```

### PushSubscription model

```csharp
public record PushSubscription
{
    public string Id { get; init; }
    public string? UserId { get; init; }         // null = anonymous subscriber
    public string Endpoint { get; init; }
    public string P256Dh { get; init; }
    public string Auth { get; init; }
    public DateTimeOffset SubscribedAtUtc { get; init; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
}
```

### API endpoints

```
POST   /api/push/subscribe      ← { endpoint, keys: { p256dh, auth } }
DELETE /api/push/subscribe      ← { endpoint }
GET    /api/push/vapid-public-key  ← returns { publicKey: "..." }
```

`/api/push/subscribe` does not require authentication. Associates with `UserId` if request is authenticated, otherwise anonymous.

### Client-side subscription

```javascript
// wwwroot/js/push-subscribe.js
async function subscribeToPush() {
    const reg = await navigator.serviceWorker.ready;
    const { publicKey } = await fetch('/api/push/vapid-public-key').then(r => r.json());

    const sub = await reg.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: urlBase64ToUint8Array(publicKey)
    });

    await fetch('/api/push/subscribe', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(sub)
    });
}

function urlBase64ToUint8Array(base64String) {
    const padding = '='.repeat((4 - base64String.length % 4) % 4);
    const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
    const raw = atob(base64);
    return Uint8Array.from([...raw].map(c => c.charCodeAt(0)));
}
```

Add a "Enable push notifications" toggle to the user preferences page and the dashboard.

### Delivery service

```csharp
public interface IPushNotificationService
{
    Task SendAsync(string userId, string title, string body, string url, CancellationToken ct);
    Task SendToAnonymousAsync(string endpoint, string p256dh, string auth, string title, string body, CancellationToken ct);
}
```

Integrate into the Phase 2 `IAlertDeliveryService`: when firing an alert for a user, also call `IPushNotificationService.SendAsync`. Remove stale subscriptions (410 Gone response from push endpoint → delete from DB).

### iOS notes

Web Push on iOS requires:
1. iOS 16.4+
2. User must "Add to Home Screen" (install the PWA) before push works
3. `userVisibleOnly: true` is mandatory

Add a note to the notification settings UI explaining this for iOS users.

---

## Part 6 — Rate limiting

Use ASP.NET Core 8 built-in rate limiting (`Microsoft.AspNetCore.RateLimiting`).

```csharp
// Program.cs
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("public-api", o =>
    {
        o.PermitLimit = 60;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });

    options.AddFixedWindowLimiter("push-subscribe", o =>
    {
        o.PermitLimit = 10;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
    });

    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Rate limit exceeded. Please slow down." }, ct);
    };
});
```

Apply limiters in route definitions:
- All `/api/transit/*` and `/api/grid/*` → `public-api` limiter
- `/api/push/subscribe` → `push-subscribe` limiter
- `/api/me/*` → no rate limit (authenticated)
- Admin routes → no rate limit

Rate limit key is client IP from `X-Forwarded-For` (set correctly by nginx above).

---

## Part 7 — GDPR compliance

### Cookie audit

Cookies set by the application:

| Cookie | Purpose | Category | Lifetime |
|---|---|---|---|
| `.AspNetCore.Identity.Application` | Auth session | Necessary | Session / sliding |
| `__RequestVerificationToken` | CSRF protection | Necessary | Session |
| `signal_uid` | Anonymous transit report identity | Functional | 1 year |
| Google Maps cookies | Maps display | Functional (third-party) | Set by Google |

### Cookie consent banner

Display on first visit to any page. Store consent decision in `localStorage` (not a cookie):

```javascript
// wwwroot/js/cookie-consent.js
const CONSENT_KEY = 'signaleire_cookie_consent';

document.addEventListener('DOMContentLoaded', () => {
    const consent = localStorage.getItem(CONSENT_KEY);
    if (!consent) {
        document.getElementById('cookie-banner')?.removeAttribute('hidden');
    } else if (consent === 'accepted') {
        loadFunctionalScripts();
    }
});

function acceptCookies() {
    localStorage.setItem(CONSENT_KEY, 'accepted');
    document.getElementById('cookie-banner')?.setAttribute('hidden', '');
    loadFunctionalScripts();
}

function declineCookies() {
    localStorage.setItem(CONSENT_KEY, 'declined');
    document.getElementById('cookie-banner')?.setAttribute('hidden', '');
    // Maps pages will show a placeholder instead of the map
}

function loadFunctionalScripts() {
    // Dynamically inject Google Maps only after consent
    if (document.getElementById('google-maps-placeholder')) {
        const script = document.createElement('script');
        script.src = `https://maps.googleapis.com/maps/api/js?key=${window.GMAPS_KEY}&callback=initMap`;
        script.async = true;
        script.defer = true;
        document.head.appendChild(script);
    }
}
```

Google Maps script tag must be removed from `_Layout.cshtml` and injected only after consent. Pages with maps must have a `<div id="google-maps-placeholder">` that shows a message ("Enable functional cookies to view the map") if consent is declined.

`window.GMAPS_KEY` is set server-side in a `<script>` block before this script runs:
```html
<script>window.GMAPS_KEY = '@Model.BrowserKey';</script>
```

### Privacy policy page (`/privacy`)

A `Privacy.cshtml` Razor Page. Must cover:
- What data is collected (email address, anonymous session ID, approximate location from confirmations)
- How it is used
- How long it is retained
- Third-party processors: Google Maps (Alphabet Inc.), your SMTP provider
- User rights under GDPR (access, rectification, erasure)
- Contact email for data requests
- No data sold to third parties
- No advertising

### Data attribution page (`/attribution`)

A static Razor Page. Required by NTA and EirGrid usage terms. Must clearly display:
- Transport data: National Transport Authority of Ireland, used under fair-use developer terms
- Energy data: EirGrid plc, sourced from the Smart Grid Dashboard
- Maps: Google Maps, © Google
- Weather (if added): Met Éireann
- Disclaimer: "All predictions and confidence scores are advisory only and are not guaranteed to be accurate. Do not rely on this platform for safety-critical decisions."

### Error pages

Add custom error pages:
- `Pages/Error.cshtml` — generic 500 page
- `Pages/NotFound.cshtml` — 404 page
- `Pages/RateLimited.cshtml` — 429 page

All show the app navigation and are branded. No stack traces in production (`ASPNETCORE_ENVIRONMENT=Production` suppresses them automatically).

---

## Part 8 — RAG summaries (parallel track — does not block going live)

*This section is independent. Build it after everything above is live and stable.*

### Purpose

Generate plain-English daily summaries of transit reliability and grid cleanliness, index them into the existing Qdrant pipeline, and surface them via a dashboard "Intelligence" page.

### Configuration

```json
"Qdrant": {
  "Endpoint": "http://localhost:6333",
  "CollectionName": "signaleire-summaries"
}
```

### Nightly summary generation job

Runs at 04:00 Ireland local time (after the Phase 4 reliability aggregation job at 03:00).

For each date with data:

**Transit summary** (one per region/day):
```
Summary for {date}:

Transit reliability across tracked routes was {high/moderate/low}.
{N} routes had vehicle-confirmed arrival rates above 85%.
{N} routes showed elevated timetable-only rates, suggesting GPS coverage gaps.

Notable patterns:
- Route {X}: {reliability}% reliable ({confirmed}/{total} arrivals confirmed)
- Route {Y}: {reliability}% reliable

Ghost risk events (user-reported bus not appeared): {N} reports across {M} stops.
Average confidence score across all arrivals: {score}/100.
```

**Grid summary** (one per region/day):
```
Grid cleanliness for {date}:

Average CO₂ intensity: {X} g/kWh (Ireland daily average).
Peak renewable share: {X}% at {time}.
Lowest CO₂ window: {start}–{end} ({intensity} g/kWh average).
Cleanest sustained period: {N} hours below 200 g/kWh.
```

### Qdrant indexing

```csharp
public interface IQdrantSummaryIndexer
{
    Task IndexAsync(SignalSummary summary, CancellationToken ct);
    Task<IReadOnlyList<SignalSummary>> SearchAsync(string query, int topK, CancellationToken ct);
}

public record SignalSummary
{
    public Guid Id { get; init; }
    public string Module { get; init; }       // "transit" | "grid"
    public string Region { get; init; }
    public DateOnly Date { get; init; }
    public string SummaryText { get; init; }
    public Dictionary<string, object> Metadata { get; init; }
}
```

Use Qdrant's HTTP API directly (`HttpClient`). No Qdrant .NET SDK required — the REST API is stable.

Create collection on startup if it doesn't exist. Embeddings: use the same embedding model as the existing pipeline. If the embedding endpoint is not configured, skip indexing with a log warning (so the app starts cleanly without Qdrant).

### Intelligence dashboard page (`/intelligence`)

Razor Page with:
- Text search input → calls `IQdrantSummaryIndexer.SearchAsync`
- Results listed as expandable cards (date, module, summary text)
- "Recent summaries" section showing last 7 days without a search query
- Shows "Qdrant not configured" gracefully if endpoint is missing

---

## Tests (Phase 5 additions)

### Unit tests

**RateLimitingTests** — verify rate limit middleware is applied to correct routes (using `WebApplicationFactory` test host)

**PushNotificationServiceTests** — mock `WebPushClient`; verify subscription stored, stale subscription (410) removed, delivery failure logged without throw

**CookieConsentTests** — verify Maps script not rendered when consent header absent; verify it is rendered after consent

### Integration tests

**IdentityIntegrationTests** — using `WebApplicationFactory`: register user → confirm email (extract token from mock email) → login → access `/api/me/favourites` → 200; unauthenticated → 401

**DeploymentSmokeTest** — a simple bash script (not a .NET test):
```bash
#!/bin/bash
# smoke-test.sh — run after each deployment
BASE=https://yourdomain.ie
curl -sf "$BASE/api/grid/health" | grep '"status":"ok"'
curl -sf "$BASE/api/grid/current" | grep '"greenScore"'
curl -sf "$BASE/api/push/vapid-public-key" | grep '"publicKey"'
curl -o /dev/null -sw "%{http_code}" "$BASE/privacy" | grep 200
curl -o /dev/null -sw "%{http_code}" "$BASE/attribution" | grep 200
echo "Smoke tests passed."
```

---

## Production readiness checklist (from spec section 18)

Complete the full checklist before sharing the URL publicly:

- [ ] Confirm NTA and EirGrid data-source terms and fair usage
- [ ] All secrets in environment file, not in repository
- [ ] Raw snapshot archive rotation (cron job pruning files older than 30 days)
- [ ] Database backup cron job running and verified
- [ ] Health check endpoint returns `ok`
- [ ] Source freshness monitoring (health endpoint reports seconds since last reading)
- [ ] Alert suppression working (Phase 2 MaxAlertsPerDay)
- [ ] Privacy policy page live at `/privacy`
- [ ] Error pages rendering correctly (test by visiting `/nonexistent`)
- [ ] Rate limiting verified (60 req/min per IP)
- [ ] Admin page for source health (`/admin/sources`) — shows last fetch time and status per adapter
- [ ] Data attribution page live at `/attribution`
- [ ] Disclaimer visible on all prediction-bearing pages
- [ ] Cookie consent banner showing on first visit
- [ ] Google Maps not loading before cookie consent
- [ ] HTTPS redirect working (HTTP → HTTPS)
- [ ] systemd service auto-starts after VM reboot (`systemctl enable ireland-live-signals`)
- [ ] GitHub Actions deployment pipeline tested end-to-end
- [ ] Smoke test script passing against production URL

---

## Constraints

1. **No Postgres in Phase 5.** SQLite is sufficient for launch. Migration is Phase 6.
2. **No MAUI in Phase 5.** PWA first, MAUI deferred.
3. **No secrets in the repository.** All production secrets via environment file.
4. **Google Maps only loads after cookie consent.**
5. **RAG section does not block deployment.** Ship without it if Qdrant is not ready.
6. **All Phase 1–4 tests must remain green.**
7. **Service worker must never cache `/api/*` responses.** Live data must always come from the network.
8. **Push subscription deletion on 410.** Never retry a gone endpoint.

---

## Definition of done for Phase 5

- [ ] `dotnet build` and `dotnet test` pass — all Phase 1–5 tests green
- [ ] Application running on VM at `https://yourdomain.ie`
- [ ] HTTPS working, HTTP redirects to HTTPS
- [ ] GitHub Actions deploys on push to `main` and smoke test passes
- [ ] User can register, confirm email, and log in
- [ ] Authenticated user can save a favourite stop
- [ ] Authenticated user can create a personal alert rule
- [ ] Push notification subscription flow works on Android Chrome
- [ ] Push notification delivered when an alert rule fires
- [ ] PWA installs via "Add to Home Screen" on Android
- [ ] Service worker registered and active
- [ ] Cookie consent banner shown on first visit
- [ ] Google Maps does not load before consent is given
- [ ] `/privacy` and `/attribution` pages live
- [ ] Rate limiting returns 429 after 60 requests/minute
- [ ] All items on production readiness checklist ticked

---

## What comes in Phase 6

- Postgres + TimescaleDB migration (when real load justifies it)
- MAUI native client (C#, uses existing API)
- OpenTelemetry + structured logging to Grafana
- Tariff-aware grid scoring (Night Rate / Energia Smart integration)
- Digest mode for alerts (daily summary email instead of per-event)
- RAG anomaly explanations (if not completed in Phase 5)
