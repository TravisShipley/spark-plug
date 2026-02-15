---
document_role: policy
audience: ai, developers
scope: repository
status: active
---

# Spark Plug – Document Role Decision Tree

## Step 1 — Does this document define constraints?

**Question**

Does this document tell developers or AI what they must or must not do?

Examples:

- Naming rules
- Architecture constraints
- Layer boundaries
- Coding standards
- AI behavior rules
- Dependency direction
- Allowed / forbidden patterns

**If YES → `policy`**

Examples:

- ARCHITECTURE.md
- CODING_STANDARDS.md
- AI_STYLE.md
- AI_TASK_STRATEGY.md

**Definition**

Policy documents are **authoritative constraints**.  
They override design preferences and implementation habits.

### Policy Authority

Policy documents are authoritative.

If a **policy** document conflicts with a **design**, **reference**, or **process** document, the policy document takes precedence.

---

## Step 2 — Does this explain how a system works?

**Question**

Is this document describing:

- How a system behaves?
- How components interact?
- Data flow?
- Lifecycle?
- Runtime architecture?

Does it answer:  
**“How does this feature/system work?”**

**If YES → `design`**

Examples:

- GeneratorFlow.md
- UpgradeArchitecture.md
- DisplayPipeline.md
- SaveSystemDesign.md

**Definition**

Design documents explain intent and structure, but do not impose rules.

---

## Step 3 — Is this structured information or lookup material?

**Question**

Is this document primarily:

- A schema
- Field descriptions
- Data structure reference
- ID lists
- Format specification

Does it answer:  
**“What data exists and what does it mean?”**

**If YES → `reference`**

Examples:

- DATA_SCHEMA.md
- CONTENT_SCHEMA.md
- MODIFIER_TARGETS.md

**Definition**

Reference documents are factual and descriptive.  
They should avoid opinions or implementation guidance.

---

## Step 4 — Does this describe a workflow or procedure?

**Question**

Is this document a step-by-step guide for:

- Releasing builds
- Importing content
- Running tools
- Creating content
- Testing flows

Does it answer:  
**“How do I perform this task?”**

**If YES → `process`**

Examples:

- CONTENT_PIPELINE.md
- RELEASE_PROCESS.md
- VERTICAL_SLICE_CHECKLIST.md

**Definition**

Process documents describe repeatable workflows.

---

## Location

All project documentation must live under the `/Docs` directory.

Exceptions:

- `README.md` (repository entry point)
- `LICENSE` and other standard root files

Documentation should not be scattered across feature folders unless it is tightly coupled to that feature’s runtime assets.

Use this guide when creating a new document.

Every document should declare its role in frontmatter:

```
---
document_role: policy | design | reference | process
---
```

This decision tree helps you choose the correct role.

---

## Avoid Duplication

Before creating a new document:

1. Check whether an existing document already covers the topic.
2. Prefer extending an existing document over creating a new one.
3. If unsure, update the closest existing document.

Multiple documents describing the same system or rules are not allowed.

---

## When to Create a New Document

Create a new document only if:

- The content exceeds approximately one page, **or**
- The topic is reused across multiple systems, **or**
- The content defines project‑level behavior or policy

Otherwise, add the content to an existing document.

---

## Document Status

Documents must declare a status in frontmatter:

- `active` — authoritative and current
- `draft` — incomplete or evolving
- `deprecated` — retained for historical reference only

AI and developers should treat **deprecated** documents as non‑authoritative.

---

## Summary

| Role      | Purpose                              |
| --------- | ------------------------------------ |
| policy    | Rules and constraints (must follow)  |
| design    | System architecture and behavior     |
| reference | Data definitions and lookup material |
| process   | Step-by-step workflows               |

---

## Quick Decision Shortcut

If the document says:

- **“You must / must not”** → policy
- **“This system works like this”** → design
- **“These fields exist”** → reference
- **“Follow these steps”** → process

---

## Spark Plug Naming Convention

| Role      | Recommended filename style    |
| --------- | ----------------------------- |
| policy    | UPPERCASE (ARCHITECTURE.md)   |
| design    | PascalCase (GeneratorFlow.md) |
| reference | UPPERCASE or PascalCase       |
| process   | PascalCase or descriptive     |

Frontmatter is the authoritative role indicator.

---

## AI Interpretation

AI should treat roles differently:

- **policy** → strict constraints
- **design** → architectural intent
- **reference** → source of truth for data
- **process** → execution instructions

When in doubt, prefer **policy** for constraints and **design** for explanations.
