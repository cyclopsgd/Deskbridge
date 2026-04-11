# Domain Pitfalls

**Domain:** WPF RDP Connection Manager (Deskbridge)
**Researched:** 2026-04-11

---

## Critical Pitfalls

Mistakes that cause rewrites, crashes, or data loss.

---

### Pitfall 1: ActiveX Siting Order Violation

**What goes wrong:** Setting any property on `AxMsRdpClient9NotSafeForScripting` before it is sited (added to a container) throws `AxHost.InvalidActiveXStateException`. This is the single most common mistake in every RDP-in-WPF project.

**Why it happens:** The ActiveX control requires a valid window handle (HWND) before it can accept property changes. WPF developers instinctively configure controls before adding them to the visual tree. The WinForms hosting layer requires the opposite order.

**Consequences:** Hard crash with an unintuitive exception. If this is in a code path that retries or catches too broadly, it can cascade into orphaned COM objects.

**Prevention:**
1. Always follow this exact sequence: create `WindowsFormsHost` -> create `AxMsRdpClient` -> set `host.Child = rdp` -> add host to WPF parent -> THEN configure properties.
2. Wrap the siting sequence in a dedicated `RdpHostControl` class so it is only written once.
3. Add a defensive check: assert `rdp.Created` is true before setting any property.

**Detection:** Unit/integration test that instantiates `RdpHostControl`, configures it, and verifies no exception is thrown. Any `InvalidActiveXStateException` in logs during development.

**Phase:** RDP Integration (Phase 5). Must be correct from day one in the `RdpHostControl` wrapper.

---

### Pitfall 2: Incorrect Disposal Order Leaks GDI Handles and Crashes

**What goes wrong:** Disposing the `WindowsFormsHost` before disconnecting the RDP session, or failing to null out `host.Child`, leaks GDI handles. Over 15-20 sessions, the process hits the practical GDI handle ceiling and new connections fail silently or crash. mRemoteNG suffers from exactly this bug.

**Why it happens:** .NET garbage collection does not deterministically release COM objects. Without explicit, ordered disposal, the RDP ActiveX control holds GDI resources indefinitely. On .NET 8+, closing a window containing a `WindowsFormsHost` can also trigger an infinite recursion crash during resource lookup (dotnet/wpf#10171).

**Consequences:** Memory and handle leaks accumulate over a session. After opening and closing ~20-30 tabs, the application becomes unstable. On .NET 8+, improper disposal during window close can hard-crash the process.

**Prevention:**
1. Follow the exact disposal sequence documented in REFERENCE.md:
   ```csharp
   if (rdp.Connected != 0) rdp.Disconnect();
   rdp.Dispose();
   host.Child = null;
   host.Dispose();
   parentPanel.Children.Remove(host);
   ```
2. Wrap this in a `DisposeSession()` method on `RdpHostControl` that is idempotent (safe to call multiple times).
3. Subscribe to the window `Closing` event and explicitly dispose all active hosts BEFORE the window teardown begins.
4. Never call `Marshal.ReleaseComObject` -- let `AxHost.Dispose()` handle COM release.

**Detection:** Monitor GDI handle count in Task Manager during development. Add a periodic health check that logs `Process.GetCurrentProcess().HandleCount`. Warn at 8,000 GDI handles.

**Phase:** RDP Integration (Phase 5) and Tab Management (Phase 6). The disposal logic must be embedded in `RdpHostControl`; the tab manager must call it on tab close.

---

### Pitfall 3: .NET 9/10 Built-in Fluent Theme Breaks WindowsFormsHost Rendering

**What goes wrong:** If the .NET built-in fluent theme is enabled (`ThemeMode = Light` or `ThemeMode = Dark` in App.xaml), all `WindowsFormsHost` controls render as black rectangles with garbage pixels in dark mode, and text becomes invisible (white-on-white) in light mode. This is a confirmed regression in .NET 9 (dotnet/wpf#9635, #10044) that remains unresolved as of .NET 10 preview.

**Why it happens:** The .NET 9+ built-in fluent theme applies a Mica backdrop to windows by modifying `PresentationSource.CompositionTarget.Background` and calling `DwmExtendFrameIntoClientArea`. This interferes with the HWND-based rendering that `WindowsFormsHost` uses. Deskbridge uses WPF-UI (lepoco/wpfui), which is a *separate* library from the built-in fluent theme and does NOT trigger this bug -- but it is dangerously easy to accidentally enable both.

**Consequences:** The entire RDP viewport becomes a black or garbled rectangle. The application is unusable.

**Prevention:**
1. NEVER set `ThemeMode` on the WPF `Application` or `Window` elements. This is the .NET built-in fluent theme toggle. Deskbridge uses WPF-UI's own theming system instead.
2. Explicitly disable the built-in backdrop as a safety net in the `.csproj`:
   ```xml
   <RuntimeHostConfigurationOption Include="Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop" Value="true" />
   ```
3. WPF-UI's `FluentWindow` sets `AllowsTransparency = false` and uses DWM APIs for Mica, which is compatible with `WindowsFormsHost`. Verify this during shell setup.

**Detection:** Visually inspect the RDP viewport after setting up the FluentWindow shell. If the viewport is black or garbled, the built-in theme was accidentally enabled. Automated UI test: capture a screenshot of the viewport area and verify it is not solid black.

**Phase:** WPF Shell (Phase 3). Must be validated immediately when the FluentWindow shell is created, before any RDP integration work begins.

---

### Pitfall 4: CommunityToolkit.Mvvm 8.4.0 Fails to Compile on .NET 10

**What goes wrong:** CommunityToolkit.Mvvm v8.4.0's source generators emit code that requires C# 14 features, but the Roslyn version bundled with the generator does not yet support C# 14. The build fails with `MVVMTK0041`, `CS9248`, and `CS8050` errors on a default .NET 10 project.

**Why it happens:** The toolkit's source generators were compiled against an older Roslyn version. A fix was committed (November 2025, commit `272a974`) but no release containing it has been published yet (as of research date).

**Consequences:** The project will not compile at all with default settings. All `[ObservableProperty]` and `[RelayCommand]` partial property source generation fails.

**Prevention:**
1. Add `<LangVersion>preview</LangVersion>` to `Directory.Build.props` as a workaround.
2. Pin to CommunityToolkit.Mvvm `8.4.2+` if a fixed version is available at scaffold time. Check NuGet before creating the solution.
3. If no fixed version exists, use the `<LangVersion>preview</LangVersion>` workaround and add a TODO comment to remove it when 8.5.0 ships.

**Detection:** Build failure on first `dotnet build`. This will be caught immediately.

**Phase:** Solution Scaffold (Phase 1). Must be resolved before any view model code can be written.

---

### Pitfall 5: Airspace Violation -- WPF Elements Overlapping RDP Viewport

**What goes wrong:** Any WPF element (tooltip, dropdown, notification toast, context menu, dialog, slide-out panel) that overlaps the `WindowsFormsHost` area is invisible or renders underneath the RDP session. The RDP ActiveX control always paints on top because it owns its own HWND.

**Why it happens:** This is the fundamental WPF/Win32 airspace limitation. WPF renders to a DirectX surface; Win32/WinForms controls render to their own HWND. These rendering pipelines cannot interleave -- the HWND content always wins in the overlap region.

**Consequences:** Notification toasts disappear behind the RDP viewport. Context menus are clipped or invisible. Dropdown controls in the status bar are hidden. The application appears broken even though the controls are technically there.

**Prevention:**
1. Design the layout so NO WPF elements can physically overlap the viewport grid cell. The slide-out panel must PUSH the viewport (change column widths), never overlay it.
2. Notification toasts must be positioned in the title bar area, tab bar area, or status bar -- never floating over the viewport.
3. Dialogs (connection editor, command palette) must be modal windows or positioned entirely outside the viewport bounds.
4. Context menus on tabs work fine (they are above the viewport). Context menus inside the viewport are impossible -- do not attempt them.
5. The status bar must be a separate row below the viewport, not overlaid.

**Detection:** Visual inspection during UI development. Any element that "disappears" when the RDP viewport is active is an airspace violation.

**Phase:** WPF Shell (Phase 3) layout design. The grid layout must enforce this from the start. Validated again during RDP Integration (Phase 5).

---

### Pitfall 6: Keyboard Focus Black Hole in WindowsFormsHost

**What goes wrong:** Once an RDP session inside `WindowsFormsHost` receives keyboard focus, WPF keyboard shortcuts (Ctrl+Tab, Ctrl+W, Ctrl+Shift+P) stop working. The RDP control consumes all keyboard input because it is a full Win32 HWND. Conversely, after interacting with WPF ribbon/menu controls, clicking back into the RDP session may not restore keyboard focus to the RDP control.

**Why it happens:** WPF and WinForms use different focus management systems. `WindowsFormsHost` implements `IKeyboardInputSink` to bridge them, but the bridge is imperfect. The RDP ActiveX control is particularly aggressive about capturing input because it needs to forward keystrokes to the remote session.

**Consequences:** Users cannot use keyboard shortcuts to switch tabs, open the command palette, or close connections while an RDP session is focused. This defeats the keyboard-first design goal.

**Prevention:**
1. Register global hotkeys at the window level using `InputManager.Current.PreProcessInput` or `HwndSource.AddHook` to intercept key combinations BEFORE they reach the `WindowsFormsHost`. Ctrl+Tab, Ctrl+W, Ctrl+Shift+P, Ctrl+L, and F11 must be caught at this level.
2. For the Escape key (exit fullscreen), use a low-level keyboard hook since the RDP control will consume Escape for its own purposes.
3. Test focus transitions: WPF control -> RDP viewport -> WPF control. All three transitions must work cleanly.
4. Do NOT set `HwndSource.DefaultAcquireHwndFocusInMenuMode = false` -- it breaks focus recovery from WinForms controls.

**Detection:** Manual testing: open an RDP session, try every keyboard shortcut. Automated test: simulate key input and verify the expected command handler fires.

**Phase:** Tab Management (Phase 6) and Keyboard Shortcuts (Phase 7). Must be designed into the input architecture, not retrofitted.

---

## Moderate Pitfalls

---

### Pitfall 7: DPI Scaling Mismatch Between WPF and WindowsFormsHost

**What goes wrong:** On multi-monitor setups with different DPI scaling (e.g., 150% laptop + 100% external monitor), the RDP control inside `WindowsFormsHost` does not scale correctly when the window moves between monitors. The control may appear too small, too large, or blurry.

**Why it happens:** `WindowsFormsHost` has a documented bug (dotnet/wpf#6294) where the physical size of hosted WinForms controls does not change on DPI transitions, even though the logical size of the `WindowsFormsHost` adjusts correctly. This is worse in .NET 6+ compared to .NET Framework 4.8.

**Prevention:**
1. Declare `PerMonitorV2` DPI awareness in the app manifest (already in REFERENCE.md).
2. Subscribe to `DpiChanged` on the `WindowsFormsHost` control and manually resize the hosted RDP control.
3. After a DPI change, call `UpdateSessionDisplaySettings` on the RDP control to re-negotiate the remote session resolution.
4. Test on a multi-monitor setup with different DPI values during development.

**Detection:** Move the window between monitors with different DPI settings. The RDP viewport should resize correctly without blurriness or clipping.

**Phase:** RDP Integration (Phase 5). DPI handling should be built into `RdpHostControl`.

---

### Pitfall 8: Window Resize Flicker With WindowChrome

**What goes wrong:** During window drag or resize, the RDP ActiveX control flickers violently because the WPF rendering pipeline and the Win32 HWND repaint at different rates. This is a confirmed framework limitation (dotnet/wpf#5892) with no fix planned.

**Why it happens:** WPF uses hardware-accelerated DirectX rendering; WinForms/ActiveX uses GDI software rendering. During resize, these two pipelines race, producing visible flicker as the HWND lags behind the WPF layout pass.

**Prevention:**
1. Hook `WM_ENTERSIZEMOVE` and `WM_EXITSIZEMOVE` via `HwndSource.AddHook` on the main window.
2. On `WM_ENTERSIZEMOVE`: capture a `RenderTargetBitmap` of the current RDP viewport, display it as a WPF `Image` overlay, and set `host.Visibility = Collapsed`.
3. On `WM_EXITSIZEMOVE`: remove the bitmap overlay, restore `host.Visibility = Visible`, and call `UpdateSessionDisplaySettings` if SmartSizing is enabled.
4. This produces a smooth, static preview during resize with a single repaint on drop.

**Detection:** Resize the window while an RDP session is connected. Without the mitigation, flicker is immediately obvious.

**Phase:** WPF Shell (Phase 3) for the hook infrastructure, completed during RDP Integration (Phase 5).

---

### Pitfall 9: GetOcx() / IMsTscNonScriptable Cast Failure

**What goes wrong:** Casting `rdp.GetOcx()` to `IMsTscNonScriptable` to set the password throws `InvalidCastException: Unable to cast COM object of type 'System.__ComObject' to interface type 'MSTSCLib.IMsTscNonScriptable'`.

**Why it happens:** The interop assemblies generated by `aximp.exe` must match the version of the RDP client installed on the build/target machine. If the assemblies were generated against a different version of `mstscax.dll`, or if the COM registration is stale, the cast fails because the interface GUIDs do not match.

**Consequences:** Passwords cannot be set programmatically. Connections fail or prompt for credentials every time, defeating the credential management system.

**Prevention:**
1. Regenerate the interop assemblies (`aximp.exe`) on the CI build machine or document the exact `mstscax.dll` version they were built against.
2. Wrap the `GetOcx()` cast in a try-catch with a clear error message: "RDP interop assemblies may be out of date."
3. If the cast to `IMsTscNonScriptable` fails, try `IMsRdpClientNonScriptable` as a fallback (older interface, same `ClearTextPassword` property).
4. Test the interop assemblies on clean Windows 10 and Windows 11 machines.

**Detection:** First connection attempt with saved credentials will fail. Log the exception type and message at ERROR level.

**Phase:** Solution Scaffold (Phase 1) for assembly verification, RDP Integration (Phase 5) for the cast logic.

---

### Pitfall 10: Velopack Custom Main() Breaks WPF Resource Loading

**What goes wrong:** After converting `App.xaml` from `ApplicationDefinition` to `Page` (required for Velopack's custom `Main`), XAML resources defined in `App.xaml` are not loaded. Styles, themes, and DynamicResource references resolve to null, producing an unstyled or crashing application.

**Why it happens:** When `App.xaml` is `ApplicationDefinition`, WPF auto-generates a `Main()` that calls `InitializeComponent()` before `Run()`. When you write your own `Main()`, you must explicitly call `app.InitializeComponent()` to load the XAML resources. Forgetting this call (or calling it after `app.Run()`) leaves all resources unloaded.

**Consequences:** The entire WPF-UI Fluent theme fails to apply. Controls render with default Windows chrome. DynamicResource bindings return null, causing binding errors or crashes.

**Prevention:**
1. Follow the exact Velopack WPF pattern:
   ```csharp
   [STAThread]
   static void Main(string[] args)
   {
       VelopackApp.Build().Run();
       var app = new App();
       app.InitializeComponent();
       app.Run();
   }
   ```
2. Never make `Main` async -- this silently changes the thread to MTA, which crashes ActiveX controls.
3. Verify `[STAThread]` is on the `Main` method, not just a comment.
4. Set `<StartupObject>Deskbridge.Program</StartupObject>` in the csproj to avoid ambiguity about which `Main` to use.

**Detection:** Launch the application. If the window appears with default Windows styling instead of WPF-UI Fluent dark theme, `InitializeComponent()` was not called.

**Phase:** Solution Scaffold (Phase 1) for the entry point, Auto-Update (Phase 9) for Velopack integration.

---

### Pitfall 11: COM Thread Affinity -- ActiveX on Wrong Thread

**What goes wrong:** Creating, configuring, or disposing the RDP ActiveX control on a background thread (or an MTA thread) throws `ActiveXControlCannotBeInstantiated` or produces undefined behavior. The COM object silently marshals calls to the wrong apartment, leading to deadlocks or crashes.

**Why it happens:** The RDP ActiveX control is an STA COM object. All interactions must occur on the same STA thread (the WPF UI thread). Background tasks, `Task.Run()`, or `ConfigureAwait(false)` can inadvertently move execution to a thread pool thread.

**Prevention:**
1. All `RdpHostControl` creation, configuration, connection, disconnection, and disposal must run on the UI thread via `Dispatcher.Invoke` or `Dispatcher.InvokeAsync`.
2. The connection pipeline stages that touch the RDP control (`CreateHostStage`, `ConnectStage`) must marshal back to the UI thread.
3. Never use `async Main` -- it silently overrides `[STAThread]` and starts the app on an MTA thread.
4. Credential resolution and other non-UI pipeline stages can run on background threads, but the handoff to `IProtocolHost.ConnectAsync()` must dispatch to UI.

**Detection:** Any `InvalidOperationException` mentioning "wrong thread" or "single-threaded apartment" in logs. Deadlocks during connection or disconnection.

**Phase:** Core Services (Phase 2) for pipeline design, RDP Integration (Phase 5) for implementation.

---

### Pitfall 12: JSON Config File Corruption on Crash or Power Loss

**What goes wrong:** If the application crashes or power is lost while writing `connections.json`, the file is left truncated or empty. On next launch, all connections are lost.

**Why it happens:** `File.WriteAllText` is not atomic. It truncates the file before writing. If the process dies mid-write, the file contains partial JSON.

**Prevention:**
1. Use atomic write pattern: write to a temporary file (`connections.json.tmp`), then rename (which is atomic on NTFS) to replace the original.
   ```csharp
   var tempPath = path + ".tmp";
   File.WriteAllText(tempPath, json);
   File.Move(tempPath, path, overwrite: true);
   ```
2. Keep a backup: before writing, copy the current file to `connections.json.bak`. On load, if the primary file is corrupted, fall back to the backup.
3. Validate JSON on load -- if parsing fails, try the backup before showing an error.

**Detection:** Corrupt JSON parse errors on startup. Zero-byte `connections.json` file after a crash.

**Phase:** Connection Management (Phase 4) for the `JsonConnectionStore` implementation.

---

## Minor Pitfalls

---

### Pitfall 13: UseWindowsForms in Directory.Build.props Causes Type Ambiguity

**What goes wrong:** If `<UseWindowsForms>true</UseWindowsForms>` is set in `Directory.Build.props` instead of only in the RDP protocol project, all projects see both `System.Windows.Application` (WPF) and `System.Windows.Forms.Application` (WinForms). The build fails with ambiguous type errors.

**Prevention:** Set `UseWindowsForms` ONLY in `Deskbridge.Protocols.Rdp.csproj`. Never in `Directory.Build.props`.

**Phase:** Solution Scaffold (Phase 1).

---

### Pitfall 14: AdysTech.CredentialManager Edge Cases

**What goes wrong:** Several documented edge cases:
- Credential blob size was limited to 512 bytes in older versions; version 3.1.0+ supports up to 5*512 bytes (Windows 10+ limit).
- `GetCredentials` can throw `ArgumentOutOfRangeException` on invalid Win32 FileTime values from corrupted or unusual credential entries.
- Credentials saved via `PromptForCredentials` may be stored as "Windows Credentials" rather than "Generic Credentials," making them invisible to `GetCredentials`.
- The TERMSRV target name has a 337-character limit (`CRED_MAX_DOMAIN_TARGET_NAME_LENGTH`).

**Prevention:**
1. Pin to AdysTech.CredentialManager 3.1.0+ for the correct blob size limit.
2. Wrap all credential operations in try-catch and log failures clearly.
3. Use `TERMSRV/<hostname>` (not `TERMSRV\<hostname>`) format consistently -- both work for credential resolution but mixing them creates duplicate entries.
4. Validate hostname length before constructing the target name.

**Phase:** Connection Management (Phase 4) for the `WindowsCredentialService` wrapper.

---

### Pitfall 15: RDP Disconnect Reason Codes Are Not Human-Readable

**What goes wrong:** The `OnDisconnected` event returns a numeric reason code (e.g., 2, 263, 2308, 1798). Displaying these raw codes to users is useless. Many codes have no additional information (code 0). Some codes that look like errors are actually normal (code 263 = "no error").

**Prevention:**
1. Build a disconnect reason code lookup table mapping numeric codes to human-readable messages.
2. Use `IMsRdpClient5.GetErrorDescription(disconnectCode)` for extended error descriptions.
3. Differentiate user-initiated disconnects (code 1, 2, 11, 12) from errors (2308 = socket closed, 1798 = certificate error, etc.).
4. For code 0 and code 263, show "Disconnected" with no error indicator.
5. Check `ExtendedDisconnectReason` for additional context when the primary code is uninformative.

**Phase:** RDP Integration (Phase 5) for the disconnect handler.

---

### Pitfall 16: WeakReferenceMessenger Handlers Run on Publisher's Thread

**What goes wrong:** `WeakReferenceMessenger.Send()` invokes handlers synchronously on the calling thread. If a background pipeline stage publishes an event, the handler (which may update UI) runs on the background thread, causing a cross-thread access exception.

**Prevention:**
1. The `EventBus` wrapper should dispatch handler invocation to the UI thread via `Application.Current.Dispatcher.InvokeAsync()` for all events that may be published from non-UI threads.
2. Alternatively, ensure all event publishing happens on the UI thread by marshaling in the pipeline before publishing.
3. Document in the `IEventBus` contract whether handlers are guaranteed to run on the UI thread.

**Phase:** Core Services (Phase 2) for the `EventBus` implementation.

---

### Pitfall 17: BitmapPersistence Typo in Microsoft Documentation

**What goes wrong:** The RDP ActiveX property is named `BitmapPeristence` (note the missing 's' -- "Peristence" not "Persistence"). This is a genuine typo in the Microsoft API. Using the correctly-spelled name produces a compile error.

**Prevention:** Use `rdp.AdvancedSettings9.BitmapPeristence = 0;` (the misspelled version). Add a comment explaining the typo so future developers do not "fix" it.

**Phase:** RDP Integration (Phase 5).

---

### Pitfall 18: Velopack Update Replaces App Directory -- User Data Must Be External

**What goes wrong:** Velopack replaces the entire `current` application directory during updates. Any settings, logs, or data files stored alongside the executable are destroyed.

**Prevention:**
1. Store ALL user data in `%AppData%/Deskbridge/` (already specified in REFERENCE.md): `connections.json`, `settings.json`, `auth.json`, `audit.jsonl`, `logs/`.
2. Never read from or write to `AppDomain.CurrentDomain.BaseDirectory` for user data.
3. Use `Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)` to resolve the path.
4. Configure Serilog's file sink to write to `%AppData%/Deskbridge/logs/`, not to the application directory.

**Phase:** Solution Scaffold (Phase 1) for path constants, every subsequent phase for adherence.

---

## Phase-Specific Warnings

| Phase | Likely Pitfall | Mitigation |
|-------|---------------|------------|
| Phase 1: Scaffold | CommunityToolkit.Mvvm 8.4.0 build failure on .NET 10 (#4) | Add `LangVersion=preview` workaround, check for 8.4.2+ |
| Phase 1: Scaffold | UseWindowsForms in wrong location (#13) | Set only in Protocols.Rdp csproj |
| Phase 1: Scaffold | Velopack entry point missing InitializeComponent (#10) | Follow exact Program.cs pattern from REFERENCE.md |
| Phase 1: Scaffold | Interop assembly version mismatch (#9) | Verify aximp DLLs match target Windows version |
| Phase 2: Core Services | Event bus cross-thread handler invocation (#16) | Dispatch to UI thread in EventBus wrapper |
| Phase 2: Core Services | Pipeline stages running on wrong thread (#11) | Design thread marshaling into pipeline |
| Phase 3: WPF Shell | Accidentally enabling .NET built-in fluent theme (#3) | Disable backdrop switch, use only WPF-UI theming |
| Phase 3: WPF Shell | Airspace layout violations (#5) | Grid layout must never overlap viewport cell |
| Phase 3: WPF Shell | WindowChrome resize flicker (#8) | Add WM_ENTERSIZEMOVE hook infrastructure |
| Phase 4: Connections | JSON file corruption on crash (#12) | Atomic write pattern with backup |
| Phase 4: Connections | Credential edge cases (#14) | Try-catch all credential operations, validate target names |
| Phase 5: RDP | Siting order violation (#1) | Follow exact siting sequence in RdpHostControl |
| Phase 5: RDP | Disposal order leak (#2) | Idempotent DisposeSession() with correct ordering |
| Phase 5: RDP | GetOcx() cast failure (#9) | Wrap in try-catch, log clearly, test on clean machines |
| Phase 5: RDP | DPI scaling mismatch (#7) | Handle DpiChanged event, resize hosted control |
| Phase 5: RDP | BitmapPeristence typo (#17) | Use misspelled API name, add comment |
| Phase 5: RDP | Disconnect reason codes (#15) | Build human-readable lookup table |
| Phase 6: Tabs | GDI handle exhaustion from leaked sessions (#2) | Monitor handle count, warn at 15 sessions |
| Phase 6: Tabs | Keyboard focus trapped in RDP control (#6) | Pre-process input at window level |
| Phase 7: Shortcuts | Focus not returning from WindowsFormsHost (#6) | Low-level keyboard hook for Escape |
| Phase 9: Auto-Update | App data destroyed by update (#18) | All user data in %AppData% |

---

## Sources

- [dotnet/wpf#10044 - WindowsFormsHost rendering broken with fluent themes](https://github.com/dotnet/wpf/issues/10044)
- [dotnet/wpf#9635 - WindowsFormsHost ForeColor issue with fluent theme](https://github.com/dotnet/wpf/issues/9635)
- [dotnet/wpf#10171 - Infinite recursion crash with WindowsFormsHost on .NET 8](https://github.com/dotnet/wpf/issues/10171)
- [dotnet/wpf#6294 - WindowsFormsHost DPI scaling](https://github.com/dotnet/wpf/issues/6294)
- [dotnet/wpf#5892 - WindowChrome flicker with WindowsFormsHost](https://github.com/dotnet/wpf/issues/5892)
- [dotnet/wpf#152 - WPF and Win32 airspace issue](https://github.com/dotnet/wpf/issues/152)
- [dotnet/wpf discussions#10387 - Fluent Theme in .NET 10 Plan](https://github.com/dotnet/wpf/discussions/10387)
- [CommunityToolkit/dotnet#1139 - MVVM 8.4.0 fails on .NET 10](https://github.com/CommunityToolkit/dotnet/issues/1139)
- [lepoco/wpfui FluentWindow source - AllowsTransparency = false](https://github.com/lepoco/wpfui/blob/main/src/Wpf.Ui/Controls/FluentWindow/FluentWindow.cs)
- [DeepWiki - WPF-UI Window Backdrop implementation](https://deepwiki.com/lepoco/wpfui/5.3-window-backdrop)
- [Velopack - Preserving Files & Settings](https://docs.velopack.io/integrating/preserved-files)
- [Microsoft Learn - WPF and Windows Forms interop input architecture](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/windows-forms-and-wpf-interoperability-input-architecture)
- [Microsoft Learn - IMsTscAxEvents OnDisconnected](https://learn.microsoft.com/en-us/windows/win32/termserv/imstscaxevents-ondisconnected)
- [Microsoft Learn - Troubleshooting Hybrid Applications (WPF)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/troubleshooting-hybrid-applications)
- [AdysTech/CredentialManager GitHub - known issues](https://github.com/AdysTech/CredentialManager)
- [Microsoft Learn - GDI Objects](https://learn.microsoft.com/en-us/windows/win32/sysinfo/gdi-objects)
- [Old New Thing - GDI handle limit consequences](https://devblogs.microsoft.com/oldnewthing/20210831-00/?p=105624)
- [mRemoteNG GitHub - crashing with many sessions](https://github.com/mRemoteNG/mRemoteNG/issues/853)
- [mRemoteNG GitHub - crash on multiple RDP sessions](https://github.com/mRemoteNG/mRemoteNG/issues/1198)
