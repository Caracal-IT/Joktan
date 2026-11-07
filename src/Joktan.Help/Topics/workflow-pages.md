# Workflow pages

**Workflows / Pages** builds the pages that [user input steps](help:workflow-steps) show on the
[website](help:website), where the person a question is assigned to fills it in. A step does not need a page:
without one, the website shows the step's question and a box for each of its fields.

## Building a page

- **New page** makes a page with a sample text, check box and comment, to change or remove. **Duplicate** copies
  the page selected, and **Delete** removes it after asking. Every change is saved at once, as a
  `<name>.page.json` file in the `workflows/pages` folder of the data folder.
- On the left, under **Add to the page**, click what to add; it goes under the item selected.
  - **Shown**: a **Heading**, **Text**, a **Value from the run** (a label and a value, such as an order's total)
    and a **Divider**.
  - **Filled in**: a **Text box**, **Text area**, **Number**, **Check box**, **Drop-down** and **Date**.
- The middle shows the page as the website lays it out. Click an item to change it on the right, and use
  **Move up**, **Move down** and **Remove**.
- On the right, the page's **Name** (what steps list it by), **Title** (the step's question when empty),
  **Description** and the text of its **Button** (Send when empty), then the settings of the item selected:
  - a field's **Field name** is the name its value has in the answer, as in `{{vars.answer.approved}}`;
  - **Label**, **Starting value**, **Placeholder** and **Help** are what the person sees;
  - **Required** stops the page being sent without it; a required check box must be ticked;
  - a drop-down's **Options** are one to a line. A line that is only a path to a list, such as
    `{{vars.colours}}`, gives each item of the list.

Text, labels, starting values and options may use paths into the run, such as `{{input.orderId}}` or
`{{vars.customer.name}}`. They are filled in when the step asks, so each run's page shows its own values. A
problem, such as two fields with the same name or a drop-down with no options, is shown under the page.

**Preview in browser** opens the page on the website, with the paths as they are and nothing to send. The website
must be on ([Settings / Website](help:website)) and you must be signed in with the Administrator role.

## Using a page

On a user input step, choose the page under **Page on the website**, or press **Create a page** to make one from
the step: it is named after the workflow and the step, its title is left empty so the step's question shows, and it
has a text box for each of the step's fields, or a box to tick and a comment when the step has none. The page's fields are asked for as well as
the step's own **Fields to fill in**; a field on both starts with the step's value. Fields of the step that are not
on the page are not shown, and keep their starting values. The answer is one object of all the fields, as
before. The page is copied into the question when the step asks, so changing it later does not change a question
already waiting. A step whose page was deleted fails when it asks.
