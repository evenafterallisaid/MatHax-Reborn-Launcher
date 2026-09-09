<p align="center">
  <img src="Assets/logo.png" width="170" alt="MatHax Reborn logo">
</p>

<h1 align="center">MatHax Reborn Launcher</h1>

<p align="center">A small, purpose-built launcher for the standalone MatHax Reborn client.</p>

<p align="center">
  <a href="https://github.com/evenafterallisaid/MatHax-Reborn-Launcher/releases/latest">Download launcher</a>
  ·
  <a href="https://github.com/evenafterallisaid/MatHax-Reborn">Client source</a>
</p>

## What it does

- Signs in through the real Microsoft/Xbox/Minecraft Java authentication flow.
- Encrypts cached account tokens with Windows Data Protection for the current user.
- Downloads the latest MatHax Reborn JAR from the official GitHub release.
- Maintains an isolated game directory and downloads the required Minecraft, Fabric, libraries, assets, natives, and Java runtime.
- Launches MatHax directly with configurable memory.
- Can instead create a dedicated **MatHax Reborn** Fabric profile in the official Minecraft Launcher.
- Keeps MatHax saves, configs, and mods separate from the default `.minecraft` game directory in both modes.

The launcher does not provide offline/cracked authentication. A Microsoft account that owns Minecraft: Java Edition is required for direct launch.

## Official launcher installation

Select **Install to official launcher**. The launcher will:

1. Download the current MatHax release.
2. Install the Fabric version descriptor into `.minecraft/versions`.
3. Create or update the `MatHax Reborn` entry in `launcher_profiles.json`.
4. Install the client into `.minecraft/mathax-reborn/mods`.
5. Preserve a backup at `launcher_profiles.json.mathax.bak`.

Restart the official launcher if it was open during installation.

## Build

Requires the .NET 8 SDK on Windows:

```powershell
dotnet restore
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true
```

Tagged releases publish a self-contained Windows x64 executable, so end users do not need to install .NET.

## Privacy and storage

- Launcher settings and the isolated instance are stored under `%LOCALAPPDATA%\MatHax Reborn Launcher`.
- Authentication data is encrypted with Windows DPAPI and can only be decrypted by the same Windows user.
- Client updates come only from `evenafterallisaid/MatHax-Reborn` GitHub Releases.

## Licensing

Launcher source is MIT-licensed. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for CmlLib attribution and the Minecraft/Microsoft disclaimer.
