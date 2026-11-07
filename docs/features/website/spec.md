# Website and workflow pages

Asked for on 2026-11-07: a website that can be switched on and off, showing the work items (questions of user input
steps) and the pages workflows need people to complete; a page builder in the Workflows module whose pages the website
shows; a login page; each user sees only their own items and an administrator sees everything; a user input step does
not need a page; and outside systems can get a user's items and the page items as JSON over REST.

## Where things are

- **Workflows / Pages** (`Joktan.Workflows`: `Model/WorkflowPage.cs`, `Engine/PageLibrary.cs`,
  `ViewModels/PageBuilderViewModel.cs`, `Views/PageBuilderView.axaml`). Pages are `<name>.page.json` files in
  `<data>/workflows/pages`, saved on every change. Items: Heading, Text, Value, Divider (shown); TextBox, TextArea,
  Number, CheckBox, DropDown, Date (filled in), each with name, label, text, default, placeholder, help, required and
  options.
- **User input step**: `WorkflowStep.PageId`, chosen under **Page on the website** (optional). When the step asks,
  `StepRunner.Fill` copies the page with its paths filled in into `PendingInput.Page` and adds the page's fields to the
  question's. A drop-down option that is only a path to a list expands to its items. A missing page fails the step.
  With no page chosen, **Create a page** (`WorkflowPage.For(step, name)`) saves a page named "<workflow> - <step>"
  (made unique by `PageLibrary.FreeName`) with an empty title, a text box per named field (label and default kept),
  or an `approved` check box and a `comment` area when there are none, and chooses it.
  Each question gets a new `PendingInput.Id` and an `Asked` time; `WorkflowEngine.Answer` takes who answered for the log.
- **Settings / Website** (`Joktan.Website`): on/off, answer other computers (off by default), port 47822, hours to stay
  signed in. Served by the REST API's `RestServer`, on the UI thread.
- **Sign-in**: `UserDirectory.SignInAsync` checks the PBKDF2 hash in the users database and reads the users afresh.
  Sessions are in memory (`WebsiteSessions`), as an HttpOnly SameSite=Strict cookie for the browser or a bearer token
  for programs. Five wrong passwords in 15 minutes lock a username out until the window passes.

## Who sees what

A question is a user's when it is assigned to their role (by name, ignoring case) or to their user id, as it is after
escalation. Users with the built-in Administrator role see and may answer every question; a question assigned to
nobody is only theirs. Another user's item answers 404, not 403.

## Calls

`POST /api/login`, `POST /api/logout`, `GET /api/me`, `GET /api/items`, `GET|POST /api/items/{id}`,
`GET /api/pages[/{id}]` (administrators). Answers are checked against the page (required, number, yyyy-MM-dd date,
drop-down option, check box true/false) and refused with 422 and `fields`. An empty Number or Date answers JSON
`null` (not `""`), so a database step writes NULL into a date or number column. The front end checks the same rules
before sending (`problemOf`): problems under each field with `aria-invalid`, the first focused, each cleared as its field
changes; the server's `fields` are shown the same way. Sign-in says which box is empty. Dates use the front end's own
picker (`datePicker`: a text box taking yyyy-MM-dd and a month popup, Monday first, arrow keys, Today, Clear, Escape),
as browsers' date boxes cannot be styled to match. A question without a page is given a page
made from its message and fields (`WorkflowPage.For`).

## Security

Host header checked when only this computer may call (DNS rebinding), Origin of other sites refused, CSP
`default-src 'self'`, no framing; the front end (`wwwroot/`, embedded) sets workflow text only as text. Plain HTTP:
the settings page warns when other computers may call.

UI text of the site is in `Resources/Strings.resx` as `Web_*`, served as `/assets/strings.json`.
