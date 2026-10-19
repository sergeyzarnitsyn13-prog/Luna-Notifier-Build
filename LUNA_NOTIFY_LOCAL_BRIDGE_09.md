# Luna Notify 0.9 — read-only local Systematic bridge

The approved MoonGlow 0.8 bubble remains unchanged. The app now accepts the existing SiteBridge task snapshot and mail reminders over 127.0.0.1:58731. It makes no outbound HTTP calls, Firebase reads/writes or Yandex API requests.

Routes: POST /v1/state, POST /v1/mail-notification, GET /v1/health, GET /v1/pending-todos (empty), GET /v1/todo-actions (empty), POST /luna/test. Only exact production or isolated staging Systematic origins with loopback Host. A state includes max 120 tasks (ID, due, truncated action and client name); no contacts, conversations or session IDs retained. Tasks are held only in process memory. New tasks and due reminders show original MoonGlow, mail reminders are deduplicated. First employee name pins application process; another profile pauses tasks. Browser Origin is not strong local-user authentication.

The existing Systematic loader enables its bridge only by explicit opt-in. Luna Notify itself does not change that flag or any production site files. After restarting the app, the browser must re-sync tasks. Existing Chrome notifications may duplicate native bubbles when permission is granted. No silent production activation or automatic account pairing is provided by this release.
