# Workflows compared with n8n and K2

How the Workflows module stands next to two well-known tools: [n8n](https://n8n.io), an integration platform, and
K2, Nintex's business process platform, now sold as **Nintex Automation** (on premises, formerly K2 Five) and
**Nintex K2 Cloud**. Facts about Joktan come from its code; those about n8n and K2 were checked in November 2026,
partly from third-party sources, which are listed at the end. Check prices and licences with the vendors.

## When to use Joktan Workflows

- **MQTT and devices are at the centre.** A message from a broker starts the work and the result goes back as a
  message. Folder messages, the MQTT proxy and the recorder sit beside the workflows.
- **People make decisions.** Approvals, checks and answers go to a role, are escalated when nobody answers, and are
  filled in on a page you design, on a website where people sign in and see their own tasks.
- **It stays on one computer or a small team.** A desktop app: no server, database server or IIS to look after, and
  the data stays on your own machine, in plain files and your own PostgreSQL containers.
- **You want C# or Go in a step,** with NuGet packages or Go modules, as well as JavaScript.
- **Databases must be protected.** Updates and deletes never touch every row: the check is made in the designer, at
  run time and in the SQL.
- **Workflows belong in version control.** Each is a `.workflow.json` file in a folder, so Git works with no extra tier.
- **You build and test APIs.** REST and gRPC steps reuse the requests saved in the API Client.

Choose something else when the work must run as a server around the clock for many users, connect dozens of SaaS
applications, or meet formal compliance and audit requirements.

## Compared with n8n

| | Joktan Workflows | n8n |
|---|---|---|
| Runs as | A desktop app, one process | A server (Docker, self-hosted or n8n Cloud); queue mode with Redis and workers scales out |
| Integrations | MQTT, REST, gRPC (by reflection), PostgreSQL | 400+ built-in nodes and community nodes |
| Code | C# (NuGet), JavaScript, Go (modules) | JavaScript and Python |
| Data between steps | One JSON object: `input`, `vars`, `last`, `run` | Lists of items; each node runs once per item |
| Branching | Condition, Switch; parallel branches with Parallel and Join | Parallel branches, rejoined with Merge |
| Triggers | MQTT, schedule, by hand, REST, webhooks | Webhooks, schedule, app events, MQTT, forms |
| Errors | Retries, error outputs, Fail, an error workflow | Retry on fail, continue on error, error workflows |
| People | Website with sign-in and a task list, page builder, roles, escalation | Wait node resumed by a webhook or form, Form nodes, "send and wait" over email or chat; no task list, roles or escalation of its own |
| Version control | Plain files, Git for free | Git source control is an enterprise feature |
| Users and roles | Built in, for who answers | Roles, SSO and audit logs are paid |
| AI | The agent panel builds workflows with its tools | AI agent and LangChain nodes inside workflows |
| Licence | Joktan's own | Sustainable Use License: free for internal use, not to resell as a hosted service |

**Joktan is the better fit** for device, API and database work where people decide. **n8n is the better fit** for
always-on business integration across many applications.

## Compared with K2 (Nintex Automation) for small companies

K2 is a full business process platform: SmartForms built on SmartObjects (a data layer over SQL, web services and
line-of-business systems), a worklist with delegation and out-of-office, escalations, reporting and directory
integration. It is made for organisations with an IT team. For a small company, Joktan is the better choice when:

| | Joktan Workflows | K2 / Nintex Automation |
|---|---|---|
| What you need to run it | Joktan on a computer, and Podman or Docker for PostgreSQL | Windows Server, SQL Server and IIS on premises, or K2 Cloud |
| Cost | No licence fee | A licence or subscription: listings found range from per-user prices on the UK G-Cloud to about $900–$2,000 a month on aggregator sites; reviewers call it expensive |
| Getting started | Install, draw a workflow, done; the agent can build one from a description | Reviewers mention needing professional services or an implementation team |
| Machines and messages | MQTT triggers, waits and publishing are built in | Reached through connectors or custom services |
| Code | C#, JavaScript and Go in a step | Mostly no-code, with custom code as service brokers and SmartObjects |
| Your data | Plain files and your own databases | In K2's SQL Server or Nintex's cloud |
| People and forms | Roles, escalation, a page builder and a website task list: the core of what a small team needs | Much more: delegation, out-of-office, SLAs, rich forms with rules and views, reporting, Active Directory and Entra ID |
| Scale and governance | One machine; users and roles of its own | Built for many users, environments, audit and compliance |

**Choose Joktan** when a small team wants approvals and checks around machines, APIs or a database without a
server estate or a licence budget, and someone is happy to write a little code. **Choose K2** when the company needs
directory sign-in, delegation, audited processes across departments, rich business forms, and has the people and
budget to run it.

## What Joktan does not do

- Run as a highly available server for many users, or spread runs over machines.
- Offer ready-made connectors for SaaS applications: the REST step reaches them, written by hand.
- Delegate tasks, cover out-of-office or sign people in from a directory.
- Python steps, and gRPC streaming.

## Sources

- n8n: [TemperStack, human-in-the-loop in n8n](https://www.temperstack.com/learn/n8n/implement-human-loop-workflows/),
  [KirimEmail, approval workflows in n8n](https://en.kirim.email/blog/email-approval-workflow-n8n-human-in-the-loop/),
  [Activepieces, n8n licence (2026)](https://www.activepieces.com/blog/n8n-license-fair-code-community-enterprise-2026)
  and [enterprise cost (2026)](https://www.activepieces.com/blog/n8n-enterprise-license-cost-quotes-and-tiers-2026.md)
  (a competitor of n8n), [Jimmy Song, n8n deep dive (2026)](https://jimmysong.io/blog/n8n-deep-dive/),
  [DEV Community, n8n pricing (2026)](https://dev.to/beton/n8n-pricing-teardown-2026-4oa).
- K2: [Nintex help, SmartForms](https://help.nintex.com/en-us/k2five/userguide/5.6/content/Create/SmartForms.htm),
  [UK Digital Marketplace, Nintex Automation](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/388743427312475)
  and [Nintex K2 Cloud](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/930790707742689),
  [GoodFirms, Nintex K2](https://www.goodfirms.co/software/nintex-k2),
  [SpotSaaS, Nintex vs K2](https://www.spotsaas.com/compare/nintex-vs-k2),
  [PeerSpot, Nintex Process Platform comparisons](https://www.peerspot.com/products/comparisons/agiloft-workflow-and-bpm_vs_nintex-process-platform).
