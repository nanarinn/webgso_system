package my.webgso;

import java.util.Locale;
import java.util.UUID;
import my.webgso.Models.ApplicationUser;
import org.springframework.boot.ApplicationArguments;
import org.springframework.boot.ApplicationRunner;
import org.springframework.core.annotation.Order;
import org.springframework.core.env.Environment;
import org.springframework.stereotype.Component;
import org.springframework.transaction.annotation.Transactional;

/** Explicit, one-time replacement; no default production demo credentials. */
@Component
@Order(100)
public class DemoAccountSetup implements ApplicationRunner {
  private final Store db;
  private final Accounts accounts;
  private final Environment env;

  public DemoAccountSetup(Store db, Accounts accounts, Environment env) {
    this.db = db;
    this.accounts = accounts;
    this.env = env;
  }

  @Override
  @Transactional
  public void run(ApplicationArguments args) {
    if (env.getProperty("webgso.database.shared", Boolean.class, false)) return;
    if (!env.getProperty("webgso.demo.replace-affiqah", Boolean.class, false)) return;
    String password = env.getProperty("WEBGSO_DEMO_PASSWORD", "");
    Accounts.validatePassword(password);
    var targets = db.list(ApplicationUser.class,
        "WHERE \"NormalizedEmail\"=? FOR UPDATE", "AFFIQAHADRIANNA@GMAIL.COM");
    var demos = db.list(ApplicationUser.class,
        "WHERE \"NormalizedEmail\"=? OR \"NormalizedUserName\"=?",
        Accounts.DEMO_EMAIL.toUpperCase(Locale.ROOT), "DEMO.ADMIN");
    if (targets.isEmpty() && !demos.isEmpty()) return; // Never reset a changed password on restart.
    if (!targets.isEmpty() && !demos.isEmpty())
      throw new IllegalStateException("Demo account already exists; replacement was not applied.");
    ApplicationUser user;
    if (targets.isEmpty()) {
      user = accounts.register("demo.admin", Accounts.DEMO_EMAIL, "Demo Admin", password);
    } else {
      user = targets.getFirst();
      user.userName = "demo.admin";
      user.normalizedUserName = "DEMO.ADMIN";
      user.email = Accounts.DEMO_EMAIL;
      user.normalizedEmail = Accounts.DEMO_EMAIL.toUpperCase(Locale.ROOT);
      user.displayName = "Demo Admin";
      user.passwordHash = new IdentityPasswordEncoder().encode(password);
      db.jdbc().update("DELETE FROM \"GsoGoogleIdentities\" WHERE \"UserId\"=?", user.id);
      db.jdbc().update("DELETE FROM \"GsoEmailVerifications\" WHERE \"UserId\"=?", user.id);
    }
    user.accessStatus = "Approved";
    user.emailConfirmed = false;
    user.lockoutEnd = null;
    user.accessFailedCount = 0;
    user.securityStamp = UUID.randomUUID().toString();
    user.concurrencyStamp = UUID.randomUUID().toString();
    db.update(user);
    accounts.setRole(user.id, Accounts.ADMIN);
  }
}
