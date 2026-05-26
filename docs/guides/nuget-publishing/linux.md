 # Linux publishing guide

 Use this guide when you are publishing from Linux with a POSIX shell such as bash.

 ## 1) Prepare the environment

 Confirm the SDK and create an API key environment variable for the current shell:

 ```bash
 dotnet --version
 export NUGET_API_KEY="<your-nuget-api-key>"
 ```

 If you do not want the key stored in shell history, export it through your shell profile or use a secret manager.

 ## 2) Update the version

 Open `Directory.Build.props` and update `VersionPrefix` to the next release version.
 All publishable projects inherit the same version from this file.

 ## 3) Restore and build the release assets

 From the repository root:

 ```bash
 dotnet restore ./MinhKakart.sln
 dotnet build ./MinhKakart.sln -c Release
 ```

 ## 4) Pack the publishable projects

 Create a local package output folder and pack each project explicitly:

 ```bash
 mkdir -p ./artifacts/packages
 dotnet pack ./DiDecoration/DiDecoration.csproj -c Release -o ./artifacts/packages/DiDecoration
 dotnet pack ./DiDecoration.Analyzers/DiDecoration.Analyzers.csproj -c Release -o ./artifacts/packages/DiDecoration.Analyzers
 dotnet pack ./DiDecoration.Generators/DiDecoration.Generators.csproj -c Release -o ./artifacts/packages/DiDecoration.Generators
 ```

 ## 5) Inspect the generated packages

 Check that the expected `.nupkg` files were created:

 ```bash
 find ./artifacts/packages -name '*.nupkg' -print
 ```

 Optionally inspect a package archive before publishing:

 ```bash
 unzip -l "$(find ./artifacts/packages/DiDecoration -name '*.nupkg' | head -n 1)"
 ```

 For analyzer and generator packages, confirm that the package contains analyzer assets under `analyzers/dotnet/cs`.

 ## 6) Push to NuGet

 Publish each package file to the NuGet v3 endpoint:

 ```bash
 dotnet nuget push ./artifacts/packages/DiDecoration/*.nupkg --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY" --skip-duplicate
 dotnet nuget push ./artifacts/packages/DiDecoration.Analyzers/*.nupkg --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY" --skip-duplicate
 dotnet nuget push ./artifacts/packages/DiDecoration.Generators/*.nupkg --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY" --skip-duplicate
 ```

 If you are publishing to another feed, replace the `--source` URL and use that feed's API key.

 ## 7) Verify the release

 After the push completes, verify that:

 - the package version matches `VersionPrefix`
 - the package page exists on the feed
 - the package metadata is correct
 - the analyzer and generator packages show the expected package type and assets

 A quick local verification is to restore the packages into the sample app or a clean test project and confirm restore succeeds.

 ## Suggested release checklist

 - [ ] Update `VersionPrefix`
 - [ ] Restore and build in Release mode
 - [ ] Pack all three publishable projects
 - [ ] Inspect the generated `.nupkg` files
 - [ ] Push to NuGet
 - [ ] Verify the packages on the feed

