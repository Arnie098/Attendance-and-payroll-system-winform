using System;
using Xunit;
using AttendancePayrollSystem.Services;

namespace AttendancePayrollSystem.Tests
{
    /// <summary>
    /// Tests for the PayrollDeductionConfig configurable deduction engine.
    /// </summary>
    public class PayrollDeductionConfigTests
    {
        #region Default Configuration Tests

        [Fact]
        public void Current_ShouldReturnNonNull()
        {
            var config = PayrollDeductionConfig.Current;
            Assert.NotNull(config);
        }

        [Fact]
        public void DefaultSssRate_ShouldBe4Point5Percent()
        {
            var config = PayrollDeductionConfig.Current;
            Assert.Equal(0.045m, config.SssRate);
        }

        [Fact]
        public void DefaultPhilHealthRate_ShouldBe2Percent()
        {
            var config = PayrollDeductionConfig.Current;
            Assert.Equal(0.02m, config.PhilHealthRate);
        }

        [Fact]
        public void DefaultPagIbigRate_ShouldBe2Percent()
        {
            var config = PayrollDeductionConfig.Current;
            Assert.Equal(0.02m, config.PagIbigRate);
        }

        [Fact]
        public void DefaultPagIbigCap_ShouldBe100()
        {
            var config = PayrollDeductionConfig.Current;
            Assert.Equal(100m, config.PagIbigCap);
        }

        [Fact]
        public void DefaultTaxBrackets_ShouldHave5Brackets()
        {
            var config = PayrollDeductionConfig.Current;
            Assert.Equal(5, config.TaxBrackets.Count);
        }

        [Fact]
        public void DefaultTaxBrackets_ShouldBeOrderedByFloor()
        {
            var config = PayrollDeductionConfig.Current;
            for (int i = 1; i < config.TaxBrackets.Count; i++)
            {
                Assert.True(config.TaxBrackets[i].Floor > config.TaxBrackets[i - 1].Floor);
            }
        }

        #endregion

        #region CalculateDeductions Tests

        [Fact]
        public void CalculateDeductions_ZeroGross_ShouldReturnZero()
        {
            var config = PayrollDeductionConfig.Current;
            var result = config.CalculateDeductions(0m);
            Assert.Equal(0m, result);
        }

        [Fact]
        public void CalculateDeductions_ShouldMatchExpectedFormula()
        {
            var config = PayrollDeductionConfig.Current;
            decimal grossPay = 20000m;

            // SSS: 20000 * 0.045 = 900
            // PhilHealth: 20000 * 0.02 = 400
            // Pag-IBIG: min(20000 * 0.02, 100) = 100
            // Tax: 937.50 + (20000 - 16666) * 0.20 = 937.50 + 666.80 = 1604.30
            // Total: 900 + 400 + 100 + 1604.30 = 3004.30
            decimal expected = 900m + 400m + 100m + (937.50m + (20000m - 16666m) * 0.20m);

            var result = config.CalculateDeductions(grossPay);
            Assert.Equal(Math.Round(expected, 2), Math.Round(result, 2));
        }

        [Fact]
        public void CalculateDeductions_LowGross_PagIbigBelowCap()
        {
            var config = PayrollDeductionConfig.Current;
            decimal grossPay = 3000m;

            // SSS: 3000 * 0.045 = 135
            // PhilHealth: 3000 * 0.02 = 60
            // Pag-IBIG: min(3000 * 0.02, 100) = min(60, 100) = 60
            // Tax: 0 (below 10417)
            // Total: 135 + 60 + 60 + 0 = 255
            decimal expected = 135m + 60m + 60m;

            var result = config.CalculateDeductions(grossPay);
            Assert.Equal(expected, result);
        }

        #endregion

        #region CalculateWithholdingTax Tests

        [Theory]
        [InlineData(0, 0)]
        [InlineData(5000, 0)]
        [InlineData(10417, 0)]
        [InlineData(10418, 0.15)]
        [InlineData(20000, 1604.30)]
        [InlineData(50000, 8437.70)]
        public void CalculateWithholdingTax_ShouldMatchBrackets(decimal grossPay, decimal expectedTax)
        {
            var config = PayrollDeductionConfig.Current;
            var result = config.CalculateWithholdingTax(grossPay);
            Assert.Equal(expectedTax, Math.Round(result, 2));
        }

        [Fact]
        public void CalculateWithholdingTax_HighestBracket_ShouldApply35Percent()
        {
            var config = PayrollDeductionConfig.Current;
            decimal grossPay = 500000m;

            // 91770.70 + (500000 - 333332) * 0.35 = 91770.70 + 58333.80 = 150104.50
            decimal expected = 91770.70m + (500000m - 333332m) * 0.35m;

            var result = config.CalculateWithholdingTax(grossPay);
            Assert.Equal(Math.Round(expected, 2), Math.Round(result, 2));
        }

        #endregion
    }
}
