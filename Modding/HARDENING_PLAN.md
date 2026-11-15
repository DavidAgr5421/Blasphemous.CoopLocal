# Hardening / optimization plan (bug prevention + performance, no new features)

Tools: use CodeGraph (`codegraph_explore`) before grep/Read; save decisions to Engram; delegate read-only audits to cheaper subagents (sonnet/haiku), implement and build here. Build: `dotnet build` (0 errors) and `cd Tests && dotnet test`.

## Phase 1 - Prevent wrong-player bugs (items 1, 2)
1. Audit every remaining "P1 owns this" assumption: raw `Input.GetKey*` used for gameplay decisions, `Core.Logic.Penitent` used to decide who acted, Rewired player 0 reads, `CoopLocal.Player2` single-guest checks. Fix each by resolving the owner per slot (`Player2Input.GetXxx(slot)`, `CoopPlayers`, `IsGuestPlayer`).
2. Orphan/duplicate guard: periodic check (~1 s) that live `Penitent` objects == registered players (P1 + `Player2/3/4`); log a warning and destroy strays. Makes bugs like the P4 clone visible.

## Phase 2 - Online / input robustness (items 3, 4)
3. Stale remote input: clear a remote session's buttons/axes after ~0.5 s without packets (`CoopNetworkManager.GetRemote*`).
4. Held vs. press: interactables/doors should use press-down (edge), not held Up.

## Phase 3 - Performance (items 6, 7, 8)
6. Cache owner per Animator and reflection reads in per-frame guest patches (`GetComponentInParent<Penitent>` in ~8 animation patches, FieldInfo.GetValue/SetValue).
7. Remove remaining per-frame `Find*`; use events instead of polling where a hook exists.
8. Measure enemy retarget throttle (`RetargetEveryNFrames = 3`) in play; tune.

## Phase 4 - Quality (items 5, 9, 10)
5. More unit tests on pure logic (edge helper, guest keep/respawn decision, placement, layout parsing).
9. Partial-class splits of SwordElementalUpgrades, CoopGameSetupUI, CoopMatchConfigUI, Player2Input.
10. Finish migrating `CoopLocal.Player2/3/4` / `GetAllExtraPlayers()` call sites to `CoopPlayers`.

## Status
- Phase 1: DONE. Phase 2: DONE. Phase 3: DONE. Phase 4: tests done; migration and splits deliberately deferred (see NOTES.md).
