using System.Collections.Generic;
using System.Threading.Tasks;
using SewerScan.Application.Models;
using SewerScan.Infrastructure.Parsers;
using Xunit;

namespace SewerScan.Application.Tests
{
    public class SewerProjectParserTests
    {
        [Fact]
        public async Task ParsesManholeAndInletAndPipe()
        {
            var parser = new SewerProjectParser();

            var pages = new List<PageText>
            {
                new PageText
                {
                    PageNumber = 1,
                    Text = "KD 123\r\nWp 45\r\nDN 200 PVC"
                }
            };

            var result = await parser.ParseAsync(pages);

            Assert.Single(result.Manholes);
            Assert.Equal("123", result.Manholes[0].Identifier);

            Assert.Single(result.Inlets);
            Assert.Equal("45", result.Inlets[0].Identifier);

            Assert.Single(result.Pipes);
            Assert.Equal(200, result.Pipes[0].DiameterMm);
            Assert.Equal("PVC", result.Pipes[0].Material, ignoreCase: true);
        }
    }
}
