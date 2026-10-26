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
