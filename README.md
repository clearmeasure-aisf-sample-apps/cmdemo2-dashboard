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
  last 30 checks;
- which region is expected to serve the traffic, a "Failed over to <region>" banner when the primary is not healthy
  but a standby is, and whether the Front Door endpoint agrees;
- the version the last deployment pinned in Git next to the versions the nodes run ("Pinned 2.4.7. In sync: all 2
  nodes run 2.4.7." or "Differs: eastus2 runs 2.4.6."), with a link to the Octopus Deploy project that deploys the
  deployable and to the history of the pins on GitHub;
- in the header: the overall summary ("All 7 nodes healthy", "2 of 7 nodes not healthy"), the time of the last
  refresh, pause and resume, the interval (10 s, 30 s, 60 s) and the probe (health check or liveness), and a second
  line only while versions differ somewhere ("Versions differ in 1 environment").

The browser holds no secret: it reads public addresses only (the nodes' health and version endpoints and one public
file on GitHub), and the links to Octopus Deploy and GitHub are plain links that ask the viewer to sign in there.

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
  "system": { "slug": "cmdemo2", "name": "CM demo 2 multi-region", "repository": "https://github.com/example-org/cmdemo2-system" },
  "generated": "2026-10-04T22:00:00Z",
  "environments": [
    {
      "name": "uat", "tier": "nonprod",
      "versionsUrl": "https://raw.githubusercontent.com/example-org/cmdemo2-system/main/environments/uat/versions.json",
      "versionsHistoryUrl": "https://github.com/example-org/cmdemo2-system/commits/main/environments/uat/versions.json",
      "deployables": [
        {
          "name": "ui",
          "projectUrl": "https://example.octopus.app/app#/Spaces-1/projects/cmdemo2-ui",
          "frontDoor": "https://cmdemo2-uat-def456.z01.azurefd.net",
          "healthPath": "/_healthcheck", "alivePath": "/alive", "versionPath": "/_version",
          "nodes": [
            { "name": "app-cmdemo2-uat-ui", "region": "westus3", "role": "primary", "url": "https://app-cmdemo2-uat-ui.azurewebsites.net" },
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
| `generated` | no | The footer does not show when the topology was generated. |
| `environments` | yes, an array | Error. |
| `environments[].name` | yes | Error. |
| `environments[].tier` | no | No tier label. |
| `environments[].versionsUrl` | no, may be `null` | No pinned versions for this environment: nothing is read and nothing is compared. |
| `environments[].versionsHistoryUrl` | no, may be `null` | No "Pin history" link. |
| `environments[].deployables` | no | The environment is shown without tiles. |
| `deployables[].name` | no | `app`. It is also the deployable's key in `versions.json`. |
| `deployables[].projectUrl` | no, may be `null` | No "Octopus project" link. |
| `deployables[].frontDoor` | no, may be `null` | No Front Door tile. |
| `deployables[].healthPath` | no | `/_healthcheck`. |
| `deployables[].alivePath` | no | `/alive`. |
| `deployables[].versionPath` | no | `/_version`. |
| `deployables[].telemetryPath` | no, may be `null` | No calls per minute: the arrows show "–". The nodes' own counts of the last minute (`/_telemetry`, see "Calls per minute"). |
| `deployables[].trafficPaths` | no, may be `null` | The traffic button calls `/` only. |
| `deployables[].nodes` | no | No node tiles. |
| `nodes[].url` | yes, an absolute http(s) address | Error. |
| `nodes[].name` | no | The host of `url`. |
| `nodes[].region` | no | The tile is titled with the node's name. |
| `nodes[].role` | no | `primary` for the first node of the deployable, `standby` for the others. |

An address that is present (`system.repository`, `versionsUrl`, `versionsHistoryUrl`, `projectUrl`, `frontDoor`,
`nodes[].url`) must be an absolute http(s) address: anything else is an error. A topology without `repository`,
`versionsUrl`, `versionsHistoryUrl` and `projectUrl` is shown as before these fields existed: no line about versions,
no request to GitHub.

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
| Web app, Front Door endpoint | The box's colour and border by state (Healthy, Unhealthy, Unreachable, Checking), and a tile: the state's badge with its word, HTTP status and latency, the version, the last 30 checks. A web app also shows its version next to the pinned one ("pinned 2.4.21: in sync", "differs from pinned 2.4.21") and its role ("primary: serves traffic", "standby: ready, no traffic"); the endpoint shows where it routes ("routes to eastus2 (failed over)") and whether it agrees with the web apps. |
| Region of web apps | A mark with words: "serving traffic" (green frame), "standby: ready", "not serving" (red frame), by the same serving decision as the health view's banner. |
| Front Door to an origin | Solid and green while it carries the traffic, dotted grey while idle, dashed red when the origin is not healthy: a failover is the green line moving from priority 1 to priority 2. |
| Web app to the database | Green from the web app that serves, dotted from the others. |
| Number line of a relationship | Calls per minute of the last minute, in a solid frame, as the web apps count them: browser to Front Door (the sum of its origins' requests from Front Door), Front Door to an origin (its requests from Front Door; Front Door's health probes next to the role), web app to database (SQL commands). A dashed frame with "–" where no web app reports a number (no `telemetryPath`, or an app without the endpoint). |
| Database | The browser cannot ask Azure SQL, but a web app's health check connects to it: "Reachable" (healthy) when the health check of a web app that uses it passes; "Not confirmed" (neutral) when none passes, since the web app may be the cause; "Not probed" (neutral) with the Liveness probe, which leaves the database alone. |
| Static site | Neutral, "Not probed": the dashboard does not check itself. The static site that serves the page says "This page". |

A state is never colour alone: the badge has an icon and a word, the regions a word, the lines differ in dash and
width. Hover a node or a line for its details.

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
with tdd and uat, uat with a standby in eastus2, Front Door, plans B1, the database in centralus, and the dashboard at
http://localhost:5210 in tdd. Render it again when the diagram or the sample topology changes; the tests read it.

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
(and when opened on its own) is neutral.

**The update.** `RuntimePayloadBuilder` (plain C#, unit-tested) maps the monitor's state and the manifest to a
payload, and `js/runtime.js` draws it. Every word and state is decided in C#; the script sets `data-rt-state` on the
elements and draws text and small shapes with classes, and `css/app.css` ("Runtime view") gives them their colours.
The payload, as JSON:

```json
{
  "nodes": [ { "alias": "app_ui_primary", "state": "healthy", "label": "Healthy", "facts": "HTTP 200 · 41 ms",
               "lines": [ { "text": "version 2.4.21", "tone": "strong" }, { "text": "pinned 2.4.21: in sync", "tone": "insync" },
                          { "text": "primary: serves traffic", "tone": "serving" } ],
               "history": [ "healthy", "unhealthy", "healthy" ], "title": "app-cmdemo2-uat-ui: Healthy\n..." } ],
  "regions": [ { "alias": "region_primary", "state": "serving", "label": "serving traffic" } ],
  "edges": [ { "id": "fd_ui-to-app_ui_primary", "state": "active", "number": "–", "unit": "calls/min",
               "text": "first, while healthy", "title": "..." } ]
}
```

Node states `healthy`, `unhealthy`, `unreachable`, `checking`, `neutral`; region states `serving`, `standby`, `down`,
`checking`, `neutral`; relationship states `active`, `idle`, `down`, `checking`, `neutral`; line tones `strong`,
`plain`, `muted`, `serving`, `ok`, `warn`, `insync`, `differs`, `unknown`. `number` is absent for a relationship
without a number line, and "–" where no node reports calls per minute. The script reports every alias or id of the
payload that the SVG lacks, and the view names them.

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
  Components/                tile, history strip, state badge, deployable section, version line, runtime view, legend
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
(`VersionAssessment`, `VersionSummary`). The runtime view's logic is in `src/Dashboard/Runtime`: reading `runtime/`
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
  "sql": { "perMinute": 30, "p95Ms": 12 },
  "http": { "perMinute": 0 }
}
```

`requests` is traffic (not the checks); `probes.perMinute` the dashboards' and diagnostics' checks, `probes.frontDoor`
Front Door's health probes. Every round reads it from each regional node next to the health check (never through Front
Door, which would answer for one node only). An answer that is not this JSON is no numbers, not a failure.

The panel at the bottom of the page sends two requests a second for a minute (`TrafficPlan`) from the browser to the
chosen environment's public addresses, the Front Door endpoint or else the primary node, round-robin over
`trafficPaths`. They are plain GETs in mode `no-cors` (`js/traffic.js`): the browser needs no CORS answer, and the
response stays opaque. While it runs, the page checks every 10 s.

