# Step 11I: disabled local Ollama provider foundation

Application defines ILocalAiProvider, prompt-only AiGenerationRequest, safe
AiGenerationResult/Status and prompt validation. It contains no Ollama transport types.
Infrastructure implements the provider with HttpClient and System.Text.Json.
API registers a typed factory-managed client and startup-bound server options only.
No generation endpoint is mapped. ConversationService and all persistence services
are unchanged; no caller automatically invokes this provider in the current API.

## Configuration and trust boundary

Section AI:Ollama binds OllamaOptions. Missing config: Enabled=false,
BaseUrl=http://localhost:11434, Model empty, TimeoutSeconds=60. No model is downloaded
or selected automatically. Enabling requires an explicit local model and valid config.
No appsettings changes, keys, user IDs, absolute machine paths or Enabled=true settings.
Enabled invalid configuration fails startup with a generic message. The provider also
validates on every generation call, before HTTP; disabled calls return without validation
or HTTP. There is no client configuration DTO/public toggle.

Only HTTP origins of the exact forms localhost, 127.0.0.1, [::1], case-insensitive,
optional numeric port 1..65535, optional final slash are accepted. HTTP's default port
is accepted when omitted. Reject HTTPS and all other schemes in this V1 policy, userinfo,
path prefixes, queries, fragments, remote/LAN/public hosts, trailing-dot aliases,
integer/hex/shorthand IP forms, mapped IPv6 and surrounding whitespace. Input is bounded
to 128 characters before regex matching. The allowlist examines original input, avoiding
URI canonicalization accepting ambiguous IP spellings. Localhost becomes literal
127.0.0.1 for the actual request, eliminating DNS/hosts-file rebinding. IPv6 remains
numeric ::1. Fixed request path /api/generate; no arbitrary hostname resolution.

Model is an explicit 1..128-character configuration string beginning with an ASCII
letter/digit and containing only ASCII letters/digits, dot, underscore, colon, slash,
hyphen. No whitespace; reject names containing cloud (case-insensitive), including
known cloud selectors. This is a conservative selector filter, not proof of model provenance.

IMPORTANT: loopback HTTP does not prove that an Ollama daemon will infer locally.
Ollama supports cloud models through the local daemon. Enablement requires a trusted
local daemon with cloud features disabled (OLLAMA_NO_CLOUD=1 or documented equivalent),
an installed local model and operator verification. Arbitrary aliases or a forwarding
daemon cannot be policed solely by client-side host/name checks. AURA itself has no
cloud client, cloud fallback, automatic download, retry, or provider router. This change
does not inspect daemon files, modify its environment, or call a process/CLI.

## HTTP policy and bounds

Production HttpClientHandler: AllowAutoRedirect=false, UseProxy=false, UseCookies=false,
UseDefaultCredentials=false, Credentials=null, PreAuthenticate=false, decompression=None.
No certificate-validation callbacks or global TLS changes. HTTP only is intentional
for explicit loopback. Redirect statuses are failures; Location is never followed.
System proxy settings cannot route this client externally. Host is always numeric
loopback. Typed client uses RemoveAllLoggers; provider adds no logs. No prompt/output,
raw transport, headers or exceptions are logged by this integration.

POST /api/generate sends configured model, trimmed prompt, stream=false. No tools,
images, conversation IDs, user IDs, database entities or context appear in the contract.
The documented protocol is https://docs.ollama.com/api/generate.
Cloud disable guidance: https://github.com/ollama/ollama/blob/main/docs/faq.mdx.

Prompt: required, reject whitespace-only; maximum 16,384 original UTF-16 code units
before Trim or transport serialization. This bounds request growth (including JSON
escaping), not a model context limit, secret detector or safe-prompt assertion.

Response: maximum 1,048,576 raw body bytes. ResponseHeadersRead avoids HttpClient's
whole-body buffering. Reject excessive Content-Length before reading. Unknown or
misleading length remains bounded: allocate limit+1 and read only remaining capacity;
the extra detection byte rejects overflow. Deserialize only the bounded bytes. Require
valid JSON, non-whitespace response text, done=true, and absent/null error. Ignore extra
transport fields. No unsuccessful status body is consumed. Failures return no content,
model, internal URLs or exception details. Success returns untrusted Content, Provider
Ollama, and server-configured Model; output is not executed, persisted or treated as approval.

TimeoutSeconds must be 1..120. Client has a 120-second timeout; a linked cancellation
source enforces configured deadline across sending, headers and body read. JSON processing
is input-size bounded, with cancellation checked before successful return. Caller cancellation
propagates OperationCanceledException. Internal cancellation/timeouts return TimedOut.
No retries. Categories: Success, Disabled, InvalidConfiguration, Unavailable, TimedOut,
InvalidResponse. Invalid prompts use existing safe AppValidationException. Expected IO/HTTP
exceptions become Unavailable; JSON failures become InvalidResponse.

## Review findings

- Critical: none identified in this disabled internal foundation.
- High: none identified in the AURA transport implementation. Existing Step 11F
  filesystem TOCTOU remains separate if physical access is enabled (still disabled).
- Medium deployment trust: loopback daemon forwarding/cloud aliases can disclose prompts
  if an operator enables against an untrusted or cloud-capable daemon. Cloud-disabled
  daemon and local-model provenance must be verified before enablement; the name filter
  does not certify either condition. Do not advertise loopback alone as end-to-end privacy.
- Medium resource concern: per-call input/body/deadline bounds do not bound aggregate
  concurrent CPU/GPU/memory use, model load time/resources, or daemon output computation.
  No queue, generation-token budget or rate limit exists; there is also no public caller.
- Low: default Model is empty by design; operator must configure/install an appropriate
  local model. HTTP-only and localhost-to-IPv4 pinning deliberately exclude HTTPS-only
  or IPv6-only localhost installations unless explicit IPv6 URL is configured.

## Verification and scope

Required solution build passed with zero warnings/errors. Package-free checks passed
all 180 assertions (65 new provider assertions plus 115 existing checks). Custom handlers
exercise real provider calls, prompt validation, all URL/model/timeout failures, payload,
results, safe failures, redirect rejection, bounded known/unknown-length and exact-limit
responses, cancellation and stalled headers/body. Production handler properties are checked.
No real Ollama, live socket/proxy/redirect integration or model generation was performed.
No prompt was sent to a real service. Actual local-daemon behavior and production DI/client
pipeline need runtime verification before enablement. NU1900 vulnerability metadata remains
unavailable in the checks project; vulnerability scanning is not verified.

No changes to ConversationService, message creation, ProjectMemory or file context.
No assistant persistence, databases, embeddings, vector search, tool actions, filesystem,
shell/process/ollama CLI, OpenAI/Groq/cloud fallback, migrations, schema changes or packages.
Step 11F retains its default-disabled options, unmapped route and independent service/reader
gates. Generated bin/obj remain ignored. No commit/push. Ready for a separate review-only pass.
