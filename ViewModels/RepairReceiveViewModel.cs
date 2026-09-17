using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;
using PhoneAccounting.App.Services;

namespace PhoneAccounting.App.ViewModels;

public partial class RepairReceiveViewModel : ObservableObject
{
    [ObservableProperty]
    private string customerName = "";

    [ObservableProperty]
    private string customerPhone = "";

    [ObservableProperty]
    private string deviceName = "";

    [ObservableProperty]
    private string issue = "";

    [ObservableProperty]
    private string notes = "";

    [ObservableProperty]
    private string laborFeeText = "";

    [ObservableProperty]
    private string resultMessage = "";

    [ObservableProperty]
    private bool isResultError;

    public event EventHandler? Saved;

    public string DateText => DateTime.Now.ToString("dddd، dd MMMM yyyy",
        new System.Globalization.CultureInfo("ar-IQ"));

    public RepairReceiveViewModel()
    {
        LaborFeeText = Database.GetSetting("RepairDefaultFee", "25000");
    }

    [RelayCommand]
    private void Save()
    {
        ResultMessage = "";
        IsResultError = false;

        if (string.IsNullOrWhiteSpace(CustomerName))
        {
            ShowError("أدخل اسم العميل");
            return;
        }

        if (string.IsNullOrWhiteSpace(DeviceName))
        {
            ShowError("أدخل نوع الجهاز / اسم الجهاز");
            return;
        }

        if (string.IsNullOrWhiteSpace(Issue))
        {
            ShowError("أدخل وصف العطل");
            return;
        }

        if (!decimal.TryParse(LaborFeeText, out var laborFee) || laborFee < 0)
        {
            ShowError("أدخل أجرة صيانة صحيحة");
            return;
        }

        try
        {
            using var db = new AppDbContext();
            var dayCount = InvoiceService.NextRepairNumber();

            var job = new RepairJob
            {
                JobNumber = InvoiceService.RepairJobNumber(dayCount),
                ReceivedDate = DateTime.Now,
                CompletedDate = null,
                DeliveredDate = null,
                CustomerName = CustomerName.Trim(),
                CustomerPhone = string.IsNullOrWhiteSpace(CustomerPhone) ? null : CustomerPhone.Trim(),
                DeviceName = DeviceName.Trim(),
                Issue = Issue.Trim(),
                Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
                Status = RepairStatus.Received,
                UserId = Session.CurrentUser?.Id ?? 0,
                Device = Session.Device,
                LaborFee = laborFee,
                PaymentMethod = PaymentMethod.Cash,
                IsPaid = false,
                PartsTotal = 0,
                Total = laborFee
            };

            db.RepairJobs.Add(job);

            // سجل العمليات: يُمنح Seq عند Commit مع نفس الحفظ
            var repairOp = OperationWriter.Register(
                db,
                OperationType.RepairJob,
                job.JobNumber,
                job.Total,
                entityName: "RepairJobs");

            db.SaveChanges();

            // M2.4: ربط معرّف الكيان للمستند الجديد (يصل الـ Id بعد الحفظ) — فرض وجود EntityId في مسار الكتابة
            if (job.Id > 0 && repairOp.EntityId is null)
            {
                OperationWriter.SetEntityTarget(repairOp, "RepairJobs", job.Id);
                db.SaveChanges();
            }

            ResultMessage = $"تم استقبال الجهاز برقم قطعة {job.JobNumber}";
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ShowError($"خطأ في الحفظ: {ex.Message}");
        }
    }

    private void ShowError(string message)
    {
        ResultMessage = message;
        IsResultError = true;
    }
}
