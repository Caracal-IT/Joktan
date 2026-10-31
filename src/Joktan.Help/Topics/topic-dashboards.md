# Topic Dashboards

**Dashboard / Topic Dashboards** draws the numbers on MQTT topics as graphs, on dashboards you name and
lay out yourself. Each dashboard has its own tab. Graphs use the clients on the [MQTT](help:mqtt) page.

## Adding a graph

From the Topic Explorer, when you are already watching the topic:

1. Select the topic in the [Topic Explorer](help:topic-explorer) and press **Add to dashboard**.
2. The graph editor opens, with the numbers being drawn there already ticked and the messages received
   so far already on the preview.
3. To draw it on a graph you already have, choose that graph under **Add this topic to**.

From the dashboard, when you know the topic:

1. Press **Add graph**.
2. Choose the **Client**, type the **Topic**, and press **Subscribe** or Enter. If the client is not
   connected, press **Connect** beside the message.
3. The numbers in the topic's messages are listed as they arrive.
4. To draw another topic on the same graph, type it and press **Subscribe** again.

Then, in the editor, whichever way you came:

1. Give the graph a **Name**.
2. Choose its **Type**, and watch the preview change.
3. Tick up to 8 **Numbers to draw**, from any of the topics. Each can have its own name and colour.
4. Choose the **Dashboard**, or tick **Put it on a new dashboard**.
5. Press **Add to dashboard**.

Each topic is one exact topic, so `+` and `#` cannot be used. If nothing is being sent to a topic yet,
choose it beside the path box and add a number by its path, such as `sensor.value` or
`readings[*].value`. Leave the path empty for a payload that is a single number.

Paths mean the same as in the Topic Explorer: `readings[*].value` draws every item of an array, and
`readings[0].value` only the first.

## Several topics on one graph

A graph can draw numbers from as many topics as you like, up to 8 numbers in all, to compare them over
the same time and on the same scale. All of a graph's topics use the same client.

- The first number to arrive on each topic is ticked for you. Clear it and tick another if you prefer.
- A number you have not named is called by as much of its topic as tells it from the others, such as
  `line1/temp value`. Use the name box beside it for something shorter.
- To add a topic later, press **Edit layout**, then **Edit**, or use **Add to dashboard** in the Topic
  Explorer and choose the graph. **Remove** beside a topic takes it and its numbers off.
- Every topic on a graph needs a number ticked before it can be saved, so that none goes unnoticed. A topic
  with nothing arriving yet can have a number added by its path.
- A topic that has sent nothing yet shows "–" beside its name, while the others draw as usual.

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
- **Waiting for a message on …**: nothing has arrived since the app started, on any of the graph's topics.
  Check the topic in the [Topic Explorer](help:topic-explorer), where it must match exactly, including upper
  and lower case.
- **There is no MQTT client called …**: the client was removed. Add a client of that name, or press
  **Edit layout**, then **Edit**, and choose another. Renaming a client does not cause this; its graphs
  follow it.
- A graph stays empty when the number is not in the messages. Only numbers can be drawn: text, true or
  false, and numbers written as text have no graph.
