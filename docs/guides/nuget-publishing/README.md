 # NuGet publishing guide

 This guide walks through manual local publishing from zero to a finished NuGet release.
 It is organized into a shared overview plus separate platform-specific sub-guides for Windows and Linux.

 ## Scope

 The solution currently ships three packable NuGet packages:

 - `DiDecoration` — runtime registration helpers
 - `DiDecoration.Analyzers` — compile-time diagnostics
 - `DiDecoration.Generators` — source generator package

 The sample app `DiDecoration.Sample` is for local testing only and is not meant to be published.

 ## What you need before you start

 - .NET SDK 8.0 or newer
 - Access to a NuGet account and publishing API key
 - A clean working tree
 - A release version ready to set in `Directory.Build.props`

 ## Versioning

 All packable projects inherit the shared version from `Directory.Build.props`.
 Before publishing, update:

 - `VersionPrefix` for the next release number

 If you want the package version to include additional metadata, set `VersionSuffix` locally or in your release branch.

 ## Publish flow

 1. Prepare the release version.
 2. Restore dependencies.
 3. Pack each publishable project.
 4. Inspect the generated `.nupkg` files.
 5. Push the packages to your NuGet feed.
 6. Verify the packages on the feed.

 ## Platform-specific guides

 - [Windows](windows.md)
 - [Linux](linux.md)

 ## Notes for this repository

 - The shared README is packed through `Directory.Build.targets`.
 - The analyzer and generator projects are marked as analyzer packages and exclude build output from the published payload.
 - Use the release notes under `docs/releases/` when you publish a new version.

