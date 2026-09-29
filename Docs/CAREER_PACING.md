# Career mode pacing and ease of use

Draft, 29 September 2026. Design research and proposals for [career mode](CURRICULUM.md).
Nothing here is implemented. Time targets are hypotheses to test with novice observation
(see [CREATOR_EXPERIENCE.md](CREATOR_EXPERIENCE.md)), not measured results.

## What we learned from other games and apps

| Source | What it does well | What Ember takes | What Ember avoids |
| --- | --- | --- | --- |
| Game Dev Tycoon | Garage → office → big office; each move adds staff and features. Release screen with review scores. After releases, you learn which topic/genre combinations work. | Office moves mark stage changes. A release ritual with reviews. Post-release reports that teach what worked. | Sliders that stand in for development. In Ember, the player does the real work. |
| Game Dev Story (Kairosoft) | Short cycles, lots of small celebrations, growing fan count. | Short feedback cycles and a visible fan count. | Grind for random stat boosts. |
| Mad Games Tycoon 2, Software Inc. | Rooms, staff with skills and traits, specs that teams build from. | Simulated teammates with traits from Stage 6, building from player specs. | Management spreadsheets that replace building. |
| The Farmer Was Replaced | Continuous progression. Resources from your code buy unlocks, including language features such as loops. Manual work → automation. | Reveal code features as the player needs them. Repeated manual work motivates automation. | Long idle waits and grind between unlocks. |
| Human Resource Machine, 7 Billion Humans | Office career story; you start with 2 commands and earn more with promotions. Optional size and speed challenges. Humor. | A career ladder story with humor. Optional optimization targets on missions. | Difficulty walls on the main path. |
| Zachtronics (Opus Magnum, TIS-100, Exapunks) | Histograms compare your solution to others on cost, cycles or area. Sandbox solutions. In-game manual. | Optional histograms from bundled reference data, offline. A codex that fills as you learn. | Expert-only difficulty. |
| Game Builder Garage | Guided lessons build a game step by step; each is followed by checkpoint puzzles, including fixing broken games. | Lesson → checkpoint is our mission → transfer check. "Fix this broken game" puzzles. | Long guided lessons without free play in between. |
| Super Mario Maker 2 (Yamamura's Dojo) | Very short, funny level-design theory lessons. | Short design lessons tied to the stage. | Theory without a making task. |
| Dreams, LittleBigPlanet | Friendly guide characters, remixing others' creations. | Remixable starter projects and example games. | Online-only sharing. |
| Kerbal Space Program | Sandbox, Science and Career modes; career contracts, funds and reputation. | Free Create next to Career. Optional contracts as side missions. | Money grind that blocks experimenting. |
| Factorio | Each new problem makes the next technology feel necessary. | Need-driven unlocks: feel the pain, then get the tool. | Very long tech trees. |
| Duolingo, Mimo, Grasshopper | 5-minute lessons, spaced review. | Short missions and spaced "refresher" contracts. | Streak guilt and daily rewards. The roadmap forbids these. |
| Scratch, Swift Playgrounds, CodeCombat | Blocks → text bridge; immediate visual result from code. | Rule ↔ C# view. Code changes are visible in Play right away. | Toy languages the player must unlearn later. |

Sources: [The Farmer Was Replaced unlocks](https://thefarmerwasreplaced.wiki.gg/wiki/Unlocks),
[Game Builder Garage checkpoints](https://game-builder-garage.fandom.com/wiki/Checkpoints_-_An_Introduction),
[Game Dev Tycoon development phases](https://gamedevtycoon.fandom.com/wiki/Tech_and_Design_Points_Generation_Algorithm),
[Human Resource Machine](https://en.wikipedia.org/wiki/Human_Resource_Machine).

## Pacing principles

1. **Three nested loops.**
   - *Minute loop:* change → Play → see. Target under 2 seconds from edit to playing.
   - *Session loop:* one mission or contract, 5–20 minutes, ending at a natural save point
     with a teaser for the next one.
   - *Career loop:* stage project → release → reactions → office move. About 1 hour for
     Stage 1, growing in later stages.
2. **Pain before tool.** Introduce each tool just after the player feels its absence:

   | The player first... | Then gets |
   | --- | --- |
   | Places ten gems by hand | Templates |
   | Copies the same rule five times | Code and loops |
   | Loses work to a bad change | Checkpoints and history |
   | Both they and Sam move the same object | Diff and merge |
   | Forgets what teammates are doing | Task board |
   | Ships a slow build and gets bad reviews | Profiler |

3. **Sawtooth difficulty.** Each stage starts easy with one new idea, ramps up, ends with a
   capstone (the stage project), then drops back for the next stage. Never two new tools
   in one mission.
4. **Small mandatory path, deep optional layer.** Required: one mission per concept, the
   transfer check and the stage project. Optional: bonus contracts, optimization targets,
   histograms, remix challenges and design dojo lessons.
5. **Short sessions end cleanly.** Missions autosave at completion. On return, a
   three-line recap says what the player made, what they learned and what is next.
6. **Safe failure.** Before each mission, save a checkpoint. "Rewind mission" restores it.
   A failed release is a story event with a report, never a game over.
7. **Deadlines are optional pressure.** Relaxed mode turns story deadlines off. Standard mode
   uses story hours that only pass during actions and never while the player reads or thinks.

Stage length targets (hypotheses):

| Stage | Target | Stage | Target |
| --- | --- | --- | --- |
| 1 My First Game | 45–60 min | 6 Indie Studio | 3–5 h |
| 2 Game Jam | 1–1.5 h | 7 AA Specialist | 2–3 h per track |
| 3 Indie store | 2–3 h | 8 AAA | 3–5 h |
| 4 Large store | 3–4 h | 9 Worlds Online | 4–6 h |
| 5 Co-op | 1.5–2 h | | |

## Creative proposals

### One game grows with you

In real life, many developers polish a jam game and ship it. Use that path:

- The Stage 2 jam game becomes the Stage 3 indie-store release.
- Stage 4 is its sequel or big expansion, reusing its assets and code.
- In Stage 5, the friend joins that same project.

The player sees their own game improve over hours of play. Content cost also drops,
because later stages build on the player's work instead of new starter projects.
A "fresh start" option supplies a prepared project for players who skipped a stage.

### The studio is an Ember scene

The career hub is a 3D office scene built with Ember:
bedroom → jam venue → coworking desk → shared flat → small office → AA floor → AAA campus
→ live-ops center. Players can decorate it with the normal editor, which is casual
practice with Move/Turn/Size. Each office move is the visible reward for a stage.
The hub also proves Ember can make it.

### Reviews that teach

Release reactions follow the Game Dev Tycoon ritual, but every comment comes from a
measurement and links to a fix:

- "I got stuck behind the crate for two minutes." → Playtest heatmap → Level design dojo.
- "It stuttered in the cave." → Profiler mission.
- "The store page promised co-op." → Store page mission.

Simulated players are bundled bots that play the build and record where they stop, fail
or finish. Scores come only from measured results (completable, crashes, frame time, time
stuck, promises kept). They never judge art.

### Contracts: practice without grind

Optional side jobs from simulated clients, available from Stage 1:

- "Fix my broken game": troubleshooting puzzles in the style of Game Builder Garage.
- "Build to spec": a small scene or rule from a client brief.
- "Make it faster" or "make it smaller": optimization targets, with optional histograms.

Contracts reuse earlier concepts. They are the spaced review. The game suggests one when a
concept has not been used for a while. Rewards are fans, money for the office, and
cosmetic office items. Contracts never gate the main path or Free Create.

### Light economy

- **Fans** come from releases and grow with measured quality. They drive story reactions.
- **Money** matters from Stage 6 (hiring and office). Below that, it is a score.
- **Know-how** marks concepts proven by transfer checks. It is shown in the codex and
  unlocks optional deep-dive lessons, never tools.

### Cast

- **Ember**, a spark mascot and mentor, who gives the "Why?" explanations and hints.
- **Sam**, the friend and later co-founder, who makes mistakes the player learns to catch.
- **A rival developer**, whose releases make market events.
- **Clients, a publisher and players**, who give contracts, deadlines and reviews.

Tone: warm, dry humor, like Human Resource Machine. Characters never mock the player's work.

### Codex

A searchable in-game manual. Every concept the player meets adds a page with the plain
explanation, the industry term, a tiny example and a "try it" button that opens a practice
scene. It works offline and doubles as a reference in Free Create.

## Making it easy to work with

- **Instant Play.** Play starts in place without reloading. C# changes reload during Play (L.3).
- **One goal on screen.** The mission panel shows one goal line, the current step, Why?
  and Hint. It never covers the target object.
- **Hint ladder with "Show me".** Nudge → pointer arrow → a ghost demonstration that the
  player accepts as one undoable action.
- **Errors in plain words.** Every error names the object, says what happened and links to
  a fix or codex page. Technical details stay behind "More".
- **Rule trace and debug overlay.** In Play, show which rules fired and why others did not.
  Beginners learn debugging from Stage 2.
- **Pause anywhere.** Missions can be left and resumed. Nothing expires in real time.
- **Difficulty modes.** Relaxed (no deadlines, extra hints), Standard, Challenge (story
  deadlines on, optimization targets visible).
- **Accessibility.** Readable scaling, full keyboard use, non-color cues, and captions for
  all sound feedback.

## Making content cheap to build

- Missions, stages, contracts, reviews and dialogue are data (L.1).
- A mission test harness plays each mission's completion conditions against a recorded or
  scripted solution in CI, so content changes do not silently break missions.
- A mission authoring template: goal, pain point, tool, steps, hints, transfer check.
- Reuse the player's growing game across Stages 2–5.
- Bundled simulated players are one configurable bot, not per-stage code.

## Anti-patterns to avoid

- Fake development (sliders or progress bars) replacing real building in Stages 1–7.
- Grind: repeating content to afford the next step.
- Streaks, daily rewards or guilt notifications.
- Text walls. One idea per screen; explanations stay optional.
- Locking Free Create or any finished tool behind career progress.
- Reviews that judge taste instead of measured facts.
- A Stage 8 that becomes spreadsheets. The plan must produce a playable result.

## Suggested next steps

1. Paper-prototype Stage 1 with the pacing above and test it with one novice.
2. Add a "pain point" field to each mission in CURRICULUM.md.
3. Add the mission test harness and "Rewind mission" to L.1 acceptance.
4. Prototype the reviews-that-teach screen with one bot playtester, as part of C.2.
