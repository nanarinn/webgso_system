package my.webgso;

import java.security.SecureRandom;
import java.time.*;
import java.util.*;
import my.webgso.Models.*;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
public class EmailChange {
  private final Store db;
  private final Accounts accounts;
  private final IdentityPasswordEncoder encoder = new IdentityPasswordEncoder();

  public EmailChange(Store db, Accounts accounts) {
    this.db = db;
    this.accounts = accounts;
  }

  private ApplicationUser owner(String id) {
    var u =
        db.list(ApplicationUser.class, "WHERE \"Id\"=? FOR UPDATE", id).stream()
            .findFirst()
            .orElseThrow(() -> new IllegalArgumentException("Account not found."));
    if (!"Approved".equals(u.accessStatus) || Boolean.TRUE.equals(u.isEntraUser))
      throw new IllegalArgumentException("This account cannot change its email here.");
    return u;
  }

  private void available(String id, String email) {
    GmailAccounts.require(email);
    if (email.length() > 256
        || !db.list(
                ApplicationUser.class,
                "WHERE \"Id\"<>? AND (\"NormalizedEmail\"=? OR \"NormalizedUserName\"=?)",
                id,
                email.toUpperCase(Locale.ROOT),
                email.toUpperCase(Locale.ROOT))
            .isEmpty())
      throw new IllegalArgumentException("That Gmail address is already in use or invalid.");
  }

  public Optional<String> pending(String id) {
    return db
        .jdbc()
        .queryForList(
            "SELECT \"NewEmail\" FROM \"GsoEmailChanges\" WHERE"
                + " \"UserId\"=? AND \"ExpiresAt\">? AND \"Attempts\"<5",
            String.class,
            id,
            OffsetDateTime.now(ZoneOffset.UTC))
        .stream()
        .findFirst();
  }

  @Transactional
  public void request(String id, String email, String password) {
    var u = owner(id);
    if (password == null || password.length() > 256 || !encoder.matches(password, u.passwordHash))
      throw new IllegalArgumentException("Enter your current password to change your email.");
    email = email.strip().toLowerCase(Locale.ROOT);
    available(id, email);
    if (email.equalsIgnoreCase(u.email))
      throw new IllegalArgumentException("Enter a different Gmail address.");
    var now = OffsetDateTime.now(ZoneOffset.UTC);
    if (db.jdbc()
            .queryForObject(
                "SELECT COUNT(*) FROM \"GsoEmailChanges\" WHERE"
                    + " \"UserId\"=? AND \"RequestedAt\">?",
                Long.class,
                id,
                now.minusMinutes(1))
        > 0) throw new IllegalArgumentException("Wait 60 seconds before requesting another code.");
    String code = String.format(Locale.ROOT, "%06d", new SecureRandom().nextInt(1000000));
    db.jdbc().update("DELETE FROM \"GsoEmailChanges\" WHERE \"UserId\"=?", id);
    db.jdbc()
        .update(
            "INSERT INTO \"GsoEmailChanges\" (\"UserId\",\"NewEmail\",\"OldEmail\","
                + "\"CodeHash\",\"SecurityStamp\",\"RequestedAt\",\"ExpiresAt\",\"Attempts\")"
                + " VALUES (?,?,?,?,?,?,?,0)",
            id,
            email,
            u.email,
            encoder.encode(code),
            u.securityStamp,
            now,
            now.plusMinutes(10));
    db.jdbc()
        .update(
            "UPDATE \"UserAccessNotifications\" SET \"DeliveryStatus\"='Cancelled',"
                + " \"Body\"='Verification code replaced.' WHERE \"UserId\"=? AND"
                + " \"NotificationType\"='EmailChangeCode' AND \"DeliveryStatus\"='Pending'",
            id);
    var note = new UserAccessNotification();
    note.userId = id;
    note.relatedUserId = id;
    note.email = email;
    note.notificationType = "EmailChangeCode";
    note.subject = "GSO Web Services - Verify your new email";
    note.body =
        "Your email change verification code is: "
            + code
            + "\n\nEnter this code in My Profile. It expires in 10 minutes and can be used once."
            + " Your account email stays unchanged until verification succeeds."
            + " If you did not request this, ignore this email.";
    note.deliveryStatus = "Pending";
    db.insert(note);
  }

  @Transactional
  public boolean confirm(String id, String code) {
    var u = owner(id);
    var rows =
        db.jdbc()
            .queryForList(
                "SELECT * FROM \"GsoEmailChanges\" WHERE \"UserId\"=?"
                    + " AND \"ExpiresAt\">? AND \"Attempts\"<5",
                id,
                OffsetDateTime.now(ZoneOffset.UTC));
    if (rows.isEmpty()) return false;
    var row = rows.getFirst();
    db.jdbc()
        .update(
            "UPDATE \"GsoEmailChanges\" SET \"Attempts\"=\"Attempts\"+1 WHERE \"UserId\"=?", id);
    if (!Objects.equals(row.get("SecurityStamp"), u.securityStamp)
        || !Objects.equals(row.get("OldEmail"), u.email)
        || code == null
        || !code.matches("[0-9]{6}")
        || !encoder.matches(code, (String) row.get("CodeHash"))) return false;
    String email = (String) row.get("NewEmail");
    available(id, email);
    accounts.queueNotification(
        id,
        id,
        "GSO Web Services - Email address changed",
        "Your GSO account email has been changed to "
            + email
            + ". If you did not make this change, contact support and secure your account.",
        "EmailChangeNotice");
    db.jdbc()
        .update(
            "UPDATE \"AspNetUsers\" SET"
                + " \"Email\"=?,\"NormalizedEmail\"=?,\"EmailConfirmed\"=true,\"SecurityStamp\"=?,\"ConcurrencyStamp\"=?"
                + " WHERE \"Id\"=?",
            email,
            email.toUpperCase(Locale.ROOT),
            UUID.randomUUID().toString(),
            UUID.randomUUID().toString(),
            id);
    db.jdbc().update("DELETE FROM \"GsoEmailChanges\" WHERE \"UserId\"=?", id);
    db.jdbc()
        .update(
            "UPDATE \"UserAccessNotifications\" SET \"DeliveryStatus\"=CASE WHEN"
                + " \"DeliveryStatus\"='Pending' THEN 'Cancelled' ELSE \"DeliveryStatus\" END,"
                + " \"Body\"='Verification completed.' WHERE \"UserId\"=? AND"
                + " \"NotificationType\"='EmailChangeCode'",
            id);
    return true;
  }
}
