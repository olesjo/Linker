public static class Paths
{
    public static FilePath SolutionFile => "Linker.sln";
    public static FilePath ProjectFile => "src/Linker/Linker.csproj";
    public static FilePath TestProjectFile => "test/Linker.Tests/Linker.Tests.csproj";
    public static DirectoryPath TestResultsDirectory => "testResults";
    public static FilePath CodeCoverageReportFile => TestResultsDirectory + "/coverage.xml";
    public static DirectoryPath PublishDirectory => "publish";
}

public static FilePath Combine(DirectoryPath directory, FilePath file)
{
    return directory.CombineWithFilePath(file);
}
