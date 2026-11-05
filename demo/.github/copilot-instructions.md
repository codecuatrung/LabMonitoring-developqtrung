# LabMonitoring Demo

- Stack: Vite, React, TypeScript, Ant Design, sql.js.
- Product UI is Vietnamese and uses a restrained teal/green palette.
- SQLite runs in the Node API handler from `/api/state`, in memory per serverless instance; Vite mounts the same handler for local development.
- Do not add a remote database or environment variables; this RAM-only server demo may reset or diverge across Vercel instances.
- Keep the Vite frontend and internal API deployable together on Vercel without external services.
- Validate edits with `npm run build`.
