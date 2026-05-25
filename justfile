set shell := ["powershell.exe", "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command"]

solution := "TreeSitter.slnx"
tests := "TreeSitter.WpfSyntaxBox.Tests/TreeSitter.WpfSyntaxBox.Tests.csproj"
benchmarks := "TreeSitter.WpfSyntaxBox.Benchmarks/TreeSitter.WpfSyntaxBox.Benchmarks.csproj"
package := "TreeSitter.WpfSyntaxBox/TreeSitter.WpfSyntaxBox.csproj"
package_id := "TreeSitter.WpfSyntaxBox"
package_output := "artifacts/packages"

default:
    just --list

# Restore NuGet packages.
restore:
    dotnet restore "{{solution}}" --locked-mode

# Build all .NET projects, including the WPF SyntaxBox implementation and demo.
build:
    dotnet build "{{solution}}" --configuration Debug

# Build all .NET projects in Release.
build-release:
    dotnet build "{{solution}}" --configuration Release

# Run all .NET tests.
test:
    dotnet test "{{tests}}" --configuration Debug --no-restore

# Run the C# fuzzing and WPF scroll simulation tests.
test-stress:
    dotnet test "{{tests}}" --configuration Debug --no-restore --filter "FullyQualifiedName~CSharpFuzzTests"

# Run only the generated C# TextBox user-scroll simulation test.
test-scroll:
    dotnet test "{{tests}}" --configuration Debug --no-restore --filter "FullyQualifiedName~SyntaxBox_SimulatesUserScrollingGeneratedCSharpTextBox"

# Run demo UI tests that resize and scroll the C# SyntaxBox window.
test-ui:
    dotnet test "{{tests}}" --configuration Debug --no-restore --filter "FullyQualifiedName~DemoUiTests"

# Run C# Tree-sitter highlighting benchmarks.
benchmark:
    dotnet run --project "{{benchmarks}}" --configuration Release

# List available benchmarks.
benchmark-list:
    dotnet run --project "{{benchmarks}}" --configuration Release -- --list flat

# Smoke-run the WPF user-scroll benchmark with a short BenchmarkDotNet job.
benchmark-scroll-smoke:
    dotnet run --project "{{benchmarks}}" --configuration Release -- --filter "*UserScrollsSyntaxTextBox*" --job Dry --warmupCount 1 --iterationCount 1

# Run the full local CI sequence.
ci:
    dotnet restore "{{solution}}" --locked-mode
    dotnet build "{{solution}}" --configuration Release --no-restore
    dotnet test "{{tests}}" --configuration Release --no-build

# Generate CHANGELOG.md from git history with git-cliff.
changelog:
    git-cliff --config cliff.toml --output CHANGELOG.md

# Pack and publish the NuGet package to GitHub Packages. Set GITHUB_TOKEN and GITHUB_REPOSITORY_OWNER.
publish version:
    dotnet pack "{{package}}" --configuration Release --output "{{package_output}}" /p:PackageVersion="{{version}}" /p:Version="{{version}}" /p:ContinuousIntegrationBuild=true
    if ([string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) { throw "Set GITHUB_TOKEN before running just publish." }
    if ([string]::IsNullOrWhiteSpace($env:GITHUB_REPOSITORY_OWNER)) { throw "Set GITHUB_REPOSITORY_OWNER before running just publish." }
    dotnet nuget push "{{package_output}}/{{package_id}}.{{version}}.nupkg" --source "https://nuget.pkg.github.com/$env:GITHUB_REPOSITORY_OWNER/index.json" --api-key "$env:GITHUB_TOKEN" --skip-duplicate

# Clean .NET build outputs.
clean:
    dotnet clean "{{solution}}"
