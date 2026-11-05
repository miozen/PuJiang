using Xunit;
using ZupuPaiBan.Domain;

namespace PuJiang.Tests;

public class RecordedGenerationParserTests
{
    [Theory]
    [InlineData("1", 1)] [InlineData("第 12 世", 12)] [InlineData("一世", 1)]
    [InlineData("十一世", 11)] [InlineData("二十三世", 23)] [InlineData("一百零二世", 102)]
    [InlineData("一百二十世", 120)] [InlineData("二〇二", 202)] [InlineData("一千世", 1000)]
    public void RecognizesCommonExplicitFormsWithoutChangingSource(string text, int expected)
    { Assert.True(RecordedGenerationParser.TryParse(text, out var actual)); Assert.Equal(expected, actual); }

    [Theory]
    [InlineData("")] [InlineData("0")] [InlineData("1001")] [InlineData("一百二")]
    [InlineData("十一百")] [InlineData("二十零")] [InlineData("十零二")] [InlineData("第十代")] [InlineData("1-2")]
    public void RejectsAmbiguousOrUnsupportedText(string text) => Assert.False(RecordedGenerationParser.TryParse(text, out _));
}
