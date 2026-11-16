package my.webgso;

import java.util.*;
import my.webgso.Models.ApplicationUser;
import org.springframework.security.access.AccessDeniedException;
import org.springframework.security.core.Authentication;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
public class UserManagement {
  private final Store db;
  private final Accounts accounts;
  public UserManagement(Store db, Accounts accounts) { this.db = db; this.accounts = accounts; }

  @Transactional
  public void manage(Authentication auth, String id, String operation, String confirmation) {
    if (auth == null || !(auth.getPrincipal() instanceof Accounts.Principal actor))
      throw new AccessDeniedException("Admin access required.");
    var admin = db.find(ApplicationUser.class, actor.id()).orElseThrow(() -> new AccessDeniedException("Admin access required."));
    if (!accounts.roles(actor.id()).contains(Accounts.ADMIN)
        || !"Approved".equals(admin.accessStatus) || !Objects.equals(actor.stamp(), admin.securityStamp))
      throw new AccessDeniedException("Admin access required.");
    var target = db.list(ApplicationUser.class, "WHERE \"Id\"=? FOR UPDATE", id).stream().findFirst()
        .orElseThrow(() -> new IllegalArgumentException("Account not found."));
    if (actor.id().equals(id) || !accounts.roles(id).equals(List.of(Accounts.USER)))
      throw new IllegalArgumentException("Only User accounts can be managed here. Admin accounts are protected.");
    if (operation.equals("delete")) {
      if (!"delete".equals(confirmation)) throw new IllegalArgumentException("Confirm account deletion first.");
      // Retain only a fingerprint of sign-in identifiers, not the deleted profile or password.
      for (String identifier : java.util.stream.Stream.of(target.email, target.userName).filter(Objects::nonNull).distinct().toList()) {
        String hash = DeletedAccountException.identifierHash(identifier);
        db.jdbc().update("DELETE FROM \"GsoDeletedAccounts\" WHERE \"IdentifierHash\"=?", hash);
        db.jdbc().update("INSERT INTO \"GsoDeletedAccounts\" (\"IdentifierHash\",\"DeletedAt\") VALUES (?,?)",
            hash, java.time.OffsetDateTime.now(java.time.ZoneOffset.UTC));
      }
      // Identity links may predate cascade constraints. Historical API results are retained.
      for (String table : List.of("AspNetUserClaims", "AspNetUserLogins", "AspNetUserTokens", "AspNetUserRoles", "GsoAccessReviewer")) {
        boolean exists = Boolean.TRUE.equals(db.jdbc().execute((org.springframework.jdbc.core.ConnectionCallback<Boolean>) connection -> {
          try (var tables = connection.getMetaData().getTables(connection.getCatalog(), null, table, new String[]{"TABLE"})) {
            return tables.next();
          }
        }));
        if (exists) db.jdbc().update("DELETE FROM \"" + table + "\" WHERE \"UserId\"=?", id);
      }
      db.jdbc().update("DELETE FROM \"UserAccessNotifications\" WHERE \"UserId\"=? OR \"RelatedUserId\"=?", id, id);
      db.jdbc().update("DELETE FROM \"AspNetUsers\" WHERE \"Id\"=?", id);
    } else {
        if (operation.equals("activate") && !target.emailConfirmed
            && db.jdbc().queryForObject("SELECT COUNT(*) FROM \"GsoEmailVerifications\" WHERE \"UserId\"=?", Long.class, id) > 0)
          throw new IllegalArgumentException("This user must verify their email before activation.");
      String status = switch (operation) { case "activate" -> "Approved"; case "deactivate" -> "Inactive"; default -> throw new IllegalArgumentException("Invalid account action."); };
      db.jdbc().update("UPDATE \"AspNetUsers\" SET \"AccessStatus\"=?, \"SecurityStamp\"=?, \"ConcurrencyStamp\"=? WHERE \"Id\"=?",
          status, UUID.randomUUID().toString(), UUID.randomUUID().toString(), id);
    }
    accounts.audit(actor.username(), "User " + operation, "User", id, "Success", "User account managed");
  }
}
