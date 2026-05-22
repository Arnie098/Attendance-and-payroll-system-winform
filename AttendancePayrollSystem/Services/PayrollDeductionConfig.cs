using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;

namespace AttendancePayrollSystem.Services
{
    /// <summary>
    /// Configurable payroll deduction rates and withholding tax brackets.
    /// Values can be overridden via environment variables or App.config appSettings.
    /// Defaults reflect current Philippine statutory rates.
    /// </summary>
    public sealed class PayrollDeductionConfig
    {
        private const string SssRateEnvVar = "ATTENDANCE_SSS_RATE";
        private const string PhilHealthRateEnvVar = "ATTENDANCE_PHILHEALTH_RATE";
        private const string PagIbigRateEnvVar = "ATTENDANCE_PAGIBIG_RATE";
        private const string PagIbigCapEnvVar = "ATTENDANCE_PAGIBIG_CAP";
        private const string TaxBracketsEnvVar = "ATTENDANCE_TAX_BRACKETS";

        private const string SssRateSetting = "PayrollSssRate";
        private const string PhilHealthRateSetting = "PayrollPhilHealthRate";
        private const string PagIbigRateSetting = "PayrollPagIbigRate";
        private const string PagIbigCapSetting = "PayrollPagIbigCap";
        private const string TaxBracketsSetting = "PayrollTaxBrackets";

        // Default Philippine statutory rates
        private const decimal DefaultSssRate = 0.045m;
        private const decimal DefaultPhilHealthRate = 0.02m;
        private const decimal DefaultPagIbigRate = 0.02m;
        private const decimal DefaultPagIbigCap = 100m;

        private static PayrollDeductionConfig? _current;
        private static readonly object _lock = new();

        public decimal SssRate { get; }
        public decimal PhilHealthRate { get; }
        public decimal PagIbigRate { get; }
        public decimal PagIbigCap { get; }
        public IReadOnlyList<TaxBracket> TaxBrackets { get; }

        private PayrollDeductionConfig(
            decimal sssRate,
            decimal philHealthRate,
            decimal pagIbigRate,
            decimal pagIbigCap,
            IReadOnlyList<TaxBracket> taxBrackets)
        {
            SssRate = sssRate;
            PhilHealthRate = philHealthRate;
            PagIbigRate = pagIbigRate;
            PagIbigCap = pagIbigCap;
            TaxBrackets = taxBrackets;
        }

        /// <summary>
        /// Gets the current configuration, loading from environment/appSettings on first access.
        /// </summary>
        public static PayrollDeductionConfig Current
        {
            get
            {
                if (_current != null) return _current;
                lock (_lock)
                {
                    _current ??= Load();
                }
                return _current;
            }
        }

        /// <summary>
        /// Forces a reload of the configuration from environment/appSettings.
        /// </summary>
        public static void Reload()
        {
            lock (_lock)
            {
                _current = Load();
            }
        }

        /// <summary>
        /// Calculates total deductions for a given gross pay using the configured rates.
        /// </summary>
        public decimal CalculateDeductions(decimal grossPay)
        {
            var sss = grossPay * SssRate;
            var philHealth = grossPay * PhilHealthRate;
            var pagIbig = Math.Min(grossPay * PagIbigRate, PagIbigCap);
            var tax = CalculateWithholdingTax(grossPay);

            return sss + philHealth + pagIbig + tax;
        }

        /// <summary>
        /// Calculates withholding tax using the configured brackets.
        /// </summary>
        public decimal CalculateWithholdingTax(decimal grossPay)
        {
            for (int i = TaxBrackets.Count - 1; i >= 0; i--)
            {
                var bracket = TaxBrackets[i];
                if (grossPay > bracket.Floor)
                {
                    return bracket.BaseTax + (grossPay - bracket.Floor) * bracket.Rate;
                }
            }

            return 0m;
        }

        private static PayrollDeductionConfig Load()
        {
            var sssRate = GetDecimalValue(SssRateSetting, SssRateEnvVar, DefaultSssRate);
            var philHealthRate = GetDecimalValue(PhilHealthRateSetting, PhilHealthRateEnvVar, DefaultPhilHealthRate);
            var pagIbigRate = GetDecimalValue(PagIbigRateSetting, PagIbigRateEnvVar, DefaultPagIbigRate);
            var pagIbigCap = GetDecimalValue(PagIbigCapSetting, PagIbigCapEnvVar, DefaultPagIbigCap);
            var taxBrackets = LoadTaxBrackets();

            return new PayrollDeductionConfig(sssRate, philHealthRate, pagIbigRate, pagIbigCap, taxBrackets);
        }

        private static IReadOnlyList<TaxBracket> LoadTaxBrackets()
        {
            var raw = GetStringValue(TaxBracketsSetting, TaxBracketsEnvVar);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var parsed = ParseTaxBrackets(raw);
                if (parsed.Count > 0) return parsed;
            }

            // Default Philippine withholding tax brackets (monthly)
            return new List<TaxBracket>
            {
                new(10417m, 0m, 0.15m),
                new(16666m, 937.50m, 0.20m),
                new(33332m, 4270.70m, 0.25m),
                new(83332m, 16770.70m, 0.30m),
                new(333332m, 91770.70m, 0.35m)
            };
        }

        /// <summary>
        /// Parses tax brackets from a semicolon-separated string.
        /// Format: "floor:baseTax:rate;floor:baseTax:rate;..."
        /// Example: "10417:0:0.15;16666:937.50:0.20;33332:4270.70:0.25;83332:16770.70:0.30;333332:91770.70:0.35"
        /// </summary>
        private static List<TaxBracket> ParseTaxBrackets(string raw)
        {
            var brackets = new List<TaxBracket>();

            foreach (var segment in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = segment.Split(':');
                if (parts.Length != 3) continue;

                if (decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var floor) &&
                    decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var baseTax) &&
                    decimal.TryParse(parts[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var rate))
                {
                    brackets.Add(new TaxBracket(floor, baseTax, rate));
                }
            }

            return brackets.OrderBy(b => b.Floor).ToList();
        }

        private static decimal GetDecimalValue(string appSettingKey, string envVar, decimal defaultValue)
        {
            var raw = GetStringValue(appSettingKey, envVar);
            if (!string.IsNullOrWhiteSpace(raw) &&
                decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return defaultValue;
        }

        private static string? GetStringValue(string appSettingKey, string envVar)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(value)) return value;

            value = ConfigurationManager.AppSettings[appSettingKey];
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    /// <summary>
    /// Represents a single withholding tax bracket.
    /// </summary>
    public sealed record TaxBracket(decimal Floor, decimal BaseTax, decimal Rate);
}
