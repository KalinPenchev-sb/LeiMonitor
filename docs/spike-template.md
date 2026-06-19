# Spike: [Short Title – Feature or Decision Being Investigated]

**Epic:** [Parent Epic name or Jira reference]
**Type:** Spike
**Target:** [N sprint(s)] (time-boxed)

---

## Background

[2–4 sentences describing the business or technical context that makes this investigation necessary. Explain the problem or open question, why it matters now, and what decision or output is needed before implementation can begin. Avoid describing the solution here.]

---

## Key Questions

- [Primary question this spike must answer]
- [Secondary question or dependency that must be resolved]
- [Infrastructure or platform question that gates a decision]
- [Data or schema question that must be confirmed]
- [Operational or observability question that affects the recommended approach]

> Add or remove questions as needed. Each question should map to at least one Acceptance Criterion.

---

## Options Under Consideration

> Include this section only when the spike involves a hosting, architecture, or technology selection decision. Remove it for pure investigation spikes.

### Option A – [Name]

[Describe the option: how it works, how it is deployed, how it is configured, how observability is achieved, and what networking or infrastructure it requires. End with the main trade-off or risk.]

### Option B – [Name]

[Describe the option using the same structure as Option A.]

### Option C – [Name]

[Describe the option using the same structure as Option A.]

---

## Out of Scope

- [Thing that is explicitly excluded and why, to prevent scope creep]
- [Related feature or concern owned by a different team or ticket]
- [Non-functional concern deferred to a later spike or story]

---

## Risks and Assumptions

- [Assumption about data, schema, or infrastructure that must be validated early]
- [Dependency on a team, environment, or resource that may not be available]
- [Risk that would block or significantly change the recommendation if the assumption proves false]

---

## Acceptance Criteria

- [ ] [Specific, verifiable output: a schema is confirmed, a question is answered, a recommendation is documented]
- [ ] [Specific, verifiable output: options are evaluated and a recommendation is made with rationale and trade-offs]
- [ ] [Specific, verifiable output: constraints, dependencies, and risks are captured and linked to the ticket]
- [ ] Findings and recommendations are documented and linked to this Jira ticket.

---

## Findings

> Populate this section during or after the spike. Leave blank when creating the spike.

### [Key Question 1 – restate question as heading]

[Finding, evidence, and any caveats or follow-up actions.]

### [Key Question 2 – restate question as heading]

[Finding, evidence, and any caveats or follow-up actions.]

---

## Recommendation

> Populate this section when the spike is complete.

**Recommended approach:** [Option X / approach name]

**Rationale:** [Why this option was selected over the alternatives.]

**Pre-requisites:** [Infrastructure, access, or decisions that must be in place before implementation begins.]

**Follow-up actions:**

- [ ] [Action, owner, target date]
- [ ] [Action, owner, target date]
