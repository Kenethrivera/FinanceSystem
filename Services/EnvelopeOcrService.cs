using FinanceSystem.Models;
using System.Globalization;
using System.Text.RegularExpressions;
using Tesseract;

namespace FinanceSystem.Services
{
    public class EnvelopeOcrService : IEnvelopeOcrService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<EnvelopeOcrService> _logger;

        public EnvelopeOcrService(IWebHostEnvironment environment, ILogger<EnvelopeOcrService> logger)
        {
            _environment = environment;
            _logger = logger;
        }

        public async Task<EnvelopeScanResult> ScanAsync(string imagePath)
        {
            return await Task.Run(() =>
            {
                var tessDataPath = Path.Combine(_environment.WebRootPath, "tessdata");

                if (!Directory.Exists(tessDataPath))
                {
                    throw new DirectoryNotFoundException($"Tessdata folder not found: {tessDataPath}");
                }

                var result = new EnvelopeScanResult();

                using var engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
                using var img = Pix.LoadFromFile(imagePath);
                using var page = engine.Process(img, PageSegMode.Auto);

                var rawText = page.GetText() ?? "";
                result.RawText = NormalizeText(rawText);

                _logger.LogInformation("Envelope OCR raw text: {RawText}", result.RawText);

                // Date is optional only
                result.DateText = ExtractDate(result.RawText);

                // Name and amounts are the important parts
                result.Name = ExtractName(result.RawText);
                ExtractAmounts(result.RawText, out decimal tithes, out decimal offering, out decimal solomon);

                result.Tithes = tithes;
                result.Offering = offering;
                result.Solomon = solomon;

                _logger.LogInformation(
                    "Parsed values => Name: {Name}, Tithes: {Tithes}, Offering: {Offering}, Solomon: {Solomon}",
                    result.Name, result.Tithes, result.Offering, result.Solomon);

                return result;
            });
        }

        private static string NormalizeText(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            var text = input.Replace("\r", "\n");

            text = text.Replace("|", "")
                       .Replace("—", "-")
                       .Replace("–", "-")
                       .Replace("_", " ");

            text = Regex.Replace(text, @"[ \t]+", " ");
            text = Regex.Replace(text, @"\n{2,}", "\n\n");

            return text.Trim();
        }

        private static string ExtractDate(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            var normalized = NormalizeText(text);

            var patterns = new[]
            {
                @"Date\s*:\s*([0-9]{1,2}[/-][0-9]{1,2}[/-][0-9]{2,4})",
                @"Date\s*:\s*([A-Za-z]{3,9}\s+\d{1,2},?\s+\d{4})",
                @"Date\s*:\s*([0-9]{1,2}\s+[A-Za-z]{3,9}\s+[0-9]{2,4})"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(normalized, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                    return match.Groups[1].Value.Trim();
            }

            return "";
        }

        private static string ExtractName(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            var normalized = NormalizeText(text);

            var labeledMatch = Regex.Match(
                normalized,
                @"Name\s*:\s*([^\n]+)",
                RegexOptions.IgnoreCase);

            string name = labeledMatch.Success
                ? labeledMatch.Groups[1].Value.Trim()
                : "";

            if (string.IsNullOrWhiteSpace(name))
                return "";

            var stopWords = new[] { "Tithes", "Offering", "Solomon", "Date" };
            foreach (var stop in stopWords)
            {
                var idx = name.IndexOf(stop, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    name = name[..idx].Trim();
                }
            }

            name = name.Trim('-', ' ', ':');
            name = Regex.Replace(name, @"^\bNam[e]?\b\s*", "", RegexOptions.IgnoreCase).Trim();

            return name;
        }

        private static void ExtractAmounts(string text, out decimal tithes, out decimal offering, out decimal solomon)
        {
            tithes = 0m;
            offering = 0m;
            solomon = 0m;

            if (string.IsNullOrWhiteSpace(text))
                return;

            var normalized = NormalizeText(text);

            // First try strict label-based extraction
            tithes = ExtractLabeledAmount(normalized, "Tithes", new[] { "Offering", "Solomon" });
            offering = ExtractLabeledAmount(normalized, "Offering", new[] { "Solomon" });
            solomon = ExtractLabeledAmount(normalized, "Solomon", Array.Empty<string>());

            // If OCR merged labels badly, try ordered fallback
            if ((tithes == 0m && offering == 0m && solomon == 0m) ||
                CountDetectedAmounts(tithes, offering, solomon) <= 1)
            {
                ExtractAmountsByOrder(normalized, out tithes, out offering, out solomon);
            }
        }

        private static int CountDetectedAmounts(decimal t, decimal o, decimal s)
        {
            int count = 0;
            if (t > 0) count++;
            if (o > 0) count++;
            if (s > 0) count++;
            return count;
        }

        private static void ExtractAmountsByOrder(string text, out decimal tithes, out decimal offering, out decimal solomon)
        {
            tithes = 0m;
            offering = 0m;
            solomon = 0m;

            var amountLineMatch = Regex.Match(
                text,
                @"Tithes\s*:.*",
                RegexOptions.IgnoreCase);

            var amountText = amountLineMatch.Success ? amountLineMatch.Value : text;

            amountText = amountText.Replace("O", "0")
                                   .Replace("o", "0")
                                   .Replace("I", "1")
                                   .Replace("l", "1");

            var matches = Regex.Matches(amountText, @"\d+(?:[.,]\d{1,2})?");
            var numbers = new List<decimal>();

            foreach (Match match in matches)
            {
                var raw = match.Value.Replace(",", ".").Trim();
                if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                {
                    numbers.Add(value);
                }
            }

            if (numbers.Count > 0) tithes = numbers[0];
            if (numbers.Count > 1) offering = numbers[1];
            if (numbers.Count > 2) solomon = numbers[2];
        }

        private static decimal ExtractLabeledAmount(string text, string label, string[] stopLabels)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0m;

            var startMatch = Regex.Match(text, $@"{label}\s*:\s*", RegexOptions.IgnoreCase);
            if (!startMatch.Success)
                return 0m;

            var startIndex = startMatch.Index + startMatch.Length;
            var remaining = text[startIndex..];

            int endIndex = remaining.Length;
            foreach (var stopLabel in stopLabels)
            {
                var stopMatch = Regex.Match(remaining, $@"\b{stopLabel}\b", RegexOptions.IgnoreCase);
                if (stopMatch.Success && stopMatch.Index < endIndex)
                {
                    endIndex = stopMatch.Index;
                }
            }

            var segment = remaining[..endIndex].Trim();

            segment = segment.Replace("O", "0")
                             .Replace("o", "0")
                             .Replace("I", "1")
                             .Replace("l", "1");

            var numberMatch = Regex.Match(segment, @"\d+(?:[.,]\d{1,2})?");
            if (!numberMatch.Success)
                return 0m;

            var raw = numberMatch.Value.Replace(",", ".").Trim();

            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            return 0m;
        }
    }
}