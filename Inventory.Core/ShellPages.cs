namespace Inventory.Core;

public static class ShellPages
{
    public static readonly IReadOnlyList<string> MenuTags =
    [
        "dashboard",
        "stock",
        "receive",
        "issue",
        "stats",
        "users",
        "backup",
        "settings"
    ];

    public static string Title(string tag) => tag switch
    {
        "dashboard" => "대시보드",
        "stock" => "재고현황",
        "receive" => "입고",
        "issue" => "사용",
        "stats" => "통계·보고서",
        "users" => "사용자·권한",
        "backup" => "백업·복원",
        "settings" => "환경설정",
        _ => "재고관리"
    };

    public static string Hint(string tag) => tag switch
    {
        "dashboard" => "품목을 선택하면 사용 추이를 비교합니다.",
        "stock" => "로컬 · 오프라인",
        "receive" => "품목을 이름으로 고른 뒤 담아 저장합니다.",
        "issue" => "재고는 의원 한 곳입니다. 부서는 기록만 남깁니다.",
        "stats" => "연도가 다른 같은 달은 합치지 않습니다.",
        "users" => "비밀번호는 저장하지 않고 해시만 남깁니다.",
        "backup" => "엑셀 파일 가져오기·내보내기는 여기에서만 합니다.",
        "settings" => "경고일·백업 폴더·프로그램 업데이트입니다.",
        _ => ""
    };

    public static readonly IReadOnlyList<string> NavOrder =
    [
        "receive", "issue", "stock", "stats",
        "dashboard", "users", "backup", "settings"
    ];

    public static string NavLabel(string tag) => tag switch
    {
        "dashboard" => "대시보드",
        "stock" => "재고",
        "receive" => "입고",
        "issue" => "사용",
        "stats" => "통계",
        "users" => "사용자",
        "backup" => "백업",
        "settings" => "설정",
        _ => Title(tag)
    };

    public static bool CanSee(string tag, PermissionFlags flags) => tag switch
    {
        "dashboard" => flags.CanViewDashboard,
        "stock" => flags.CanViewStock,
        "receive" => flags.CanReceive,
        "issue" => flags.CanIssue,
        "stats" => flags.CanViewReports,
        "users" => flags.CanManageUsers,
        "backup" => flags.CanBackup,
        "settings" => flags.CanChangeSettings,
        _ => false
    };

    public static IReadOnlyList<string> VisibleTags(PermissionFlags flags) =>
        MenuTags.Where(tag => CanSee(tag, flags)).ToArray();
}
