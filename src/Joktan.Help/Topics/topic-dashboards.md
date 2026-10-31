# Topic Dashboards

**Dashboard / Topic Dashboards** draws the numbers on MQTT topics as graphs, on dashboards you name and
lay out yourself. Each dashboard has its own tab. Graphs use the clients on the [MQTT](help:mqtt) page.

## Adding a graph

From the Topic Explorer, when you are already watching the topic:

1. Select the topic in the [Topic Explorer](help:topic-explorer) and press **Add to dashboard**.
2. The graph editor opens, with the numbers being drawn there already ticked and the messages received
   so far already on the preview.

From the dashboard, when you know the topic:

1. Press **Add graph**.
2. Choose the **Client**, type the **Topic**, and press **Subscribe**. If the client is not connected,
   press **Connect** beside the message.
3. The numbers in the topic's messages are listed as they arrive.

Then, in the editor, whichever way you came:

1. Give the graph a **Name**.
2. Choose its **Type**, and watch the preview change.
3. Tick up to 8 **Numbers to draw**. Each can have its own name and colour.
4. Choose the **Dashboard**, or tick **Put it on a new dashboard**.
5. Press **Add to dashboard**.

A graph draws one exact topic, so `+` and `#` cannot be used. If nothing is being sent to the topic yet,
add a number by its path, such as `sensor.value` or `readings[*].value`. Leave the path empty for a
payload that is a single number.

Paths mean the same as in the Topic Explorer: `readings[*].value` draws every item of an array, and
`readings[0].value` only the first.

## Types of graph

- **Line**: how values moved over time.
- **Area**: a line filled down to zero, for amounts.
- **Bars**: one number shows its recent readings. Several show the latest of each, side by side.
- **Gauge**: the latest value on a dial. Set a fixed scale, so the dial means something.
- **Number**: the latest value in large type, with its trend below when there is room.

All the numbers on one graph share one scale. Numbers of very different sizes are easier to read on
graphs of their own.

Point at a line, an area or a bar to read its values.

## Other settings for a graph

- **Shows the last**: how far back it goes, from 1 minute to 24 hours.
- **Unit**: shown after each value, such as `°C`.
- **Decimal places**: **Automatic** shows up to three, as needed.
- **Fixed scale**: keeps the scale between a **Minimum** and a **Maximum**, rather than fitting the values.
- **Columns wide** and **Rows high**: its size on the dashboard.

## Laying out a dashboard

Press **Edit layout**. Each graph then shows a bar across its top and a handle in its bottom corner.

- **Move** a graph by dragging its bar onto another graph, or with **‹** and **›**.
- **Resize** it by dragging the corner handle. It snaps to whole columns and rows.
- **Edit** opens the graph editor again, and **Remove** takes the graph off, after asking.

Press **Edit layout** again when you are done. Graphs fill the grid in order, each in the first place it
fits.

## Formatting a dashboard

Press **Format** to set these for the dashboard on the tab:

- **Dashboard name**, shown on the tab.
- **Columns**: from 1 to 6. A narrow window shows fewer, so graphs stay readable.
- **Row height** and **Space between graphs**.
- **Background**: a colour behind the graphs. **Default** goes back to the theme's.
- **Remove dashboard** removes it and its graphs, after asking. The last dashboard cannot be removed.

**Add dashboard**, at the top of the page, adds another. Everything is saved as it changes.

## Receiving messages

- The bar at the top of a dashboard lists the clients its graphs use and how each stands. **Connect** is
  shown for one that is not connected, and connects it on the MQTT page too.
- Graphs subscribe for themselves as soon as the app starts, and keep drawing whether or not the page or
  the Topic Explorer is open. They subscribe again after a reconnect.
- Readings are not kept between runs, so a graph starts empty each time. A topic's retained value, if it
  has one, becomes its first reading.
- A retained value sent again, as happens on a reconnect, is not counted as a new reading.
- Each number keeps up to 4,000 readings. A topic faster than that keeps fewer of its older ones, so a
  long span on a very fast topic shows less detail.

## If a graph is empty

- **Connect … to receive …**: the client is not connected. Press **Connect** at the top of the dashboard.
- **Waiting for a message on …**: nothing has arrived since the app started. Check the topic in the
  [Topic Explorer](help:topic-explorer), where it must match exactly, including upper and lower case.
- **There is no MQTT client called …**: the client was removed. Add a client of that name, or press
  **Edit layout**, then **Edit**, and choose another. Renaming a client does not cause this; its graphs
  follow it.
- A graph stays empty when the number is not in the messages. Only numbers can be drawn: text, true or
  false, and numbers written as text have no graph.
