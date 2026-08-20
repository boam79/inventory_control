namespace Inventory.Core;

public static class RolePermissions
{
    public static PermissionFlags For(UserRole role) => role switch
    {
        UserRole.Administrator => new PermissionFlags(
            CanManageUsers: true,
            CanManageMasters: true,
            CanRegisterOpeningStock: true,
            CanReceive: true,
            CanRequestCancel: true,
            CanIssue: true,
            CanAdjust: true,
            CanApproveCancel: true,
            CanCloseMonth: true,
            CanBackup: true,
            CanChangeSettings: true,
            CanViewReports: true,
            CanViewDashboard: true,
            CanViewStock: true),
        UserRole.Purchasing => new PermissionFlags(
            CanManageUsers: false,
            CanManageMasters: false,
            CanRegisterOpeningStock: false,
            CanReceive: true,
            CanRequestCancel: true,
            CanIssue: false,
            CanAdjust: false,
            CanApproveCancel: false,
            CanCloseMonth: false,
            CanBackup: false,
            CanChangeSettings: false,
            CanViewReports: true,
            CanViewDashboard: true,
            CanViewStock: true),
        UserRole.DepartmentUser => new PermissionFlags(
            CanManageUsers: false,
            CanManageMasters: false,
            CanRegisterOpeningStock: false,
            CanReceive: false,
            CanRequestCancel: false,
            CanIssue: true,
            CanAdjust: false,
            CanApproveCancel: false,
            CanCloseMonth: false,
            CanBackup: false,
            CanChangeSettings: false,
            CanViewReports: true,
            CanViewDashboard: true,
            CanViewStock: true),
        UserRole.Viewer => new PermissionFlags(
            CanManageUsers: false,
            CanManageMasters: false,
            CanRegisterOpeningStock: false,
            CanReceive: false,
            CanRequestCancel: false,
            CanIssue: false,
            CanAdjust: false,
            CanApproveCancel: false,
            CanCloseMonth: false,
            CanBackup: false,
            CanChangeSettings: false,
            CanViewReports: true,
            CanViewDashboard: true,
            CanViewStock: true),
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "알 수 없는 역할입니다.")
    };
}
