# Asset management research protocol

Status: proposed protocol for [#671](https://github.com/honua-io/honua-console/issues/671).
No participant sessions or usability findings have been recorded.

This protocol supports the task-first asset workflows in
[Server #7300](https://github.com/honua-io/honua-server/issues/7300), the Console
[implementation ticket](https://github.com/honua-io/honua-console/issues/670), and
the [resident reporting ticket](https://github.com/honua-io/honua-studio/issues/225).
Prototype sessions precede UI implementation. Repeat the same tasks against the
integrated demo, retaining failures and changes between rounds.

## Before scheduling sessions

The product lead supplies a researcher, prototype owner, partner contacts, and
session dates. Recruit three to five consenting partner staff across public works
lead, field crew, parks manager, and resident-service clerk roles. Record which
roles the small sample does not cover. Recruit residents separately; staff acting
as residents do not establish resident usability.

Agree the consent wording, withdrawal process, permitted recording, access, and
retention before collecting participant information. Keep names, contact details,
employers, recordings, and the participant-to-session key in the approved restricted
research location. Public repository findings use anonymous session IDs only.
Participation is voluntary; use synthetic work rather than a participant's live
service requests, resident contact information, or personnel records.

Freeze the prototype revision, task script, fixture revision, and proposed targets
below before the first session. Confirm the researcher can reset each task without
changing another participant's state. Mark simulated responses explicitly in the
research record. A prototype result does not establish backend correctness.

The integrated round uses the canonical Server evaluation scenario document
(`docs/internal/contributor/specs/asset-management-evaluation.md`, tracked in
[Server #7302](https://github.com/honua-io/honua-server/issues/7302)). Record its
commit and scenario IDs with the session rather than maintaining a second fixture
or inventing different completion, programme, or privacy rules here.

## Questions and contrary evidence

| Hypothesis | Evidence that would challenge it |
| --- | --- |
| Crews can start from today's assigned work without learning a GIS layer model. | Participants search by layer, cannot identify their assignment, or need coaching to begin. |
| A manager can understand a recurring programme before approving work. | Participants approve the wrong cohort or misread the first due date, timezone, or task count. |
| Residents can report a problem with a text location and understand the receipt. | A map is required, optional contact feels mandatory, or the receipt implies dispatch or resolution. |
| Clerks can separate private intake from operational tasks. | Participants disclose contact details or mistake an untriaged request for assigned work. |

Ask participants how they currently accomplish the work before showing the
prototype. Observe the attempted task before asking for preferences. Keep observed
actions, participant opinions, researcher interpretations, and unresolved questions
in separate fields. Preserve examples that contradict the preferred design.

## Tasks and proposed measures

These are draft research targets, not operational service promises. The researcher
and product lead settle them before recruitment and record any revision before
sessions begin. Time begins after the prompt is read and ends at the participant's
stated finish; record interruptions separately.

| Role and neutral prompt | Observable success | Initial target |
| --- | --- | --- |
| Crew: “Find the work assigned to you today and record what you found at this asset.” | Selects the intended assignment, records the required inspection and evidence, and distinguishes accepted completion from pending sync or conflict. | Unaided within 3 minutes; no lost evidence or false completion. |
| Crew recovery: “Your connection stopped after you sent the result. Continue when it returns.” | Checks the existing submission, preserves entered evidence, and understands the eventual outcome without creating another inspection. | Unaided within 2 minutes after reconnect; no duplicate submission accepted as new work. |
| Manager: “Plan the next inspection round for these assets. Check the proposed work before approving it.” | Identifies cohort, cadence, timezone, first due date, and preview count; detects the deliberately wrong selection before approval. | Unaided within 5 minutes; no approval of the wrong cohort. |
| Resident: “Report the fallen branch near this location. You prefer not to provide contact details.” | Chooses an appropriate category, describes the location without the map, submits without contact, and explains how to check the receipt. | Unaided within 3 minutes; no disclosure or implied dispatch guarantee. |
| Clerk: “Review this new public report and decide what should happen next.” | Recognizes untriaged state, keeps contact details private, and distinguishes triage from creation or assignment of an operational task. | Unaided within 3 minutes; no unintended publication or assignment. |

Programme and intake tasks require settled product policy and a corresponding
prototype. Record a missing prerequisite as blocked; do not silently substitute a
policy. The integrated round additionally requires the published native client
contracts and the relevant Server scenario to run successfully.

Record success as unaided, assisted, failed, or blocked. An answer to a procedural
question counts as assistance if it reveals the next action. Record the first
error, recovery path, elapsed time, assistance, and the participant's understanding
of the final state. For a sample this small, report counts with denominators and
individual anonymous observations rather than population success rates.

## Session sequence

1. Confirm consent, role, access needs, device familiarity, and permission to take
   notes. Explain that the design is being tested and the participant can stop.
2. Ask about one recent ordinary work example without collecting identifying data.
3. Present each relevant task without naming controls or coaching the path. Rotate
   task order where it does not break the scenario's prerequisites.
4. Observe the first attempt. Ask what the participant expects before a risky or
   ambiguous transition, without correcting the expectation.
5. After the attempt, ask what happened, what was uncertain, and what they would
   do next. Distinguish these answers from the observed outcome.
6. Ask about missing work and conflicting evidence. Explain any simulated behavior
   and close using the agreed withdrawal and retention process.

For crew sessions, assess one-handed use, glare, and gloves where practical while
the participant is stationary in a safe setting. Record conditions and adaptations
instead of treating a desktop session as outdoor evidence. Include keyboard-only
and screen-reader attempts with participants or qualified accessibility reviewers;
record device, browser, assistive technology, focus path, labels, announcements,
and blockers. Usability sessions alone do not establish WCAG conformance.

Exercise empty work lists, loading, unavailable service, lost response, stale
assignment, and insufficient access as distinct states. Participants must be able
to distinguish a confirmed outcome from an unknown one. Never ask them to bypass
an authorization or validation failure to finish a task.

## Session record

Use one record per task with the following fields in the approved research store:

- Anonymous session ID, round, role, task ID, date, moderator, and observer.
- Consent reference and permitted collection; no identifying details in exports.
- Prototype/build, Server scenario and fixture revisions, device, conditions, and
  accessibility setup; simulated versus connected behavior.
- Frozen target, start/end time, interruptions, outcome, assistance, and errors.
- Observation with timestamp, participant opinion, interpretation, and contrary
  evidence in separate fields.
- Evidence reference, affected workflow, severity, proposed change, owner, and
  unresolved policy or contract prerequisite.

The findings report lists actual session counts, roles absent, inaccessible tasks,
limitations, and observed failures before proposing changes. Every recommendation
links to an observation or is marked as an untested hypothesis. Use vendor-neutral
workflow language and remove identifying details before repository publication.

## Progression and retest

Prototype review is complete only when the planned sessions have occurred and the
findings, limitations, and resulting design decisions are available. A protocol or
an internal walkthrough does not satisfy the participant research requirement.

Before UI implementation, resolve critical wrong-target approvals, privacy failures,
lost evidence, misleading completion, and keyboard or screen-reader blockers in the
prototype and retest the affected tasks. Record any remaining issue and the owner
who accepts its scope; do not relabel a failed task as successful.

Repeat the task set against the integrated demo after implementation, using the
same frozen measures and recording build and contract versions. Link retest
observations to the original failures, record whether the behavior changed, and
retain contrary findings. Close #671 only with actual research and integrated
retest evidence, not this preparation document.
