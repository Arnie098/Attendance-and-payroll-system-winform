using AttendancePayrollSystem.Services;
using Xunit;

namespace AttendancePayrollSystem.Tests;

public class QrCodeServiceTests
{
    private readonly QrCodeService _sut = new();

    [Fact]
    public void GeneratePng_ReturnsNonEmptyBytes_ForValidCode()
    {
        byte[] result = _sut.GeneratePng("EMP-001");
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void GeneratePng_ReturnsPngMagicBytes_ForValidCode()
    {
        byte[] result = _sut.GeneratePng("T-1001");
        // PNG files start with: 89 50 4E 47
        Assert.Equal(0x89, result[0]);
        Assert.Equal(0x50, result[1]);
        Assert.Equal(0x4E, result[2]);
        Assert.Equal(0x47, result[3]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GeneratePng_ThrowsArgumentException_ForBlankCode(string? code)
    {
        Assert.Throws<ArgumentException>(() => _sut.GeneratePng(code!));
    }
}
