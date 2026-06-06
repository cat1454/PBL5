using ELearnGamePlatform.Core.Options;
using ELearnGamePlatform.Services.DocumentProcessing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class DoclingDocumentParserTests
{
    [Fact]
    public async Task TryParseAsync_ReturnsFailureWithoutStartingCommand_WhenDisabled()
    {
        var parser = CreateParser(new DocumentParsingSettings { Enabled = false });

        var result = await parser.TryParseAsync("lesson.pdf", "pdf");

        Assert.False(result.Success);
        Assert.Equal("External document parsing is disabled.", result.Error);
        Assert.False(result.CommandMissing);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task TryParseAsync_ReturnsCommandMissing_WhenExecutableDoesNotExist()
    {
        var inputPath = Path.GetTempFileName();
        var outputRoot = Path.Combine(Path.GetTempPath(), $"pbl5-docling-test-{Guid.NewGuid():N}");

        try
        {
            var parser = CreateParser(new DocumentParsingSettings
            {
                Enabled = true,
                DoclingCommand = $"missing-docling-{Guid.NewGuid():N}",
                OutputDirectory = outputRoot
            });

            var result = await parser.TryParseAsync(inputPath, "pdf");

            Assert.False(result.Success);
            Assert.True(result.CommandMissing);
            Assert.Contains("could not be started", result.Error);
            Assert.True(Directory.Exists(outputRoot));
            Assert.Single(Directory.GetDirectories(outputRoot));
        }
        finally
        {
            File.Delete(inputPath);
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TryParseAsync_ReturnsFailure_WhenInputFileDoesNotExist()
    {
        var parser = CreateParser(new DocumentParsingSettings { Enabled = true });

        var result = await parser.TryParseAsync(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.pdf"),
            "pdf");

        Assert.False(result.Success);
        Assert.Contains("does not exist", result.Error);
    }

    [Fact]
    public async Task TryParseAsync_ReturnsTimedOut_WhenCommandExceedsTimeout()
    {
        var fixture = CreateFakeCommandFixture(timeout: true);

        try
        {
            var parser = CreateParser(new DocumentParsingSettings
            {
                Enabled = true,
                DoclingCommand = fixture.Command,
                OutputDirectory = fixture.OutputRoot,
                TimeoutSeconds = 1,
                MinMarkdownLength = 1
            });

            var result = await parser.TryParseAsync(fixture.ScriptPath, "pdf");

            Assert.False(result.Success);
            Assert.True(result.TimedOut);
            Assert.False(result.CommandMissing);
            Assert.Contains("timed out", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task TryParseAsync_ReturnsMarkdown_WhenCommandSucceeds()
    {
        var fixture = CreateFakeCommandFixture(timeout: false);

        try
        {
            var parser = CreateParser(new DocumentParsingSettings
            {
                Enabled = true,
                DoclingCommand = fixture.Command,
                OutputDirectory = fixture.OutputRoot,
                TimeoutSeconds = 10,
                MinMarkdownLength = 10
            });

            var result = await parser.TryParseAsync(fixture.ScriptPath, "pdf");

            Assert.True(result.Success, result.Error);
            Assert.Equal("docling", result.Provider);
            Assert.Contains("# Parsed lesson", result.Markdown);
            Assert.Contains("| Name | Value |", result.Markdown);
            Assert.Contains("Parsed lesson", result.PlainText);
            Assert.False(result.TimedOut);
            Assert.False(result.CommandMissing);
            Assert.True(File.Exists(result.OutputPath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task TryParseAsync_ReturnsRepairedMarkdown_WhenVietnameseMojibakeCanBeRepaired()
    {
        var fixture = CreateFakeCommandFixture(timeout: false, writeMojibake: true);

        try
        {
            var parser = CreateParser(new DocumentParsingSettings
            {
                Enabled = true,
                DoclingCommand = fixture.Command,
                OutputDirectory = fixture.OutputRoot,
                TimeoutSeconds = 10,
                MinMarkdownLength = 1
            });

            var result = await parser.TryParseAsync(fixture.ScriptPath, "pdf");

            Assert.True(result.Success, result.Error);
            Assert.Equal("docling-repaired", result.Provider);
            Assert.Contains(
                "D\u00F9ng Docling \u0111\u1EC3 sinh c\u00E2u h\u1ECFi",
                result.Markdown);
            Assert.Null(result.Error);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private static DoclingDocumentParser CreateParser(DocumentParsingSettings settings)
        => new(
            Options.Create(settings),
            NullLogger<DoclingDocumentParser>.Instance);

    private static FakeCommandFixture CreateFakeCommandFixture(bool timeout, bool writeMojibake = false)
    {
        var root = Path.Combine(Path.GetTempPath(), $"pbl5-docling-cli-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var outputRoot = Path.Combine(root, "output");

        if (OperatingSystem.IsWindows())
        {
            var scriptPath = Path.Combine(root, "fake-docling.ps1");
            var script = timeout
                ? "Start-Sleep -Seconds 5"
                : writeMojibake
                    ? """
                      if (-not ($args -contains '--no-ocr') -or
                          [Array]::IndexOf($args, '--image-export-mode') -lt 0 -or
                          -not ($args -contains 'placeholder')) {
                          exit 9
                      }
                      $outputIndex = [Array]::IndexOf($args, '--output')
                      $outputDirectory = $args[$outputIndex + 1]
                      New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
                      $content = [Convert]::FromBase64String('IyBIw6HCu+KAoSB0aMOhwrvigJhuZwoKRMODwrluZyBEb2NsaW5nIMOE4oCYw6HCu8aSIHNpbmggY8ODwqJ1IGjDocK7wo9p')
                      [IO.File]::WriteAllBytes((Join-Path $outputDirectory 'result.md'), $content)
                      """
                    : """
                  if (-not ($args -contains '--no-ocr') -or
                      [Array]::IndexOf($args, '--image-export-mode') -lt 0 -or
                      -not ($args -contains 'placeholder')) {
                      exit 9
                  }
                  $outputIndex = [Array]::IndexOf($args, '--output')
                  $outputDirectory = $args[$outputIndex + 1]
                  New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
                  @'
                  # Parsed lesson

                  | Name | Value |
                  | --- | --- |
                  | Alpha | 10 |
                  '@ | Set-Content -Encoding UTF8 -Path (Join-Path $outputDirectory 'result.md')
                  """;
            File.WriteAllText(scriptPath, script);
            return new FakeCommandFixture("powershell.exe", scriptPath, outputRoot, root);
        }

        var shellScriptPath = Path.Combine(root, "fake-docling.sh");
        var shellScript = timeout
            ? "sleep 5"
            : writeMojibake
                ? """
                  printf '%s\n' "$@" | grep -qx -- '--no-ocr' || exit 9
                  printf '%s\n' "$@" | grep -qx -- '--image-export-mode' || exit 9
                  printf '%s\n' "$@" | grep -qx -- 'placeholder' || exit 9
                  output_directory=""
                  while [ "$#" -gt 0 ]; do
                    if [ "$1" = "--output" ]; then
                      output_directory="$2"
                      break
                    fi
                    shift
                  done
                  mkdir -p "$output_directory"
                  printf '%s' 'IyBIw6HCu+KAoSB0aMOhwrvigJhuZwoKRMODwrluZyBEb2NsaW5nIMOE4oCYw6HCu8aSIHNpbmggY8ODwqJ1IGjDocK7wo9p' | base64 -d > "$output_directory/result.md"
                  """
                : """
              printf '%s\n' "$@" | grep -qx -- '--no-ocr' || exit 9
              printf '%s\n' "$@" | grep -qx -- '--image-export-mode' || exit 9
              printf '%s\n' "$@" | grep -qx -- 'placeholder' || exit 9
              output_directory=""
              while [ "$#" -gt 0 ]; do
                if [ "$1" = "--output" ]; then
                  output_directory="$2"
                  break
                fi
                shift
              done
              mkdir -p "$output_directory"
              cat > "$output_directory/result.md" <<'EOF'
              # Parsed lesson

              | Name | Value |
              | --- | --- |
              | Alpha | 10 |
              EOF
              """;
        File.WriteAllText(shellScriptPath, shellScript);
        return new FakeCommandFixture("/bin/sh", shellScriptPath, outputRoot, root);
    }

    private sealed record FakeCommandFixture(
        string Command,
        string ScriptPath,
        string OutputRoot,
        string Root) : IDisposable
    {
        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
