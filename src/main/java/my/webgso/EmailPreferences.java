package my.webgso;

import java.util.*;
import my.webgso.Models.ApplicationUser;
import org.springframework.context.annotation.DependsOn;
import org.springframework.core.env.Environment;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
@DependsOn("accountToolsSchema")
public class EmailPreferences {
  public record Settings(
      boolean failure, boolean recovery, boolean stale, boolean excel, boolean digest) {}

  private final Store db;
  private final Environment env;

  public EmailPreferences(Store db, Environment env) {
    this.db = db;
    this.env = env;
  }

  public Settings get(String id) {
    var rows =
        db.jdbc()
            .query(
                "SELECT * FROM \"GsoEmailPreferences\" WHERE \"UserId\"=?",
                (r, n) ->
                    new Settings(
                        r.getBoolean("Failure"),
                        r.getBoolean("Recovery"),
                        r.getBoolean("Stale"),
                        r.getBoolean("Excel"),
                        r.getBoolean("Digest")),
                id);
    if (!rows.isEmpty()) return rows.getFirst();
    boolean configured =
        db.find(ApplicationUser.class, id)
            .map(
                u ->
                    u.email != null
                        && u.email.equalsIgnoreCase(
                            env.getProperty("webgso.api-alerts.recipient", "").strip()))
            .orElse(false);
    return new Settings(configured, configured, configured, true, configured);
  }

  @Transactional
  public void save(String id, Settings settings) {
    db.list(ApplicationUser.class, "WHERE \"Id\"=? FOR UPDATE", id).stream()
        .findFirst()
        .orElseThrow();
    db.jdbc().update("DELETE FROM \"GsoEmailPreferences\" WHERE \"UserId\"=?", id);
    db.jdbc()
        .update(
            "INSERT INTO \"GsoEmailPreferences\""
                + " (\"UserId\",\"Failure\",\"Recovery\",\"Stale\",\"Excel\",\"Digest\") VALUES"
                + " (?,?,?,?,?,?)",
            id,
            settings.failure(),
            settings.recovery(),
            settings.stale(),
            settings.excel(),
            settings.digest());
  }

  public boolean allows(String id, String type) {
    var s = get(id);
    return switch (type) {
      case "ApiTestFailed" -> s.failure();
      case "ApiTestRecovered" -> s.recovery();
      case "StaleDataAlert" -> s.stale();
      case "ApiDailySummary" -> s.digest();
      case "Excel" -> s.excel();
      default -> true;
    };
  }

  public boolean allowsDelivery(Models.UserAccessNotification note) {
    String type = note.notificationType;
    if ("ApiTestAlert".equals(type))
      type =
          note.subject != null && note.subject.contains("recovered")
              ? "ApiTestRecovered"
              : "ApiTestFailed";
    if (!Set.of("ApiTestFailed", "ApiTestRecovered", "StaleDataAlert", "ApiDailySummary")
        .contains(type)) return true;
    return allows(note.userId, type)
        && owner(note.userId).map(u -> u.email.equalsIgnoreCase(note.email)).orElse(false);
  }

  public Optional<ApplicationUser> owner(String id) {
    return db.find(ApplicationUser.class, id)
        .filter(u -> "Approved".equals(u.accessStatus) && GmailAccounts.allowed(u.email));
  }

  public List<ApplicationUser> recipients() {
    return db
        .list(
            ApplicationUser.class,
            "WHERE \"AccessStatus\"='Approved' AND (LOWER(\"Email\")=? OR \"Id\" IN (SELECT"
                + " \"UserId\" FROM \"GsoEmailPreferences\"))",
            env.getProperty("webgso.api-alerts.recipient", "").strip().toLowerCase(Locale.ROOT))
        .stream()
        .filter(u -> GmailAccounts.allowed(u.email))
        .toList();
  }
}
