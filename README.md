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
- Checks for launcher releases and can securely replace/restart the packaged launcher in place.
- Maintains an isolated game directory and downloads the required Minecraft, Fabric, libraries, assets, natives, and Java runtime.
- Launches MatHax directly with configurable memory.
- Can optionally install ViaFabricPlus from Modrinth for automatic multiplayer protocol translation across modern, release, Beta, Alpha, and Classic servers.
- Includes a repair action that revalidates Minecraft, Fabric, MatHax, enabled protocol support, libraries, assets, natives, and the Java runtime.
- Includes a curated mods panel for compatible Sodium, Lithium, and Iris builds; Iris installs its required Sodium dependency automatically.
- Can instead create a dedicated **MatHax Reborn** Fabric profile in the official Minecraft Launcher.
- Keeps MatHax saves, configs, and mods separate from the default `.minecraft` game directory in both modes.

The launcher does not provide offline/cracked authentication. A Microsoft account that owns Minecraft: Java Edition is required for direct launch.

## Minecraft version support

MatHax Reborn itself runs on its native Minecraft 26.2 build. Enable **Wide server support** to have the launcher download a compatible ViaFabricPlus release, verify its SHA-512 hash, and install it beside MatHax. ViaFabricPlus can then auto-detect and translate multiplayer server protocols from Classic, Alpha, and Beta through current releases.

This mode changes multiplayer network compatibility; it does not turn MatHax into an older singleplayer client. True native support for another Minecraft version requires a separately compiled and tested MatHax release for that version.

## Updates, repair, and mods

- When a newer launcher release exists, an **Update** control appears in the title bar. The ZIP is checked against GitHub's SHA-256 release digest before an external updater replaces and restarts the launcher.
- **Repair** asks the same installation pipeline used for launch to verify and restore the complete isolated game runtime.
- **Mods** manages a deliberately small compatibility catalog for the direct-launch instance. It requests only stable Fabric releases tagged for Minecraft 26.2 and verifies every file against Modrinth's SHA-512 metadata.

## Official launcher installation

Select **Install to official launcher**. The launcher will:

1. Download the current MatHax release.
2. Install the Fabric version descriptor into `.minecraft/versions`.
3. Install ViaFabricPlus too when **Wide server support** is enabled.
4. Create or update the `MatHax Reborn` entry in `launcher_profiles.json`.
5. Install the client into `.minecraft/mathax-reborn/mods`.
6. Preserve a backup at `launcher_profiles.json.mathax.bak`.

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
- Optional ViaFabricPlus builds come from the verified ViaFabricPlus project on Modrinth and are checked against Modrinth's SHA-512 digest before installation.

## Licensing

Launcher source is MIT-licensed. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for CmlLib attribution and the Minecraft/Microsoft disclaimer.
