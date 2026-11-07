# Demo: order approval workflow

## The prompt

Paste this into the agent panel (the sparkle icon in the top bar). It builds the whole demo from nothing: the broker,
the database and its tables, the folder messages, the website page and both workflows. Do the two things under
[Before you send it](#before-you-send-it) first.

```text
Build the Joktan order approval demo from scratch. Read workflow_step_types before writing any workflow, and use
database_list, mqtt_clients, workflow_pages and workflow_assignees to find ids instead of guessing them. Keep going
until everything below exists, and fix whatever workflow_save reports.

1. Broker: make sure a Mosquitto broker called "secondary" exists (containers_broker_save, plain port only, no
   username) and is running (containers_run "up" on its compose project). It gets an MQTT client called "secondary".

2. Database: make a PostgreSQL database called "demo" (containers_database_save, password "changeme") and start it.
   When it is up, create these tables in schema public with database_table_create (each with the numbered id column):
   - customers: customer text not null unique, name text, tier text, credit_limit numeric.
   - orders: order_ref text not null unique, customer text, priority text, total numeric, discount numeric,
     net numeric, status text default 'received', decision text, comment text, approved_by text, po_number text,
     deliver_by date, notify_customer boolean, tracking text, created timestamptz default now(),
     updated timestamptz default now().
   - order_lines: order_ref text not null, sku text, description text, qty integer, unit_price numeric.
   - order_events: order_ref text, event text not null, detail jsonb, at timestamptz default now().
   Insert three customers with database_row_insert: acme "Acme Corporation" gold 10000, globex "Globex Ltd"
   silver 2500, initech "Initech" bronze 800.

3. Folder messages: give the "secondary" client the folder ~/Documents/Joktan/samples/demo-messages (mqtt_folder_use
   with create), then write these with mqtt_folder_message_save, all manual and switched off, group "Demo/Orders",
   QoS 1, with varying fields orderRef (guid) and ts (isoTimestamp). The body of each is
   {"orderRef": "", "customer": ..., "priority": ..., "ts": "", "lines": [{"sku", "description", "qty", "unitPrice"}]}.
   - demo/orders/new/small-order.json: acme, normal, 4 x BOLT-10 "Bolts, pack of 10" at 12.5.
   - demo/orders/new/large-order.json: globex, normal, 3 x PUMP-200 "Water pump 200 l/min" at 420 and
     6 x HOSE-5M "Hose, 5 m" at 18.
   - demo/orders/new/rush-order.json: initech, rush, 8 x VALVE-3 "Ball valve 3 inch" at 95.
   - demo/orders/new/invalid-order.json: acme, normal, 0 x BOLT-10 at 12.5.
   - demo/orders/new/random-order.json: acme, normal, 1 x GEAR-12 "Gear, 12 teeth" at 35, sent every 2 minutes,
     with customer oneOf acme/globex/initech/newco, priority oneOf normal/rush/low and lines[0].qty randomInteger 1-20.
   - demo/orders/result/result.json, group "Demo/Results": {"orderRef": "", "status": "", "decision": "", "net": 0,
     "tracking": "", "run": 0, "source": "joktan-demo", "ts": ""}, with ts isoTimestamp.
   Then connect the "secondary" client.

4. Page: make a page "Demo order approval" (workflow_page_save), title "Approve order {{vars.orderRef}}", submit
   text "Send decision". Show the customer ({{vars.customerRow.name}}), tier ({{vars.pricing.tier}}), priority,
   lines and items ({{vars.check.lineCount}}, {{vars.check.totalQty}}), total ({{vars.check.total}}), discount and net
   ({{vars.pricing.discount}}, {{vars.pricing.net}}) as Values, a short Text explaining the rules, a Divider, then:
   decision (DropDown Approve/Reject, required), comment (TextArea), poNumber (TextBox), deliverBy (Date),
   boxes (Number, default {{vars.boxes}}) and notifyCustomer (CheckBox, default true).

5. Workflow "Demo warehouse simulator" in folder Demo (Short, REST trigger on), globals orderRef and boxes from the
   input (required) and packed = 0: a REST GET of http://localhost:47821/api/openapi.json that does not fail on an
   error status, logging the answer (and logging and carrying on from its error output); a 3s delay; a While
   {{vars.packed}} < {{vars.boxes}} whose loop runs JavaScript that adds 1 to vars.packed and logs it; when done, a
   Go script that reads the run data from stdin and prints {"tracking": "JK-<unix time>-<orderRef>"}; then publish
   {"orderRef", "tracking", "boxes"} from the secondary client to demo/warehouse/ack; then End.

6. Workflow "Demo order approval" in folder Demo: Long, MQTT trigger on client "secondary", topic demo/orders/new,
   and the REST trigger on. The MQTT message's payload is the run's input itself, so globals read {{input.orderRef}} (required),
   {{input.customer}}, {{input.priority}} and {{input.lines}} (not {{input.payload...}}), and order =
   {"source": "{{run.topic}}", "receivedBy": "Joktan demo"}. Swimlanes Intake,
   Decision and Fulfilment. Four named samples: small, large, rush and invalid orders like the folder messages, with
   orderRefs DEMO-1001 to DEMO-1004, each the bare payload, as the trigger gives it. Label every branch.
   - Intake: log the arrival with {{run.topic}} and {{run.serial}}; a C# script that checks the lines (quantity above 0, an orderRef)
     and returns {valid, total, totalQty, lineCount, reasons} into vars.check; a condition on valid. Invalid: insert
     an "invalid" row into order_events with the check as detail, publish {"orderRef", "status": "invalid",
     "reasons"} to demo/orders/result, then a Fail step. Valid: read the customer (first row only, into
     vars.customerRow, 2 retries 2s apart), with its error output going to a log step and on.
   - Decision: upsert the order matching order_ref (status received, total), putting its id in vars.orderId; insert
     a "working-note" row into order_events; merge {orderRef, customer, priority, total, id} into vars.order; a
     JavaScript script that gives a discount of 10% for gold, 5% silver, 2% bronze, sets vars.boxes to one box per
     5 items and returns {tier, rate, discount, net} into vars.pricing; a switch on priority with cases rush and
     normal and a default, each logging, all leading to a condition "net over 500". Over 500: a user input step
     showing the page, assigned to role Approver, escalated to role Manager after 10m, timing out after 1h, with
     metadata orderRef, customer, net and priority, answer into vars.approval. Its timeout sets vars.approval to a
     Reject saying nobody answered. 500 or less: set vars.approval to an automatic Approve with notifyCustomer true
     and boxes {{vars.boxes}}. Then a switch on {{vars.approval.decision}}: Approve, Reject, default treated as reject.
   - Rejected: update the order (status rejected, decision, comment, net, discount, updated now), filtered on
     order_ref.
   - Fulfilment for Approve: update the order (status approved, decision, comment, po_number, deliver_by,
     notify_customer, net, discount, updated now); a For each over {{vars.lines}} that logs each line and comes
     back; then insert every line into order_lines (many rows); start "Demo warehouse simulator" without waiting,
     with {"orderRef", "boxes": {{vars.approval.boxes}}}; wait up to 2m on demo/warehouse/ack for a message whose
     payload.orderRef equals {{vars.orderRef}}, into vars.ack. Then: update status shipped with the tracking number,
     or on timeout log that the warehouse was silent.
   - Both paths then: delete this order's "working-note" rows from order_events; read the order (first row, into
     vars.finalOrder); publish the folder message demo/orders/result/result.json, merging in orderRef, status,
     decision, net, tracking and {{run.serial}}; End with {orderRef, status, net, order}.

When it is all made, open Workflows / Designer and tell me what you built and anything you could not do.
```

The agent asks before it starts containers, makes a table and connects the client. Click **Allow for this
conversation** on each to let it carry on without asking again. Building everything takes a few minutes and many tool
calls. If it stops part way, say "carry on".

## Before you send it

Two things the agent has no tools for, on purpose, as one stores people's passwords and the other opens a server:

1. **Users.** Open **Users** and press **Create database**, giving it a name, port, username and password. It comes
   with the recommended roles, including **Approver** and **Manager**, and a **Default Administrator** (`admin`)
   without a password. Edit `admin` and give them one, so you can sign in to the website.
2. **The website.** Switch it on on **Settings / Website**. It is at `http://localhost:47822`.

Also check that the container engine is running (Podman or Docker), and that Go is installed for the Go step (`go` on
the path, or set on **Settings / Workflow Settings**).

## After it is built

Open **Workflows / Designer**. Both workflows should be in the **Demo** folder with no problems shown. If a step shows
one, ask the agent to fix it, or fix it in the designer. Then go on to [Running it](#running-it).

The agent writes its own version of the demo, so names and layout can differ a little from the prepared copy
described below, and from run to run.

## What it shows

| Feature | Where in the demo |
|---|---|
| MQTT trigger | **Demo order approval** starts on `demo/orders/new` from the `secondary` client |
| REST trigger | Both workflows can be started with `workflow_start` |
| Long and short runs | The order workflow is Long, because it waits for a person. The warehouse workflow is Short |
| Sample inputs | Four named samples on the order workflow, one on the warehouse workflow |
| Global variables | `orderRef` (required), `customer`, `priority`, `lines` and `order`, taken from the message |
| Swimlanes | Intake, Decision and Fulfilment |
| Labels on links | On every condition and switch, and on the timeout outputs |
| Log | Arrival, each priority, each line reserved |
| C# script | **Check order** validates the lines and works out the total |
| JavaScript script | **Price order** gives a discount by customer tier; **Pack a box** in the warehouse |
| Go script | **Tracking number** in the warehouse |
| Condition | **Valid?** and **Net over 500?** |
| Switch | **Priority?** (rush, normal, other) and **Decision?** (approved, rejected, unclear) |
| Set variable, and merge | **Merge order summary** merges into `vars.order`; **Approve automatically** and **Reject: timed out** |
| For each | **Each line** goes through the order's lines |
| While | **While boxes left** in the warehouse |
| Delay | **Walk to the shelves** in the warehouse |
| Read, insert, update, upsert and delete rows | Customer lookup, saving the order and its lines, status changes, removing the working note |
| Retries and error outputs | The customer lookup is tried three times, then goes on by its error output |
| User input with a page | **Approver decides** shows the **Demo order approval** page on the website |
| Assignment and escalation | Assigned to the Approver role, escalated to Manager after 10 minutes, rejected after an hour |
| Metadata on a question | `orderRef`, `customer`, `net` and `priority` |
| Call workflow, without waiting | **Start warehouse** starts **Demo warehouse simulator** and carries on |
| Wait for MQTT | **Wait for warehouse** waits up to 2 minutes for this order's reply on `demo/warehouse/ack` |
| MQTT publish, written on the step | The rejection of an invalid order; the warehouse's reply |
| MQTT publish, from a folder message | **Publish result** merges the outcome into `demo/orders/result/result.json` |
| REST request | **Check REST API** in the warehouse, which carries on by its error output if the API is off |
| Fail | **Fail: invalid order** |
| End with an output | The order's reference, status, net amount and summary |

The only step type the demo does not use is **gRPC call**, as there is no gRPC server to call.

## The prepared copy

Instead of asking the agent, you can install a finished copy. It is in the profile, so `make -C profile/ettiene` copies it
to `~/Documents/Joktan`:

| File | What it is |
|---|---|
| `workflows/definitions/Demo/Demo order approval.workflow.json` | The order workflow, in the Demo folder |
| `workflows/definitions/Demo/Demo warehouse simulator.workflow.json` | The warehouse workflow |
| `workflows/pages/Demo order approval.page.json` | The page shown on the website |
| `samples/demo-messages/` | Folder messages for the secondary client |
| `samples/order-demo.sql` | The tables and three customers |

The folder messages, all sent by hand unless you switch them on:

| File | Topic | What happens |
|---|---|---|
| `demo/orders/new/small-order.json` | `demo/orders/new` | Acme (gold), net 45: approved automatically, then shipped |
| `demo/orders/new/large-order.json` | `demo/orders/new` | Globex (silver), net 1299.60: needs approval |
| `demo/orders/new/rush-order.json` | `demo/orders/new` | Initech (bronze), rush, net 744.80: needs approval |
| `demo/orders/new/invalid-order.json` | `demo/orders/new` | A quantity of 0: recorded, rejected on `demo/orders/result`, and the run fails |
| `demo/orders/new/random-order.json` | `demo/orders/new` | Every 2 minutes once switched on: a random customer, priority and quantity |
| `demo/orders/result/result.json` | `demo/orders/result` | The template the result is merged into. Not meant to be sent by itself |

Each order gets a new `orderRef` (a GUID) every time it is sent, so you can send the same file again and again.

## Setting up the prepared copy

Do [Before you send it](#before-you-send-it) first, then:

1. **Copy the files.** Run `make -C profile/ettiene` with Joktan stopped. Joktan reads workflows and pages when it starts,
   so files copied while it runs only appear after a restart.
2. **The broker and its client.** On **Data / MQTT**, make sure there is a broker called `secondary` and that it is
   running. Its client, also called `secondary`, is on **Dev Tools / Brokers / MQTT**.
3. **The folder messages.** On that client, press **Choose folder** and pick `~/Documents/Joktan/samples/demo-messages`.
   Then **Connect**. The MQTT trigger only listens while the client is connected.
4. **The database.** On **Data / Database**, create a database called `demo`, then create its tables:

   ```sh
   podman exec -i joktan-postgres-demo-1 psql -U postgres -d demo < profile/ettiene/samples/order-demo.sql
   ```

   Database steps name a database by its id. The demo's files name `a551962927c6`. If your `demo` database has another
   id (it is in `settings/containers.json`), either open each database step's **Configure…** and choose `demo`, or
   replace the id in the file with Joktan stopped:

   ```sh
   sed -i '' 's/a551962927c6/<your id>/g' ~/Documents/Joktan/workflows/definitions/Demo/*.workflow.json
   ```

5. **Workflow settings.** On **Settings / Workflow Settings**, keep **MQTT triggers** on.
6. **Optional:** switch on the REST API on **Settings / REST API**. Without it, the warehouse's **Check REST API** step
   logs that the API could not be reached, and carries on.

Open **Workflows / Designer** and check that both workflows sit in the **Demo** folder with no problems shown.

## Running it

1. On the `secondary` client, send **small-order.json**. On **Workflows / Runs** you see a run of
   **Demo order approval** finish as Completed, and a run of **Demo warehouse simulator** beside it. The result arrives
   on `demo/orders/result` with status `shipped` and a tracking number. Watch it in the **Topic Explorer** on the
   secondary client.
2. Send **large-order.json**. The run stops at **Approver decides** and waits for input. Open the website, sign in, and
   open the item. The page shows the customer, tier, lines, total, discount and net amount. Choose **Approve**, fill in
   a PO number and a date if you like, and press **Send decision**. The run saves the lines, waits for the warehouse,
   and publishes `shipped`.
3. Send **rush-order.json** and choose **Reject** on the website. The order is marked rejected and the result says so.
4. Send **invalid-order.json**. The run records the problem in `order_events`, publishes `invalid` with the reasons,
   and fails. That is meant to happen.
5. Leave a large order unanswered. After 10 minutes the question is escalated to Manager and an alert appears. After an
   hour it is rejected.

To see what was stored, use the browser on **Data / Database**, or:

```sh
podman exec joktan-postgres-demo-1 psql -U postgres -d demo -c 'select order_ref, status, decision, net, tracking from orders'
```

### Starting it by hand or over REST

On **Workflows / Designer**, pick one of the four samples on the order workflow and press **Run**. These use the fixed
references `DEMO-1001` to `DEMO-1004`. The order is upserted, so running a sample again updates the same row.

Over the REST API, with a key and the `workflow_start` call switched on:

```sh
curl -X POST http://localhost:47821/api/workflow_start -H "Authorization: Bearer $JOKTAN_KEY" \
  -H 'Content-Type: application/json' \
  -d '{"workflow": "Demo order approval", "input": {"orderRef": "REST-1", "customer": "acme", "priority": "normal", "lines": [{"sku": "BOLT-10", "qty": 2, "unitPrice": 12.5}]}}'
```

## Starting again

To empty the demo's tables and keep the customers:

```sh
podman exec joktan-postgres-demo-1 psql -U postgres -d demo -c 'TRUNCATE orders, order_lines, order_events RESTART IDENTITY'
```

## When something does not happen

| What you see | Why |
|---|---|
| Sending an order starts no run | The `secondary` client is not connected, or MQTT triggers are off on Settings / Workflow Settings (the designer shows a trigger problem). Or the run was refused: a log warning such as "The input has nothing for orderRef" means a global reads a path the payload does not have. The payload is the input itself: `{{input.orderRef}}`, not `{{input.payload.orderRef}}` |
| A database step fails with "not on Data / Database" | The workflow names another database id; see step 4 above |
| **Tracking number (Go)** fails | Go is not installed, or not on the path Joktan sees |
| No question on the website | The website is off, or you are signed in as someone without the Approver or Manager role, and not as the administrator |
| The order stays approved, not shipped | The warehouse did not reply within 2 minutes; see the simulator's run |
