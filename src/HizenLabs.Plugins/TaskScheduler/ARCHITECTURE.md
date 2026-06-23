# TaskScheduler - Architecture & Design (planning draft)

Status: planning doc, not implementation. Written by AI at Ethan's request to think through the
model and architecture; Ethan owns the actual plugin code.

## 1. What it is

An in-game automation console for server admins. Open `/tasksched`, browse/search a library of
"tasks", and run, enable, inspect, or compose them. A task is a small automation: *when X, if Y,
do Z* - run a command, wait, broadcast a warning, call a hook, chain into another task, etc.

Mental model: cron + IFTTT + a tiny rules engine, with a CUI front end. The admin is assembling
tasks out of three kinds of building block - **triggers** (when), **conditions** (if), and
**actions** (do) - instead of writing code.

## 2. The Task (core concept)

```
ScheduledTask
  id, name, description, tags[], enabled
  trigger        : when this task starts        (exactly one)
  steps[]        : ordered actions to perform    (each optionally guarded + timed)
  runPolicy      : what to do if it's already running (skip | queue | parallel)
  bookkeeping    : lastRunAt, nextRunAt, lastResult, runCount
```

A **step** is the unit of work:

```
Step
  guard?    : Condition  - if present and false, skip (or abort the run)
  waitBefore?: Duration  - delay before running this step
  action    : Action     - the thing to do
```

Concrete example - "nightly wipe warning" (interval trigger, three timed broadcast steps then a
save):

```jsonc
{
  "name": "Nightly restart warning",
  "tags": ["restart", "broadcast"],
  "enabled": true,
  "trigger": { "type": "timeOfDay", "at": "05:55", "clock": "realLocal" },
  "runPolicy": "skip",
  "steps": [
    { "action": { "type": "broadcast", "message": "Server restart in 5 minutes" } },
    { "waitBefore": "4m", "action": { "type": "broadcast", "message": "Restart in 1 minute" } },
    { "waitBefore": "55s", "guard": { "type": "playerCount", "op": ">", "value": 0 },
      "action": { "type": "command", "target": "server", "text": "server.save" } },
    { "waitBefore": "5s", "action": { "type": "command", "target": "server", "text": "restart 5" } }
  ]
}
```

This shape covers "run a command, chain conditionals, set durations" directly, and extends to
hooks / plugin calls / sub-tasks via more `action`/`trigger` variants.

## 3. Domain model (platform-neutral, testable)

- **ScheduledTask** - as above. Pure data + a couple of helpers (isDue, describe).
- **Trigger** (tagged union): `Manual`, `Interval(every)`, `Once(delay|at)`, `TimeOfDay(at, clock)`,
  `Cron(expr)`, `Event(hook, match?)`.
- **Condition** (tagged union, composable): leaf predicates (`PlayerCount`, `TimeWindow`,
  `ServerState`, `PluginLoaded`, `PermissionHeld`, `VariableEquals`, `Chance(p)`,
  `PrevStepSucceeded`) plus `All[]` / `Any[]` / `Not` combinators.
- **Action** (tagged union): `RunCommand(target, text)`, `Broadcast(message, filter?)`,
  `CallHook(name, args)`, `InvokePlugin(plugin, method, args)`, `RunTask(taskId)`,
  `SetVariable(scope, key, value)`, `Wait(duration)`.
- **Duration** - parse `"4m"`, `"55s"`, `"1h30m"`; integer seconds at runtime.
- **TaskRun / RunContext** - one execution instance: carries variables, captured step results, the
  start time, and the cursor through `steps[]`. Conditions/actions read & write the context, which
  is what makes "chain conditionals" and "use a previous result" work.
- **StepResult / RunHistory** - per-step outcome (ok/skip/error + message), kept in a ring buffer
  for the inspect view.

Everything here is plain C# with no Carbon/Oxide/game references, so it lives in the plugin and is
unit-testable in isolation (and bundles cleanly).

## 4. Runtime architecture

Layers, outermost to core:

```
  /tasksched (CUI)  ->  UI view-models  ->  SchedulerService  ->  domain model
                                              |  |  |
                 TriggerEvaluator  ConditionEvaluator  ActionExecutor
                                              |
                       HizenLabs.Shared platform helpers (timers, CUI, hooks,
                       data, commands, messaging)  -- the only Carbon/Oxide seam
```

- **SchedulerService** - owns the in-memory task registry; subscribes time + event triggers;
  on a 1s tick evaluates due time-triggers and dispatches runs; enforces `runPolicy`; persists
  `nextRunAt`. Single front door the UI talks to.
- **TriggerEvaluator** - turns triggers into "due now?" decisions (time) or hook bindings (event).
- **ConditionEvaluator** - evaluates a `Condition` against a `RunContext` + live server state.
- **ActionExecutor** - runs a `TaskRun` as a small state machine: walk `steps[]`, honor
  `waitBefore` via a shared timer, check each `guard`, dispatch the `action`, record a
  `StepResult`. Because steps have durations, a run spans many ticks - it is a coroutine-style
  sequence, not a blocking loop.

## 5. Triggers in depth

- **Manual** - only runs from the UI / a command. The default for a freshly authored task.
- **Interval(every)** - every N seconds from enable/last-run.
- **Once(delay | at)** - fire a single time after a delay or at an absolute time.
- **TimeOfDay(at, clock)** / **Cron(expr)** - `clock` selects realLocal / realUtc / inGame (Rust
  day cycle). Cron is the power-user form; TimeOfDay is the friendly one.
- **Event(hook, match?)** - run when a named hook fires (e.g. `OnPlayerConnected`), optional arg
  match. *Constraint:* Oxide/Carbon dispatch hooks by method presence, so you can't subscribe to
  arbitrary hook names at runtime cleanly. v1 ships a **curated set** of supported hooks the plugin
  actually implements (connect/disconnect, death, wipe/init, etc.); tasks bind to those by name.
  Expanding the set is a code change, deliberately.

## 6. Conditions in depth

Leaf predicates compare server/run state to an operand (`PlayerCount > 0`, `TimeWindow 22:00-06:00`,
`PluginLoaded "Clans"`, `VariableEquals warned true`, `Chance 0.25`). `All`/`Any`/`Not` compose
them. Conditions appear in two places: a task-level pre-gate (optional) and per-step `guard`s.
Because they read `RunContext`, a later step can branch on what an earlier step captured.

## 7. Actions in depth

- **RunCommand(target, text)** - `target` = server console or a player (rcon-style vs runuser).
- **Broadcast(message, filter?)** - chat message to all / a filtered set; localized.
- **CallHook(name, args)** - fire a hook into the plugin ecosystem.
- **InvokePlugin(plugin, method, args)** - call another plugin's method (the most coupling-heavy;
  see open questions).
- **RunTask(taskId)** - compose tasks; needs a recursion/depth guard.
- **SetVariable(scope, key, value)** - task-run-local or global persisted state; powers conditionals.
- **Wait(duration)** - explicit pause as a step (vs `waitBefore`).

Safety: command/hook/plugin actions are powerful, so they sit behind permissions and (recommended)
an allow-list + audit log. `RunTask` needs cycle detection.

## 8. Execution model

- A run is a **TaskRun** with its own `RunContext`. The `ActionExecutor` advances it across ticks,
  realizing durations through the shared timer.
- **runPolicy** on re-trigger while running: `skip` (default), `queue`, or `parallel`.
- **Survival**: definitions + `nextRunAt` are persisted, so a reload/restart resumes the schedule;
  in-flight runs are not resumed in v1 (documented), only rescheduled.
- **History**: a bounded ring buffer of recent runs + per-step results feeds the inspect view.

## 9. UI (CUI)

`/tasksched` (permission-gated) opens a stateful per-player panel. Three views:

- **Browse** - searchable/filterable, paginated list (filter by name/tag/enabled/trigger type);
  per-row quick actions: run, enable/disable, edit, delete, inspect.
- **Inspect** - a task's recent runs, step results, errors, next scheduled time.
- **Edit/Compose** - add/remove/reorder steps; pick action & condition types from dropdowns;
  enter values. Full in-CUI value entry is the heavy part - hybrid approach: pickers in CUI, free
  text values captured via a chat-input prompt ("type the command, or `cancel`").

CUI is stateful and per-player, so model it as **view-model + renderer**: each open panel has a
view-model (current view, selection, search text, page); a renderer turns it into CUI elements;
input handlers mutate the view-model and re-render. Keep UI state out of the domain model.

## 10. Persistence & cross-cutting

- **Definitions + state** - JSON data files (one for tasks, one for variables/state). Schema
  `version` field + a migration step so the format can evolve.
- **Config** - plugin-level settings via the `[Config]`/Settings source generator (tick rate,
  history size, default permission, command allow-list toggle).
- **Localization** - all UI/broadcast strings via the Localizer source generator.
- **Permissions** - `taskscheduler.use` (open UI / run), `taskscheduler.admin` (edit/delete).

## 11. What HizenLabs.Shared must provide

The plugin references **only** HizenLabs.Shared; every Carbon/Oxide seam is a shared helper that
hides the `#if CARBON` internally. TaskScheduler needs (mark = net-new shared work):

| Capability                         | Used for                          | Status        |
|------------------------------------|-----------------------------------|---------------|
| Timer / scheduler primitive        | tick loop, waits, durations       | net-new       |
| CUI fluent builder                 | the whole UI                      | net-new (big) |
| Chat/console command registration  | `/tasksched`, command actions     | net-new       |
| Hook subscription bridge           | event triggers (curated set)      | net-new       |
| Data store (read/write JSON)       | definitions + state persistence   | net-new       |
| Player messaging + permission API  | broadcast, gating                 | net-new       |
| Server/console command execution   | RunCommand action                 | net-new       |
| Server time + in-game time         | TimeOfDay/Cron triggers           | net-new       |
| Config + Localizer source gens     | settings + strings                | exists (SDK)  |

This table is really the build order: **the shared helpers gate the plugin.** The CUI builder and
the timer primitive are the long poles; everything else is small. Worth deciding which of these
are TaskScheduler-specific vs. general enough to live in shared for all plugins (most are general).

## 12. Bundling & file layout

No partial classes. Each type is its own file in a sub-namespace under the plugin folder; the
plugin references them and the bundler inlines the reachable ones as private nested types (verified:
referenced types inline, unreferenced ones tree-shake, sub-namespace usings are dropped). Helper
types must be `internal` (not public) so the bundler's "first public type = the plugin" heuristic
stays correct.

```
TaskScheduler/
  TaskScheduler.cs              // the public plugin class (entry); wires everything
  Model/                        // ns ...TaskScheduler.Model   (neutral, testable)
    ScheduledTask.cs  Trigger.cs  Condition.cs  Action.cs  Step.cs  Duration.cs
    TaskRun.cs  RunContext.cs  StepResult.cs
  Engine/                       // ns ...TaskScheduler.Engine
    SchedulerService.cs  TriggerEvaluator.cs  ConditionEvaluator.cs  ActionExecutor.cs
  Persistence/                  // ns ...TaskScheduler.Persistence
    TaskStore.cs  StateStore.cs
  Ui/                           // ns ...TaskScheduler.Ui
    TaskSchedulerUi.cs  BrowseView.cs  InspectView.cs  EditView.cs  UiState.cs
```

Deploy -> bundler wiring (separate TODO) must pass **the plugin folder's .cs files + the shared
runtime** together as the inlinable set, with `TaskScheduler.cs` as the `--plugin` entry.

## 13. Phasing

- **v1 (MVP)**: domain model; engine with Manual / Interval / Once / TimeOfDay triggers; Command /
  Broadcast / Wait actions; PlayerCount / TimeWindow / Chance conditions; persistence; Browse +
  Inspect + Run/Enable UI. Editing minimal (or hand-edit the data file).
- **v2**: Event(hook) triggers (curated set); full per-step guards + RunTask chaining; variables &
  SetVariable; in-CUI compose/edit; CallHook action.
- **v3**: InvokePlugin; Cron + in-game-time; templates/import-export; richer history & audit log.

## 14. Decisions for you

1. **In-CUI editor depth for v1** - read/run/enable only (edit via data file), or attempt the
   hybrid compose UI? (Big scope swing.)
2. **Scheduling clock priority** - real-time first, or is in-game (Rust day) time a v1 must?
3. **InvokePlugin scope** - include cross-plugin method calls (powerful, coupling + safety cost) or
   defer to v3?
4. **Command-execution security** - allow-list + audit by default, or trust the `admin` permission?
5. **Shared vs plugin** - which helpers from section 11 go into HizenLabs.Shared (reusable) vs.
   stay local to TaskScheduler? (My lean: all of them are general enough for shared.)
```
