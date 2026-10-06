# Step 11K: OpenAI cloud provider foundation

## Scope and architecture

Application defines ICloudAiProvider using the existing provider-neutral request,
result and status types. Infrastructure owns OpenAI configuration, HTTP and private
transport DTOs. Program registers an independent typed HttpClient. No endpoint,
router, fallback or cloud integration into AiChatService is added. Step 11J still
consumes only ILocalAiProvider; its behavior and Ollama registration are unchanged.
No packages, migrations, schema changes, memory, embeddings, files or actions.

## Official contract and exact request

Verified official documentation on 2026-10-06:

- https://developers.openai.com/api/reference/python/resources/responses/methods/create
- https://developers.openai.com/api/docs/guides/migrate-to-responses
- https://developers.openai.com/api/docs/guides/your-data

POST https://api.openai.com/v1/responses with exactly model (server configuration),
input (current explicit trimmed prompt), store:false, stream:false. Bearer API key
is sent only in the Authorization header. No history, Summary, project metadata,
memory, file/path, task/decision/error, browser, screenshot or microphone data is
retrieved or sent. No instructions, previous_response_id, conversation, tools or
tool configuration is sent. No remote conversation state is retained/replayed by
AURA. store:false disables response application-state storage; it does NOT promise
zero provider retention. Abuse-monitoring retention and account-specific controls
remain governed by OpenAI's policy. The prompt itself may contain sensitive data
that the caller explicitly submits; this foundation provides no cloud consent UI.

## Configuration and destination

AI:OpenAI options are startup-bound. Enabled defaults false: Disabled is returned
before key/prompt validation, with zero HTTP. Enabling invalid configuration fails
startup with a generic message; the provider validates again before every call.
Supply ApiKey through runtime secrets/environment configuration (for example
AI__OpenAI__ApiKey), never checked-in appsettings. No real secret was added.
Keys are opaque secrets, required and bounded to 512 characters. Null, empty,
whitespace-only and control-character values (including CR/LF) are rejected for
header safety. Printable punctuation is accepted without assuming a key format.
This does not establish credential validity or account access. Model is required,
server-selected, <=100 permitted ASCII characters, matching Step 11J persistence.
TimeoutSeconds is 1-120. Future model formats outside this conservative syntax
fail closed and require review.

There is no BaseUrl option. The immutable fixed HTTPS URI uses platform DNS/TLS.
The production handler disables redirects, proxies, cookies, credentials,
preauthentication and decompression. No certificate bypass/custom routing is used.
HttpClient factory loggers are removed; no deliberate key/prompt/output logging is
added. Process diagnostics, external instrumentation and platform logging remain
outside this guarantee. Secrets reside in configuration/process memory and require
normal deployment access controls and rotation.

## Bounds, parsing and errors

Reuse the 16,384 original UTF-16-code-unit prompt limit; trim, require nonblank and
reject NUL. Use ResponseHeadersRead, reject declared bodies above 1 MiB, and read
at most 1 MiB plus one detection byte into a fixed buffer. Strict UTF-8/JSON parsing
rejects malformed input. Parsing allocations are bounded by the body limit.
Only completed response objects with null error, completed assistant message items
and output_text parts are accepted. Reasoning items are ignored, never returned or
replayed. Refusals/tool/unknown items fail closed. Text parts concatenate in order;
empty/whitespace, NUL, invalid Unicode or >1 MiB UTF-8 text is rejected. Output is
untrusted text, never executable instructions. Result metadata is OpenAI plus the
validated configured selector, not arbitrary metadata copied from the response.

One HTTP attempt only; no retry/fallback. A linked deadline covers headers/body
and a post-parse deadline check. Caller cancellation propagates; deadline expiration
maps TimedOut. Non-success HTTP (including redirects), network and IO failure map
Unavailable; malformed/oversized output maps InvalidResponse; invalid enabled
settings map InvalidConfiguration. Failures contain no raw body, URL, key, prompt
or exception. Invalid caller prompts follow existing AppValidationException style
with generic validation messages. Disabled ignores cancellation like the local
provider; enabled precancellation prevents HTTP. Cancellation/disconnect does not
prove remote generation or billing stopped.

## Verification and separate review

Package-free fake-handler checks exercise disabled/invalid settings, header and
exact payload, limits, parsing, malformed Unicode, HTTP/network/IO errors, no retry,
fixed destination/hardened handler, cancellation, header/body deadlines and Step 11J
isolation. Existing checks remain enabled. No real OpenAI/Ollama/PostgreSQL was used.
dotnet build Aura.slnx --no-restore passed with zero warnings/errors. The full
package-free suite passed 300 checks (87 new plus 213 existing). The check project
reported NU1900 because vulnerability metadata was unavailable; scanning was not
verified. git diff --check passed with only Git line-ending conversion notices.
Review-only pass inspected tracked/untracked changes, dependency direction, privacy,
secret exposure, fixed destination, handler, bounds, cancellation, safe results and
non-integration. No Critical/High issue identified in this foundation.

Remaining Medium limitations: no aggregate concurrency, token/cost quotas or rate
limits; store:false is not zero retention; future cloud exposure requires explicit
authorization/consent design. Low: conservative configuration/output schema rejects
unsupported formats and model refusals; selected model access/support is unverified.
No max_output_tokens is introduced in this foundation; response byte/deadline limits
do not guarantee a remote generation cost cap. Account-level budget controls remain
necessary before enabling production use.

Fake transport/static inspection and compilation do not prove live DNS/TLS, account
permissions, selected model compatibility, service retention settings, billing,
production secret loading/rotation or external log controls. These require separate
authorized runtime verification. NU1900 metadata availability must be reported
separately; passing checks do not establish successful vulnerability scanning.
