# Health dashboard

A static web page that shows the health of every node of a multi-region system. It is a Blazor WebAssembly
application (.NET 10, standalone): the browser itself calls the health endpoint of every node, so the dashboard needs
no server of its own and shows what a client on the internet sees.

The page has two views of the same checks, switched by the tab bar under the header: **Health** (the default, the
tiles below) and **Runtime** (the C4 deployment diagram of one environment, updated live; see "The runtime view"). The
address keeps the choice: `#runtime` opens the runtime view, `#runtime/uat` the diagram of uat, so a link opens it.
The header's controls (pause, interval, probe, check now) and its summary apply to both views: one set of checks, two
renderings.

For each environment (tdd, uat, prod) and each deployable in it, the health view shows:

- one tile for the Azure Front Door endpoint (the public address) and one tile per regional node (web app), with its
  region and role (primary or standby);
- per tile: the state, the HTTP status, the latency, the app version, the time of the last check and a strip with the
  last 30 checks; for a web app that reports them, its calls of the last minute and its process's vitals (CPU, memory,
  requests in flight, exceptions, uptime), with a small trend line next to the requests and the CPU;
- per deployable, a "Code" card (the build its primary node runs: commit, lines of code by language, tests, coverage,
  complexity, CRAP, Qodana) and a "Delivery" card (deployed when, signed off by whom, lead time, how far behind the
  first environment), each only when its source answers;
- under both views, "What just happened": the last 50 events this page observed (state changes, restarts,
  deployments, failovers, pins, traffic);
- where the topology has a link for it, every number and name leads to its place in the Azure portal or in Octopus
  Deploy;
- which region is expected to serve the traffic, a "Failed over to <region>" banner when the primary is not healthy
  but a standby is, and whether the Front Door endpoint agrees;
- the version the last deployment pinned in Git next to the versions the nodes run ("Pinned 2.4.7. In sync: all 2
  nodes run 2.4.7." or "Differs: eastus2 runs 2.4.6."), with a link to the Octopus Deploy project that deploys the
  deployable and to the history of the pins on GitHub;
- in the header: the overall summary ("All 7 nodes healthy", "2 of 7 nodes not healthy"), the time of the last
  refresh, pause and resume, the interval (10 s, 30 s, 60 s) and the probe (health check or liveness), and a second
  line only while versions differ somewhere ("Versions differ in 1 environment").

The browser holds no secret: it reads public addresses only (the nodes' health, version, telemetry and build
endpoints and public files on GitHub), and the links to the Azure portal, Octopus Deploy and GitHub are plain links
that ask the viewer to sign in there.

Everything beyond the health checks is optional. A source that is absent or cannot be read (an older app without the
endpoint, a file that is not published yet, a topology without the field) is never an error: its element is not
shown, or shows a dash.

## Run it locally

```
dotnet run --project src/Dashboard
```

Then open http://localhost:5210 (http://localhost:5210/#runtime for the runtime view). The repository ships a sample
`src/Dashboard/wwwroot/topology.json` and, rendered from it, a sample `src/Dashboard/wwwroot/runtime/` (tdd and uat);
their hosts do not exist, so every tile and every web app of the diagram shows Unreachable and no pinned version is
found. Point the sample at real nodes and at a real
system repository to see them (and allow `http://localhost:5210` in the nodes' CORS settings, see below).

```
dotnet build -c Release     # warnings are errors
dotnet test -c Release      # the unit tests of the health logic
dotnet publish src/Dashboard -c Release -o publish    # the site is publish/wwwroot
```

The SDK version is pinned in `global.json` (10.0.100, rolling forward to the latest 10.0 feature band).

## The topology: `topology.json`

The dashboard loads `topology.json` from its own address (next to `index.html`) when it starts and when "Reload
topology" is pressed. The deployment writes the real file; the build does not know the system.

```json
{
  "system": { "slug": "cmdemo2", "name": "CM demo 2 multi-region", "repository": "https://github.com/example-org/cmdemo2-system",
              "deliveryUrl": "https://raw.githubusercontent.com/example-org/cmdemo2-system/status/delivery.json" },
  "generated": "2026-10-04T22:00:00Z",
  "environments": [
    {
      "name": "uat", "tier": "nonprod",
      "versionsUrl": "https://raw.githubusercontent.com/example-org/cmdemo2-system/main/environments/uat/versions.json",
      "versionsHistoryUrl": "https://github.com/example-org/cmdemo2-system/commits/main/environments/uat/versions.json",
      "links": { "applicationInsights": "https://portal.azure.com/#@<tenant>/resource/<id of appi-cmdemo2-uat>/overview",
                 "applicationMap": "...", "database": "...", "resourceGroup": "..." },
      "deployables": [
        {
          "name": "ui",
          "projectUrl": "https://example.octopus.app/app#/Spaces-1/projects/cmdemo2-ui",
          "frontDoor": "https://cmdemo2-uat-def456.z01.azurefd.net",
          "healthPath": "/_healthcheck", "alivePath": "/alive", "versionPath": "/_version",
          "telemetryPath": "/_telemetry", "trafficPaths": [ "/" ], "buildPath": "/_build",
          "links": { "frontDoor": "...", "logs": "..." },
          "nodes": [
            { "name": "app-cmdemo2-uat-ui", "region": "westus3", "role": "primary", "url": "https://app-cmdemo2-uat-ui.azurewebsites.net",
              "links": { "portal": "...", "liveMetrics": "...", "performance": "...", "failures": "...", "dependencies": "..." } },
            { "name": "app-cmdemo2-uat-ui-eastus2", "region": "eastus2", "role": "standby", "url": "https://app-cmdemo2-uat-ui-eastus2.azurewebsites.net" }
          ]
        }
      ]
    }
  ]
}
```

| Field | Required | When it is missing |
|---|---|---|
| `system.slug`, `system.name` | no | The name falls back to the slug, then to "System". |
| `system.repository` | no, may be `null` | The footer names the system without a link to its repository. |
| `system.deliveryUrl` | no, may be `null` | No "Delivery" cards and no last failover test: nothing is read (see "Delivery"). |
| `generated` | no | The footer does not show when the topology was generated. |
| `environments` | yes, an array | Error. |
| `environments[].name` | yes | Error. |
| `environments[].tier` | no | No tier label. |
| `environments[].versionsUrl` | no, may be `null` | No pinned versions for this environment: nothing is read and nothing is compared. |
| `environments[].versionsHistoryUrl` | no, may be `null` | No "Pin history" link. |
| `environments[].links` | no | No links to the environment's resources (see "Links"). Keys: `applicationInsights`, `applicationMap`, `database`, `resourceGroup`. |
| `environments[].deployables` | no | The environment is shown without tiles. |
| `deployables[].name` | no | `app`. It is also the deployable's key in `versions.json`. |
| `deployables[].projectUrl` | no, may be `null` | No "Octopus project" link. |
| `deployables[].frontDoor` | no, may be `null` | No Front Door tile. |
| `deployables[].healthPath` | no | `/_healthcheck`. |
| `deployables[].alivePath` | no | `/alive`. |
| `deployables[].versionPath` | no | `/_version`. |
| `deployables[].telemetryPath` | no, may be `null` | No calls per minute: the arrows show "–"; no process vitals and no trend lines. The nodes' own counts of the last minute (`/_telemetry`, see "Calls per minute"). |
| `deployables[].trafficPaths` | no, may be `null` | The traffic button calls `/` only. |
| `deployables[].buildPath` | no, may be `null` | No "Code" card: nothing is read (see "Code"). |
| `deployables[].links` | no | The Front Door tile's name is no link, and there is no "Requests in Logs" link. Keys: `frontDoor`, `logs`. |
| `deployables[].nodes` | no | No node tiles. |
| `nodes[].url` | yes, an absolute http(s) address | Error. |
| `nodes[].name` | no | The host of `url`. |
| `nodes[].region` | no | The tile is titled with the node's name. |
| `nodes[].role` | no | `primary` for the first node of the deployable, `standby` for the others. |
| `nodes[].links` | no | The node's numbers and its name are plain text. Keys: `portal`, `liveMetrics`, `performance`, `failures`, `dependencies`. |

An address that is present (`system.repository`, `system.deliveryUrl`, `versionsUrl`, `versionsHistoryUrl`,
`projectUrl`, `frontDoor`, `nodes[].url`) must be an absolute http(s) address: anything else is an error. A topology
without `repository`, `versionsUrl`, `versionsHistoryUrl` and `projectUrl` is shown as before these fields existed: no
line about versions, no request to GitHub. A `links` object is more forgiving, because a link is a courtesy: an entry
whose value is not an absolute http(s) address is left out (so is a `links` that is not an object), an unknown key is
ignored, and the number it would have belonged to stays plain text.

Unknown fields are ignored. A file that is missing, is not JSON or breaks a rule above is not shown in part: the
dashboard shows "The topology could not be read" with every reason and its place in the file (for example
`environments[1].deployables[0].nodes[0].url: missing or not an absolute http or https address.`).

The version endpoint (`versionPath`) answers JSON such as `{"version":"2.4.21+0a1b2c3"}`; the tile shows the part
before `+`. A node that stops answering keeps the last version it reported.

## Pinned versions: what Git says next to what runs

Octopus Deploy deploys every deployable. The first step of a deployment commits the version it deploys to the system
repository: `environments/<environment>/versions.json` on `main`, a JSON object with one entry per deployable.

```json
{ "dashboard": "1.0.1", "ui": "2.4.7" }
```

That entry is the **pinned** version: what Git says the environment runs. When a later step of the deployment fails,
the deployment puts the previous version back, so the file names the last version that deployed.

The dashboard reads the file from `versionsUrl` and shows, in one line under the banner of each deployable, the
pinned version next to what the nodes report at `versionPath`:

| Line | Meaning |
|---|---|
| Pinned 2.4.7. In sync: all 2 nodes run 2.4.7. | Every node whose version is known runs the pinned version. |
| Pinned 2.4.7. Differs: eastus2 runs 2.4.6. | A node runs another version; each such node is named with its version. A deployment is in flight or failed part-way, a node was rolled back or deployed by hand, or the file is ahead of the copy GitHub serves (see below). |
| Pinned 2.4.7. Not compared: westus3 is unreachable. | The pinned version is known, and no node's version is. |
| No pinned version. | The file has no entry for the deployable, or was not found (HTTP 404): nothing was deployed yet. A repository that is not public answers HTTP 404 too. |
| Pinned version not known. | The file could not be read: no answer, another HTTP status or not a JSON object. The reason is shown. |
| Reading the pinned version | Before the first answer. |

- Only the nodes are compared, not the Front Door endpoint: it answers with the version of whichever node served the
  request.
- Versions are compared as the tiles show them: without build metadata (`2.4.7+0a1b2c3` is `2.4.7`), and without
  regard to case.
- A node whose version is not known (unreachable, not checked yet, no answer from its version endpoint) is left out
  of the comparison and named ("eastus2 is unreachable"). It never makes the versions differ, although its tile
  still shows the last version it reported.
- A node that answers its probe with an error still runs a version, and that version is compared.
- "Differs" is a warning, never an error of the dashboard: the state has its own sign (≠, against = for "in sync" and
  dots for "not known") and its words, and the header gets a second line, "Versions differ in 1 environment". The
  summary above it stays about health.

The file is read once per round of checks and per environment (not per node), at the same time as the nodes and with
the same rules: a `GET` with `cache: no-store` and no header of its own (so the browser sends no CORS preflight), and
the same timeout. `raw.githubusercontent.com` allows every origin. A file that cannot be read changes no tile and
fails no check; the next round reads it again, and a failed reading replaces a good one (the dashboard does not
compare with a pinned version it can no longer read).

`raw.githubusercontent.com` may serve a copy that is a few minutes old (it caches for about five minutes, whatever
the browser asks for). Right after a deployment the nodes can therefore already run the new version while the
dashboard still reads the old pin, and shows "Differs" until GitHub serves the new file.

The links open in a new tab: "Octopus project" (`projectUrl`: the releases and deployments of the deployable) and
"Pin history" (`versionsHistoryUrl`: one commit per pin, each naming the deployment). Octopus Deploy asks the viewer
to sign in; the dashboard itself never calls Octopus Deploy.

## The runtime view

The runtime view shows one environment as a C4 deployment diagram: the Azure subscription, the resource groups (the
tier's, and the Front Door's), the regions (primary, standby, and the region of the database and of the static
sites), the App Service plans with their size, the web apps, the Front Door endpoint, the Azure SQL database, the
dashboard's Static Web App and the browser, with the relationships between them. One button per environment selects
the diagram; "Fit to width" fits it to the page (down to three quarters of its size; below that, and at its actual
size, it scrolls sideways inside its own frame). The diagram is a light sheet in the dark theme too.

**Drawn when the dashboard was deployed** (static): the resources, their names, sizes, regions and relationships. The
deployment renders the diagram with PlantUML from `system.json` and the topology, so a change to the environments, a
standby region, a plan's size or the Front Door endpoint shows after the dashboard is deployed again (as in
`topology.json`).

**Live, from the checks of this page** (every round, and when another environment is selected), drawn into the
diagram in place:

| Element | What it shows |
|---|---|
| Web app, Front Door endpoint | The box's colour and border by state (Healthy, Unhealthy, Unreachable, Checking), and a tile: the state's badge with its word, HTTP status and latency, the version, the last 30 checks. A web app also shows its version next to the pinned one ("pinned 2.4.21: in sync", "differs from pinned 2.4.21"), its own numbers where it reports them ("12 req/min · p95 85 ms" with the trend of the requests, "0 errors · 0 exceptions/min", "CPU 3.2 % · 412 MB · 2 in flight" with the trend of the CPU, "up 2 h" or "restarted 3 min ago") and its role ("primary: serves traffic", "standby: ready, no traffic"); the endpoint shows where it routes ("routes to eastus2 (failed over)") and whether it agrees with the web apps. |
| Region of web apps | A mark with words: "serving traffic" (green frame), "standby: ready", "not serving" (red frame), by the same serving decision as the health view's banner. |
| Front Door to an origin | Solid and green while it carries the traffic, dotted grey while idle, dashed red when the origin is not healthy: a failover is the green line moving from priority 1 to priority 2. |
| Web app to the database | Green from the web app that serves, dotted from the others. |
| Number line of a relationship | Calls per minute of the last minute, in a solid frame, as the web apps count them: browser to Front Door (the sum of its origins' requests from Front Door), Front Door to an origin (its requests from Front Door; Front Door's health probes next to the role), web app to database (the SQL commands of its requests, with the background ones next to the role, "queries of the app · 55 background"; all SQL commands for an app that does not tell them apart). Next to the number, its trend over the last checks of this page. A dashed frame with "–" where no web app reports a number (no `telemetryPath`, or an app without the endpoint). |
| Database | The browser cannot ask Azure SQL, but a web app's health check connects to it: "Reachable" (healthy) when the health check of a web app that uses it passes, with the queries per minute its web apps' traffic causes (the background ones are in the tooltip); "Not confirmed" (neutral) when none passes, since the web app may be the cause; "Not probed" (neutral) with the Liveness probe, which leaves the database alone. |
| Links | Where the topology has a link (see "Links"): a web app's badge (Live Metrics), its name (the web app in the portal), its version (the release in Octopus Deploy, or else the commit of its build), its requests (Performance) and its errors (Failures); the Front Door endpoint's and the database's name; the numbers on the arrows (Performance, the dependency calls, the requests in Logs). They are real `a` elements: underlined, in the order of the keyboard, each with a title that says where it goes. |
| Static site | Neutral, "Not probed": the dashboard does not check itself. The static site that serves the page says "This page". |

A state is never colour alone: the badge has an icon and a word, the regions a word, the lines differ in dash and
width. Hover a node or a line for its details.

Under the diagram, for the environment it shows: the links to its resources in the Azure portal, and per deployable
the "Code" and "Delivery" cards of the health view (see "Code" and "Delivery"); then the legend.

A deployment from before the runtime view has no `runtime/`: the tab then says that the diagram is not available for
this deployment, and the health view works as before.

### The files: `runtime/`

The deployment (`deploy-staticwebapp.ps1` of the system repository) writes, next to `topology.json`:

| File | Content |
|---|---|
| `runtime/index.json` | `{ "generated": "...", "plantuml": "1.2026.8", "environments": [ { "name": "uat", "manifest": "uat.json", "svg": "uat.svg" } ] }`, in the order of `system.json`. A file name is a plain name in `runtime/`. |
| `runtime/<env>.svg` | The diagram, rendered by PlantUML (the pinned version, layout engine smetana). |
| `runtime/<env>.json` | The manifest: which drawn element is which (below). |
| `runtime/<env>.puml` | The PlantUML source, for reading; the page does not load it. |

The manifest maps each element's alias to what the browser knows, so the page never reads names out of the SVG:

```json
{
  "environment": "uat",
  "svg": "uat.svg",
  "nodes": [
    { "alias": "browser", "qualifiedName": "browser", "kind": "person", "name": "Browser" },
    { "alias": "fd_ui", "qualifiedName": "sub.rg_edge.afd.fd_ui", "kind": "frontdoor", "deployable": "ui",
      "name": "cmdemo2-uat-ui", "url": "https://cmdemo2-uat-def456.z01.azurefd.net" },
    { "alias": "app_ui_primary", "qualifiedName": "sub.rg_tier.region_primary.plan_primary.app_ui_primary",
      "kind": "webapp", "deployable": "ui", "name": "app-cmdemo2-uat-ui", "role": "primary", "region": "westus3",
      "regionAlias": "region_primary", "url": "https://app-cmdemo2-uat-ui.azurewebsites.net" },
    { "alias": "sqldb", "qualifiedName": "sub.rg_tier.region_data.sqldb", "kind": "sql", "name": "sqldb-cmdemo2-uat",
      "region": "centralus", "regionAlias": "region_data", "url": null },
    { "alias": "swa_dashboard", "qualifiedName": "sub.rg_tier.region_data.swa_dashboard", "kind": "staticsite",
      "deployable": "dashboard", "name": "swa-cmdemo2-uat-dashboard", "region": "centralus",
      "regionAlias": "region_data", "url": null }
  ],
  "regions": [ { "alias": "region_primary", "qualifiedName": "sub.rg_tier.region_primary", "name": "westus3", "roles": [ "primary" ] } ],
  "edges": [
    { "id": "fd_ui-to-app_ui_primary", "from": "fd_ui", "to": "app_ui_primary", "kind": "origin", "priority": 1 },
    { "id": "app_ui_primary-to-sqldb", "from": "app_ui_primary", "to": "sqldb", "kind": "sql" }
  ],
  "generated": "2026-10-04T22:00:00Z",
  "plantuml": "1.2026.8"
}
```

- **Aliases** (`<d>` is the deployable's name with every character but a letter or a digit as `_`): `browser`; the
  boundaries `sub`, `rg_edge`, `rg_tier`, `afd`, `region_primary`, `region_standby`, `region_data`, `region_static`
  (a region with two roles is one boundary, named after its first role), `plan_primary`, `plan_standby`; the nodes
  `fd_<d>`, `app_<d>_primary`, `app_<d>_standby`, `sqldb`, `swa_<d>`. A relationship's id is `<from>-to-<to>`.
- **Kinds**: nodes `person`, `frontdoor`, `webapp`, `sql`, `staticsite`; relationships `public` (the browser to a
  public address), `origin` (with its `priority`), `sql`, `dashboard`. An unknown kind is drawn and not updated.
- **Addresses**: `url` is the address the page checks (web app, Front Door endpoint: the same as in `topology.json`,
  which is how a node finds its checks) or, for a static site, the dashboard's address where the deployment knows it
  (its own environment's); `null` for the database, which the browser cannot probe, for a Front Door endpoint that
  is not deployed yet, and for another environment's dashboard.
- A node whose address `topology.json` does not have is drawn neutral, "Not checked: not in topology.json".

The sample `runtime/` was made by the same functions as a deployment (`ConvertTo-Topology` and `Write-RuntimeDiagram`
of `deploy-staticwebapp.ps1`, with PlantUML 1.2026.8) from a `system.json` whose topology is the sample's: cmdemo2
with tdd and uat (both with capability "telemetry"), uat with a standby in eastus2, Front Door, plans B1, the database
in centralus, `telemetryPath`, `buildPath` and `trafficPaths` for ui, a subscription and tenant of zeros for the links,
and the dashboard at http://localhost:5210 in tdd. Render it again when the diagram or the sample topology changes; the tests read it.

**What the page relies on in the SVG.** PlantUML writes these attributes, and they are not a documented contract (they
changed in PlantUML 1.2026.3 and 1.2026.4), so the deployment pins one version and checks every render for them; a
missing one fails the deployment:

| Element | In the SVG |
|---|---|
| Node | `<g class="entity" data-qualified-name="<alias path through the boundaries>">`; its `<rect>` (a database: its two `<path>`) is the box; its `<image>` is the slot. |
| Region | `<g class="cluster" data-qualified-name="...">`; its first `<rect>` is the frame; its `<image>` is the slot. |
| Relationship | `<g class="link" data-entity-1="<id of the from node's g>" data-entity-2="<id of the to node's g>">`, with its `<path>`, `<polygon>` (the head) and `<image>` (the slot). PlantUML's own layout engine (smetana, which the deployment uses: the worker has no Graphviz) gives the `<path>` no id; a Graphviz layout names it `<from>-to-<to>`, and the page reads that too. |

**Slots.** Text in PlantUML's SVG has a fixed `textLength`, and the layout depends on the length of every text, so
live values cannot be written into the rendered text. Instead every node, every region of web apps and every Front
Door and database relationship carries a transparent image of a fixed size in its description: PlantUML lays it out
like any image, the page hides it and draws into its rectangle (`js/runtime.js`). The diagram's look before an update
(and when opened on its own) is neutral. The sizes are set in `deploy-staticwebapp.ps1`: a web app's slot is 250 by
146 (the badge, seven lines 15 px apart and the history strip), a Front Door endpoint's 250 by 98, a database's and
a static site's 250 by 46, a region's 190 by 22, a relationship's 160 by 34. The script draws as many lines as a slot
holds, so a newer page on an older diagram loses lines, never its place.

**The update.** `RuntimePayloadBuilder` (plain C#, unit-tested) maps the monitor's state and the manifest to a
payload, and `js/runtime.js` draws it. Every word and state is decided in C#; the script sets `data-rt-state` on the
elements and draws text and small shapes with classes, and `css/app.css` ("Runtime view") gives them their colours.
The payload, as JSON:

```json
{
  "nodes": [ { "alias": "app_ui_primary", "state": "healthy", "label": "Healthy", "facts": "HTTP 200 · 41 ms",
               "lines": [ { "text": "version 2.4.21", "tone": "strong",
                            "parts": [ { "text": "version " }, { "text": "2.4.21", "link": { "href": "https://...", "title": "Release 2.4.21 of ui in Octopus Deploy (...)" } } ] },
                          { "text": "pinned 2.4.21: in sync", "tone": "insync" },
                          { "text": "12 req/min · p95 85 ms", "tone": "plain", "parts": [ "..." ],
                            "trend": { "points": [ 0.25, 1, 0.5 ], "title": "Requests per minute, last 3 checks: 10 to 40, now 20" } },
                          { "text": "primary: serves traffic", "tone": "serving" } ],
               "history": [ "healthy", "unhealthy", "healthy" ], "title": "app-cmdemo2-uat-ui: Healthy\n...",
               "link": { "href": "https://...", "title": "Live Metrics ..." }, "nameLink": { "href": "https://...", "title": "The web app ..." } } ],
  "regions": [ { "alias": "region_primary", "state": "serving", "label": "serving traffic" } ],
  "edges": [ { "id": "fd_ui-to-app_ui_primary", "state": "active", "number": "10", "unit": "calls/min",
               "text": "first, while healthy", "title": "...",
               "link": { "href": "https://...", "title": "Performance ..." }, "trend": { "points": [ 0, 1 ], "title": "..." } } ]
}
```

Node states `healthy`, `unhealthy`, `unreachable`, `checking`, `neutral`; region states `serving`, `standby`, `down`,
`checking`, `neutral`; relationship states `active`, `idle`, `down`, `checking`, `neutral`; line tones `strong`,
`plain`, `muted`, `serving`, `ok`, `warn`, `insync`, `differs`, `unknown`. `number` is absent for a relationship
without a number line, and "–" where no node reports calls per minute. The script reports every alias or id of the
payload that the SVG lacks, and the view names them.

Optional in the payload, and absent where there is nothing to say: `link` (a tile's badge, a relationship's number)
and `nameLink` (the node's name, which PlantUML drew: the script wraps it), each `{ href, title }`; a line's `parts`
(the same words as `text` in pieces, present only when a piece has a `link`); `trend` on a line or a relationship
(`points` are heights from 0 to 1, oldest first, on a scale from zero to the largest reading; `title` is the same in
words). The script draws a link as an `a` element (new tab, `rel="noopener"`, its own `title`) and gives the focus
back to the link that had it when an update redraws the tile.

## Links: where a number leads

The deployment writes optional `links` maps into `topology.json`, and the page turns every number or name that has a
matching link into one. A link opens a new tab (`rel="noopener"`), and its title says where it goes and that the
destination asks for a sign-in: the page holds no credential and calls none of these places itself.

| Where | Key | It leads to | In the page |
|---|---|---|---|
| `nodes[].links` | `portal` | The web app in the Azure portal. | The web app's name. |
| | `liveMetrics` | Live Metrics of the environment's Application Insights. | The tile's state badge. |
| | `performance` | The Performance blade. | Requests per minute; the number on the Front Door to origin arrow. |
| | `failures` | The Failures blade. | Errors, exceptions. |
| | `dependencies` | A Logs query: the dependency calls (SQL, HTTP) of the app's role, by target. | SQL per minute; the number on the web app to database arrow. |
| `deployables[].links` | `frontDoor` | The Front Door profile in the Azure portal. | The Front Door endpoint's name. |
| | `logs` | A Logs query: the requests of the app's role, by five minutes and instance. | "Requests in Logs" in the versions line; the number on the browser to Front Door arrow. |
| `environments[].links` | `applicationInsights`, `applicationMap`, `database`, `resourceGroup` | The environment's Application Insights, its application map, its database, its resource group. | The links at the end of the environment's heading and under the runtime diagram; the database's name in the diagram. |

Two links need no entry, because the page builds them from what it reads: a web app's **version** leads to its
release in Octopus Deploy (`projectUrl` + `/deployments/releases/<version>`), or else, when its build reports that
version, to the commit (`commitUrl` of the build endpoint); a Front Door endpoint's version is no link, since it
answers with the version of whichever node served. The "Delivery" card's version leads to `releaseUrl` of the
delivery facts.

`deploy-staticwebapp.ps1` builds the addresses from `system.json` by the naming convention of the stack:
`https://portal.azure.com/#@<tenantId>/resource<resource id>/<blade>`, with the web app
`app-<slug>-<env>-<deployable>[-<region>]` (blade `appServices`), the component `appi-<slug>-<env>` when the
environment has capability "telemetry" (blades `overview`, `applicationMap`, `quickPulse`, `performance`, `failures`),
the database `sqldb-<slug>-<env>` on the environment's SQL server (the one name it reads from Azure, because it ends
in a generated suffix), the Front Door profile `azure.frontDoor` and the tier's resource group. The Logs links are the
portal's "share a link to the query" form (the KQL gzipped and base64-encoded), filtered to
`cloud_RoleName == "<slug>-<deployable>"`: Application Insights is one component per environment, and both regions'
web apps report under that role. A link the deployment cannot build (no subscription in `system.json`, no telemetry,
a SQL server it may not list) is left out.

## How the states are decided

Every check is a `GET` from the browser with `cache: no-store` and a timeout of 10 seconds. All nodes are checked at
the same time, and every result is shown as it arrives: a node that hangs delays no other node. The pinned versions
are read alongside (see above).

| State | Meaning |
|---|---|
| Healthy | The endpoint answered HTTP 200. |
| Unhealthy | The endpoint answered with any other HTTP status (for example 503 from a failing health check). |
| Unreachable | The browser got no answer it may read: a network failure, a refused CORS request or no answer within 10 seconds. |
| Checking | Not checked yet. |

A state is never shown by colour alone: every state has its own icon shape and its text label, and the bars of the
history strip differ in height (tall: healthy, half: unhealthy, stub: unreachable).

**Expected to serve traffic.** Per deployable, the dashboard takes the nodes in priority order (primary nodes first,
then the others, each in the order of the topology) and names the first healthy one. When that node is not a primary,
the deployable has failed over, and the banner says to which region and why. When no node is healthy, nothing can
serve. The Front Door endpoint agrees when it is healthy exactly when a node is; a disagreement is flagged (Front
Door down although a node is healthy, or Front Door healthy although no node is).

**Summary.** Every tile counts as one node: the Front Door endpoints and the web apps. Unhealthy and Unreachable both
count as not healthy.

**Probe.** "Health check" calls `healthPath`: the full check also connects to the database, which keeps a serverless
database awake. "Liveness" calls `alivePath`: it only asks whether the web app is running and leaves the database
alone, so a serverless database can pause while the dashboard is open.

**Polling.** A round of checks runs every interval (30 seconds unless changed). Polling stops while the dashboard is
paused and while its browser tab is hidden (Page Visibility API), and a round runs at once when it resumes or the tab
is shown again. "Check now", changing the probe and reloading the topology run one round at once, also while paused.

## CORS: every node must allow the dashboard's origin

The browser reads an answer from another origin only when that origin allows it. Each node must therefore allow the
dashboard's origin in its CORS settings, on App Service for example:

```
az webapp cors add --resource-group <group> --name <web app> --allowed-origins https://<dashboard host>
```

Without it the browser blocks the answer and the dashboard reports the node as **Unreachable**, although the node may
be healthy: from inside the page, a refused CORS request and a network failure look the same. The same holds for an
answer that does not come from the app, such as the platform's own page for a stopped web app: it carries no CORS
header, so the tile shows Unreachable, not Unhealthy. The browser's console names the cause.

The Front Door endpoint forwards the request to a node, and the node's CORS header comes back through it, so the same
setting covers the Front Door tile.

## The repository

```
Dashboard.sln
Directory.Build.props        warnings as errors, nullable, analyzers; shared by both projects
global.json                  the SDK
src/Dashboard                the Blazor WebAssembly app
  App.razor                  the page: header, view tabs, environments, footer, polling
  Components/                tile, history strip, trend line, state badge, deployable section, version line, code and
                             delivery cards, events strip, links, runtime view, legend
  Health/                    the health logic, plain C# without a browser
  Runtime/                   the runtime view's files, payload and address, plain C# without a browser
  wwwroot/                   index.html, css/app.css, js/visibility.js, js/location.js, js/runtime.js, the sample
                             topology.json and runtime/
src/Dashboard.Tests          xUnit tests of the health logic
.github/workflows            build.yml, release.yml, secret-scan.yml
```

The health logic is in `src/Dashboard/Health` and has no dependency on the browser: reading the topology
(`TopologyParser`), classifying an answer (`HealthClassifier`, `NodeProber`), the serving and failover decision
(`ServingAssessment`), the summary (`HealthSummary`), the history (`HistoryBuffer`), the polling loop (`Poller`),
reading the pinned versions (`PinnedVersions`, `PinnedVersionsReader`) and comparing them with the nodes
(`VersionAssessment`, `VersionSummary`), a node's telemetry with its process (`TelemetrySnapshot`, `ProcessVitals`),
the trends (`Trend`, `Trends`), the events (`EventDetector`, `EventLog`), the build facts (`BuildInfo`, `BuildText`),
the delivery facts (`DeliveryReport`, `DeliveryText`) and the links (`LinkSet`, `LinkText`). The runtime view's logic is in `src/Dashboard/Runtime`: reading `runtime/`
(`RuntimeManifestParser`, `RuntimeLoader`), the update of the diagram (`RuntimePayloadBuilder`) and the view in the
address (`ViewAddress`). `HttpClient` and `TimeProvider` are injected, so the tests run them with a stub handler and
fake time.

There is no external dependency at run time: no CDN, no web font, no CSS framework. The style sheet is
`wwwroot/css/app.css` and follows the viewer's light or dark preference.

The site is static and expects to be served from the root of its host (`<base href="/">`); the host must serve
`.wasm` files as `application/wasm`.

## Build and release

- **Build** (`.github/workflows/build.yml`): on every pull request and every push to `master`. It builds with
  warnings as errors, runs the tests, publishes the site and uploads the content of the published `wwwroot` folder as
  the artifact `dashboard-site`. The job `Build result` is the check the default-branch ruleset requires.
- **Release** (`.github/workflows/release.yml`): after a green Build of `master`. It zips the content of
  `dashboard-site` (`index.html` at the root of the zip) as `<SYSTEM_SLUG>-<DEPLOYABLE_NAME>.<version>.zip`, pushes
  it to the Octopus built-in feed and creates the release `<version>` of the Octopus project
  `<SYSTEM_SLUG>-<DEPLOYABLE_NAME>`. It signs in to Octopus with GitHub OIDC (environment `release`) and reads the
  repository variables `SYSTEM_SLUG`, `DEPLOYABLE_NAME`, `OCTOPUS_URL`, `OCTOPUS_SPACE_NAME` and
  `OCTOPUS_SERVICE_ACCOUNT_ID`.
- **secret-scan** (`.github/workflows/secret-scan.yml`): gitleaks over the history of the commit every pull request and push checks out.

The version is `MAJOR_VERSION.MINOR_VERSION.<run number of the Build run>`; the two numbers are in `build.yml`. The
build passes it as `-p:Version=...`, and the dashboard shows it in its footer. A local build shows `0.0.0-local`.

The zip carries the sample `topology.json` and `runtime/`; the deployment replaces them with the real ones (it removes
the sample's `runtime/` first). The build writes no precompressed copy of those files (`topology.json.br`,
`runtime/*.gz`, ...), so no stale copy can be served.

## Calls per minute and the traffic button

A node with `telemetryPath` answers it with its own counts over the last minute (any origin may read them; numbers
only):

```json
{
  "windowSeconds": 60,
  "startedAt": "2026-10-05T23:00:00Z",
  "requests": { "perMinute": 12, "frontDoor": 10, "direct": 2, "errors": 0, "p95Ms": 85 },
  "probes": { "perMinute": 4, "frontDoor": 6 },
  "sql": { "perMinute": 117, "requests": 62, "background": 55, "p95Ms": 12 },
  "http": { "perMinute": 0 },
  "process": { "cpuPercent": 3.2, "workingSetMb": 412, "gcHeapMb": 96, "threads": 41, "inFlight": 2, "exceptionsPerMinute": 0, "uptimeSeconds": 5321 }
}
```

`requests` is traffic (not the checks); `probes.perMinute` the dashboards' and diagnostics' checks, `probes.frontDoor`
Front Door's health probes. Every round reads it from each regional node next to the health check (never through Front
Door, which would answer for one node only). An answer that is not this JSON is no numbers, not a failure.

`sql.perMinute` is every SQL command the process ran. An app may tell them apart: `sql.requests` (run while handling
an HTTP request) and `sql.background` (all others, mostly the message bus polling the database). When both are there,
the number the page shows for the web app's queries is `requests`, what traffic causes: on the web app to database
arrow (with the background next to the role, "queries of the app · 55 background", and both in the tooltip), in the
database's tile (the sum over its web apps, the background in the tooltip) and as "SQL" on the health view's tile. An
app that reports `perMinute` only (an older one) keeps the total everywhere. The arrow's trend follows the number
shown.

### Process vitals

`process` is optional (an older app omits it), and so is each of its values. A web app's tile shows, in both views:

| Value | Shown as |
|---|---|
| `cpuPercent` | "CPU 3.2 %" (one decimal below ten), with its trend. |
| `workingSetMb` | "412 MB", from 1024 on "1.4 GB"; the tooltip adds the managed heap (`gcHeapMb`) and the `threads`. |
| `inFlight` | Requests being answered at this moment. |
| `exceptionsPerMinute` | "0 exceptions/min"; a warning (the warning shape and bold words) when above zero, like errors. |
| `uptimeSeconds` | "up 3 min", "up 2 h", "up 4 d" (minutes up to two hours, hours up to two days). Under five minutes it is a restart worth a look, "restarted 3 min ago", marked as a warning. |

### Trends

The page keeps the last 60 telemetry readings of every node in memory (half an hour at the default interval; nothing
is stored) and draws a small line next to a number: requests per minute and CPU on the tiles, calls per minute on
the arrows of the runtime diagram (for the browser to Front Door arrow, the origins' readings of the same check added
up). Oldest on the left, the last reading as a dot, on a scale from zero to the largest reading, so a line that stays
low is a number that stays small. Its title says the same in words ("Requests per minute, last 12 checks: 0 to 45,
now 12"). A line needs two readings; a check without a reading is left out.

The panel at the bottom of the page sends two requests a second for a minute (`TrafficPlan`) from the browser to the
chosen environment's public addresses, the Front Door endpoint or else the primary node, round-robin over
`trafficPaths`. They are plain GETs in mode `no-cors` (`js/traffic.js`): the browser needs no CORS answer, and the
response stays opaque. While it runs, the page checks every 10 s.

## What just happened

Above the traffic panel, in both views: the last 50 events this page observed since it was opened, newest first,
each with its time, its environment and node, and words. The page finds them itself, by comparing every check with
the one before (`EventDetector`); nothing comes from a server's log, and a reload of the page starts an empty list
(a reload of the topology keeps it).

| Event | When | Example |
|---|---|---|
| Health | An endpoint's state changed; at the first check only when it is not healthy. | "ui: Healthy → Unreachable: No answer within 10 s" |
| Restart | A web app's `uptimeSeconds` went down, or (without it) its `startedAt` is later. Compared with its last reading, also across checks it did not answer. | "ui restarted, up 12 s" |
| Deployment | A web app reports another version. Not for a Front Door endpoint, which answers for whichever node served. | "ui: 2.4.14 → 2.4.15, deployed" |
| Serving region | At the end of a round, the node expected to serve changed: a failover, a failback, nothing serves, serves again. | "Failover: westus3 → eastus2. Primary westus3 is unreachable; eastus2 is expected to serve traffic." |
| Pin | The version pinned in Git changed between two readings of `versions.json`. | "ui: pinned 2.4.14 → 2.4.15 in Git" |
| Traffic | The traffic button was started, stopped or ran out. | "Traffic started: 2 requests a second for 60 s to ui at cmdemo2-uat-def456.z01.azurefd.net" |

The list is a `role="log"` region: additions are announced politely, and it scrolls inside its own frame. An event's
level has a shape (check, warning triangle, cross, dots) next to its words.

## Code: the build a deployable runs

With `buildPath` (for example `/_build`), the page asks the deployable's primary node for the build it runs: once
per load of the topology, and again when the node reports another version; not every round. A node that is
unreachable is asked once it answers. Any origin must be allowed to read it, like the telemetry. The answer, every
part optional and any section possibly `null`:

```json
{ "version": "2.4.15", "commit": "<sha>", "commitUrl": "https://github.com/o/r/commit/<sha>", "builtAt": "2026-10-06T05:00:00Z",
  "buildUrl": "https://github.com/o/r/actions/runs/1",
  "code": { "linesOfCode": 84210, "files": 1203, "languages": [ { "name": "C#", "lines": 61234, "files": 800 } ] },
  "tests": { "unit": 1009, "integration": 240, "acceptance": 168 },
  "coverage": { "linePercent": 81.2, "branchPercent": 70.1 },
  "complexity": { "average": 1.9, "max": 34, "methods": 5210 },
  "crap": { "max": 28.5, "threshold": 30, "overThreshold": 0 },
  "analysis": { "qodanaProblems": 0 } }
```

The "Code" card (in the health view inside the deployable's section, in the runtime view under the diagram) shows
what is there: the build's version, the commit (its first seven characters, linked to `commitUrl`), when it was built
(relative, the exact time in the title) and the build's run; the lines of code and files, with one bar divided by
language (the five largest by name, the rest as "Other") and under it the same as a list in words, so no colour has
to be told apart; the tests by kind with their sum; the coverage of lines and branches; the average and the worst
cyclomatic complexity; the worst CRAP score and how many methods are over the threshold (a warning when one is); the
problems Qodana reports (a warning when there are any). Each environment's card is its own primary node's answer, so
two environments that run different builds show different cards. An answer that is not this JSON, or no answer, is
no card.

## Delivery

With `system.deliveryUrl`, the page reads the system's delivery facts: with the first round of checks and then every
five minutes. The deployment writes the address
`https://raw.githubusercontent.com/<githubOrg>/<repository>/status/delivery.json`; a workflow of the system
repository publishes the file to its branch `status`. Until it has, the address answers 404 and nothing is shown. A
reading that fails later keeps the last good one.

```json
{ "generated": "2026-10-06T05:00:00Z",
  "environments": [ { "name": "prod", "deployables": [
    { "name": "ui", "version": "2.4.14", "deployedAt": "2026-10-06T03:37:00Z", "signedOffBy": "cm-ai-ops", "reason": "...",
      "commit": "<sha>", "commitAt": "...", "leadTimeHours": 5.2, "behindFirst": { "versions": 0, "days": 0 },
      "deploymentsLast7Days": 4, "failedLast7Days": 0, "releaseUrl": "https://..." } ] } ],
  "failover": { "environment": "uat", "at": "2026-10-05T12:00:00Z", "seconds": 44 } }
```

The "Delivery" card of a deployable in an environment shows what is there:

| Line | From |
|---|---|
| Deployed: the version (linked to `releaseUrl`) and how long ago, the exact time in the title. | `version`, `deployedAt` |
| Signed off: by whom, and the reason. The first environment has no sign-off step, so its card has no such line. | `signedOffBy`, `reason` |
| Lead time: from the commit to this deployment ("5.2 h from commit 0a1b2c3 to this deployment"). | `leadTimeHours`, `commit`, `commitAt` |
| Compared: with the first environment of the topology: "same as tdd", "2 versions, 3 days behind tdd". Versions are the distance in the project's list of releases; days are since both last ran the same release (absent when they never did). Not shown in the first environment. | `behindFirst` |
| Last 7 days: "4 deployments, none failed"; a warning when one failed. | `deploymentsLast7Days`, `failedLast7Days` |

`generated` is when the file's content last changed, not the time of a check: the card's title has it as "Delivery
facts as of the last change". Entries of the file that belong to no deployable of the topology get a card of their
own after the environment's deployables: first the one named `system`, the system project itself, labelled "the
system (infrastructure and pipeline)", then the others (such as the dashboard). The last failover test of the system
(`failover`: environment, when, the seconds until the standby answered) is a line above the events.

