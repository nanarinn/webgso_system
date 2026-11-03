package my.webgso;

import java.time.Instant;
import java.time.OffsetDateTime;
import java.util.Objects;
import my.webgso.Models.ApplicationUser;
import org.springframework.security.authentication.UsernamePasswordAuthenticationToken;
import org.springframework.security.core.Authentication;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
public class GoogleAccountLink {
  public static final String SESSION_KEY = GoogleAccountLink.class.getName();

  public record Intent(String userId, String stamp, Instant expires, String state)
      implements java.io.Serializable {
    public Intent withState(String value) {
      return new Intent(userId, stamp, expires, value);
    }
  }

  private final Store db;
  private final Accounts accounts;

  public GoogleAccountLink(Store db, Accounts accounts) {
    this.db = db;
    this.accounts = accounts;
  }

  public boolean linked(String id) {
    return !db.jdbc()
        .queryForList(
            "SELECT \"Subject\" FROM \"GsoGoogleIdentities\" WHERE \"UserId\"=?", String.class, id)
        .isEmpty();
  }

  public Intent begin(Authentication auth, String password) {
    var principal = WebController.principal(auth);
    var intent =
        new Intent(principal.id(), principal.stamp(), Instant.now().plusSeconds(300), null);
    var user = current(intent);
    if (linked(user.id)) throw new IllegalArgumentException("A Google account is already linked.");
    if (password == null || password.isBlank())
      throw new IllegalArgumentException("Enter your current GSO password.");
    accounts.authenticate(
        UsernamePasswordAuthenticationToken.unauthenticated(user.email, password));
    return intent;
  }

  public ApplicationUser current(Intent intent) {
    if (intent == null || !intent.expires().isAfter(Instant.now()))
      throw new IllegalArgumentException(
          "Google linking expired. Please start again from My Profile.");
    var user = db.find(ApplicationUser.class, intent.userId()).orElseThrow();
    if (!"Approved".equals(user.accessStatus)
        || !Objects.equals(intent.stamp(), user.securityStamp)
        || (user.lockoutEnd != null && user.lockoutEnd.isAfter(OffsetDateTime.now())))
      throw new IllegalArgumentException("Your account session changed. Please sign in again.");
    GmailAccounts.require(user.email);
    return user;
  }

  @Transactional
  public ApplicationUser complete(Intent intent, String subject, String email, Boolean verified) {
    db.jdbc()
        .queryForList(
            "SELECT \"Id\" FROM \"AspNetUsers\" WHERE \"Id\"=? FOR UPDATE",
            String.class,
            intent.userId());
    var user = current(intent);
    if (!Boolean.TRUE.equals(verified)
        || subject == null
        || !subject.matches("[a-zA-Z0-9_-]{1,255}")
        || email == null
        || !email.equalsIgnoreCase(user.email))
      throw new IllegalArgumentException(
          "Choose the verified Google account with the same Gmail address as your GSO profile.");
    var owner =
        db.jdbc()
            .queryForList(
                "SELECT \"UserId\" FROM \"GsoGoogleIdentities\" WHERE \"Subject\"=?",
                String.class,
                subject);
    if (!owner.isEmpty() && !owner.getFirst().equals(user.id))
      throw new IllegalArgumentException(
          "This Google account is already linked to another GSO account.");
    var existing =
        db.jdbc()
            .queryForList(
                "SELECT \"Subject\" FROM \"GsoGoogleIdentities\" WHERE \"UserId\"=?",
                String.class,
                user.id);
    if (!existing.isEmpty() && !existing.getFirst().equals(subject))
      throw new IllegalArgumentException("A different Google account is already linked.");
    if (owner.isEmpty())
      db.jdbc()
          .update(
              "INSERT INTO \"GsoGoogleIdentities\" (\"Subject\",\"UserId\") VALUES (?,?)",
              subject,
              user.id);
    accounts.audit(
        user.userName,
        "Link Google account",
        "User",
        user.id,
        "Success",
        "Google identity verified; local password retained");
    return user;
  }
}
