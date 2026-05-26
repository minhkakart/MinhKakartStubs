 # Windows publishing guide

 Use this guide when you are publishing from Windows with PowerShell.

 ## 1) Prepare the environment

 Confirm the SDK and create an API key environment variable for the current session:

 ```powershell
 dotnet --version
 $env:NUGET_API_KEY = "<your-nuget-api-key>"
 ```

 If you prefer to keep the key out of your console history, set it as a user or machine environment variable instead.

 ## 2) Update the version

 Open `Directory.Build.props` and update `VersionPrefix` to the next release version.
 Because the property is centralized, all packable projects inherit the same version.

 ## 3) Restore and build the release assets

 From the repository root:

 ```powershell
 dotnet restore .\MinhKakart.sln
 dotnet build .\MinhKakart.sln -c Release
 ```

 ## 4) Pack the publishable projects

 Create a local package output folder and pack each project explicitly:

 ```powershell
 New-Item -ItemType Directory -Force -Path .\artifacts\packages | Out-Null
 dotnet pack .\DiDecoration\DiDecoration.csproj -c Release -o .\artifacts\packages\DiDecoration
 dotnet pack .\DiDecoration.Analyzers\DiDecoration.Analyzers.csproj -c Release -o .\artifacts\packages\DiDecoration.Analyzers
 dotnet pack .\DiDecoration.Generators\DiDecoration.Generators.csproj -c Release -o .\artifacts\packages\DiDecoration.Generators
 ```

 ## 5) Inspect the generated packages

 Check that the expected `.nupkg` files were created:

 ```powershell
 Get-ChildItem .\artifacts\packages -Recurse -Filter *.nupkg |
     Select-Object FullName, Length
 ```

 Optionally inspect a package archive before publishing:

 ```powershell
 $package = Get-ChildItem .\artifacts\packages\DiDecoration -Filter *.nupkg | Select-Object -First 1
 Expand-Archive -Path $package.FullName -DestinationPath .\artifacts\inspection\DiDecoration -Force
 Get-ChildItem .\artifacts\inspection\DiDecoration -Recurse
 ```

 For analyzer and generator packages, confirm that the package contains analyzer assets under `analyzers\dotnet\cs`.

 ## 6) Push to NuGet

 Publish each package file to the NuGet v3 endpoint:

 ```powershell
 dotnet nuget push .\artifacts\packages\DiDecoration\*.nupkg --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY --skip-duplicate
 dotnet nuget push .\artifacts\packages\DiDecoration.Analyzers\*.nupkg --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY --skip-duplicate
 dotnet nuget push .\artifacts\packages\DiDecoration.Generators\*.nupkg --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY --skip-duplicate
 ```

 If you are publishing to a different feed, replace the `--source` URL and use the API key required by that feed.

 ## 7) Verify the release

 After the push completes, verify that:

 - the package version matches `VersionPrefix`
 - the package page exists on the feed
 - the package metadata is correct
 - the analyzer and generator packages show the expected package type and assets

 A quick local verification is to reinstall the packages into the sample app or a clean test project and confirm restore succeeds.

 ## Suggested release checklist

 - [ ] Update `VersionPrefix`
 - [ ] Restore and build in Release mode
 - [ ] Pack all three publishable projects
 - [ ] Inspect the generated `.nupkg` files
 - [ ] Push to NuGet
 - [ ] Verify the packages on the feed

