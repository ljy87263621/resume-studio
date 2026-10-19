# Resume layout capability completion

**Goal:** Bring the Windows editor's content composition and A4 layout controls up to the useful capabilities of resume-helper, retaining existing documents and exports.

**Architecture:** Keep WinUI 3/C# native controls, Java local persistence, and the shared HTML document for preview/PDF. Lists, experiences, directory subheadings and tables remain editable Markdown in `sections[].content`; images use local data URLs in an optional section image collection referenced by stable tokens. New settings have defaults matching old documents. No new runtime dependencies or Windows App registration.

**Reference:** https://github.com/ljy87263621/resume-helper, main commit `6b1b69cf3bd2071c6f3cc17aeea41b492108ac80`. Existing retained MIT sources under ThirdParty/resume-helper were reviewed.

## Audit and scope

Already present: module drag ordering and visibility, templates, photo import, Markdown emphasis/list/experience headings/tables, fonts/colors/size/line-height, A4 estimate, JSON, PDF and editable Word export.

Gaps addressed: custom basic information items; native content builders and table cell editing; section images and one-level directory headings; page margins, section spacing and header alignment; adaptive directory/preview/editor workspace; stable A4 preview sizing; capacity measurement after fonts and images settle.

Windows is the priority selected by the user. Optional fields remain in JSON and sync; Android does not gain new editing controls in this change. Existing Markdown remains the canonical text and the Word export retains its independent editable layout.

## Tasks

- [x] Add regression coverage in `tests/Layout.Tests`: old JSON defaults, unknown fields, image token validation and escaping, table cell round-trip, finite settings and A4 CSS.
- [x] Extend `client-winui/Models.cs`; implement pure `Services/ResumeLayout.cs` helpers; update `Services/ResumeRenderer.cs` and verify the existing deep-copy behavior of `Services/ResumeExamples.cs`. Validate with `dotnet run --project tests/Layout.Tests` and existing workbench/sync checks.
- [x] Implement native content builders and custom contact fields in `MainWindow.ContentTools.cs`; connect `MainWindow.Editor.cs` and `MainWindow.EditingTools.cs`. Provide text/list/experience/directory/image/table entry points and image editing/removal.
- [x] Adapt workspace layout with a native view selector at constrained widths; add margins, spacing and alignment controls. Keep the WebView alive while rearranging columns. Verify widths in the native application.
- [x] Update Java validation and Word export for custom contacts, images and layout settings; cover malformed optional fields and embedded Word media using `ResumeLayoutTest`.
- [x] Extend native `WorkbenchTest` with layout, image, table, capacity and PDF checks; build using `scripts/build.ps1 -Configuration Debug`, run isolated acceptance, inspect screenshots and generated PDF pages, and leave a verified app running.
- [x] Update README/development notes with usage and compatibility limits; review `git diff --check` and final diff.

## Validation contracts

`ResumeLayout.Table(rows)` emits Markdown preserving literal pipes/backslashes/newlines. `ResumeLayout.ReadTable(content, caret)` returns a replaceable range and cell matrix. `ResumeLayout.ImageToken(image)` references only the generated identifier. `ResumeLayout.ValidImage(dataUrl)` permits bounded PNG/JPEG/WebP data URLs. Renderer must escape all user labels/captions/text and reject external images. Old documents retain 14/16 mm page margins and template header alignment. Preview zoom must never affect printed A4 geometry.

No release publishing or remote repository mutations are part of this task.

## Acceptance, 2026-10-20

- Windows Debug build: 0 warnings, 0 errors; native acceptance: 68 checks, including all 14 template alignment/spacing overrides, EXIF=6 pixel placement, alpha preservation, table dialog cancel/apply, adaptive layout, JSON import/export and actual single-page PDF.
- Pure C# workbench: 19 checks; layout: 16 checks. Full Sync.Tests passed, including real Java store/DAV integration and unknown-field preservation.
- Java offline suite passed, including 8 layout/Word checks, existing workflows/schedule/optimization, and 468 generated-template catalogue field checks.
- PDF pages rendered with Poppler and visually inspected. Word exported with embedded media/editable cells, converted with LibreOffice and visually inspected.
- Independent review findings fixed: literal pipe-only line crash; template CSS precedence; rotated image stride; replacement image budget double-counting. Reviewer confirmed no remaining findings.
- Final GUI assembly copied to a fresh `artifacts/layout-verified` directory after an old verification process locked the previous output. DLL SHA-256 matches the final build: `B3AA0093CE2694E4380AFC1C944A8174C94A3FA2509AB340022B89B1E317E9FA`.
- Branch: `Beta/layout-capabilities`. Changes remain local for review; no release or pull request created.
