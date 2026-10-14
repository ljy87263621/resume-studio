> 历史贡献者开发计划：原始 Fork 的安排，仅作实现背景。当前官方集成与验证边界见 docs/TAKEOVER.md；更新源和签名沿用 scj040921/resume-studio。

# Multi-level Navigation Implementation Plan

**Goal:** Display 发现职位 as a parent with 主页面 and 公司来源管理 children in both classic and Haru shells.

**Architecture:** A shared immutable navigation catalog supplies labels, keys and parent relationships. Classic renders expandable groups and a compact icon menu; Haru renders native NavigationViewItem children. All programmatic navigation goes through UpdateNavigation, retaining the existing editor-to-resumes mapping.

**Tech Stack:** C# / WinUI 3, dependency-free .NET console tests, native UI isolation test.

## Global Constraints

- Preserve the settings page and other agents' changes. Do not modify CompanyJobs or Git state.
- Use --navigation-test with isolated BackendClient storage; never read or write real user data.
- Preserve jobs and sources page IDs so existing button entry points keep working.

### Task 1: Shared hierarchy

Files: create client-winui/Services/NavigationCatalog.cs, tests/Navigation.Tests and scripts/test-navigation.ps1.

- [x] Add failing console checks for group labels/order, editor selection and parent relationships.
- [ ] Run `pwsh -File scripts/test-navigation.ps1` and verify the missing catalog assertion fails.
- [ ] Add `NavigationNode` and `NavigationCatalog.Roots`, `SelectedKey(string)` and `ParentKey(string)`.
- [ ] Run the same tests and verify every assertion passes.

### Task 2: Both shells and compact layout

Files: modify navigation portions of MainWindow.Appearance.cs and MainWindow.cs.

- [ ] Render NavigationCatalog.Roots recursively in Haru, with group items unselectable.
- [ ] Render classic group button, indented child buttons and a vertically scrollable menu.
- [ ] At width < 900 DIP render classic icons and a flyout for the group children.
- [ ] Recursively select Haru child items and expand parents when navigating from a page button.
- [ ] Preserve editor selection and expansion across shell rebuilds.

### Task 3: Native verification

Files: create MainWindow.NavigationVerification.cs; add Initialize's --navigation-test entry.

- [ ] Verify actual child rendering, expansion, sources/jobs selection and editor highlight in both shells.
- [ ] Verify a collapsed group reopens on programmatic child navigation and compact flyout contains both routes.
- [ ] Save native screenshots and navigation-result.json in RESUMESTUDIO_TEST_OUTPUT.
- [ ] Build and run the isolated test after the parent assembles the portable app.

Integration: parent adds --navigation-test to BackendClient test-data isolation and Updates.VerificationSession. Parent controls full build and packaging. No independent commit.
