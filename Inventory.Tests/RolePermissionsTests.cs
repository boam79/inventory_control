using Inventory.Core;

namespace Inventory.Tests;

public class RolePermissionsTests
{
    [Fact]
    public void Administrator_has_all_management_flags()
    {
        var flags = RolePermissions.For(UserRole.Administrator);

        Assert.True(flags.CanManageUsers);
        Assert.True(flags.CanManageMasters);
        Assert.True(flags.CanRegisterOpeningStock);
        Assert.True(flags.CanReceive);
        Assert.True(flags.CanIssue);
        Assert.True(flags.CanAdjust);
        Assert.True(flags.CanApproveCancel);
        Assert.True(flags.CanCloseMonth);
        Assert.True(flags.CanBackup);
        Assert.True(flags.CanChangeSettings);
        Assert.True(flags.CanViewReports);
        Assert.True(flags.CanViewDashboard);
    }

    [Fact]
    public void Purchasing_can_receive_but_cannot_issue_or_manage_users()
    {
        var flags = RolePermissions.For(UserRole.Purchasing);

        Assert.True(flags.CanReceive);
        Assert.True(flags.CanRequestCancel);
        Assert.True(flags.CanViewReports);
        Assert.False(flags.CanIssue);
        Assert.False(flags.CanManageUsers);
        Assert.False(flags.CanAdjust);
        Assert.False(flags.CanCloseMonth);
    }

    [Fact]
    public void DepartmentUser_can_issue_but_cannot_receive()
    {
        var flags = RolePermissions.For(UserRole.DepartmentUser);

        Assert.True(flags.CanIssue);
        Assert.True(flags.CanViewStock);
        Assert.False(flags.CanReceive);
        Assert.False(flags.CanManageUsers);
        Assert.False(flags.CanAdjust);
    }

    [Fact]
    public void Viewer_can_only_view()
    {
        var flags = RolePermissions.For(UserRole.Viewer);

        Assert.True(flags.CanViewDashboard);
        Assert.True(flags.CanViewReports);
        Assert.True(flags.CanViewStock);
        Assert.False(flags.CanReceive);
        Assert.False(flags.CanIssue);
        Assert.False(flags.CanManageUsers);
        Assert.False(flags.CanChangeSettings);
    }
}
