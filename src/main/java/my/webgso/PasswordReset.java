package my.webgso;

import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.security.*;
import java.time.*;
import java.util.*;
import my.webgso.Models.ApplicationUser;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.DependsOn;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
@DependsOn("accountToolsSchema")
public class PasswordReset {
  private final Store db;
  private final Accounts accounts;
  private final String base;

  public PasswordReset(
      Store db,
      Accounts accounts,
      @Value("${webgso.public-base-url:http://localhost:5187}") String base) {
    this.db = db;
    this.accounts = accounts;
    var uri = URI.create(base);
    if (uri.getHost() == null
        || uri.getUserInfo() != null
        || uri.getQuery() != null
        || uri.getFragment() != null
        || !("https".equals(uri.getScheme())
            || "http".equals(uri.getScheme())
                && Set.of("localhost", "127.0.0.1").contains(uri.getHost())))
      throw new IllegalArgumentException("Configure a trusted HTTPS application URL.");
    this.base = base.replaceAll("/+$", "");
  }

  public static boolean tokenFormat(String token) {
    return token != null && token.matches("[A-Za-z0-9_-]{43}");
  }

  private static String hash(String token) {
    try {
      return HexFormat.of()
          .formatHex(
              MessageDigest.getInstance("SHA-256").digest(token.getBytes(StandardCharsets.UTF_8)));
    } catch (NoSuchAlgorithmException e) {
      throw new IllegalStateException(e);
    }
  }

  @Transactional
  public void request(String email) {
    if (!GmailAccounts.allowed(email == null ? null : email.strip())) return;
    var users =
        db.list(
            ApplicationUser.class,
            "WHERE \"NormalizedEmail\"=? FOR UPDATE",
            email.strip().toUpperCase(Locale.ROOT));
    if (users.size() != 1) return;
    var u = users.getFirst();
    if (!"Approved".equals(u.accessStatus)
        || Boolean.TRUE.equals(u.isEntraUser)
        || u.passwordHash == null
        || u.passwordHash.isBlank()) return;
    var now = OffsetDateTime.now(ZoneOffset.UTC);
    if (db.jdbc()
            .queryForObject(
                "SELECT COUNT(*) FROM \"GsoPasswordResets\" WHERE \"UserId\"=? AND"
                    + " \"RequestedAt\">?",
                Long.class,
                u.id,
                now.minusMinutes(1))
        > 0) return;
    byte[] bytes = new byte[32];
    new SecureRandom().nextBytes(bytes);
    String token = Base64.getUrlEncoder().withoutPadding().encodeToString(bytes);
    db.jdbc().update("DELETE FROM \"GsoPasswordResets\" WHERE \"UserId\"=?", u.id);
    db.jdbc()
        .update(
            "INSERT INTO \"GsoPasswordResets\""
                + " (\"UserId\",\"TokenHash\",\"Email\",\"SecurityStamp\",\"RequestedAt\",\"ExpiresAt\")"
                + " VALUES (?,?,?,?,?,?)",
            u.id,
            hash(token),
            u.normalizedEmail,
            u.securityStamp,
            now,
            now.plusMinutes(30));
    db.jdbc()
        .update(
            "DELETE FROM \"UserAccessNotifications\" WHERE \"UserId\"=? AND"
                + " \"NotificationType\"='PasswordReset' AND \"DeliveryStatus\"='Pending'",
            u.id);
    accounts.queueNotification(
        u.id,
        u.id,
        "GSO Web Services - Reset your password",
        "A password reset was requested for your GSO account. Open this link to choose a new"
            + " password:\n\n"
            + base
            + "/account/reset-password?token="
            + token
            + "\n\n"
            + "This link expires in 30 minutes and can be used once. If you did not request this,"
            + " ignore this email. Your password has not changed.",
        "PasswordReset");
  }

  @Transactional
  public boolean reset(String token, String password) {
    Accounts.validatePassword(password);
    if (!tokenFormat(token)) return false;
    var ids =
        db.jdbc()
            .queryForList(
                "SELECT \"UserId\" FROM \"GsoPasswordResets\" WHERE \"TokenHash\"=?",
                String.class,
                hash(token));
    if (ids.size() != 1) return false;
    var users = db.list(ApplicationUser.class, "WHERE \"Id\"=? FOR UPDATE", ids.getFirst());
    if (users.size() != 1) return false;
    var u = users.getFirst();
    var now = OffsetDateTime.now(ZoneOffset.UTC);
    if (!"Approved".equals(u.accessStatus)
        || Boolean.TRUE.equals(u.isEntraUser)
        || !GmailAccounts.allowed(u.email)) return false;
    int changed =
        db.jdbc()
            .update(
                "UPDATE \"GsoPasswordResets\" SET \"UsedAt\"=? WHERE \"UserId\"=? AND"
                    + " \"TokenHash\"=? AND \"Email\"=? AND \"SecurityStamp\"=? AND \"UsedAt\" IS"
                    + " NULL AND \"ExpiresAt\">?",
                now,
                u.id,
                hash(token),
                u.normalizedEmail,
                u.securityStamp,
                now);
    if (changed != 1) return false;
    u.passwordHash = new IdentityPasswordEncoder().encode(password);
    u.securityStamp = UUID.randomUUID().toString();
    u.concurrencyStamp = UUID.randomUUID().toString();
    u.accessFailedCount = 0;
    u.lockoutEnd = null;
    db.update(u);
    accounts.audit(
        u.userName,
        "Password reset",
        "User",
        u.id,
        "Success",
        "Password reset using single-use Gmail link");
    accounts.queueNotification(
        u.id,
        u.id,
        "GSO Web Services - Password changed",
        "Your GSO password was changed. Existing sessions have been signed out. If you did not make"
            + " this change, reset your password and secure your Gmail account.",
        "PasswordReset");
    return true;
  }
}
