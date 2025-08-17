# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

MTGA Tracker Daemon is a C# .NET 6.0 HTTP server that extracts game data from Magic: The Gathering Arena (MTGA) using memory inspection via the HackF5.UnitySpy library. The daemon provides a REST API to access player data including cards, inventory, match state, and player information.

## Build and Development Commands

```bash
# Build the project
dotnet build src/mtga-tracker-daemon/mtga-tracker-daemon.csproj

# Run the daemon (default port 6842)
dotnet run --project src/mtga-tracker-daemon/mtga-tracker-daemon.csproj

# Run with custom port
dotnet run --project src/mtga-tracker-daemon/mtga-tracker-daemon.csproj -- -p 9000

# Get current version from project file
node getVersion.js
```

## Architecture

### Core Components

- **Program.cs**: Entry point handling command-line arguments and starting the HTTP server
- **HttpServer.cs**: Main server logic with REST API endpoints and MTGA process memory inspection
- **HackF5.UnitySpy**: External library for Unity process memory reading across platforms (Windows, Linux, macOS)

### API Endpoints

The daemon exposes these REST endpoints:
- `GET /status` - MTGA process status and daemon version
- `GET /cards` - Player's card collection with ownership counts
- `GET /playerId` - Player account information
- `GET /inventory` - Player's gems and gold
- `GET /events` - Available events
- `GET /matchState` - Current match information and rankings
- `POST /shutdown` - Gracefully stop the daemon
- `POST /checkForUpdates` - Check for daemon updates

### Memory Inspection

The daemon uses UnitySpy to read MTGA's Unity engine memory directly:
- Creates `AssemblyImage` from the MTGA process "Core" assembly
- Navigates object hierarchies using reflection-like syntax (e.g., `assemblyImage["WrapperController"]["<Instance>k__BackingField"]`)
- Extracts game state data from Unity objects in memory

### Platform Support

- **Windows**: Uses `ProcessFacadeWindows` to access MTGA.exe process
- **Linux**: Uses `ProcessFacadeLinuxDirect` with `/proc/{pid}/mem` access for MTGA.exe under Wine
- **macOS**: Uses `ProcessFacadeMacOSDirect` for native Unity processes

### Project Structure

- `src/mtga-tracker-daemon/` - Main daemon application
- `src/HackF5.UnitySpy/` - Unity process memory inspection library
- `deploy/linux/` - Linux deployment scripts and systemd service files

## Development Notes

- The project targets .NET 6.0 and uses Newtonsoft.Json for JSON serialization
- Version is managed in the `.csproj` file `<AssemblyVersion>` tag
- Cross-platform compatibility is handled through `RuntimeInformation.IsOSPlatform()` checks
- The daemon includes auto-update functionality that downloads releases from GitHub
- All API responses include CORS headers for web client access