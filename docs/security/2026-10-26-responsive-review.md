# Security review — responsive pass and boards (2026-10-26)

Scope: `components/app/board-scroller.tsx`, work board and CRM board, side nav auto-collapse, publishing week layout,
`site-motion.tsx` (public site), hero tilt removal, hover reel preload.

| Check | Result |
|---|---|
| New data paths or API calls | None. All changes are layout and client-side behaviour. |
| User input reaching the DOM | Column ids go into `querySelector` through `CSS.escape`; labels are rendered as React text. |
| Storage | Side nav still keeps only `karnama.sidebar.collapsed` (0/1) in localStorage; no personal data. |
| Event listeners | Wheel listener is non-passive only on the board element and is removed on unmount; pointer capture is released on pointer up/cancel. |
| Public site script | MutationObserver work is limited to one `querySelectorAll` per animation frame; no network, no eval. |
| Dependencies | None added. |
| Permissions | Unchanged: CRM and work boards still render only what the existing endpoints return for the user. |

Result: no findings.

## Addendum — waking the free-host API (same day)

| Check | Result |
|---|---|
| `POST /site-refresh` (public, web build only) | Takes no input. Acts only if the API answers `/api/v1/public/brand` right now, and at most once per 30 s per server; it only marks pages for rebuild (`revalidatePath`). Worst case for abuse: one page rebuild every 30 s. |
| `wakeApi()` | `no-cors` GET to the API's own public `/health/live` (already allowed by CSP `connect-src`); sends no cookies or tokens that matter (`no-cors`, health endpoint is anonymous); no-op when the API shares the site's origin. |
| Keep-awake timer | One request every 4 minutes while a panel tab is visible; stops when the tab is closed. |

Result: no findings.
