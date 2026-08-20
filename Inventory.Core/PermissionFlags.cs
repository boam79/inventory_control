namespace Inventory.Core;

public sealed record PermissionFlags(
    bool CanManageUsers,
    bool CanManageMasters,
    bool CanRegisterOpeningStock,
    bool CanReceive,
    bool CanRequestCancel,
    bool CanIssue,
    bool CanAdjust,
    bool CanApproveCancel,
    bool CanCloseMonth,
    bool CanBackup,
    bool CanChangeSettings,
    bool CanViewReports,
    bool CanViewDashboard,
    bool CanViewStock);
