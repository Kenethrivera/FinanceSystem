using FinanceSystem.Models;
using FinanceSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace FinanceSystem.Pages.SmartScan
{
    public class IndexModel : PageModel
    {
        private readonly IWebHostEnvironment _environment;
        private readonly IEnvelopeOcrService _ocrService;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(IWebHostEnvironment environment, IEnvelopeOcrService ocrService, ILogger<IndexModel> logger)
        {
            _environment = environment;
            _ocrService = ocrService;
            _logger = logger;
        }

        [BindProperty]
        [Required]
        public IFormFile? Upload { get; set; }

        [BindProperty]
        public EnvelopeScanResult Result { get; set; } = new();

        [BindProperty]
        public string UploadedImageUrl { get; set; } = "";

        public string ErrorMessage { get; set; } = "";
        public bool HasResult { get; set; }

        public void OnGet() { }

        public async Task<IActionResult> OnPostExtractAsync()
        {
            HasResult = false;

            if (Upload == null || Upload.Length == 0)
            {
                ErrorMessage = "Please upload an image first.";
                return Page();
            }

            var ext = Path.GetExtension(Upload.FileName).ToLowerInvariant();
            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };

            if (!allowed.Contains(ext))
            {
                ErrorMessage = "Only JPG, JPEG, PNG, and WEBP are allowed.";
                return Page();
            }

            if (Upload.Length > 10 * 1024 * 1024)
            {
                ErrorMessage = "File is too large. Maximum is 10 MB.";
                return Page();
            }

            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "envelope-scans");
            Directory.CreateDirectory(uploadsFolder);

            var fileName = $"{Guid.NewGuid()}{ext}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await Upload.CopyToAsync(stream);
            }

            UploadedImageUrl = $"/uploads/envelope-scans/{fileName}";

            try
            {
                Result = await _ocrService.ScanAsync(filePath);
                HasResult = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Envelope OCR failed.");
                ErrorMessage = $"OCR failed: {ex.Message}";
            }

            return Page();
        }

        public IActionResult OnPostConfirm()
        {
            HasResult = true;

            var payload = new EnvelopeScanPayload
            {
                Name = Result.Name,
                Tithes = Result.Tithes,
                Offering = Result.Offering,
                Solomon = Result.Solomon
            };

            HttpContext.Session.SetString("PendingEnvelopeScan",
                System.Text.Json.JsonSerializer.Serialize(payload));

            TempData["SuccessMessage"] = "Scanned envelope is ready to be added to New Entry.";
            return RedirectToPage("/FinancialDays/Add");
        }

        public class EnvelopeScanPayload
        {
            public string Name { get; set; } = "";
            public decimal Tithes { get; set; }
            public decimal Offering { get; set; }
            public decimal Solomon { get; set; }
        }

    }
}
