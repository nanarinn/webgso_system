package my.webgso;

import java.security.SecureRandom;
import java.time.*;
import java.util.*;
import my.webgso.Models.*;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
public class PasswordChange {
  private final Store db;
  private final Accounts accounts;
  private final IdentityPasswordEncoder encoder = new IdentityPasswordEncoder();

  public PasswordChange(Store db, Accounts accounts) {
    this.db = db;
    this.accounts = accounts;
  }

  private ApplicationUser owner(String id) {
    var user =
        db.list(ApplicationUser.class, "WHERE \"Id\"=? FOR UPDATE", id).stream()
            .findFirst()
            .orElseThrow(() -> new IllegalArgumentException("Account not found."));
    if (!"Approved".equals(user.accessStatus)
        || Boolean.TRUE.equals(user.isEntraUser)
        || !GmailAccounts.allowed(user.email)
        || user.passwordHash == null)
      throw new IllegalArgumentException("This account cannot change its password here.");
    return user;
  }

  private List<Map<String, Object>> challenge(String id) {
    return db.jdbc()
        .queryForList(
            "SELECT * FROM \"GsoPasswordChanges\" WHERE \"UserId\"=?" + " AND \"ExpiresAt\">?",
            id,
            OffsetDateTime.now(ZoneOffset.UTC));
  }

  private boolean bound(Map<String, Object> row, ApplicationUser user) {
    return Objects.equals(row.get("SecurityStamp"), user.securityStamp)
        && Objects.equals(row.get("Email"), user.email);
  }

  public String stage(String id) {
    var rows = challenge(id);
    var user = db.find(ApplicationUser.class, id).orElseThrow();
    if (rows.isEmpty() || !bound(rows.getFirst(), user)) return "request";
    var row = rows.getFirst();
    if (row.get("VerifiedAt") != null) return "change";
    return ((Number) row.get("Attempts")).intValue() < 5 ? "verify" : "request";
  }

  @Transactional
  public void request(String id, String currentPassword) {
    var user = owner(id);
    if (currentPassword == null
        || currentPassword.length() > 256
        || !encoder.matches(currentPassword, user.passwordHash))
      throw new IllegalArgumentException("Your current password is incorrect.");
    var now = OffsetDateTime.now(ZoneOffset.UTC);
    if (db.jdbc()
            .queryForObject(
                "SELECT COUNT(*) FROM \"GsoPasswordChanges\" WHERE"
                    + " \"UserId\"=? AND \"RequestedAt\">?",
                Long.class,
                id,
                now.minusMinutes(1))
        > 0) throw new IllegalArgumentException("Wait 60 seconds before requesting another code.");
    String code = String.format(Locale.ROOT, "%06d", new SecureRandom().nextInt(1000000));
    db.jdbc().update("DELETE FROM \"GsoPasswordChanges\" WHERE \"UserId\"=?", id);
    db.jdbc()
        .update(
            "INSERT INTO \"GsoPasswordChanges\""
                + " (\"UserId\",\"Email\",\"CodeHash\",\"SecurityStamp\",\"RequestedAt\",\"ExpiresAt\",\"Attempts\")"
                + " VALUES (?,?,?,?,?,?,0)",
            id,
            user.email,
            encoder.encode(code),
            user.securityStamp,
            now,
            now.plusMinutes(10));
    clearQueued(id, "Verification code replaced.");
    accounts.queueNotification(
        id,
        id,
        "GSO Web Services - Password change verification",
        "Your password change verification code is: "
            + code
            + "\n\n"
            + "Enter this code in My Profile to continue. It expires in 10 minutes and can be used"
            + " once. Your password has not changed. If you did not request this, secure your"
            + " account.",
        "PasswordChangeCode");
  }

  @Transactional
  public boolean verify(String id, String code) {
    var user = owner(id);
    var rows = challenge(id);
    if (rows.isEmpty()) return false;
    var row = rows.getFirst();
    if (!bound(row, user)
        || row.get("VerifiedAt") != null
        || ((Number) row.get("Attempts")).intValue() >= 5) return false;
    db.jdbc()
        .update(
            "UPDATE \"GsoPasswordChanges\" SET \"Attempts\"=\"Attempts\"+1 WHERE \"UserId\"=?", id);
    if (code == null
        || !code.matches("[0-9]{6}")
        || !encoder.matches(code, (String) row.get("CodeHash"))) return false;
    db.jdbc()
        .update(
            "UPDATE \"GsoPasswordChanges\" SET \"VerifiedAt\"=? WHERE \"UserId\"=?",
            OffsetDateTime.now(ZoneOffset.UTC),
            id);
    clearQueued(id, "Verification completed.");
    return true;
  }

  @Transactional
  public void change(String id, String password, String confirmation) {
    var user = owner(id);
    var rows = challenge(id);
    if (rows.isEmpty()
        || !bound(rows.getFirst(), user)
        || rows.getFirst().get("VerifiedAt") == null)
      throw new IllegalArgumentException("Verify a new email code before changing your password.");
    if (!Objects.equals(password, confirmation))
      throw new IllegalArgumentException("Passwords do not match.");
    Accounts.validatePassword(password);
    if (encoder.matches(password, user.passwordHash))
      throw new IllegalArgumentException("Choose a password different from your current password.");
    db.jdbc()
        .update(
            "UPDATE \"AspNetUsers\" SET"
                + " \"PasswordHash\"=?,\"SecurityStamp\"=?,\"ConcurrencyStamp\"=?,\"AccessFailedCount\"=0,\"LockoutEnd\"=NULL"
                + " WHERE \"Id\"=?",
            encoder.encode(password),
            UUID.randomUUID().toString(),
            UUID.randomUUID().toString(),
            id);
    db.jdbc().update("DELETE FROM \"GsoPasswordChanges\" WHERE \"UserId\"=?", id);
    accounts.queueNotification(
        id,
        id,
        "GSO Web Services - Password changed",
        "Your GSO password has been changed. Sign in with your new password. If you did not make"
            + " this change, reset your password and secure your Gmail account.",
        "PasswordReset");
  }

  private void clearQueued(String id, String text) {
    db.jdbc()
        .update(
            "UPDATE \"UserAccessNotifications\" SET \"DeliveryStatus\"=CASE WHEN"
                + " \"DeliveryStatus\"='Pending' THEN 'Cancelled' ELSE \"DeliveryStatus\" END,"
                + " \"Body\"=? WHERE \"UserId\"=? AND \"NotificationType\"='PasswordChangeCode'",
            text,
            id);
  }
}
