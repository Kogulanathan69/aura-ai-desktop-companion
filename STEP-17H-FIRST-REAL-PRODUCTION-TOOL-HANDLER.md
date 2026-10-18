# Step 17H — first real production tool handler

**Result: blocked; ToolHandlers=false.** No concrete production `IToolHandler` exists. `ToolInvocationRequest` carries only `ToolId`; the trusted executor constructs it with that ID alone. A handler cannot derive an authenticated user, approved action, owned project or bounded target from this request. A fixed-success/no-op handler would be a test fixture, and a file/process/URL handler would introduce an unreviewed access boundary.

The selected design candidate is a read-only owned-project metadata observation with canonical `ProjectRead` capability and `OwnedProjectRead` permission. It is **not** registered or called. First define an immutable, server-created action/scope/resource target; bind it through the trusted executor after ownership, permission and approval; choose a bounded result observation; and build 17G's independent re-query/compare proof. Then review exact tool ID, descriptor, registry construction, handler DI, cancellation, sanitization, duplicate/unknown-tool denial and one-call behavior together. The Step 14N catalog checks descriptors but cannot by itself prove the runtime registry pairing. No arbitrary payload, path or command was added.

No live tool command ran. There is no reviewed handler to execute safely. Runtime flags remain false and activation is denied.
