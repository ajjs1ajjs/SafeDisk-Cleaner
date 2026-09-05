using FluentAssertions;
using SafeDiskCleaner.Core.Utils;

namespace SafeDiskCleaner.Tests;

public sealed class Sha256ChecksumTests
{
    // well-known SHA-256 of the ASCII string "abc"
    private const string AbcSha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Fact]
    public void ComputeFile_MatchesKnownVector()
    {
        var file = Path.Combine(Path.GetTempPath(), $"sdc-sha-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(file, "abc");

            Sha256Checksum.ComputeFile(file).Should().Be(AbcSha256);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Parse_AcceptsBareDigest_AndNormalizesCase()
    {
        Sha256Checksum.Parse(AbcSha256.ToUpperInvariant()).Should().Be(AbcSha256);
        Sha256Checksum.Parse($"  {AbcSha256}\n").Should().Be(AbcSha256);
    }

    [Fact]
    public void Parse_AcceptsSha256SumStyleLines()
    {
        Sha256Checksum.Parse($"{AbcSha256} *SafeDiskCleaner-portable.exe").Should().Be(AbcSha256);
        Sha256Checksum.Parse($"{AbcSha256}  SafeDiskCleaner-portable.exe").Should().Be(AbcSha256);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-hash")]
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015a")] // 63 chars
    [InlineData("zz7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")] // non-hex
    public void Parse_RejectsInvalidPayload(string payload)
    {
        Sha256Checksum.Parse(payload).Should().BeNull();
    }
}
