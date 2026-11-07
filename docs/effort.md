# How much work Joktan is

How big Joktan is, how long one developer would take to build it by hand, and how long it took with an AI assistant.
Counted on 7 November 2026. The developer figures are estimates, not measurements; the reasoning is given so they can be
checked.

## Size

Every file git tracks in `src` and `tests`, with files not yet committed, without build output. Lines include blank
lines and comments.

| Kind | src files | src lines | tests files | tests lines |
|---|---:|---:|---:|---:|
| C# (`.cs`) | 362 | 65,078 | 262 | 46,306 |
| Avalonia XAML (`.axaml`) | 74 | 12,001 | — | — |
| UI text (`.resx`) | 20 | 11,711 | — | — |
| Help pages (`.md`) | 36 | 2,865 | — | — |
| Website (`.js`, `.css`, `.html`) | 3 | 1,070 | — | — |
| **Total** | **495** | **92,725** | **262** | **46,306** |

**727 files and 139,031 lines** in all. C# is 624 files and 111,384 lines, 58% in the app and 42% in tests: about 0.7
lines of test for each line of app code.

## By hand: one developer

| What | Lines | Effort |
|---|---:|---|
| App C# | 65,000 | The hard part: the workflow engine, MQTT, gRPC, the MCP and REST server, C#, JavaScript and Go scripts, PostgreSQL, containers |
| XAML views | 12,000 | Moderate: layout and bindings, reworked once seen on screen |
| Test C# | 46,000 | Cheaper a line, but tests of timing and concurrency take real debugging |
| UI text, help pages, website | 16,000 | Writing more than coding |

A strong developer on a large new project finishes about **100 to 200 lines a day** once design, debugging, rework and
tests are counted. For about 125,000 meaningful lines, that is **660 to 1,250 working days: 3 to 5½ years** of focused
work.

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

With about half the week left for the work, the estimate becomes **6 to 9 years** for one developer. A team of three
would take **2 to 3 years**: more meetings and coordination, and parts that cannot be split, such as the engine and
the agent's tools. A change of mind in the middle of a feature, one message here, is often a meeting, a ticket and
rework in a company, which commonly costs teams 20–30% of their effort.

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

## Compared

| | Days | 8-hour days | Calendar time | The AI-assisted time is… |
|---|---:|---:|---|---:|
| **With AI, by commit days** | 12 | — | 26 days (13 Oct – 7 Nov) | — |
| **With AI, by hours** (at least 67) | — | **about 8½** | — | — |
| **With AI, at 5 days a week** | 12 | — | **about 2½ weeks** (under 2 at 40 hours a week) | — |
| One developer, focused full time | 660 – 1,210 | 660 – 1,210 | 3 – 5½ years | **55–100× fewer days; 80–140× fewer hours** |
| One developer in a company | 1,320 – 1,980 | 1,320 – 1,980 | 6 – 9 years | **110–165× fewer days; 155–235× fewer hours** |
| A team of three in a company | 1,320 – 1,980 each | — | 2 – 3 years | **about 40–60× sooner** |

A developer works about 220 days a year, after holidays, leave and sickness.

At 5 days a week, what was built in about **2½ weeks** would take one developer in a company about **6 to 9 years**.
The comparison counts days, not what the time is worth: directing the assistant, trying what it built and catching what
was wrong is real work, and it is in those days.
