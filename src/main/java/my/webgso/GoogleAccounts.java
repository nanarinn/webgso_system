package my.webgso;

import java.time.OffsetDateTime;
import java.util.Locale;
import java.util.UUID;
import my.webgso.Models.ApplicationUser;
import org.springframework.security.authentication.DisabledException;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Isolation;
import org.springframework.transaction.annotation.Transactional;

/** Bind Google identities by their verified subject, never by email alone. */
@Service
public class GoogleAccounts {
  private final Store db;
  private final Accounts accounts;

  public GoogleAccounts(Store db, Accounts accounts) {
    this.db = db;
    this.accounts = accounts;
  }

  @Transactional(isolation = Isolation.SERIALIZABLE)
  public ApplicationUser resolve(String subject, String email, Boolean verified, String name) {
    if (!Boolean.TRUE.equals(verified)
        || subject == null
        || !subject.matches("[a-zA-Z0-9_-]{1,255}")
        || email == null
        || email.length() > 256
        || !email.matches("[^\\s@]+@[^\\s@]+\\.[^\\s@]+"))
      throw new IllegalArgumentException("Google identity could not be verified.");
    var ids =
        db.jdbc()
            .queryForList(
                "SELECT \"UserId\" FROM \"GsoGoogleIdentities\" WHERE \"Subject\"=?",
                String.class,
                subject);
    if (!ids.isEmpty()) {
      var user = db.find(ApplicationUser.class, ids.getFirst()).orElseThrow();
      if (!"Approved".equals(user.accessStatus) && !"Pending".equals(user.accessStatus))
        throw new DisabledException("Account access is inactive.");
      if (user.lockoutEnd != null && user.lockoutEnd.isAfter(OffsetDateTime.now()))
        throw new DisabledException("Account access is temporarily locked.");
      activateVerified(user);
      return user;
    }
    if (!db.list(
            ApplicationUser.class, "WHERE \"NormalizedEmail\"=?", email.toUpperCase(Locale.ROOT))
        .isEmpty()) throw new ExistingEmailException();
    String displayName = name == null || name.isBlank() ? email : name;
    if (displayName.length() > 160) displayName = displayName.substring(0, 160);
    var user =
        accounts.register(
            "google." + UUID.randomUUID(), email, displayName, "Gg1!" + UUID.randomUUID());
    // Google-only accounts cannot sign in with a local password.
    user.passwordHash = null;
    activateVerified(user);
    db.update(user);
    db.jdbc()
        .update(
            "INSERT INTO \"GsoGoogleIdentities\" (\"Subject\",\"UserId\") VALUES (?,?)",
            subject,
            user.id);
    return user;
  }

  private void activateVerified(ApplicationUser user) {
    if (!"Pending".equals(user.accessStatus)) return;
    user.emailConfirmed = true;
    user.accessStatus = "Approved";
    user.accessReviewedAt = OffsetDateTime.now(java.time.ZoneOffset.UTC);
    user.accessReviewedBy = "Google email verification";
    user.securityStamp = UUID.randomUUID().toString();
    db.update(user);
  }

  public static final class ExistingEmailException extends IllegalArgumentException {}
}
