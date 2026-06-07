# O1 Message Event Bridge Design

## Objective

Upgrade QQListener from a QQ toast forwarding plugin into a stable message event bridge core. QQ remains the first source, but parsing, rules, diagnostics, and ClassIsland output must no longer depend on QQ-specific DTOs.

## Scope

This O1 implementation covers all four key results in `QQListener-OKR.local.md`:

- KR1: add a unified `MessageEvent` model with source, sender, conversation, body, timestamp, importance, raw text, and deduplication key.
- KR2: split the current chain into independent services for toast reading, QQ toast parsing, rule evaluation, event pipeline state, and ClassIsland notification output.
- KR3: add at least 20 simulated notification samples covering direct chats, group chats, mentions, calls, duplicate notifications, blacklist handling, and malformed formats.
- KR4: expose visible diagnostics for listener state, permission state, last read time, last success or ignore reason, and recent errors, while keeping structured logs.

## Architecture

The runtime chain becomes:

```text
WindowsNotificationReader
  -> QqNotificationProvider
  -> QqToastMessageParser
  -> MessageEventPipeline
  -> MessageRuleEvaluator
  -> ClassIslandMessageNotifier
  -> LatestMessageState / ClassIsland notification host
```

`WindowsNotificationReader` only reads Windows toast snapshots and reports permission state. `QqNotificationProvider` owns polling, notification identity tracking, and provider lifecycle. It delegates parsing, rule evaluation, deduplication, diagnostics, and output to smaller services.

`MessageEventPipeline` is the core testable service. It accepts a parsed event, applies deduplication and rule evaluation, records diagnostic outcomes, and returns either a final event ready for output or a clear ignore reason.

## Models

`MessageEvent` is source-agnostic:

- `Source`: stable source id such as `qq.toast`.
- `SourceDisplayName`: user-facing source name such as `QQ`.
- `Sender`: sender/person name.
- `Conversation`: group/session name, empty for unknown.
- `Body`: message body.
- `ReceivedAt`: event timestamp.
- `Importance`: `Ignored`, `Normal`, `Important`, or `Calling`.
- `RawText`: original notification text lines.
- `DeduplicationKey`: stable content key used by the pipeline.

QQ-specific parsing can add metadata later, but O1 keeps the public event compact and stable.

## Parsing And Rules

`QqToastMessageParser` converts toast text into a `MessageEvent` with `Normal` importance. It handles current formats:

- one-line `Person: Content` and `Person：Content`;
- group `Group`, `Person: Content`;
- group `Group`, `Person`, `Content`;
- malformed or minimal text by preserving raw lines and falling back to sensible sender/body values.

`MessageRuleEvaluator` applies settings in this order:

1. blacklist -> `Ignored`;
2. calling keyword -> `Calling`;
3. important person, important keyword, or `有人@我` -> `Important`;
4. otherwise -> `Normal`.

It returns a reason string for diagnostics and tests.

## Diagnostics

Add `QqListenerDiagnosticsState` as an observable singleton. It records:

- listener enabled/running state;
- notification permission status;
- last read time and last read count;
- last accepted event summary;
- last ignored reason;
- last error message and time.

The settings page gets a diagnostics section bound to this state. Logs remain the detailed source of truth for failures.

## Samples And Verification

Add `Samples/qq-toast-samples.json` with at least 20 cases. Add a small `QQListener.SampleVerifier` console project referenced by the solution. The verifier loads samples, runs the parser and rule evaluator against deterministic settings, and fails if parsed sender, conversation, body, importance, or ignore behavior differs from expectations.

Verification commands:

```powershell
dotnet build QQListener.sln --no-restore
dotnet run --project Samples/QQListener.SampleVerifier/QQListener.SampleVerifier.csproj --no-restore
```

## Commit Plan

Commit the design first. Implement and verify O1 in a second commit after build and sample verification pass.
