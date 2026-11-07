# Website

**Settings / Website** turns on a website where people do the work workflows give them. They sign in with the
username and password they have on [Users](help:users), see the questions of [user input steps](help:workflow-steps)
waiting for them (their **work items**), and answer each on its page from [Workflows / Pages](help:workflow-pages).

## Turning it on

- **Website on** starts it; **Open in browser** opens it at the address shown, such as `http://localhost:47822`.
- **Answer other computers**: off, only this computer can open it. On, anyone on the network can reach the sign-in
  page, at the address shown with this computer's name. The website is plain HTTP, so passwords and answers cross
  the network unencrypted: use it on a network you trust.
- **Port**: 47822 unless you change it.
- **Stay signed in for (hours)**: how long someone stays signed in without using the website. Everyone signs in again
  when Joktan restarts.

Nobody can sign in until the users have a database on [Settings / User Settings](help:users), it is running, and
the users have passwords; the page says which is missing. The default administrator made with the database has no
password until you give it one on the Users page.

## Who sees what

- A user sees only the work items assigned to **their role**, or to **them**, including those escalated to them.
  Asking for anyone else's gives "no such work item".
- A user with the **Administrator** role sees and can answer every work item, and can choose between **All** and
  **Assigned to me**. Only administrators can preview pages.
- Five wrong passwords for a username in 15 minutes stop it from signing in for a while.

Each work item shows its title, the workflow and run number, when it was asked, any time limit, and whether it was
escalated. Opening it shows its page, or the question and its fields when the step has no page, and **Details**
with the step's metadata. **Send** checks the fields (required ones, numbers, dates and options) and, when they are
right, answers the question; the run's log says who answered. The list refreshes itself.

## For other programs

Other programs can do what the website does, as a user, with JSON calls. Sign in, then send the token it gives as
`Authorization: Bearer <token>`:

| Call | What it does |
| --- | --- |
| `POST /api/login` with `{"username": "…", "password": "…"}` | Signs in; gives `token`, `expires` and `user`. |
| `GET /api/items` | The user's work items (every one for an administrator). |
| `GET /api/items/{id}` | One work item, with its `fields` and its `page` and their items. |
| `POST /api/items/{id}` with `{"values": {"field": "value"}}` | Answers it. Fields not sent keep their starting values. |
| `GET /api/me` | Who is signed in. |
| `POST /api/logout` | Signs out. |
| `GET /api/pages`, `GET /api/pages/{id}` | The pages of Workflows / Pages, for administrators. |

A page's items each have a `type` (`Heading`, `Text`, `Value`, `Divider`, `TextBox`, `TextArea`, `Number`,
`CheckBox`, `DropDown` or `Date`), and `name`, `label`, `text`, `default`, `placeholder`, `help`, `required` and
`options`. Wrong values are refused with status 422 and what is wrong with each field under `fields`. Requests
from web pages of other sites are refused.

The [REST API](help:rest-api) is separate: it answers with API keys rather than users, and its `workflow_answer`
can answer any question.
