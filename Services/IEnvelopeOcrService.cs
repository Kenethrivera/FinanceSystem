using FinanceSystem.Models;

namespace FinanceSystem.Services
{
    public interface IEnvelopeOcrService
    {
        Task<EnvelopeScanResult> ScanAsync(string imagePath);
    }
}
