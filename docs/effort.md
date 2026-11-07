# How much work Joktan is

How big Joktan is, how long one developer would take to build it by hand, and how long it took with an AI assistant.
Counted on 7 November 2026. The developer figures are estimates, not measurements; the reasoning is given so they can be
checked.

## Effort

Working days for one person doing everything alone. The developers' days are fully focused; "in a company" allows for
meetings and the rest (see below). A developer works about 220 days a year.

| Work | You, with AI | Junior developer | Mid-level developer | Senior developer |
|---|---:|---:|---:|---:|
| Architecture and design | ~4 days* | 300 – 550 | 180 – 320 | 90 – 160 |
| Coding (app and infrastructure) | ~3 days* | 1,000 – 1,850 | 610 – 1,110 | 406 – 741 |
| Testing | ~4 days* | 475 – 875 | 285 – 525 | 190 – 350 |
| Documentation | ~1 day* | 140 – 220 | 105 – 165 | 80 – 125 |
| **Total, working days** | **12** (at least 67 h) | **1,915 – 3,495** | **1,180 – 2,120** | **770 – 1,380** |
| In years, fully focused | about 2½ weeks at 5 days a week | 8¾ – 16 | 5½ – 9½ | 3½ – 6¼ |
| In years, in a company | — | 17½ – 26 | 10¾ – 15¾ | 7 – 10¼ |
| Your 12 days are… | — | 160–290× fewer | 100–175× fewer | 64–115× fewer |

\* A rough guess at how the 12 days were spent, which was not recorded: the assistant wrote the code, tests and
documentation, and the time went mostly to deciding what to build and trying it in the app.

## Size

945 files and 150,566 lines in the repository, committed or not, without build output; lines include blank lines.
Code documentation comments are counted as documentation. Your notes and password vault (5 files, 89 lines) are
personal data, not work, and are not counted.

| Work | What it covers | Size |
|---|---|---:|
| Architecture and design | 20 projects and how they depend on each other; the workflow engine and its run model; the MCP/REST tool registry; security (API keys, credential store, sign-in, approvals); the data folder and profile; the visual design | decisions, not lines |
| Coding: the app | C#, XAML, UI text, website | 86,176 lines |
| Coding: infrastructure | containers, compose and Dockerfiles, certificate script, CI, packaging, demo, settings, repository files | 5,088 lines |
| Testing | 283 test files | 46,485 lines |
| Documentation | specs and design (5,147), in-app help (2,865), READMEs (670), code documentation comments (4,046) | 12,728 lines |

## How it was estimated

**Senior developer.** A strong developer on a large new project finishes about **100 to 200 lines of code a day** once
design, debugging, rework and tests are counted; a test line is cheaper than an app line, though tests of timing and
concurrency take real debugging. Architecture is usually 10–15% of a project like this. Documentation and
configuration are writing more than code.

**Mid-level developer.** About 1.5 times a senior's time for coding, testing and documentation, and twice for
architecture, which needs experience of systems this broad.

**Junior developer.** About 2½ times a senior's time for coding and testing, nearly twice for documentation, and more
than 3 times for architecture. A junior could not realistically design something this broad alone, and would need a
senior's guidance, which is not counted.

Few developers know every field Joktan spans (MQTT and TLS, certificates and mutual TLS, gRPC reflection, a workflow
engine with three script languages, a website with sign-in, Docker and Podman compose, a password vault, telemetry),
and learning each costs weeks. A hand-written version would probably be leaner, with fewer comments and tests, perhaps
30–40% smaller, and less sturdy for it. The formal COCOMO model puts a codebase this size at more than 20 person-years,
but that assumes a team and its overheads.

### In a company

Focused work is only part of a developer's week:

| Where the week goes | Share |
|---|---:|
| Meetings: stand-ups, planning, reviews, demos, one-to-ones | 20–30% |
| Design back-and-forth and changes of direction on each feature | 10–15% |
| Email and chat, reviewing others' code, support, interruptions | 10–15% |
| **Coding, debugging and testing** | **40–60%** |

With about half the week left for the work, each developer's time roughly doubles. A team of three seniors would take
**2¼ to 3¾ years**: more meetings and coordination, and parts that cannot be split, such as the engine and the agent's
tools. A change of mind in the middle of a feature, one message here, is often a meeting, a ticket and rework in a
company, which commonly costs teams 20–30% of their effort.

## With an AI assistant: the commit days

116 commits on 12 days, from 13 October to 7 November 2026. The time between a day's first and last commit is a lower
bound of the time worked: work before the first commit is not counted.

| Date | Day | Commits | First | Last | First to last |
|---|---|---:|---:|---:|---:|
| 13 Oct | Tue | 6 | 08:45 | 11:23 | 2 h 38 m |
| 15 Oct | Thu | 3 | 09:45 | 13:07 | 3 h 22 m |
| 24 Oct | Sat | 14 | 10:07 | 15:34 | 5 h 27 m |
| 27 Oct | Tue | 5 | 07:10 | 07:48 | 0 h 38 m |
| 29 Oct | Thu | 2 | 07:16 | 13:56 | 6 h 40 m |
| 30 Oct | Fri | 3 | 15:56 | 15:56 | — |
| 31 Oct | Sat | 32 | 07:54 | 23:55 | 16 h 01 m |
| 1 Nov | Sun | 18 | 00:19 | 19:57 | 19 h 38 m |
| 2 Nov | Mon | 9 | 20:24 | 22:51 | 2 h 27 m |
| 3 Nov | Tue | 5 | 06:59 | 07:31 | 0 h 32 m |
| 5 Nov | Thu | 4 | 20:02 | 22:00 | 1 h 58 m |
| 7 Nov | Sat | 15 | 07:44 | 15:12 | 7 h 28 m |
| **Total** | 4 weekend, 8 weekday | **116** | | | **at least 67 hours** |

The weekend of 31 October and 1 November was the big push: 50 commits over about 36 hours. Most weekday sessions were
early mornings or evenings, around other work.

At 5 days a week, what was built in about **2½ weeks** would take a senior developer in a company about **7 to 10¼
years**. The comparison counts days, not what the time is worth: directing the assistant, trying what it built and
catching what was wrong is real work, and it is in those days.
