using System.Text;

var root = FindRepositoryRoot(AppContext.BaseDirectory);
var dist = Path.Combine(root, "dist");
Directory.CreateDirectory(dist);

var options = ParseOptions(args, root);

var runtimeBundle = Path.Combine(dist, "SqlTestSupport.cs");
var testBundle = Path.Combine(dist, "SqlTestSupport.Tests.cs");

Bundle(
    sourceRoot: Path.Combine(root, "src", "SqlTestSupport"),
    outputPath: runtimeBundle);

Bundle(
    sourceRoot: Path.Combine(root, "tests", "SqlTestSupport.Tests"),
    outputPath: testBundle);

Console.WriteLine("Generated:");
Console.WriteLine($"  {runtimeBundle}");
Console.WriteLine($"  {testBundle}");

if (options.SelfContainedScriptPath is not null)
{
    WriteSelfContainedScript(
        outputPath: options.SelfContainedScriptPath,
        files:
        [
            new BundleFile("SqlTestSupport.cs", runtimeBundle),
            new BundleFile("SqlTestSupport.Tests.cs", testBundle),
        ]);

    Console.WriteLine($"  {options.SelfContainedScriptPath}");
}

if (options.SelfContainedTargetsPath is not null)
{
    WriteSelfContainedTargets(
        outputPath: options.SelfContainedTargetsPath,
        runtimeBundlePath: runtimeBundle,
        testBundlePath: testBundle);

    Console.WriteLine($"  {options.SelfContainedTargetsPath}");
}

if (options.SelfContainedCSharpPath is not null)
{
    WriteSelfContainedCSharpBootstrap(
        outputPath: options.SelfContainedCSharpPath,
        runtimeBundlePath: runtimeBundle,
        testBundlePath: testBundle);

    Console.WriteLine($"  {options.SelfContainedCSharpPath}");
}

static BootstrapOptions ParseOptions(string[] args, string root)
{
    string? selfContainedScriptPath = null;
    string? selfContainedTargetsPath = null;
    string? selfContainedCSharpPath = null;

    for (var index = 0; index < args.Length; index++)
    {
        var arg = args[index];
        switch (arg)
        {
            case "--self-contained-script":
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("--self-contained-script requires an output path.");
                }

                selfContainedScriptPath = ResolveOutputPath(root, args[++index]);
                break;

            case "--self-contained-targets":
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("--self-contained-targets requires an output path.");
                }

                selfContainedTargetsPath = ResolveOutputPath(root, args[++index]);
                break;

            case "--self-contained-csharp":
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("--self-contained-csharp requires an output path.");
                }

                selfContainedCSharpPath = ResolveOutputPath(root, args[++index]);
                break;

            case "--help":
            case "-h":
                PrintUsage();
                Environment.Exit(0);
                break;

            default:
                throw new ArgumentException($"Unknown argument: {arg}");
        }
    }

    return new BootstrapOptions(selfContainedScriptPath, selfContainedTargetsPath, selfContainedCSharpPath);
}

static string ResolveOutputPath(string root, string path)
{
    return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project tools/SqlTestSupport.Bootstrap/SqlTestSupport.Bootstrap.csproj -- [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --self-contained-script <path>   Also generate a single-file shell script that expands the bundled sources.");
    Console.WriteLine("  --self-contained-targets <path>  Also generate a single-file MSBuild targets file that expands the runtime source during build.");
    Console.WriteLine("  --self-contained-csharp <path>   Also generate a copyable C# bootstrap source file for users without repository access.");
}

static string FindRepositoryRoot(string start)
{
    var directory = new DirectoryInfo(start);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "SqlTestSupport.slnx")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Could not find SqlTestSupport.slnx.");
}

static void Bundle(string sourceRoot, string outputPath)
{
    var files = Directory
        .GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
        .Where(path =>
            !StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(path), "MSTestSettings.cs") &&
            !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        .OrderBy(path => path, StringComparer.Ordinal)
        .ToArray();

    var usings = new SortedSet<string>(StringComparer.Ordinal);
    var namespaces = new SortedSet<string>(StringComparer.Ordinal);
    var body = new StringBuilder();

    foreach (var file in files)
    {
        var content = File.ReadAllText(file, Encoding.UTF8).TrimStart('\uFEFF');
        var lines = content.ReplaceLineEndings("\n").Split('\n');
        var bodyLines = ExtractNamespaceBody(lines, usings, namespaces);

        if (body.Length > 0)
        {
            body.AppendLine();
        }

        foreach (var line in bodyLines)
        {
            body.AppendLine(line);
        }
    }

    if (namespaces.Count != 1)
    {
        throw new InvalidOperationException($"Expected exactly one namespace under {sourceRoot}, but found {namespaces.Count}.");
    }

    var output = new StringBuilder();
    foreach (var usingLine in usings)
    {
        output.AppendLine(usingLine);
    }

    if (output.Length > 0)
    {
        output.AppendLine();
    }

    output.AppendLine($"namespace {namespaces.Single()}");
    output.AppendLine("{");
    output.Append(body);
    output.AppendLine("}");

    File.WriteAllText(outputPath, output.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}

static IReadOnlyList<string> ExtractNamespaceBody(
    IReadOnlyList<string> lines,
    ISet<string> usings,
    ISet<string> namespaces)
{
    var bodyLines = new List<string>();
    var foundNamespace = false;
    var skipNamespaceOpeningBrace = false;
    var removeNamespaceClosingBrace = false;

    foreach (var line in lines)
    {
        if (!foundNamespace)
        {
            if (line.StartsWith("using ", StringComparison.Ordinal))
            {
                usings.Add(line.Trim());
                continue;
            }

            if (line.StartsWith("[assembly:", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("namespace ", StringComparison.Ordinal))
            {
                var namespaceName = line["namespace ".Length..].Trim().TrimEnd(';').Trim();
                namespaces.Add(namespaceName);
                foundNamespace = true;
                removeNamespaceClosingBrace = !line.TrimEnd().EndsWith(";", StringComparison.Ordinal);
                skipNamespaceOpeningBrace = removeNamespaceClosingBrace;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
        }

        if (skipNamespaceOpeningBrace && line.Trim() == "{")
        {
            skipNamespaceOpeningBrace = false;
            continue;
        }

        skipNamespaceOpeningBrace = false;
        bodyLines.Add(line);
    }

    while (bodyLines.Count > 0 && string.IsNullOrWhiteSpace(bodyLines[^1]))
    {
        bodyLines.RemoveAt(bodyLines.Count - 1);
    }

    if (removeNamespaceClosingBrace && bodyLines.Count > 0 && bodyLines[^1].Trim() == "}")
    {
        bodyLines.RemoveAt(bodyLines.Count - 1);
    }

    return bodyLines;
}

static void WriteSelfContainedScript(string outputPath, IReadOnlyList<BundleFile> files)
{
    var directory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    var output = new StringBuilder();
    output.AppendLine("#!/usr/bin/env bash");
    output.AppendLine("set -euo pipefail");
    output.AppendLine();
    output.AppendLine("output_dir=\"${1:-dist}\"");
    output.AppendLine("mkdir -p \"$output_dir\"");
    output.AppendLine();
    output.AppendLine("decode_base64() {");
    output.AppendLine("  if base64 --decode >/dev/null 2>&1 </dev/null; then");
    output.AppendLine("    base64 --decode");
    output.AppendLine("  elif base64 -d >/dev/null 2>&1 </dev/null; then");
    output.AppendLine("    base64 -d");
    output.AppendLine("  else");
    output.AppendLine("    base64 -D");
    output.AppendLine("  fi");
    output.AppendLine("}");
    output.AppendLine();
    output.AppendLine("write_file() {");
    output.AppendLine("  local relative_path=\"$1\"");
    output.AppendLine("  local destination=\"$output_dir/$relative_path\"");
    output.AppendLine("  mkdir -p \"$(dirname \"$destination\")\"");
    output.AppendLine("  decode_base64 > \"$destination\"");
    output.AppendLine("  printf 'Wrote %s\\n' \"$destination\"");
    output.AppendLine("}");

    foreach (var file in files)
    {
        var base64 = Convert.ToBase64String(File.ReadAllBytes(file.SourcePath), Base64FormattingOptions.InsertLineBreaks)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        output.AppendLine();
        output.AppendLine($"write_file '{file.RelativePath}' <<'SQL_TEST_SUPPORT_BUNDLE'");
        output.AppendLine(base64);
        output.AppendLine("SQL_TEST_SUPPORT_BUNDLE");
    }

    File.WriteAllText(outputPath, output.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
    {
        File.SetUnixFileMode(
            outputPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
}


static void WriteSelfContainedTargets(string outputPath, string runtimeBundlePath, string testBundlePath)
{
    var directory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    var runtimeBase64 = Convert.ToBase64String(File.ReadAllBytes(runtimeBundlePath), Base64FormattingOptions.InsertLineBreaks)
        .Replace("\r\n", "\n", StringComparison.Ordinal);
    var testBase64 = Convert.ToBase64String(File.ReadAllBytes(testBundlePath), Base64FormattingOptions.InsertLineBreaks)
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    var output = new StringBuilder();
    output.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
    output.AppendLine("<Project>");
    output.AppendLine("  <!--");
    output.AppendLine("    SqlTestSupport self-contained MSBuild bootstrap.");
    output.AppendLine("    Copy this file as Directory.Build.targets next to the destination test project");
    output.AppendLine("    and build the project. The embedded source is expanded under obj/ and compiled.");
    output.AppendLine("  -->");
    output.AppendLine();
    output.AppendLine("  <PropertyGroup>");
    output.AppendLine("    <SqlTestSupportExpandOnBuild Condition=\"'$(SqlTestSupportExpandOnBuild)' == ''\">true</SqlTestSupportExpandOnBuild>");
    output.AppendLine("    <SqlTestSupportIncludeSelfTests Condition=\"'$(SqlTestSupportIncludeSelfTests)' == ''\">false</SqlTestSupportIncludeSelfTests>");
    output.AppendLine("    <SqlTestSupportAddPackageReferences Condition=\"'$(SqlTestSupportAddPackageReferences)' == ''\">true</SqlTestSupportAddPackageReferences>");
    output.AppendLine("  </PropertyGroup>");
    output.AppendLine();
    output.AppendLine("  <ItemGroup Condition=\"'$(SqlTestSupportAddPackageReferences)' == 'true'\">");
    output.AppendLine("    <PackageReference Include=\"Microsoft.SqlServer.TransactSql.ScriptDom\" Version=\"180.18.1\" Condition=\"'@(PackageReference->WithMetadataValue(\'Identity\', \'Microsoft.SqlServer.TransactSql.ScriptDom\'))' == ''\" />");
    output.AppendLine("    <PackageReference Include=\"MSTest.TestFramework\" Version=\"4.0.2\" Condition=\"'@(PackageReference->WithMetadataValue(\'Identity\', \'MSTest.TestFramework\'))' == ''\" />");
    output.AppendLine("  </ItemGroup>");
    output.AppendLine();
    output.AppendLine("  <UsingTask TaskName=\"SqlTestSupportWriteEmbeddedFile\" TaskFactory=\"RoslynCodeTaskFactory\" AssemblyFile=\"$(MSBuildToolsPath)\\Microsoft.Build.Tasks.Core.dll\">");
    output.AppendLine("    <ParameterGroup>");
    output.AppendLine("      <OutputPath ParameterType=\"System.String\" Required=\"true\" />");
    output.AppendLine("      <Base64Content ParameterType=\"System.String\" Required=\"true\" />");
    output.AppendLine("    </ParameterGroup>");
    output.AppendLine("    <Task>");
    output.AppendLine("      <Using Namespace=\"System\" />");
    output.AppendLine("      <Using Namespace=\"System.IO\" />");
    output.AppendLine("      <Using Namespace=\"System.Text\" />");
    output.AppendLine("      <Code Type=\"Fragment\" Language=\"cs\"><![CDATA[");
    output.AppendLine("var directory = Path.GetDirectoryName(OutputPath);");
    output.AppendLine("if (!string.IsNullOrEmpty(directory))");
    output.AppendLine("{");
    output.AppendLine("    Directory.CreateDirectory(directory);");
    output.AppendLine("}");
    output.AppendLine();
    output.AppendLine("File.WriteAllText(");
    output.AppendLine("    OutputPath,");
    output.AppendLine("    Encoding.UTF8.GetString(Convert.FromBase64String(Base64Content)),");
    output.AppendLine("    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));");
    output.AppendLine("      ]]></Code>");
    output.AppendLine("    </Task>");
    output.AppendLine("  </UsingTask>");
    output.AppendLine();
    output.AppendLine("  <PropertyGroup>");
    output.AppendLine("    <SqlTestSupportRuntimeBundleBase64>");
    output.AppendLine(runtimeBase64);
    output.AppendLine("    </SqlTestSupportRuntimeBundleBase64>");
    output.AppendLine("    <SqlTestSupportTestBundleBase64>");
    output.AppendLine(testBase64);
    output.AppendLine("    </SqlTestSupportTestBundleBase64>");
    output.AppendLine("  </PropertyGroup>");
    output.AppendLine();
    output.AppendLine("  <Target Name=\"SqlTestSupportExpandEmbeddedSources\" BeforeTargets=\"CoreCompile\" Condition=\"'$(SqlTestSupportExpandOnBuild)' == 'true'\">");
    output.AppendLine("    <PropertyGroup>");
    output.AppendLine("      <SqlTestSupportExpandedSourceDir>$(IntermediateOutputPath)SqlTestSupport\\</SqlTestSupportExpandedSourceDir>");
    output.AppendLine("      <SqlTestSupportExpandedRuntimeSource>$(SqlTestSupportExpandedSourceDir)SqlTestSupport.cs</SqlTestSupportExpandedRuntimeSource>");
    output.AppendLine("      <SqlTestSupportExpandedTestSource>$(SqlTestSupportExpandedSourceDir)SqlTestSupport.Tests.cs</SqlTestSupportExpandedTestSource>");
    output.AppendLine("    </PropertyGroup>");
    output.AppendLine();
    output.AppendLine("    <SqlTestSupportWriteEmbeddedFile OutputPath=\"$(SqlTestSupportExpandedRuntimeSource)\" Base64Content=\"$(SqlTestSupportRuntimeBundleBase64)\" />");
    output.AppendLine("    <SqlTestSupportWriteEmbeddedFile Condition=\"'$(SqlTestSupportIncludeSelfTests)' == 'true'\" OutputPath=\"$(SqlTestSupportExpandedTestSource)\" Base64Content=\"$(SqlTestSupportTestBundleBase64)\" />");
    output.AppendLine();
    output.AppendLine("    <ItemGroup>");
    output.AppendLine("      <Compile Include=\"$(SqlTestSupportExpandedRuntimeSource)\" Link=\"SqlTestSupport.cs\" />");
    output.AppendLine("      <Compile Include=\"$(SqlTestSupportExpandedTestSource)\" Link=\"SqlTestSupport.Tests.cs\" Condition=\"'$(SqlTestSupportIncludeSelfTests)' == 'true'\" />");
    output.AppendLine("    </ItemGroup>");
    output.AppendLine("  </Target>");
    output.AppendLine("</Project>");

    File.WriteAllText(outputPath, output.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}


static void WriteSelfContainedCSharpBootstrap(string outputPath, string runtimeBundlePath, string testBundlePath)
{
    var directory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    var runtimeBase64 = Convert.ToBase64String(File.ReadAllBytes(runtimeBundlePath), Base64FormattingOptions.InsertLineBreaks)
        .Replace("\r\n", "\n", StringComparison.Ordinal);
    var testBase64 = Convert.ToBase64String(File.ReadAllBytes(testBundlePath), Base64FormattingOptions.InsertLineBreaks)
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    var output = new StringBuilder();
    output.AppendLine("using System;");
    output.AppendLine("using System.IO;");
    output.AppendLine("using System.Text;");
    output.AppendLine();
    output.AppendLine("const string RuntimeBundleBase64 = \"\"\"");
    output.AppendLine(runtimeBase64);
    output.AppendLine("\"\"\";");
    output.AppendLine();
    output.AppendLine("const string TestBundleBase64 = \"\"\"");
    output.AppendLine(testBase64);
    output.AppendLine("\"\"\";");
    output.AppendLine();
    output.AppendLine("var outputDir = \"dist\";");
    output.AppendLine("var includeTests = true;");
    output.AppendLine("var includeTargets = true;");
    output.AppendLine();
    output.AppendLine("for (var index = 0; index < args.Length; index++)");
    output.AppendLine("{");
    output.AppendLine("    switch (args[index])");
    output.AppendLine("    {");
    output.AppendLine("        case \"--skip-tests\":");
    output.AppendLine("            includeTests = false;");
    output.AppendLine("            break;");
    output.AppendLine("        case \"--skip-targets\":");
    output.AppendLine("            includeTargets = false;");
    output.AppendLine("            break;");
    output.AppendLine("        case \"--help\":");
    output.AppendLine("        case \"-h\":");
    output.AppendLine("            PrintUsage();");
    output.AppendLine("            return;");
    output.AppendLine("        default:");
    output.AppendLine("            if (args[index].StartsWith(\"--\", StringComparison.Ordinal))");
    output.AppendLine("            {");
    output.AppendLine("                throw new ArgumentException($\"Unknown argument: {args[index]}\");");
    output.AppendLine("            }");
    output.AppendLine();
    output.AppendLine("            outputDir = args[index];");
    output.AppendLine("            break;");
    output.AppendLine("    }");
    output.AppendLine("}");
    output.AppendLine();
    output.AppendLine("Directory.CreateDirectory(outputDir);");
    output.AppendLine("WriteEmbeddedFile(Path.Combine(outputDir, \"SqlTestSupport.cs\"), RuntimeBundleBase64);");
    output.AppendLine("if (includeTests)");
    output.AppendLine("{");
    output.AppendLine("    WriteEmbeddedFile(Path.Combine(outputDir, \"SqlTestSupport.Tests.cs\"), TestBundleBase64);");
    output.AppendLine("}");
    output.AppendLine();
    output.AppendLine("if (includeTargets)");
    output.AppendLine("{");
    output.AppendLine("    WriteTargets(Path.Combine(outputDir, \"SqlTestSupport.Directory.Build.targets\"));");
    output.AppendLine("}");
    output.AppendLine();
    output.AppendLine("static void PrintUsage()");
    output.AppendLine("{");
    output.AppendLine("    Console.WriteLine(\"Usage: dotnet run -- [output-dir] [--skip-tests] [--skip-targets]\");");
    output.AppendLine("}");
    output.AppendLine();
    output.AppendLine("static void WriteEmbeddedFile(string path, string base64Content)");
    output.AppendLine("{");
    output.AppendLine("    var directory = Path.GetDirectoryName(path);");
    output.AppendLine("    if (!string.IsNullOrEmpty(directory))");
    output.AppendLine("    {");
    output.AppendLine("        Directory.CreateDirectory(directory);");
    output.AppendLine("    }");
    output.AppendLine();
    output.AppendLine("    File.WriteAllText(");
    output.AppendLine("        path,");
    output.AppendLine("        Encoding.UTF8.GetString(Convert.FromBase64String(base64Content)),");
    output.AppendLine("        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));");
    output.AppendLine("    Console.WriteLine($\"Wrote {path}\");");
    output.AppendLine("}");
    output.AppendLine();
    output.AppendLine("static void WriteTargets(string path)");
    output.AppendLine("{");
    output.AppendLine("    var target = new StringBuilder();");
    AppendCSharpLine(output, "    target", "<?xml version=\"1.0\" encoding=\"utf-8\"?>");
    AppendCSharpLine(output, "    target", "<Project>");
    AppendCSharpLine(output, "    target", "  <!-- SqlTestSupport self-contained MSBuild bootstrap. Copy this file as Directory.Build.targets next to the destination test project and build. -->");
    AppendCSharpLine(output, "    target", "  <PropertyGroup>");
    AppendCSharpLine(output, "    target", "    <SqlTestSupportExpandOnBuild Condition=\"'$(SqlTestSupportExpandOnBuild)' == ''\">true</SqlTestSupportExpandOnBuild>");
    AppendCSharpLine(output, "    target", "    <SqlTestSupportIncludeSelfTests Condition=\"'$(SqlTestSupportIncludeSelfTests)' == ''\">false</SqlTestSupportIncludeSelfTests>");
    AppendCSharpLine(output, "    target", "    <SqlTestSupportAddPackageReferences Condition=\"'$(SqlTestSupportAddPackageReferences)' == ''\">true</SqlTestSupportAddPackageReferences>");
    AppendCSharpLine(output, "    target", "  </PropertyGroup>");
    AppendCSharpLine(output, "    target", "  <ItemGroup Condition=\"'$(SqlTestSupportAddPackageReferences)' == 'true'\">");
    AppendCSharpLine(output, "    target", "    <PackageReference Include=\"Microsoft.SqlServer.TransactSql.ScriptDom\" Version=\"180.18.1\" Condition=\"'@(PackageReference->WithMetadataValue('Identity', 'Microsoft.SqlServer.TransactSql.ScriptDom'))' == ''\" />");
    AppendCSharpLine(output, "    target", "    <PackageReference Include=\"MSTest.TestFramework\" Version=\"4.0.2\" Condition=\"'@(PackageReference->WithMetadataValue('Identity', 'MSTest.TestFramework'))' == ''\" />");
    AppendCSharpLine(output, "    target", "  </ItemGroup>");
    AppendCSharpLine(output, "    target", "  <UsingTask TaskName=\"SqlTestSupportWriteEmbeddedFile\" TaskFactory=\"RoslynCodeTaskFactory\" AssemblyFile=\"$(MSBuildToolsPath)\\Microsoft.Build.Tasks.Core.dll\">");
    AppendCSharpLine(output, "    target", "    <ParameterGroup><OutputPath ParameterType=\"System.String\" Required=\"true\" /><Base64Content ParameterType=\"System.String\" Required=\"true\" /></ParameterGroup>");
    AppendCSharpLine(output, "    target", "    <Task><Using Namespace=\"System\" /><Using Namespace=\"System.IO\" /><Using Namespace=\"System.Text\" /><Code Type=\"Fragment\" Language=\"cs\"><![CDATA[");
    AppendCSharpLine(output, "    target", "var directory = Path.GetDirectoryName(OutputPath);");
    AppendCSharpLine(output, "    target", "if (!string.IsNullOrEmpty(directory)) { Directory.CreateDirectory(directory); }");
    AppendCSharpLine(output, "    target", "File.WriteAllText(OutputPath, Encoding.UTF8.GetString(Convert.FromBase64String(Base64Content)), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));");
    AppendCSharpLine(output, "    target", "      ]]></Code></Task>");
    AppendCSharpLine(output, "    target", "  </UsingTask>");
    AppendCSharpLine(output, "    target", "  <PropertyGroup>");
    output.AppendLine("    target.AppendLine(\"    <SqlTestSupportRuntimeBundleBase64>\");");
    output.AppendLine("    target.AppendLine(RuntimeBundleBase64);");
    output.AppendLine("    target.AppendLine(\"    </SqlTestSupportRuntimeBundleBase64>\");");
    output.AppendLine("    target.AppendLine(\"    <SqlTestSupportTestBundleBase64>\");");
    output.AppendLine("    target.AppendLine(TestBundleBase64);");
    output.AppendLine("    target.AppendLine(\"    </SqlTestSupportTestBundleBase64>\");");
    AppendCSharpLine(output, "    target", "  </PropertyGroup>");
    AppendCSharpLine(output, "    target", "  <Target Name=\"SqlTestSupportExpandEmbeddedSources\" BeforeTargets=\"CoreCompile\" Condition=\"'$(SqlTestSupportExpandOnBuild)' == 'true'\">");
    AppendCSharpLine(output, "    target", "    <PropertyGroup><SqlTestSupportExpandedSourceDir>$(IntermediateOutputPath)SqlTestSupport\\</SqlTestSupportExpandedSourceDir><SqlTestSupportExpandedRuntimeSource>$(SqlTestSupportExpandedSourceDir)SqlTestSupport.cs</SqlTestSupportExpandedRuntimeSource><SqlTestSupportExpandedTestSource>$(SqlTestSupportExpandedSourceDir)SqlTestSupport.Tests.cs</SqlTestSupportExpandedTestSource></PropertyGroup>");
    AppendCSharpLine(output, "    target", "    <SqlTestSupportWriteEmbeddedFile OutputPath=\"$(SqlTestSupportExpandedRuntimeSource)\" Base64Content=\"$(SqlTestSupportRuntimeBundleBase64)\" />");
    AppendCSharpLine(output, "    target", "    <SqlTestSupportWriteEmbeddedFile Condition=\"'$(SqlTestSupportIncludeSelfTests)' == 'true'\" OutputPath=\"$(SqlTestSupportExpandedTestSource)\" Base64Content=\"$(SqlTestSupportTestBundleBase64)\" />");
    AppendCSharpLine(output, "    target", "    <ItemGroup><Compile Include=\"$(SqlTestSupportExpandedRuntimeSource)\" Link=\"SqlTestSupport.cs\" /><Compile Include=\"$(SqlTestSupportExpandedTestSource)\" Link=\"SqlTestSupport.Tests.cs\" Condition=\"'$(SqlTestSupportIncludeSelfTests)' == 'true'\" /></ItemGroup>");
    AppendCSharpLine(output, "    target", "  </Target>");
    AppendCSharpLine(output, "    target", "</Project>");
    output.AppendLine("    WriteEmbeddedFile(path, Convert.ToBase64String(Encoding.UTF8.GetBytes(target.ToString())));");
    output.AppendLine("}");
    output.AppendLine();
    File.WriteAllText(outputPath, output.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}

static void AppendCSharpLine(StringBuilder output, string builderName, string line)
{
    output
        .Append(builderName)
        .Append(".AppendLine(\"")
        .Append(line.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal))
        .AppendLine("\");");
}

internal sealed record BootstrapOptions(string? SelfContainedScriptPath, string? SelfContainedTargetsPath, string? SelfContainedCSharpPath);

internal sealed record BundleFile(string RelativePath, string SourcePath);
