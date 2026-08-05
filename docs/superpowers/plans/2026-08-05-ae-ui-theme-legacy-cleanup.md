# AE UI Phase 7: Theme completion and legacy cleanup

**Goal:** 收口十个可选主题、公共窗口和最后一批遗留样式，使迁移台账没有 `Unreviewed`，并为发布前整体验收提供新鲜的实际 WPF 证据。

**Scope:** 只处理迁移台账剩余的主题字典、`ControlColours.xaml`、`Controls.xaml`、`AssetEditorWindow`、可取消单选按钮、网格分隔条，以及全仓残留的无效字体资源引用。Phase 8 长期规范仍须等待用户完成最终产品验收。

## Success criteria

1. 十个 `ThemeType` 均能加载完整且同构的颜色资源，并能实例化公共设计系统。
2. 所有用户界面只引用有效的 `AppFontFamily` 与 `AppFontWeight`，主题切换及自定义字体取消恢复仍有效。
3. `OptionalRadioButtonStyle` 使用语义颜色、矢量圆点和可见焦点；可选单选行为不变。
4. 水平和垂直 GridSplitter 使用居中的矢量抓手，不依赖 Unicode 字体字符。
5. 仅删除经源码、项目项、反射/资源加载扫描和回归测试共同证明无消费者的重复资源。
6. 迁移台账无 `Unreviewed`；四个必测主题完成 100%、125%、150% 视觉矩阵，另外六个可选主题完成直接主题画廊验证。
7. 受影响测试、完整 Release 构建、完整测试、`git diff --check` 均通过，Windows 缩放恢复到用户原值 150%。

## Execution

### 1. Establish failing contracts

- Add `UiThemeCompletionTests.cs` for ten-theme parity, valid font keys, legacy scan, optional radio behavior/style, splitter vector templates, `AssetEditorWindow`, and zero-unreviewed ledger state.
- Update existing splitter tests from Unicode glyph assumptions to vector and semantic-resource assertions.
- Verify the new tests fail against the current legacy implementation before editing production files.

### 2. Apply the minimal migration

- Replace invalid `AeFont.Family` / `AeFont.Weight.Normal` references with `AppFontFamily` / `AppFontWeight` in their existing root controls only.
- Rewrite the keyed optional-radio and splitter resource dictionaries without changing keys or consumers.
- Remove `Shared/EmbeddedResources/Resources/OptionalRadioButtonStyle.xaml` and its `Page` item only if the no-consumer test and complete search both prove it is dead.
- Keep all ten color dictionaries, `ControlColours.xaml`, and `Controls.xaml` because they are active compatibility resources loaded by `ThemesController`.

### 3. Close the coverage ledger

- Mark all ten palettes, both compatibility dictionaries, both active code controls, and both shared styles with direct consumer/test evidence.
- Record the removed duplicate as not user-visible with deletion evidence, then ensure the source inventory and ledger remain consistent.

### 4. Visual verification

- Add `UiThemeCompletionGallery.cs` to render palette, optional-radio, splitter, window/font, and representative dense-content states.
- Capture the four required themes at 100%, 125%, and 150% Windows scaling.
- Capture the six additional selectable themes at 150%, assemble contact sheets, and inspect every rendered state for clipping, contrast, focus, icon centering, font propagation, and theme leakage.
- Restore 150% system scaling after each validation path and at the final gate.

### 5. Final gates and checkpoint

- Run focused tests, `dotnet restore`, full Release build, full Release tests, migration coverage, legacy scans, and `git diff --check`.
- Create one local Phase 7 commit after all evidence passes.
- Launch the Release application for the user's final pre-release test and stop. Do not write Phase 8 or publish until the user explicitly confirms the actual app has no issues.
