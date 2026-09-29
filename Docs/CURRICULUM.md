# Ember career mode and curriculum map

Draft, 29 September 2026. This file plans what Ember teaches and how it is presented.
Task IDs, status and gates are in the [active roadmap](ENGINE_ROADMAP.md), under the L
(learning foundation) and C (career mode) tracks. The teaching loop, plain-language rules
and usability acceptance are in [CREATOR_EXPERIENCE.md](CREATOR_EXPERIENCE.md). Nothing in
this file exists unless its status says so.

## Pitch

**Ember is a game about becoming a game developer, and the tools you use are a real engine.**

The player starts alone, making a tiny game in one room. Each stage of the career adds
people, scale and new tools: a game jam, a first indie release, a big store launch, a
friend to work with, a small studio, a specialist role in a larger studio, a planning role
in a huge one, and finally an online world. Every stage explains its basics first, then
adds new features on top of what the player already knows.

What the player builds in the early stages is a real, playable, exportable game made with
the same editor and runtime that power Ember. Later stages add simulation (teammates,
players, reviews, budgets) around that real work, because one person at a desk cannot
staff a real AAA studio.

## Career mode and Free Create

Ember has two ways in, and both open the same projects:

- **Career mode** is the game. Stages reveal tools gradually, give story, goals and
  feedback, and track the player's career.
- **Free Create** is the engine. Every finished tool is available from the start, with
  optional "Why?" help. Career progress never hides or disables anything here.

Career mode may reveal tools in stages, because a staged reveal is the game's structure.
Free Create is never gated. A project started in either mode opens unchanged in the other.
This keeps the CREATOR_EXPERIENCE.md rule that progression never locks core editing.

## Principles

1. **Real work first.** Missions make real, saved changes with normal editor commands,
   undo and validation. Simulation adds context and consequences; it does not replace the
   player's work in stages 1–7.
2. **One concept per mission.** Each mission introduces one idea and asks for one visible
   outcome.
3. **Completion comes from observed state.** Scene state, play events, saved files, compile
   results or plan checks complete a mission. Pressing Next never does.
4. **Transfer proves learning.** Each concept has a transfer check: a different variation,
   made without step-by-step hints, and a short explanation of the rule.
5. **Only teach what works.** A mission ships only after its engine feature passes its
   roadmap gate. Planned stages stay hidden from players.
6. **Offline.** Lessons, hints, simulated characters and reviews are bundled data. No account
   or AI service is required.
7. **Accurate words.** Plain language first; the industry term appears in "Why?" text so the
   player can carry it to other engines and real jobs.
8. **Honest simulation.** Simulated reviews, sales and teammates react to measurable things
   in the player's work (bugs, completion rate, performance, scope, plan gaps). They never
   pretend to judge artistic quality.

## Levels of control

The player's control grows across the career. All levels use the same scene and runtime.

| Level | How the player controls behaviour | First stage | Roadmap dependency |
| --- | --- | --- | --- |
| 1. Direct editing | Place, move, turn, size and configure objects | 1 | M1, UX.4 |
| 2. Action presets | "What happens?" cards such as Collect or Reach goal | 1 | M2.1 |
| 3. Visual rules | When → If → Do blocks with variables and timers | 2 | L.2 |
| 4. Code | C# behaviours reloaded in Play | 3 | L.3 |
| 5. Direction | Specs, tasks and diagrams that simulated teammates build from | 6 | C.6–C.8 |

## Structure of every stage

Each stage has the same shape:

1. **Briefing.** A short story beat and "what is different now". It explains the basics of
   the new situation, such as what a game jam is or why studios split into roles.
2. **New tools.** The tools this stage adds, each introduced by one mission.
3. **Missions.** One concept each, with a transfer check.
4. **Stage project.** A larger deliverable that combines the stage's concepts.
5. **Release and reaction.** The player ships the stage project (in the story) and gets
   simulated feedback tied to measurable results. They can revise and ship again.
6. **Debrief.** Concepts learned, the real-world term for each, and what changes next.

Story time, budgets and deadlines are part of the game's fiction and scoring. Missing one
changes the story outcome; it never blocks editing or deletes work.

## Stage 1 — My First Game

Briefing: you have an idea and a free evening. Learn what a game is made of.
Stage project: a one-room game where the player collects a gem and reaches the exit.
New tools: editor basics, Add library, Move/Turn/Size, Play/Stop, Save, "What happens?" presets.

| # | Concept | Mission | Transfer check | Needs | Status |
| --- | --- | --- | --- | --- | --- |
| 1.1 | Objects and scenes | Add a crate to the starter room | Add a different model somewhere else | M1.1, UX.4 | Feature in progress |
| 1.2 | Position, rotation, scale | Put the crate on the table; turn a chair to face it | Arrange two objects to face each other | M1.2 | Feature in progress |
| 1.3 | Undo and history | Make a mistake on purpose and undo it | Undo two steps and redo one | M1.2, M1.4 | Feature in progress |
| 1.4 | Play vs edit | Predict what Play shows, press Play, then Stop | Explain why a Play-mode change is gone after Stop | UX.4, M2.2 | Feature in progress |
| 1.5 | Player and camera | Choose the player; set walk speed and camera distance | Make a slow, close-camera character and describe the feel | M2.1 | Feature in progress |
| 1.6 | Collision | Stop the player walking through a wall | Block a doorway with a different object | M2.1 | Feature in progress |
| 1.7 | Triggers | Make the gem collectible | Make a different object collectible | M2.1 | Feature in progress |
| 1.8 | Goals | Add a Reach goal at the exit | Move the goal and explain what now counts as winning | M2.1 | Feature in progress |
| 1.9 | Saving | Save, close and reopen the project | Explain what a save keeps | M1.4 | Feature in progress |

The existing `FirstCreationLesson` covers the start of 1.1–1.4 and 1.9. L.1 converts it to data.

## Stage 2 — Game Jam Weekend

Briefing: a jam gives you a theme and 48 story hours. Small scope, fast iteration, finish.
Stage project: a short game on a drawn theme, with a win and a lose condition.
New tools: visual rules, variables, timers, templates, sound feedback, playtest recorder.

| # | Concept | Mission | Transfer check | Needs | Status |
| --- | --- | --- | --- | --- | --- |
| 2.1 | Scope | Pick three features that fit the jam from a list of eight | Explain which feature you cut and why | C.2 | Planned |
| 2.2 | Events | When the player enters the room, turn on a light | Use a different event for a different reaction | L.2 | Planned |
| 2.3 | Conditions | Open the door only if the key was collected | Add a condition on a different object | L.2 | Planned |
| 2.4 | Variables | Count collected gems and show the count | Count the player's jumps | L.2, HUD | Planned |
| 2.5 | Timers and lose rules | Lose when 60 seconds run out | Make a platform appear for 3 seconds | L.2 | Planned |
| 2.6 | Reuse with templates | Save a gem-and-pedestal set and place it three times | Change one copy only and explain overrides | M1.3 | Feature in progress |
| 2.7 | Feedback | Play a sound and flash a light when a gem is collected | Choose feedback for losing | L.2, audio assignment | Planned |
| 2.8 | Playtesting | Watch a recorded playtest; fix the place where the tester got stuck | Fix a different problem from another recording | C.2 | Planned |

Release and reaction: simulated jam ratings react to whether the game can be finished,
whether its rules are clear from testing data, and whether it matches the theme tag.

## Stage 3 — First Release on Pixel Shelf

Pixel Shelf is Ember's fictional indie storefront.
Briefing: people you have never met will download this. It must work, feel good and explain itself.
Stage project: a polished 5–10 minute game with menus, saving and a store page.
New tools: C# behaviours, HUD and menu builder, animation states, save data, packaging, store page editor.

| # | Concept | Mission | Transfer check | Needs | Status |
| --- | --- | --- | --- | --- | --- |
| 3.1 | From rules to code | Open the door rule as C# and change the condition | Rewrite another rule in code | L.3 | Planned |
| 3.2 | Update loop and delta time | Make a coin spin at the same speed at any frame rate | Make a platform move back and forth | L.3 | Planned |
| 3.3 | Components and properties | Expose a Speed property in the Inspector | Expose two properties on a new behaviour | L.3 | Planned |
| 3.4 | Reading errors | Fix a behaviour that does not compile | Fix a behaviour that fails at run time | L.3 | Planned |
| 3.5 | Game feel | Add camera shake and a landing sound to a jump | Improve the feel of a different action | L.3 | Planned |
| 3.6 | Animation states | Blend idle and walk from movement speed | Add a new clip for another state | L.3, existing clips | Planned |
| 3.7 | UI and menus | Add a title screen and pause menu | Add a settings screen | HUD/menu builder | Planned |
| 3.8 | Save data | Save and load collected gems | Save a different piece of game state | L.3, runtime save API | Planned |
| 3.9 | Shipping | Package the game and run it from another folder | Find and fix a missing-asset build error | M2.3 | Feature in progress |
| 3.10 | Store page | Capture screenshots and write a clear description | Explain which screenshot best shows the core loop | C.3, frame export | Planned |

Release and reaction: simulated players and reviews react to crashes, completion rate,
tutorial clarity, load time and missing features promised on the store page.

## Stage 4 — Launch on Summit Store

Summit Store is Ember's fictional large storefront.
Briefing: more players, higher expectations, and much more competition.
Stage project: a multi-level game with options, a trailer and a release checklist.
New tools: profiler, options and input rebinding, multiple levels and areas, cutscene timeline,
scene generator, achievements, QA checklist.

| # | Concept | Mission | Transfer check | Needs | Status |
| --- | --- | --- | --- | --- | --- |
| 4.1 | Performance | Find the most expensive object in a slow scene | Fix a different planted performance problem | Profiler panel | Planned |
| 4.2 | Fixed timestep | Compare a jump at fixed and variable steps | Explain a broken jump in a sample | L.3, existing physics | Planned |
| 4.3 | Options and accessibility | Add input rebinding and a volume slider | Add one more accessibility option | Options builder | Planned |
| 4.4 | Levels and loading | Connect two levels through a door | Add a third level with a different entry | Existing worlds, M2 | Planned |
| 4.5 | Procedural generation | Generate a room; change the seed and compare | Explain what the seed controls and keep one result | M4 | Planned |
| 4.6 | Cutscenes | Trigger a cutscene, then return control to the player | Start a cutscene from a different trigger | M2.2, M3 | Planned |
| 4.7 | Trailer | Frame three shots and export a trailer frame sequence | Reframe a shot for a different mood | M3 | Planned |
| 4.8 | Achievements | Add two achievements from game events | Design one that rewards exploration | C.4 | Planned |
| 4.9 | QA and release checklist | Run the checklist and fix two failures | Explain why each check exists | C.4 | Planned |

Release and reaction: simulated sales, refunds and reviews react to performance on
simulated low-end machines, bug counts, content length and trailer accuracy.

## Stage 5 — Co-op with a Friend

Briefing: a friend wants to help. Two people now change the same project.
Stage project: a sequel or expansion built together, either with a real friend sharing
project packages, or with Sam, a simulated collaborator who sends changes.
New tools: project history, change packages, scene diff and merge, ownership notes, review comments.

| # | Concept | Mission | Transfer check | Needs | Status |
| --- | --- | --- | --- | --- | --- |
| 5.1 | Version history | Save named checkpoints and restore an older one | Find which checkpoint introduced a bug | C.5 | Planned |
| 5.2 | Sharing work | Send and receive a change package | Explain what the package contains and what it does not | C.5 | Planned |
| 5.3 | Diff | Review what Sam changed in a scene | Spot an accidental change in another package | C.5 | Planned |
| 5.4 | Merge conflicts | Resolve a conflict where both of you moved the same object | Resolve a conflict on a different property | C.5 | Planned |
| 5.5 | Ownership and conventions | Agree who owns which files and a naming rule | Fix a change that broke the naming rule | C.5 | Planned |
| 5.6 | Code review | Leave review comments on Sam's behaviour; request one fix | Review a different change and find a real bug | C.5, L.3 | Planned |

The real-friend path uses the same change packages, so the concepts carry over to Git.
The "Why?" text maps each concept to its Git term (commit, diff, merge, conflict, review).

## Stage 6 — Indie Studio (2–4 people)

Briefing: you now run a small studio. You still build, but you also plan and unblock others.
Stage project: a vertical slice delivered on a story schedule with simulated teammates.
New tools: task board, milestones, bug tracker, build pipeline, simulated teammates who
deliver pre-authored assets and behaviours based on the tasks you write.

| # | Concept | Mission | Transfer check | Needs | Status |
| --- | --- | --- | --- | --- | --- |
| 6.1 | Tasks and estimates | Split a feature into tasks a teammate can finish | Split a different feature | C.6 | Planned |
| 6.2 | Specs | Write a spec clear enough that the teammate builds the right thing | Fix a spec that produced the wrong result | C.6 | Planned |
| 6.3 | Milestones and vertical slice | Choose what the slice must prove | Explain what you cut from the slice | C.6 | Planned |
| 6.4 | Bug triage | Prioritize ten reported bugs | Triage a new batch with a different deadline | C.6 | Planned |
| 6.5 | Build automation | Set up an automatic build and test check | Explain which failure the check prevented | C.6, M2.3 | Planned |
| 6.6 | Asset pipeline | Accept an artist's model through import and review | Reject a broken asset with a useful note | C.6, M1.1 | Planned |
| 6.7 | Budget and scope | Keep the project inside its story budget | Recover from a teammate's delay | C.6 | Planned |

Simulated teammates produce results from the player's specs. A vague or missing
requirement produces a visible gap in the delivered content, and the player can see why.

## Stage 7 — AA Studio: Specialist

Briefing: the studio is big enough that you own one discipline. Others own the rest.
Stage project: your part of a larger game, delivered through handoffs with other roles.
New tools: discipline tracks. The player chooses one and can replay the stage with another.

| Track | Focus | Example missions |
| --- | --- | --- |
| Gameplay programmer | Systems, state machines, AI | Build an enemy with Idle, Patrol and Chase states; tune it from playtest data |
| Level designer | Layout, pacing, guidance | Block out a level, guide the player with light and landmarks, fix a pacing dip |
| Lighting and technical art | Mood, readability, performance | Light a scene for two moods inside a frame-time budget |
| Audio designer | Feedback, ambience, mixing | Build a sound set and mix it so key cues stay clear |
| Cinematics | Shots, timing, handoff to gameplay | Author a cutscene that hands control back cleanly |
| Tools programmer | Editor extensions, pipelines | Build a small editor tool that removes a repeated manual step |

Shared concepts for every track: interfaces between disciplines, handoff documents,
working inside someone else's constraints, and depth over breadth. Other disciplines
are supplied by pre-authored content from simulated colleagues.
Needs: C.7, plus the engine features each track uses. Status: Planned.

## Stage 8 — AAA: The Big Leagues

Briefing: hundreds of people build this game. You do not build it yourself any more.
You design it, plan it and decide.
Stage project: a design and production plan that a simulated studio executes.
New tools: design document editor, UML class, sequence and state diagrams, system flow
diagrams, dependency and milestone planner, risk register, a simulated production run.

| # | Concept | Mission | Transfer check | Needs | Status |
| --- | --- | --- | --- | --- | --- |
| 8.1 | Game design document | Write the core loop, pillars and target player | Write pillars for a different genre | C.8 | Planned |
| 8.2 | System design | Diagram the inventory system and its rules | Diagram a crafting system | C.8 | Planned |
| 8.3 | UML class diagrams | Model the classes of a combat system | Find the flaw in a given class diagram | C.8 | Planned |
| 8.4 | Sequence and state diagrams | Model a save-game sequence and a door state machine | Model a different interaction | C.8 | Planned |
| 8.5 | Architecture | Choose module boundaries for a feature | Explain a trade-off between two designs | C.8 | Planned |
| 8.6 | Dependencies and milestones | Order features so no team waits | Replan after a feature is cut | C.8 | Planned |
| 8.7 | Risk | Identify and plan for the three biggest risks | Handle a risk that happened | C.8 | Planned |

The simulated production run assembles a playable result from pre-built modules chosen by
the plan. Plan gaps show up as concrete defects: a missing state becomes a stuck door, a
missing dependency becomes a late feature. The player plays the result and revises the plan.
This stage needs the most design research; see C.8.

## Stage 9 — Worlds Online (MMORPG)

Briefing: the game never ends. Thousands of players share one world, and you run it live.
Stage project: a small online world, first as a local demo with several clients, then as
a simulated live service.
New tools: network simulator (latency, packet loss, many clients), server authority view,
economy and progression designer, live-ops dashboard, moderation and anti-cheat scenarios.
Existing `Ember.Rpg` quests, dialogue and world streaming are reused here.

| # | Concept | Mission | Transfer check | Needs | Status |
| --- | --- | --- | --- | --- | --- |
| 9.1 | Client and server | Move logic to the server so a cheating client cannot win | Find another rule the client should not own | C.9 | Planned |
| 9.2 | Latency and prediction | Make movement feel smooth at 200 ms of simulated latency | Fix a rubber-banding case | C.9 | Planned |
| 9.3 | Persistence | Store characters so they survive a server restart | Recover from a corrupted save | C.9 | Planned |
| 9.4 | Economy | Balance gold income and sinks so prices stay stable | Fix an inflation spike from a new quest | C.9 | Planned |
| 9.5 | Quests and progression | Build a quest chain with shared-world rules | Design a group quest | C.9, existing Ember.Rpg | Planned |
| 9.6 | Scaling | Split the world into zones with player limits | Handle a crowd event | C.9, existing worlds | Planned |
| 9.7 | Live operations | Ship a patch and a seasonal event without downtime | Roll back a bad patch | C.9 | Planned |
| 9.8 | Community and safety | Handle griefing, reports and moderation | Design a rule that reduces a different abuse | C.9 | Planned |

The roadmap defers real multiplayer. Stage 9 therefore starts with a local, simulated
network and does not require online servers.

## Mission data

Missions and stages are data files bundled with the editor (L.1). A stage defines its
briefing, revealed tools, missions, stage project, reaction model and debrief. A mission
defines:

- ID, stage, concept, title and short goal text;
- starter project or required state;
- steps with instruction text, optional "Why?" text, a hint ladder (a small suggestion,
  a concrete pointer, then an explicit undoable demonstration) and a completion condition;
- completion conditions as queries over authored scene state, play events, command history,
  saved files, compile results, change packages, task boards or plan diagrams;
- a transfer task with its own condition and a reflection prompt.

Career progress extends the project-local record that `ProjectLearningProgressStore`
already writes, plus a per-user career save for story state.

## Measuring the curriculum

For each stage, record through novice observation (see CREATOR_EXPERIENCE.md):

- mission completion time, wrong turns and hint levels used;
- transfer success without hints;
- whether the player can explain the rule in their own words;
- a repeat after a break, without replaying the mission;
- whether the player chooses to continue to the next stage (the game must be fun, not only useful).

Revise a mission when fewer than two of three observed players pass its transfer check.

## Build order

Stages 1–2 are the first release target: they use the M1/M2 editor, presets and visual
rules. Stages 3–4 follow with C# behaviours, cutscenes and generation. Stages 5–9 are
post-release expansions; each needs its own feature gate and a playable prototype before
more content is written for it.
