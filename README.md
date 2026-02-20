# Spark Plug

Spark Plug is a data-driven engine for building and operating idle and incremental games.

The goal of Spark Plug is to separate **game design from game code**. Economy balance, progression, generators, upgrades, and live content are defined in external data so that a game can be tuned, expanded, and operated without constant code changes.

The content schema is designed to support a wide range of idle game styles, including:

- Classic generator chains (AdCap-style businesses)
- Production pipelines and resource conversion networks
- Node/zone progression and world expansion
- Manager/automation-driven play
- Upgrade-heavy optimization games
- Prestige and long-term meta progression
- Event-based or live-ops driven economies

Rather than prescribing a specific gameplay formula, Spark Plug focuses on flexible economic primitives (nodes, resources, modifiers, and progression systems) that can be combined to model most incremental game structures—from simple tap-to-grow experiences to complex simulation-style idle games.

## Documentation

Start here: `Docs/README.md`

Primary doc sections:

- Docs/Architecture
- Docs/Data
- Docs/Content
- Docs/Policies
- Docs/Process

Key references:

- `Docs/Architecture/MentalModel.md` core runtime/economy mental model
- `Docs/Data/DatasheetGuide.md` Google Sheets authoring and table mapping guide

## Root Docs

- `CONTRIBUTING.md` contribution workflow and repository expectations
- `CODING_STANDARDS.md` coding conventions and implementation rules
- `SKILLS.md` AI/coding-agent capability profile for this project
