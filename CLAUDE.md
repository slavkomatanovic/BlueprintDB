# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

All commands run from `Blueprint/` (where `Blueprint.sln` lives):

```bash
dotnet build                        # Debug build
dotnet build -c Release             # Release build
dotnet run                          # Run the app
dotnet clean                        # Clean artifacts
```

There are no automated tests or linters configured.

## Project Overview

Blueprint is a **database metadata management tool** — it stores and manages structural definitions (Programs → Tables → Columns → Relations) for target databases. The C# WPF app is a reimplementation of an older VBA/MS Access version of the same tool.

- **Stack:** .NET 8.0 WPF, Entity Framework Core 8, SQLite, Dapper
- **Database:** `C:\mysoftware\blueprint\database\BlueprintMetadata.sqlite` (hardcoded in `BlueprintDbContext.cs`)
- **Assembly name:** `BlueprintApp`, root namespace: `Blueprint`

## Architecture

The app has three layers with no separate ViewModel classes — logic lives in code-behind.

### Presentation (XAML windows + code-behind)
- `MainWindow` — navigation hub with menu and buttons launching child windows
- `ProgramiWindow`, `TabeleWindow`, `KoloneWindow` — CRUD windows for the metadata hierarchy
- Each window follows the same UI pattern: DataGrid (read-only list) + GroupBox form + action buttons (New / Save / Delete / Close)
- `MyMsgBox.cs` — custom message box that uses `LanguageService` so dialogs are localized

### Services
- **`LanguageService`** — translates all UI controls at runtime by reading from the `rjecnik` (dictionary) table. Controls opt in via their `Tag` property (set to a translation key). DataGrid column headers use `[KEY]` notation in the Header property.
- **`DbSeeder`** — runs at startup (`App.xaml.cs`) to create the SQLite database and seed the default English language and its translation dictionary if the DB doesn't exist yet.

### Data (EF Core)
- **`Models/BlueprintDbContext.cs`** — single EF Core context with 17 `DbSet<T>` entities
- Models are in `Models/` and map directly to SQLite tables
- Soft deletes use a `Skriven` (hidden) boolean field; audit fields are `Korisnik`, `Datumupisa`, `Vremenskipecat`

## Key Conventions

- **Code and comments** mix Serbian/Montenegrin and English — preserve this style, do not translate existing identifiers or comments.
- **Naming:** legacy VBA naming conventions are preserved conceptually (forms would be `frm*`, modules `bas*`, queries `qry*`) but in C# these are just class names — don't try to apply WPF naming conventions over them.
- **Passwords/secrets:** never store credentials in code; use encryption helpers if needed.
- The connection string is currently hardcoded in `BlueprintDbContext.cs` — this is a known limitation, not something to "fix" speculatively.

## Features Status

Implemented: Programs, Tables, Columns CRUD + multilingual UI (English seeded by default).

Not yet implemented (UI buttons exist but are placeholders): Relations, Surplus, INI Parameters, Table Rename, Database Transfer, Path/Start configuration.
