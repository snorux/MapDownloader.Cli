var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////
Task("Default")
    .IsDependentOn("Build")
    .Does(() =>
    {
        DotNetCoreTest("./MapDownloader.Cli.sln", new DotNetCoreTestSettings
        {
            Configuration = configuration,
            NoBuild = true
        });
    });

Task("Clean")
    .WithCriteria(ctx => HasArgument("rebuild"))
    .Does(() =>
    {
        CleanDirectory($"./src/bin/{configuration}");
    });

Task("Build")
    .IsDependentOn("Clean")
    .Does(ctx =>
    {
        DotNetCoreBuild("./MapDownloader.Cli.sln", new DotNetCoreBuildSettings
        {
            Configuration = configuration,
            MSBuildSettings = new DotNetCoreMSBuildSettings()
                .TreatAllWarningsAs(MSBuildTreatAllWarningsAs.Default)
        });
    });

Task("Package")
    .IsDependentOn("Build")
    .Does(ctx =>
    {
        ctx.CleanDirectory("./.artifacts");
        ctx.DotNetCorePack("./MapDownloader.Cli.sln", new DotNetCorePackSettings
        {
            Configuration = configuration,
            NoRestore = true,
            NoBuild = true,
            OutputDirectory = "./.artifacts",
            MSBuildSettings = new DotNetCoreMSBuildSettings()
                .TreatAllWarningsAs(MSBuildTreatAllWarningsAs.Default)
        });
    });

Task("NuGet-Publish")
    .WithCriteria(c => BuildSystem.IsRunningOnGitHubActions, "Must be running on GitHub Actions")
    .IsDependentOn("Package")
    .Does(ctx => 
    {
        var apiKey = Argument<string>("nuget-key", null);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new CakeException("No NuGet API key provided");

        foreach (var file in ctx.GetFiles("./.artifacts/*.nupkg"))
        {
            ctx.Information("Publishing {0}...", file.GetFilename().FullPath);
            DotNetCoreNuGetPush(file.FullPath, new DotNetCoreNuGetPushSettings
            {
                Source = "https://api.nuget.org/v3/index.json",
                ApiKey = apiKey
            });
        }
    });

//////////////////////////////////////////////////////////////////////
// Targets
//////////////////////////////////////////////////////////////////////  
Task("Publish")
    .IsDependentOn("NuGet-Publish");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////   
RunTarget(target);