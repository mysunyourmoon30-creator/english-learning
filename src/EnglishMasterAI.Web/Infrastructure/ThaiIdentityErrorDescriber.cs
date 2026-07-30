using Microsoft.AspNetCore.Identity;

namespace EnglishMasterAI.Web.Infrastructure;

public sealed class ThaiIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError() =>
        Error(nameof(DefaultError), "เกิดข้อผิดพลาดที่ไม่คาดคิด กรุณาลองอีกครั้ง");

    public override IdentityError ConcurrencyFailure() =>
        Error(nameof(ConcurrencyFailure), "ข้อมูลถูกแก้ไขไปแล้ว กรุณาโหลดหน้าใหม่และลองอีกครั้ง");

    public override IdentityError PasswordMismatch() =>
        Error(nameof(PasswordMismatch), "รหัสผ่านไม่ถูกต้อง");

    public override IdentityError InvalidToken() =>
        Error(nameof(InvalidToken), "โทเคนยืนยันไม่ถูกต้องหรือหมดอายุแล้ว");

    public override IdentityError LoginAlreadyAssociated() =>
        Error(nameof(LoginAlreadyAssociated), "บัญชีภายนอกนี้เชื่อมกับผู้ใช้อื่นแล้ว");

    public override IdentityError InvalidUserName(string? userName) =>
        Error(nameof(InvalidUserName), $"ชื่อผู้ใช้ '{userName}' ไม่ถูกต้อง");

    public override IdentityError InvalidEmail(string? email) =>
        Error(nameof(InvalidEmail), $"อีเมล '{email}' ไม่ถูกต้อง");

    public override IdentityError DuplicateUserName(string userName) =>
        Error(nameof(DuplicateUserName), $"ชื่อผู้ใช้ '{userName}' ถูกใช้งานแล้ว");

    public override IdentityError DuplicateEmail(string email) =>
        Error(nameof(DuplicateEmail), $"อีเมล '{email}' ถูกใช้งานแล้ว");

    public override IdentityError InvalidRoleName(string? role) =>
        Error(nameof(InvalidRoleName), $"ชื่อบทบาท '{role}' ไม่ถูกต้อง");

    public override IdentityError DuplicateRoleName(string role) =>
        Error(nameof(DuplicateRoleName), $"บทบาท '{role}' มีอยู่แล้ว");

    public override IdentityError UserAlreadyHasPassword() =>
        Error(nameof(UserAlreadyHasPassword), "บัญชีนี้มีรหัสผ่านอยู่แล้ว");

    public override IdentityError UserLockoutNotEnabled() =>
        Error(nameof(UserLockoutNotEnabled), "บัญชีนี้ไม่ได้เปิดใช้การล็อกเมื่อเข้าสู่ระบบผิด");

    public override IdentityError UserAlreadyInRole(string role) =>
        Error(nameof(UserAlreadyInRole), $"ผู้ใช้อยู่ในบทบาท '{role}' แล้ว");

    public override IdentityError UserNotInRole(string role) =>
        Error(nameof(UserNotInRole), $"ผู้ใช้ไม่ได้อยู่ในบทบาท '{role}'");

    public override IdentityError PasswordTooShort(int length) =>
        Error(nameof(PasswordTooShort), $"รหัสผ่านต้องมีอย่างน้อย {length} ตัวอักษร");

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        Error(
            nameof(PasswordRequiresUniqueChars),
            $"รหัสผ่านต้องมีอักขระที่ไม่ซ้ำกันอย่างน้อย {uniqueChars} ตัว");

    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        Error(nameof(PasswordRequiresNonAlphanumeric), "รหัสผ่านต้องมีอักขระพิเศษอย่างน้อย 1 ตัว");

    public override IdentityError PasswordRequiresDigit() =>
        Error(nameof(PasswordRequiresDigit), "รหัสผ่านต้องมีตัวเลขอย่างน้อย 1 ตัว");

    public override IdentityError PasswordRequiresLower() =>
        Error(nameof(PasswordRequiresLower), "รหัสผ่านต้องมีตัวอักษรภาษาอังกฤษพิมพ์เล็กอย่างน้อย 1 ตัว");

    public override IdentityError PasswordRequiresUpper() =>
        Error(nameof(PasswordRequiresUpper), "รหัสผ่านต้องมีตัวอักษรภาษาอังกฤษพิมพ์ใหญ่อย่างน้อย 1 ตัว");

    public override IdentityError RecoveryCodeRedemptionFailed() =>
        Error(nameof(RecoveryCodeRedemptionFailed), "Recovery Code ไม่ถูกต้อง");

    private static IdentityError Error(string code, string description) =>
        new() { Code = code, Description = description };
}
