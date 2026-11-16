package my.webgso;

import java.util.*;
import my.webgso.Models.ApplicationUser;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
public class ProfileService {
  private final Store db;
  private final Accounts accounts;

  public ProfileService(Store db, Accounts accounts) {
    this.db = db;
    this.accounts = accounts;
  }

  @Transactional
  public void update(
      String userId, String displayName, String username, String email, String phone) {
    displayName = displayName.trim();
    username = username.trim();
    email = email.trim();
    phone = phone.trim();
    if (displayName.isBlank()
        || displayName.length() > 160
        || !username.matches("[a-zA-Z0-9_.@-]{3,256}")
        || email.length() > 256
        || (!email.isBlank() && !email.matches("[^\\s@]+@[^\\s@]+\\.[^\\s@]+"))
        || !phone.matches("[+0-9() .-]{0,30}"))
      throw new IllegalArgumentException("Enter a valid name, username, email and phone number.");
    var user =
        db.list(ApplicationUser.class, "WHERE \"Id\"=? FOR UPDATE", userId).stream()
            .findFirst()
            .orElseThrow(() -> new IllegalArgumentException("Profile not found."));
    if (Boolean.TRUE.equals(user.isEntraUser)
        && (!username.equals(user.userName) || !email.equals(user.email)))
      throw new IllegalArgumentException(
          "Your username and email are managed by your identity provider.");
    if (!email.equalsIgnoreCase(user.email))
      throw new IllegalArgumentException(
          "Use Change email in Account security to change your email.");
    String normalizedName = username.toUpperCase(Locale.ROOT),
        normalizedEmail = email.toUpperCase(Locale.ROOT);
    if (!db.list(
            ApplicationUser.class,
            "WHERE \"Id\"<>? AND (\"NormalizedUserName\" IN (?,?) OR \"NormalizedEmail\" IN (?,?))",
            userId,
            normalizedName,
            normalizedEmail,
            normalizedName,
            normalizedEmail)
        .isEmpty()) throw new IllegalArgumentException("That username or email is already in use.");
    if (!Objects.equals(user.displayName, displayName))
      db.jdbc().update("INSERT INTO \"GsoNameChangeHistory\" (\"Id\",\"UserId\",\"OldName\",\"NewName\",\"ChangedAt\") VALUES (?,?,?,?,?)",
          UUID.randomUUID().toString(), userId, Objects.toString(user.displayName, ""), displayName,
          java.time.OffsetDateTime.now(java.time.ZoneOffset.UTC));
    db.jdbc()
        .update(
            "UPDATE \"AspNetUsers\" SET \"DisplayName\"=?, \"UserName\"=?,"
                + " \"NormalizedUserName\"=?, \"Email\"=?, \"NormalizedEmail\"=?,"
                + " \"PhoneNumber\"=?, \"EmailConfirmed\"=?, \"PhoneNumberConfirmed\"=?,"
                + " \"ConcurrencyStamp\"=? WHERE \"Id\"=?",
            displayName,
            username,
            normalizedName,
            email,
            normalizedEmail.isBlank() ? null : normalizedEmail,
            phone.isBlank() ? null : phone,
            email.equalsIgnoreCase(user.email) && Boolean.TRUE.equals(user.emailConfirmed),
            Objects.equals(phone, Objects.toString(user.phoneNumber, ""))
                && Boolean.TRUE.equals(user.phoneNumberConfirmed),
            UUID.randomUUID().toString(),
            userId);
    accounts.audit(
        username, "Update own profile", "User", userId, "Success", "Profile details updated");
  }
  public record EmailHistory(String oldEmail, String newEmail, java.time.OffsetDateTime changedAt) {}
  public record NameHistory(String oldName, String newName, java.time.OffsetDateTime changedAt) {}
  public List<NameHistory> nameHistory(String userId) {
    return db.jdbc().query("SELECT \"OldName\",\"NewName\",\"ChangedAt\" FROM \"GsoNameChangeHistory\" WHERE \"UserId\"=? ORDER BY \"ChangedAt\" DESC, \"Id\" DESC LIMIT 10",
        (rs,n) -> new NameHistory(rs.getString(1),rs.getString(2),rs.getObject(3,java.time.OffsetDateTime.class)), userId);
  }

  public List<EmailHistory> emailHistory(String userId) {
    return db.jdbc().query(
        "SELECT \"OldEmail\", \"NewEmail\", \"ChangedAt\" FROM \"GsoEmailChangeHistory\""
            + " WHERE \"UserId\"=? ORDER BY \"ChangedAt\" DESC, \"Id\" DESC LIMIT 10",
        (rs, n) -> new EmailHistory(rs.getString(1), rs.getString(2),
            rs.getObject(3, java.time.OffsetDateTime.class)), userId);
  }

  @Transactional(noRollbackFor = org.springframework.security.core.AuthenticationException.class)
  public void changeEmail(String userId, String newEmail, String currentPassword) {
    var user = db.list(ApplicationUser.class, "WHERE \"Id\"=? FOR UPDATE", userId).stream()
        .findFirst().orElseThrow(() -> new IllegalArgumentException("Profile not found."));
    if (Boolean.TRUE.equals(user.isEntraUser))
      throw new IllegalArgumentException("Your email is managed by your identity provider.");
    if (currentPassword == null || currentPassword.length() > 256)
      throw new IllegalArgumentException("Enter your current password.");
    accounts.authenticate(org.springframework.security.authentication.UsernamePasswordAuthenticationToken
        .unauthenticated(user.email, currentPassword));
    String email = newEmail == null ? "" : newEmail.strip().toLowerCase(Locale.ROOT);
    if (!SaeAccounts.allowed(email)) throw new IllegalArgumentException(SaeAccounts.MESSAGE);
    if (email.equalsIgnoreCase(user.email))
      throw new IllegalArgumentException("Enter a different SAE email address.");
    String normalized = email.toUpperCase(Locale.ROOT);
    if (!db.list(ApplicationUser.class,
        "WHERE \"Id\"<>? AND (\"NormalizedEmail\"=? OR \"NormalizedUserName\"=?)",
        userId, normalized, normalized).isEmpty())
      throw new IllegalArgumentException("That email address is already in use.");
    db.jdbc().update(
        "INSERT INTO \"GsoEmailChangeHistory\" (\"Id\",\"UserId\",\"OldEmail\",\"NewEmail\",\"ChangedAt\") VALUES (?,?,?,?,?)",
        UUID.randomUUID().toString(), userId, user.email, email,
        java.time.OffsetDateTime.now(java.time.ZoneOffset.UTC));
    db.jdbc().update(
        "UPDATE \"AspNetUsers\" SET \"Email\"=?, \"NormalizedEmail\"=?, \"UserName\"=?,"
            + " \"NormalizedUserName\"=?, \"EmailConfirmed\"=false, \"SecurityStamp\"=?,"
            + " \"ConcurrencyStamp\"=? WHERE \"Id\"=?",
        email, normalized, email, normalized, UUID.randomUUID().toString(),
        UUID.randomUUID().toString(), userId);
    for (String table : List.of("GsoEmailChanges", "GsoPasswordChanges", "GsoPasswordResets"))
      db.jdbc().update("DELETE FROM \"" + table + "\" WHERE \"UserId\"=?", userId);
    db.jdbc().update("UPDATE \"UserAccessNotifications\" SET \"DeliveryStatus\"='Cancelled'"
        + " WHERE \"UserId\"=? AND \"DeliveryStatus\"='Pending'"
        + " AND \"NotificationType\" IN ('EmailChangeCode','PasswordChangeCode','PasswordReset','EmailVerification')", userId);
    accounts.audit(email, "Change own email", "User", userId, "Success", "Account email changed");
  }
}
