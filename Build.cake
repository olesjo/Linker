//
#tool nuget:?package=GitVersion.CommandLine&Version=4.0.0-beta0012
#tool nuget:?package=OctopusTools&version=6.7.0

#addin nuget:?package=Cake.Curl&version=4.1.0
#addin nuget:?package=Cake.Npm&Version=0.17.0

#load build\paths.cake
#load build\version.cake
#load build\package.cake
#load build\urls.cake

const string compileTaskName = "Compile";
const string testTaskName = "Test";
const string versionTaskName = "Version";
const string buildFrontEndTaskName = "Build-frontend";
const string packageZipTaskName = "Package-zip";
const string octoPackTaskName = "package-octo";
const string kuduDeployTaskName = "deploy_kudu";
const string setBuildNumberTaskName = "set-build-number";
const string buildCITaskName = "build-CI";
const string publishBuildArtifactTaskName = "publish-build-artifact";

const string packageName = "Linker-1";

var target = Argument("target", compileTaskName);

Setup<PackageMetadata>(context =>
{
    return new PackageMetadata(
        outputDirectory:Argument("packageOutputDirectory", "packages"),
        name:packageName);
    
});

Task(compileTaskName)
    .Does(() =>
{
    DotNetCoreBuild(Paths.SolutionFile.FullPath);   
});


Task(testTaskName)
    .IsDependentOn(compileTaskName)
    .Does(() =>
{
    DotNetCoreTest(Paths.SolutionFile.FullPath,
    new DotNetCoreTestSettings
    {
        Logger = "trx",
        ResultsDirectory = Paths.TestResultsDirectory
    });
});


Task(versionTaskName)
    .Does<PackageMetadata>(package =>
{
    package.Version = ReadVersionFromProjectFile(Context);

    if(string.IsNullOrEmpty(package.Version)) 
    {
        package.Version = GitVersion().FullSemVer;     
    }

    Information($"Calculated version number: {package.Version}");
});


Task(buildFrontEndTaskName)
    .Does(() =>
{
    NpmInstall(settings=> settings.FromPath(Paths.FrondEndDirectory));
    NpmRunScript("build", settings=> settings.FromPath(Paths.FrondEndDirectory));
});

Task(packageZipTaskName)
    .IsDependentOn(testTaskName)
    .IsDependentOn(buildFrontEndTaskName)
    .IsDependentOn(versionTaskName)
    .Does<PackageMetadata>(package=>
{
    CleanDirectory(package.OutputDirectory);

    package.Extension = "zip";

    DotNetCorePublish(
        Paths.WebProjectFile.GetDirectory().FullPath,
        new DotNetCorePublishSettings
        {
            OutputDirectory = Paths.PublishDirectory,
            NoBuild = true,
            NoRestore = true,
            MSBuildSettings = new DotNetCoreMSBuildSettings
            {
                NoLogo = true
            }
        });
    Zip(Paths.PublishDirectory, package.FullPath);

});


Task(octoPackTaskName)
    .IsDependentOn(testTaskName)
    .IsDependentOn(buildFrontEndTaskName)
    .IsDependentOn(versionTaskName)
    .Does<PackageMetadata>(package=>
{
        CleanDirectory(package.OutputDirectory);

    package.Extension = "nupkg";

    DotNetCorePublish(
        Paths.WebProjectFile.GetDirectory().FullPath,
        new DotNetCorePublishSettings
        {
            OutputDirectory = Paths.PublishDirectory,
            NoBuild = true,
            NoRestore = true,
            MSBuildSettings = new DotNetCoreMSBuildSettings
            {
                NoLogo = true
            }
        });
    
        OctoPack(package.Name, 
             new OctopusPackSettings
             {
                Format = OctopusPackFormat.NuPkg,
                Version = package.Version,
                BasePath = Paths.PublishDirectory,
                OutFolder = package.OutputDirectory
             });
    
});


Task(kuduDeployTaskName)
    .Description("Deploys to Kudu")
    .IsDependentOn(packageZipTaskName)
    .Does<PackageMetadata>(package =>
{
    CurlUploadFile(package.FullPath, 
            Urls.KuduDeployUrl,
            new CurlSettings
            {
                Username = EnvironmentVariable("DeploymentUser"),
                Password = EnvironmentVariable("DeploymentPassword"),
                RequestCommand = "POST",
                ProgressBar = true,
                ArgumentCustomization = args=> args.Append("--fail")
            });
    
});


Task("deploy-octo")
    .IsDependentOn(octoPackTaskName)
    .Does<PackageMetadata>(package =>
{
    OctoPush(Urls.OctopusServerUrl.AbsoluteUri,
        EnvironmentVariable("OctopusApiKey"),        
        package.FullPath, new OctopusPushSettings
        {
            EnableServiceMessages = true,
            ReplaceExisting = true //only development
        });

    OctoCreateRelease(
        packageName,
        new CreateReleaseSettings
        {
            Server = Urls.OctopusServerUrl.AbsoluteUri,
            ApiKey = EnvironmentVariable("OctopusApiKey"),
            ReleaseNumber = package.Version,
            DefaultPackageVersion = package.Version,
            DeployTo = "Test",
            IgnoreExisting = true,
            DeploymentProgress = true,
            WaitForDeployment = true
        });
});


Task(setBuildNumberTaskName)
    .WithCriteria(() => BuildSystem.IsRunningOnAzurePipelinesHosted)
    .Does<PackageMetadata>(package =>
{
    var buildNumber = TFBuild.Environment.Build.Number;
    TFBuild.Commands.UpdateBuildNumber($"{package.Version}+{buildNumber}");


});

Task("publish-test-results")
    .WithCriteria(BuildSystem.IsRunningOnAzurePipelinesHosted)
    .IsDependentOn(testTaskName)
    .Does(() =>
{
    TFBuild.Commands.PublishTestResults(
        new TFBuildPublishTestResultsData    
        {
            TestRunner = TFTestRunnerType.VSTest,
            TestResultsFiles = GetFiles(Paths.TestResultsDirectory + "/*.trx").ToList()
        });
    
});

Task(buildCITaskName)
    .IsDependentOn(compileTaskName)
    .IsDependentOn(testTaskName)
    .IsDependentOn(buildFrontEndTaskName)
    .IsDependentOn(versionTaskName)
    .IsDependentOn(packageZipTaskName)
    .IsDependentOn(setBuildNumberTaskName)
    .IsDependentOn(publishBuildArtifactTaskName);

Task("publish-build-artifact")
    .WithCriteria(() => BuildSystem.IsRunningOnAzurePipelinesHosted)
    .IsDependentOn(packageZipTaskName)
    .Does<PackageMetadata>(package =>
{
    TFBuild.Commands.UploadArtifactDirectory(package.OutputDirectory);

    //foreach(var p in GetFiles(package.OutputDirectory + $"/*.{package.Extension}"))
});

RunTarget(target);
