package my.webgso;

import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.time.*;
import java.util.*;
import my.webgso.Models.ApplicationUser;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
public class EmailVerification {
  private final Store db;
  private final Accounts accounts;
  private final String baseUrl;

  public EmailVerification(
      Store db,
      Accounts accounts,
      @Value("${webgso.public-base-url:http://localhost:5187}") String baseUrl) {
    this.db = db;
    this.accounts = accounts;
    var uri = URI.create(baseUrl);
    if (uri.getHost() == null
        || uri.getUserInfo() != null
        || uri.getQuery() != null
        || uri.getFragment() != null
        || !("https".equals(uri.getScheme())
            || "http".equals(uri.getScheme())
                && Set.of("localhost", "127.0.0.1").contains(uri.getHost())))
      throw new IllegalArgumentException(
          "Configure a valid HTTPS public application URL, or localhost for testing.");
    this.baseUrl = baseUrl.replaceAll("/+$", "");
  }

  @Transactional
  public ApplicationUser register(String username, String email, String name, String password) {
    var user = accounts.register(username, email, name, password);
    send(user);
    return user;
  }

  @Transactional
  public void resend(String email) {
    if (email == null || email.length() > 256) return;
    var users =
        db.list(
            ApplicationUser.class,
            "WHERE \"NormalizedEmail\"=? FOR UPDATE",
            email.trim().toUpperCase(Locale.ROOT));
    if (users.size() == 1 && "Pending".equals(users.getFirst().accessStatus))
      send(users.getFirst());
  }

  private void send(ApplicationUser user) {
    var now = OffsetDateTime.now(ZoneOffset.UTC);
    if (db.jdbc()
            .queryForObject(
                "SELECT COUNT(*) FROM \"GsoEmailVerifications\" WHERE \"UserId\"=? AND"
                    + " \"RequestedAt\">?",
                Long.class,
                user.id,
                now.minusMinutes(1))
        > 0) return;
    byte[] random = new byte[32];
    new SecureRandom().nextBytes(random);
    String token = Base64.getUrlEncoder().withoutPadding().encodeToString(random);
    db.jdbc().update("DELETE FROM \"GsoEmailVerifications\" WHERE \"UserId\"=?", user.id);
    db.jdbc()
        .update(
            "INSERT INTO \"GsoEmailVerifications\""
                + " (\"UserId\",\"TokenHash\",\"Email\",\"SecurityStamp\",\"ExpiresAt\",\"RequestedAt\")"
                + " VALUES (?,?,?,?,?,?)",
            user.id,
            digest(token),
            user.normalizedEmail,
            user.securityStamp,
            now.plusHours(24),
            now);
    db.jdbc()
        .update(
            "DELETE FROM \"UserAccessNotifications\" WHERE \"UserId\"=? AND"
                + " \"NotificationType\"='EmailVerification' AND \"DeliveryStatus\"='Pending'",
            user.id);
    accounts.queueNotification(
        user.id,
        user.id,
        "GSO Web Services - Verify your email",
        "Verify your email to activate your GSO Web Services account. No Admin approval is"
            + " needed.\n\n"
            + baseUrl
            + "/account/verify-email?token="
            + token
            + "\n\n"
            + "Open the link and select Verify email. This link expires in 24 hours and can be used"
            + " once.\n"
            + "If you did not register, ignore this email. Your account will remain inactive.",
        "EmailVerification");
  }

  @Transactional
  public boolean verify(String token) {
    if (token == null || !token.matches("[A-Za-z0-9_-]{43}")) return false;
    String hash = digest(token);
    var ids =
        db.jdbc()
            .queryForList(
                "SELECT \"UserId\" FROM \"GsoEmailVerifications\" WHERE \"TokenHash\"=?",
                String.class,
                hash);
    if (ids.size() != 1) return false;
    var users = db.list(ApplicationUser.class, "WHERE \"Id\"=? FOR UPDATE", ids.getFirst());
    if (users.size() != 1) return false;
    var user = users.getFirst();
    if (!"Pending".equals(user.accessStatus)) return false;
    var now = OffsetDateTime.now(ZoneOffset.UTC);
    int changed =
        db.jdbc()
            .update(
                "UPDATE \"GsoEmailVerifications\" SET \"UsedAt\"=? WHERE \"UserId\"=? AND"
                    + " \"TokenHash\"=? AND \"Email\"=? AND \"SecurityStamp\"=? AND \"UsedAt\" IS"
                    + " NULL AND \"ExpiresAt\">?",
                now,
                user.id,
                hash,
                user.normalizedEmail,
                user.securityStamp,
                now);
    if (changed != 1) return false;
    user.emailConfirmed = true;
    user.accessStatus = "Approved";
    user.accessReviewedAt = now;
    user.accessReviewedBy = "Email verification";
    user.securityStamp = UUID.randomUUID().toString();
    db.update(user);
    accounts.audit(
        user.userName,
        "Email verified",
        "User",
        user.id,
        "Success",
        "Account activated by email verification");
    return true;
  }

  private static String digest(String value) {
    try {
      return HexFormat.of()
          .formatHex(
              MessageDigest.getInstance("SHA-256").digest(value.getBytes(StandardCharsets.UTF_8)));
    } catch (java.security.NoSuchAlgorithmException e) {
      throw new IllegalStateException(e);
    }
  }
}
