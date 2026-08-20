namespace Inventory.Core;

public enum LoginFailureReason
{
    None = 0,
    EmptyCredentials = 1,
    InvalidCredentials = 2
}

public static class LoginMessages
{
    public static string For(LoginFailureReason reason) => reason switch
    {
        LoginFailureReason.EmptyCredentials =>
            "원인: 아이디 또는 비밀번호가 비어 있습니다.\n조치: 아이디와 비밀번호를 모두 입력한 뒤 다시 로그인하세요.",
        LoginFailureReason.InvalidCredentials =>
            "원인: 아이디 또는 비밀번호가 올바르지 않습니다.\n조치: Caps Lock과 계정을 확인하세요. 계정이 없으면 관리자에게 생성을 요청하세요.",
        _ => string.Empty
    };
}
