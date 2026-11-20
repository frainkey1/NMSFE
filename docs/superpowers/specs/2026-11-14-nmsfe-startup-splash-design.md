# NMSFE Startup Splash Design

## Intent
Show the approved NMSFE splash artwork while the application loads its databases, then open the existing save editor.

## User-facing behavior
- Center a borderless splash window with transparent pixels outside the rounded artwork.
- The splash artwork carries the exact title “NMSFE (No Man's Sky File Editor)” and “Loading Databases”.
- Load the initial SaveSession database work off the UI thread; show MainWindow only after it completes.
- If initialization fails, close the splash and show a readable error.

## Scope and constraints
- WPF/.NET 10, no new packages.
- Do not change the approved artwork, existing editor layout, save discovery, or save parsing behavior.
- Keep the startup session available to MainWindow.
- Loading a save continues through the current LoadPath flow.

## Implementation choice
Use App.OnStartup instead of StartupUri: show a dedicated SplashWindow, initialize the initial SaveSession asynchronously, then open MainWindow with that session. This makes the displayed loading state correspond to actual database initialization and keeps the existing editor window structure.
