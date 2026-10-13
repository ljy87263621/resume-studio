# Discovery and Cross-device Sync Integration Plan

> **For agentic workers:** Execute the module plans task-by-task, with the primary agent reviewing interfaces and performing final integration. The user selected architect plus module engineers acceptance mode.

**Goal:** Maintain the user's Fork with hierarchical job navigation, consented in-app recruitment collection, and manual WebDAV synchronization of existing user data.

**Architecture:** Retain the existing WinUI 3/C# desktop client and Java 21 local REST service. Recruitment collection observes the visible WebView2 session. WebDAV transports a versioned complete JSON snapshot, guarded by local compare-and-swap, remote ETags and explicit conflict choices.

**Tech Stack:** .NET 8 target using installed .NET SDK, Windows App SDK WinUI 1.8, WebView2, Java 21, System.Text.Json, HttpClient; no new service account or database.

## Global Constraints

- Fork: `ljy87263621/resume-studio`; upstream: `scj040921/resume-studio` at `e71aa3ddcc2557301a1abaf1226d382cc9bcb82f`.
- Release remains self-contained unpackaged Portable; no MSIX registration.
- User selected manual WebDAV first and a future GitHub provider interface.
- Website logins and WebDAV credentials remain local; synchronize no Cookie stores, API keys, logs, or preview caches.
- Existing collections: resumes, favorites, applications, sources, schedule; photo and submitted resume snapshots are embedded JSON. No independent attachment model currently exists.
- Preserve unknown fields in synchronized entity JSON; reject unsupported protocol versions and malformed imports before writes.
- Conflicting local and remote changes require a deliberate choice and a recoverable local backup.
- Respect user login and access verification; never invent listings or silently describe partial platform coverage as a complete index.
- Local tests and desktop acceptance use isolated directories.

### Task 1: Module implementation and interface review

**Files:** module plans in this directory; navigation models and MainWindow.Appearance.cs; MainWindow.PlatformCapture.cs and Services/PlatformCapture.cs; MainWindow.Sync.cs, Services/Sync*, backend Store/Server.

**Interfaces:** `OpenPlatformCaptureAsync(string provider,string keyword="")`; `AddSyncSettings(StackPanel panel)`; `/api/sync/snapshot` and `/api/sync/import`.

- [x] Review each module's source and tests; verify both UI styles use the same navigation catalog and sync protocol entities retain raw JSON.
- [x] Observe failing core tests before implementation and passing tests after it; module engineers record the exact test commands in their plans.

### Task 2: Connect the new features

**Files:** client-winui/MainWindow.CompanyJobs.cs; client-winui/MainWindow.Appearance.cs; client-winui/MainWindow.Updates.cs; client-winui/Services/AppPreferences.cs; tests/Updater.Tests/Program.cs.

- [x] Replace external-browser platform shortcuts with calls to `OpenPlatformCaptureAsync("boss",keyword.Text)` and `OpenPlatformCaptureAsync("nowcoder",keyword.Text)`.
- [x] In `ShowSettings`, insert `AddSyncSettings(panel);` before the final storage section and render the complete scroll contents.
- [x] Add `--navigation-test` to verification-session isolation; backend data directory flag is owned by the sync engineer.
- [x] Require `ReleaseUpdate.Repository=="ljy87263621/resume-studio"` and rejection of upstream package URLs, run the updater test to see the new expectation fail, then switch the implementation and all fixture URLs to the Fork. Default startup update check is disabled until own signed releases exist.

### Task 3: Verify the whole distribution

**Files:** scripts/build.ps1; scripts/test-offline.ps1; tests/*; artifacts/acceptance (ignored).

- [x] Recover resources and exact upstream Git objects from checksum-verified downloaded source/resource bytes; keep agent edits intact.
- [x] Run `./scripts/build.ps1`; expected self-contained distribution at `release/ResumeStudio/ResumeStudio.exe`.
- [x] Run module tests, `./scripts/test-offline.ps1`, `./scripts/test-schedule.ps1`, and `./scripts/test-updater.ps1`; expected all offline assertions pass.
- [x] Launch `ResumeStudio.exe --navigation-test` with RESUMESTUDIO_TEST_DATA/OUTPUT pointing into ignored artifacts; inspect result JSON and screenshots.
- [x] Start a final isolated demo app, verify nonzero main window handle and responsiveness, leave it open.
- [x] Document live verification limitations: no user login to recruitment sites and no user's WebDAV endpoint have been supplied.

### Task 4: Deliver reviewable changes

- [x] Update README and takeover documentation with actual implemented boundaries and test evidence; ensure all new tests are included in CI or a documented comprehensive command.
- [x] Check `git diff --check` and the final changed-file set.
- [ ] Commit to `codex/discovery-sync` and create a draft PR in the Fork.
- [ ] Deliver the PR link, local Portable app, protocol specification and real-service verification boundaries.
